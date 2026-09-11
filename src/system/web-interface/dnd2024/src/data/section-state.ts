import type { DeferredHubUpdate, PartyMemberReadModel, ReadyHubEnvelope, SectionState, WorldLocation } from "./hub-types";

type LocationUpdate = Extract<DeferredHubUpdate, { section: "locations" }>;

function mergeLocation(previous: WorldLocation | undefined, next: WorldLocation): WorldLocation {
  if (!previous) return next;
  return {
    ...previous,
    ...next,
    // These fields are populated by independent People/Current reads. A location-scope
    // page refresh must not turn their omission from this source into a deletion.
    people: previous.people,
    routes: previous.routes,
    ...(Object.hasOwn(previous, "holdings") ? { holdings: previous.holdings } : {}),
    ...(Object.hasOwn(previous, "dmSecret") ? { dmSecret: previous.dmSecret } : {}),
  };
}

function applyWorldScopePage(current: ReadyHubEnvelope, update: LocationUpdate): ReadyHubEnvelope {
  const scopeId = update.scopePage?.id;
  const incomingScope = update.world.locationScopes.find((scope) => scope.id === scopeId);
  if (!scopeId || !incomingScope) return current;

  const scopeById = new Map(current.world.locationScopes.map((scope) => [scope.id, scope]));
  const admittedHere = new Set(incomingScope.childIds);
  for (const [id, scope] of scopeById) {
    if (id === scopeId || !scope.childIds.some((childId) => admittedHere.has(childId))) continue;
    const childIds = scope.childIds.filter((childId) => !admittedHere.has(childId));
    scopeById.set(id, {
      ...scope,
      childIds,
      totalCount: Math.max(childIds.length, scope.totalCount - (scope.childIds.length - childIds.length)),
    });
  }
  scopeById.set(scopeId, incomingScope);

  const rootId = current.contextSelection?.selectedWorldId ?? current.world.id;
  const reachable = new Set<string>();
  const pending = [rootId];
  while (pending.length > 0) {
    const id = pending.pop()!;
    if (reachable.has(id)) continue;
    if (reachable.size >= 1_001) return current;
    reachable.add(id);
    pending.push(...(scopeById.get(id)?.childIds ?? []));
  }

  const locationById = new Map(current.world.locations.map((location) => [location.id, location]));
  const pageLocationIds = new Set([scopeId, ...incomingScope.childIds]);
  for (const location of update.world.locations.filter((item) => pageLocationIds.has(item.id)))
    locationById.set(location.id, mergeLocation(locationById.get(location.id), location));
  const locationScopes = [...scopeById.values()].filter((scope) => reachable.has(scope.id));
  const locations = [...locationById.values()].filter((location) => reachable.has(location.id));

  const mapById = new Map(current.world.maps.map((map) => [map.id, map]));
  const rootPage = scopeId === rootId;
  const pageMaps = update.world.maps.filter((map) =>
    pageLocationIds.has(map.subject.id) || rootPage && map.id === update.world.rootMapId);
  for (const map of pageMaps) mapById.set(map.id, map);
  const rootMapId = rootPage ? update.world.rootMapId : current.world.rootMapId;
  // Bootstrap may contain a synthetic world-root map before the atlas owner is
  // known. Retaining it makes the old selected ID appear valid forever, leaving
  // Map on an empty placeholder instead of opening the newly discovered atlas.
  if (rootPage && current.world.mapOwnerId === null && rootMapId !== current.world.rootMapId) {
    mapById.delete(current.world.rootMapId);
  }
  const maps = [...mapById.values()].filter((map) =>
    reachable.has(map.subject.id) || map.id === rootMapId);
  const mapIds = new Set(maps.map((map) => map.id));
  // Location scope pages do not own Lore/knowledge overlays. Retain those independent
  // records while their authorized map remains reachable, and prune only retired maps.
  const mapOverlays = current.campaign.mapOverlays.filter((overlay) => mapIds.has(overlay.mapId));

  return {
    ...current,
    world: {
      ...current.world,
      currentLocationId: update.world.currentLocationId,
      map: rootPage ? update.world.map : current.world.map,
      mapOwnerId: rootPage ? update.world.mapOwnerId : current.world.mapOwnerId,
      rootMapId,
      maps,
      locations,
      locationScopes,
    },
    campaign: { ...current.campaign, mapOverlays },
  };
}

export function applyDeferredHubUpdate(
  current: ReadyHubEnvelope,
  update: DeferredHubUpdate,
): ReadyHubEnvelope {
  if (update.section === "context") {
    return { ...current, contextSelection: update.contextSelection };
  }
  if (update.section === "locations" && update.scopePage) return applyWorldScopePage(current, update);
  // Current is a selected-scene facet in the Redux owner. Its partial selected
  // location and overlay inputs are never a World/Campaign directory patch.
  if (update.section === "current") return current;
  return {
    ...current,
    world: { ...current.world, ...update.world },
    ...(update.section === "lore" || update.section === "locations"
      ? { campaign: { ...current.campaign, ...update.campaign } }
      : {}),
  };
}

function sameAudienceBoundary(previous: ReadyHubEnvelope, next: ReadyHubEnvelope): boolean {
  const previousCampaignId = previous.contextSelection?.selectedCampaignId ?? previous.revision;
  const nextCampaignId = next.contextSelection?.selectedCampaignId ?? next.revision;
  return previous.applicationId === next.applicationId &&
    previous.stateSpaceId === next.stateSpaceId &&
    previousCampaignId === nextCampaignId &&
    previous.revision === next.revision &&
    previous.audience.seat === next.audience.seat &&
    previous.audience.perspective === next.audience.perspective &&
    previous.audience.allowedPerspectives.length === next.audience.allowedPerspectives.length &&
    previous.audience.allowedPerspectives.every((value, index) =>
      value === next.audience.allowedPerspectives[index]);
}

function isTransientFailure<T>(
  failed: Extract<SectionState<T>, { status: "error" }>,
): boolean {
  if (failed.failureCategory === "transport" || failed.failureCategory === "stale-data") return true;
  if (failed.failureCategory !== "http" || failed.httpStatus === undefined) return false;
  return failed.httpStatus === 408 || failed.httpStatus === 429 || failed.httpStatus >= 500;
}

function staleFrom<T>(
  previous: SectionState<T>,
  failed: Extract<SectionState<T>, { status: "error" }>,
): SectionState<T> {
  if (!isTransientFailure(failed)) return failed;
  if ((previous.status !== "ready" && previous.status !== "empty" && previous.status !== "stale") ||
      previous.source !== "canonical") return failed;
  return {
    status: "stale",
    data: previous.data,
    source: previous.source,
    failureCategory: failed.failureCategory,
    diagnosticId: failed.diagnosticId,
    ...(failed.errorCode === undefined ? {} : { errorCode: failed.errorCode }),
    ...(failed.httpStatus === undefined ? {} : { httpStatus: failed.httpStatus }),
  };
}

/**
 * Carries canonical character fields through a transient refresh failure only.
 * The scoped confirmed-character owner uses this at its write boundary; callers
 * must supply a member from the same confirmed scope.
 */
export function preserveMemberOnTransientFailure(
  previous: PartyMemberReadModel,
  next: PartyMemberReadModel,
): PartyMemberReadModel {
  const sheetState = next.sheetState.status === "error"
    ? staleFrom(previous.sheetState, next.sheetState)
    : next.sheetState;
  const inventoryState = next.inventoryState.status === "error"
    ? staleFrom(previous.inventoryState, next.inventoryState)
    : next.inventoryState;
  const sheetIsStale = sheetState.status === "stale";
  const inventoryIsStale = inventoryState.status === "stale";
  return {
    ...next,
    sheetState,
    inventoryState,
    sheet: sheetIsStale ? sheetState.data : next.sheet,
    inventory: inventoryIsStale ? inventoryState.data : next.inventory,
    ...(sheetIsStale ? { sheetStatus: "canonical" as const } : {}),
    ...(inventoryIsStale ? { inventoryStatus: previous.inventoryStatus } : {}),
    ...(sheetIsStale && previous.characterSheet ? { characterSheet: previous.characterSheet } : {}),
    ...(sheetIsStale || inventoryIsStale ? { recordStatus: "Canonical character state is stale" } : {}),
  };
}

export function preserveLastGoodPartyData(
  previous: ReadyHubEnvelope,
  next: ReadyHubEnvelope,
): ReadyHubEnvelope {
  if (!sameAudienceBoundary(previous, next)) return next;
  const previousById = new Map(previous.party.map((member) => [member.id, member]));
  return {
    ...next,
    party: next.party.map((member) => {
      const prior = previousById.get(member.id);
      return prior ? preserveMemberOnTransientFailure(prior, member) : member;
    }),
  };
}
