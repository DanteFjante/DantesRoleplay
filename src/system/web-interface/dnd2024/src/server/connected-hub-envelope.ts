import type {
  CampaignDetailFields,
  CampaignMapOverlay,
  ConnectedCampaignDetails,
  ConnectedCampaignEnvelope,
  DeferredHubSection,
  DeferredHubUpdate,
  MapBaseState,
  MapDocument,
  MapFeature,
  MapLayer,
  MapScope,
  PartyMemberReadModel,
  ReadyHubEnvelope,
  WorldHistoryEvent,
} from "../data/hub-types";
import { classifyThalorienKnowledge } from "../data/thalorien-presentation.ts";

function normalizeSlugWords(value: string | null): string | null {
  if (!value) return null;
  const words = value
    .split(/[-_]/u)
    .map((word) => word.trim())
    .filter(Boolean);
  if (words.length === 0) return null;
  return words
    .map((word) => word[0]?.toUpperCase?.() + word.slice(1).toLowerCase())
    .join(" ");
}

function deriveWorldName(connection: ConnectedCampaignEnvelope): string {
  return connection.contextSelection.worlds.find(
    (world) => world.id === connection.contextSelection.selectedWorldId,
  )?.name ?? "Unprojected world";
}

function initials(name: string): string {
  const letters = name
    .split(/\s+/u)
    .filter(Boolean)
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
  return letters || "PC";
}

type ConnectedPartyMember = NonNullable<ConnectedCampaignEnvelope["party"]>[number];

/** Applies one validated registered Campaign-detail response without mutating the retained source. */
export function mergeConnectedCampaignDetails(
  connection: ConnectedCampaignEnvelope,
  details: ConnectedCampaignDetails,
): ConnectedCampaignEnvelope {
  const { detailFields, ...campaignDetails } = details;
  const validStatuses = new Set<CampaignDetailFields[keyof CampaignDetailFields]>(
    ["ready", "partial", "empty", "absent", "invalid"],
  );
  const normalizedDetailFields = detailFields ? {
    chapters: validStatuses.has(detailFields.chapters as CampaignDetailFields[keyof CampaignDetailFields])
      ? detailFields.chapters as CampaignDetailFields["chapters"] : "invalid",
    arcs: validStatuses.has(detailFields.arcs as CampaignDetailFields[keyof CampaignDetailFields])
      ? detailFields.arcs as CampaignDetailFields["arcs"] : "invalid",
    sessions: validStatuses.has(detailFields.sessions as CampaignDetailFields[keyof CampaignDetailFields])
      ? detailFields.sessions as CampaignDetailFields["sessions"] : "invalid",
    visits: validStatuses.has(detailFields.visits as CampaignDetailFields[keyof CampaignDetailFields])
      ? detailFields.visits as CampaignDetailFields["visits"] : "invalid",
  } as CampaignDetailFields : undefined;
  return {
    ...connection,
    campaign: {
      ...connection.campaign,
      ...campaignDetails,
      ...(normalizedDetailFields ? { detailFields: normalizedDetailFields } : {}),
    },
  };
}

/** Bootstrap projection: identity and participation only; character details are feature resources. */
export function projectPartySummary(connection: ConnectedCampaignEnvelope): PartyMemberReadModel[] {
  const members: ConnectedPartyMember[] = connection.party ?? [{ ...connection.actor, current: true }];
  // A shared party union is not the selected character's private notebook.
  const canAttachBoundKnowledge = connection.audience.seat === "player" &&
    connection.knowledge.status === "ready" && connection.knowledge.audience === undefined;
  return members.map((member) => ({
    id: member.id,
    initials: initials(member.name),
    name: member.name,
    detail: "Character details not yet loaded",
    status: member.state ? displayStatus(member.state) : "Active participant",
    isCurrent: member.current,
    ...(member.media?.portrait ? { portrait: member.media.portrait } : {}),
    recordStatus: "Identity only",
    sheetStatus: "empty",
    inventoryStatus: "empty",
    sheetState: { status: "idle", data: null },
    inventoryState: { status: "idle", data: null },
    sheet: [],
    knowledge: canAttachBoundKnowledge && member.current
      ? connection.knowledge.entries.map((entry, index) => ({
          id: `${member.id}:knowledge:${index + 1}`,
          stance: displayStatus(entry.stance, "Known"),
          kind: displayStatus(entry.presentationKind, "Knowledge"),
          text: entry.text,
        }))
      : [],
    backstory: [],
    origin: [],
    inventory: [],
  }));
}

function normalizeRegion(value: string | null): string | null {
  if (!value) return null;
  const normalized = value.trim().replace(/\s+/gu, " ");
  return normalized || null;
}

function inferCountryFromLocationName(name: string): string | null {
  const normalized = normalizeRegion(name);
  if (!normalized) return null;

  const parenthetical = normalized.match(/\(([^()]+)\)\s*$/u);
  if (parenthetical?.[1]) {
    const candidate = normalizeRegion(parenthetical[1]);
    if (candidate) return candidate;
  }

  const commaParts = normalized.split(",").map((part) => part.trim()).filter(Boolean);
  if (commaParts.length > 1) {
    return normalizeRegion(commaParts.at(-1) ?? null);
  }

  const dashed = normalized.match(/^(.*)\s[-–—]\s(.*)$/u);
  return dashed?.[2] ? normalizeRegion(dashed[2]) : null;
}

function inferRegionFromDirectoryId(id: string): string {
  const slug = id.split(".").at(-1);
  if (!slug) return "Live location region";
  const normalized = normalizeSlugWords(slug);
  return normalized ?? "Live location region";
}

function inferRegionFromKnownLocation(name: string): string {
  return inferCountryFromLocationName(name) ?? "Live location";
}

function normalizeKind(value: string | null | undefined): string | null {
  if (!value) return null;
  const trimmed = value.trim().replace(/\s+/gu, " ");
  return trimmed ? trimmed[0]?.toUpperCase?.() + trimmed.slice(1).toLowerCase() : null;
}

function normalizeExactName(value: string | null | undefined): string | null {
  if (!value) return null;
  const normalized = value.trim().replace(/\s+/gu, " ").toLocaleLowerCase();
  return normalized || null;
}

type LiveLayerCategory = "regions" | "settlements" | "sites" | "other";

const LIVE_LAYER_CATEGORIES: ReadonlyArray<{
  category: LiveLayerCategory;
  label: string;
  order: number;
}> = [
  { category: "regions", label: "Regions", order: 1 },
  { category: "settlements", label: "Settlements", order: 2 },
  { category: "sites", label: "Sites & interiors", order: 3 },
  { category: "other", label: "Other places", order: 4 },
];

function liveLayerCategory(kind: string | null | undefined): LiveLayerCategory {
  switch (normalizeKind(kind)?.toLowerCase()) {
    case "region":
      return "regions";
    case "settlement":
      return "settlements";
    case "site":
    case "interior":
      return "sites";
    default:
      return "other";
  }
}

function liveLayerId(scope: MapScope, category: LiveLayerCategory): string {
  return `layer.live.${scope}.${category}`;
}

function liveLayersForFeatures(
  scope: MapScope,
  features: readonly MapFeature[],
): MapLayer[] {
  const presentLayerIds = new Set(features.map((feature) => feature.layerId));
  return LIVE_LAYER_CATEGORIES
    .map(({ category, label, order }) => ({
      id: liveLayerId(scope, category),
      kind: "markers" as const,
      order,
      label,
    }))
    .filter((layer) => presentLayerIds.has(layer.id));
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, "\\$&");
}

function findRegionInSummary(value: string | null, regionHints: readonly string[]): string | null {
  const normalized = normalizeRegion(value);
  if (!normalized || regionHints.length === 0) return null;
  const sortedHints = [...regionHints]
    .filter((hint) => typeof hint === "string" && hint.trim().length > 0)
    .sort((left, right) => right.length - left.length);
  return (
    sortedHints.find((hint) => {
      const escaped = escapeRegExp(hint);
      const pattern = new RegExp(`\\b${escaped}\\b`, "iu");
      return pattern.test(normalized);
    }) ?? null
  );
}

function inferCountryFromText(value: string | null, regionHints: readonly string[] = []): string | null {
  const normalized = normalizeRegion(value);
  if (!normalized) return null;

  const royalRegions = normalized.match(
    /\b(?:kingdom|province|realm|country|region|state)\s+of\s+([A-Z][A-Za-z][A-Za-z\s'’-]{1,80})/u,
  );
  if (royalRegions?.[1]) {
    return normalizeRegion(royalRegions[1]);
  }

  const inferred = findRegionInSummary(normalized, regionHints);
  if (inferred) return inferred;

  return null;
}

type DirectoryRegionInput = {
  id: string;
  name: string;
  summary: string | null;
  kind?: string | null;
  containerId?: string | null;
};

type DirectoryRegionMaps = {
  kindById: ReadonlyMap<string, string>;
  nameById: ReadonlyMap<string, string>;
  containerById: ReadonlyMap<string, string>;
};

function inferRegionFromDirectoryHierarchy(
  value: DirectoryRegionInput,
  maps: DirectoryRegionMaps,
): string | null {
  const normalizedKind = normalizeKind(value.kind);
  if (normalizedKind && maps.kindById.has(value.id) && normalizedKind.toLowerCase() === "region") {
    return value.name;
  }

  const seen = new Set<string>();
  let current = maps.containerById.get(value.id) ?? null;
  while (current) {
    if (seen.has(current)) break;
    seen.add(current);
    const currentKind = maps.kindById.get(current)?.toLowerCase();
    if (currentKind === "region") {
      const regionName = maps.nameById.get(current);
      return regionName ?? inferRegionFromDirectoryId(current);
    }
    current = maps.containerById.get(current) ?? null;
  }
  return null;
}

function buildDirectoryRegionMaps(values: readonly DirectoryRegionInput[]): DirectoryRegionMaps {
  const kindById = new Map<string, string>();
  const nameById = new Map<string, string>();
  const containerById = new Map<string, string>();

  for (const entry of values) {
    kindById.set(entry.id, normalizeKind(entry.kind) ?? "location");
    nameById.set(entry.id, entry.name);
    if (entry.containerId) {
      containerById.set(entry.id, entry.containerId);
    }
  }
  return { kindById, nameById, containerById };
}

function inferRegionFromDirectoryEntry(
  value: DirectoryRegionInput,
  regionHints: readonly string[],
  maps: DirectoryRegionMaps,
): string {
  if (maps.kindById.get(value.id)?.toLowerCase() === "region") {
    return value.name;
  }

  const fromHierarchy = inferRegionFromDirectoryHierarchy(value, maps);

  return (
    fromHierarchy
    ?? inferCountryFromText(value.summary, regionHints)
    ?? inferCountryFromLocationName(value.name)
    ?? inferRegionFromDirectoryId(value.id)
  );
}

/** The connected map tree is projected only from audience-safe live location records. */
type ConnectedLocationDirectoryEntry = NonNullable<ConnectedCampaignEnvelope["locationDirectory"]>[number];
type LiveDirectoryEntry = Omit<ConnectedLocationDirectoryEntry, "summary" | "containerId"> & {
  summary: string | null;
  containerId: string | null;
  sourceEntries: Array<{ text: string; stance: string; presentationKind: string }>;
};

const LIVE_MAP_SPACE_SIZE = 1000;

function validAnchor(value: LiveDirectoryEntry["mapAnchor"]): value is { x: number; y: number } {
  return !!value && Number.isInteger(value.x) && value.x >= 0 && value.x <= LIVE_MAP_SPACE_SIZE
    && Number.isInteger(value.y) && value.y >= 0 && value.y <= LIVE_MAP_SPACE_SIZE;
}

function resolvedMapBase(
  value: LiveDirectoryEntry,
): { imageUrl: string; alt: string; width?: number; height?: number } | null {
  if (!value.mapVisual || typeof value.mapVisual.imageUrl !== "string") return null;
  if (!value.mapVisual.imageUrl.startsWith("/api/applications/") ||
      !value.mapVisual.imageUrl.endsWith("/content")) return null;
  const dimensions = Number.isInteger(value.mapVisual.width) && value.mapVisual.width! > 0 &&
    Number.isInteger(value.mapVisual.height) && value.mapVisual.height! > 0
    ? { width: value.mapVisual.width, height: value.mapVisual.height }
    : {};
  return { imageUrl: value.mapVisual.imageUrl, alt: value.mapVisual.alt, ...dimensions };
}

function mapBaseState(value: LiveDirectoryEntry): MapBaseState {
  if (resolvedMapBase(value)) return "ready";
  return value.mapVisualState === "unavailable" || value.mapVisualState === "ready" || !!value.mapVisual
    ? "unavailable"
    : "absent";
}

function mapIdForLocation(locationId: string): string {
  return `map.live.${locationId}`;
}

function scopeForMap(value: LiveDirectoryEntry, isRoot: boolean): MapScope {
  if (isRoot) return "world";
  switch (value.kind?.toLowerCase()) {
    case "region": return "region";
    case "settlement": return "city";
    default: return "location";
  }
}

function buildLiveMapTree(
  entries: readonly LiveDirectoryEntry[],
  worldRootId: string,
  worldName: string,
): {
  mapOwnerId: string | null;
  rootMapId: string;
  maps: MapDocument[];
} {
  const byId = new Map(entries.map((entry) => [entry.id, entry]));
  const depthBelowWorld = (entry: LiveDirectoryEntry): number | null => {
    if (entry.id === worldRootId) return 0;
    const seen = new Set([entry.id]);
    let current: LiveDirectoryEntry | undefined = entry;
    let depth = 0;
    while (current?.containerId && depth <= entries.length) {
      depth += 1;
      if (current.containerId === worldRootId) return depth;
      if (seen.has(current.containerId)) return null;
      seen.add(current.containerId);
      current = byId.get(current.containerId);
    }
    return null;
  };
  const slotRank = (entry: LiveDirectoryEntry) =>
    /^(?:atlas|map)$/iu.test(entry.containmentSlot ?? "") ? 0 : 1;
  const rootOwner = entries
    .filter((entry) => depthBelowWorld(entry) !== null &&
      (slotRank(entry) === 0 || mapBaseState(entry) !== "absent"))
    .sort((left, right) =>
      (slotRank(left) - slotRank(right)) ||
      (depthBelowWorld(left)! - depthBelowWorld(right)!) ||
      left.name.localeCompare(right.name) || left.id.localeCompare(right.id))[0] ?? null;
  if (!rootOwner) {
    const rootEntry = byId.get(worldRootId);
    const rootMapId = mapIdForLocation(worldRootId);
    const unavailable = entries.some((entry) => entry.mapVisualState === "unavailable");
    return {
      mapOwnerId: null,
      rootMapId,
      maps: [{
        id: rootMapId,
        scope: "world",
        parentMapId: null,
        subject: { kind: "world", id: worldRootId, name: rootEntry?.name ?? worldName },
        coordinateSpace: {
          id: `space.live.${worldRootId}`,
          unit: "normalized",
          width: LIVE_MAP_SPACE_SIZE,
          height: LIVE_MAP_SPACE_SIZE,
        },
        baseState: unavailable ? "unavailable" : "absent",
        base: null,
        layers: [],
        features: [],
        scopeLinks: [],
      }],
    };
  }

  const maps: MapDocument[] = [];
  const visiting = new Set<string>();
  const visited = new Set<string>();
  const visit = (owner: LiveDirectoryEntry, parentMapId: string | null, isRoot: boolean) => {
    if (visiting.has(owner.id) || visited.has(owner.id)) return;
    const base = resolvedMapBase(owner);
    const children = entries
      .filter((entry) => entry.containerId === owner.id)
      .sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id));
    visiting.add(owner.id);
    const scope = scopeForMap(owner, isRoot);
    const mapId = mapIdForLocation(owner.id);
    const coordinateSpaceId = `space.live.${owner.id}`;
    const features: MapFeature[] = children.filter((child) => validAnchor(child.mapAnchor)).map((child) => {
      const preview = child.media?.setting ?? child.media?.scene ?? child.media?.portrait;
      return {
        id: `feature.live.${owner.id}.${child.id}`,
        kind: "point",
        layerId: liveLayerId(scope, liveLayerCategory(child.kind)),
        coordinateSpaceId,
        geometry: { x: child.mapAnchor!.x, y: child.mapAnchor!.y },
        name: child.name,
        detail: child.summary ?? `Known information about ${child.name}.`,
        locationId: child.id,
        ...(preview ? { preview } : {}),
      };
    });
    const document: MapDocument = {
      id: mapId,
      scope,
      parentMapId,
      subject: { kind: owner.kind ?? "location", id: owner.id, name: owner.name },
      coordinateSpace: {
        id: coordinateSpaceId,
        unit: "normalized",
        width: LIVE_MAP_SPACE_SIZE,
        height: LIVE_MAP_SPACE_SIZE,
      },
      baseState: mapBaseState(owner),
      base,
      layers: liveLayersForFeatures(scope, features),
      features,
      scopeLinks: [],
    };
    maps.push(document);
    for (const child of children) {
      visit(child, mapId, false);
      const childMapId = mapIdForLocation(child.id);
      if (!maps.some((candidate) => candidate.id === childMapId && candidate.parentMapId === mapId)) continue;
      document.scopeLinks.push({
        id: `scopelink.live.${owner.id}.${child.id}`,
        childMapId,
        childScope: scopeForMap(child, false),
        childName: child.name,
        viaFeatureId: validAnchor(child.mapAnchor)
          ? `feature.live.${owner.id}.${child.id}`
          : null,
      });
    }
    visiting.delete(owner.id);
    visited.add(owner.id);
  };
  visit(rootOwner, null, true);
  return { mapOwnerId: rootOwner.id, rootMapId: mapIdForLocation(rootOwner.id), maps };
}

function campaignDate(value: string | null | undefined): string {
  if (!value) return "Recorded campaign history";
  const isoDate = value.match(/^\d{4}-\d{2}-\d{2}/u)?.[0];
  return isoDate ?? "Recorded campaign history";
}

function displayStatus(value: string | null | undefined, fallback = "Unknown"): string {
  const normalized = normalizeSlugWords(value ?? null);
  return normalized ?? fallback;
}

function splitKnowledgeText(value: string, fallbackTitle: string): { title: string; detail: string } {
  const lines = value.split(/\r?\n/u).map((line) => line.trim()).filter(Boolean);
  if (lines.length === 0) return { title: fallbackTitle, detail: value };
  return {
    title: lines[0] ?? fallbackTitle,
    detail: lines.slice(1).join("\n\n") || lines[0] || value,
  };
}

const QUEST_SEED_TITLE = /^Q(\d{2})\s+[—-]\s+(.+)$/u;
const QUEST_SEED_LABEL = /(?:^|\s)(Hook|Layers|Objectives|Routes|Clues|Riddle|Creative constraints|Failure forward|Aftermath):\s*/gu;

function questSeedSections(value: string): Map<string, string> {
  const matches = [...value.matchAll(QUEST_SEED_LABEL)];
  const sections = new Map<string, string>();
  matches.forEach((match, index) => {
    const label = match[1];
    if (!label || match.index === undefined) return;
    const start = match.index + match[0].length;
    const end = matches[index + 1]?.index ?? value.length;
    const section = value.slice(start, end).trim();
    if (section) sections.set(label, section);
  });
  return sections;
}

function questSeedObjectives(value: string): Array<{ id: string; status: string; text: string }> {
  return [...value.matchAll(/(?:^|\s)([1-3])\.\s*(.*?)(?=\s+[1-3]\.\s|$)/gu)]
    .map((match) => ({
      id: `objective-${match[1]}`,
      status: "Prepared",
      text: match[2]?.trim() ?? "",
    }))
    .filter((objective) => objective.text.length > 0)
    .slice(0, 3);
}

function preparedQuestCards(
  entries: ConnectedCampaignEnvelope["knowledge"]["entries"],
  activeChapterTitle: string | null,
) {
  return entries.flatMap((entry, index) => {
    const content = splitKnowledgeText(entry.text, `Prepared adventure ${index + 1}`);
    const titleMatch = content.title.match(QUEST_SEED_TITLE);
    if (!titleMatch) return [];
    const sections = questSeedSections(content.detail);
    const objectives = questSeedObjectives(sections.get("Objectives") ?? "");
    const title = titleMatch[2]?.trim() || content.title;
    const active = activeChapterTitle === title;
    const hook = sections.get("Hook") ?? content.detail;
    return [{
      id: `live-quest-seed-${titleMatch[1]}`,
      sortOrder: active ? 1_000 : 100 - Number.parseInt(titleMatch[1] ?? String(index + 1), 10),
      kind: active ? "Opening adventure" : "Prepared adventure",
      status: active ? "Active" : "Prepared",
      title,
      summary: hook,
      nextStep: objectives[0]?.text ?? hook,
      objectives,
      links: { locations: [], people: [], factions: [] },
      dmContext: content.detail,
    }];
  }).sort((left, right) => right.sortOrder - left.sortOrder || left.title.localeCompare(right.title));
}


export function connectedCampaignToHubEnvelope(
  connection: ConnectedCampaignEnvelope,
): ReadyHubEnvelope {
  const perspective = connection.audience.perspective ?? connection.audience.seat;
  const hasLocationDirectory = Array.isArray(connection.locationDirectory)
    && (connection.locationDirectoryAudience === perspective
      || (perspective === "dm" && connection.audience.seat === "dm"))
    && connection.locationDirectory.length > 0;
  const knownLocations = connection.knowledge.status === "ready" ? connection.knowledge.locations : [];
  const knowledgeByName = new Map(knownLocations.flatMap((entry) => {
    const name = normalizeExactName(entry.name);
    return name ? [[name, entry.entries] as const] : [];
  }));
  const sourceLocations: LiveDirectoryEntry[] = hasLocationDirectory
    ? connection.locationDirectory!.map((entry) => {
      const knowledgeEntries = knowledgeByName.get(normalizeExactName(entry.name) ?? "") ?? [];
      const sourceEntries = [
        ...(entry.summary ? [{
          text: entry.summary,
          stance: "known",
          presentationKind: "statement",
        }] : []),
        ...knowledgeEntries,
      ].filter((candidate, index, values) =>
        values.findIndex((value) => value.text === candidate.text) === index);
      return {
        id: entry.id,
        name: entry.name,
        summary: entry.summary ?? null,
        sourceEntries,
        kind: entry.kind,
        isWorldRoot: entry.isWorldRoot,
        containerId: entry.containerId ?? null,
        containmentSlot: entry.containmentSlot,
        mapAnchor: entry.mapAnchor,
        mapVisualState: entry.mapVisualState,
        mapVisual: entry.mapVisual,
        media: entry.media,
        unavailableFields: entry.unavailableFields,
      };
    })
    : knownLocations.map((entry, index) => ({
      id: `live-location-${index + 1}`,
      name: entry.name,
      summary: null,
      containerId: null,
      sourceEntries: entry.entries,
    }));
  const locationEntries = sourceLocations.filter((entry) => !entry.isWorldRoot);
  const worldRootEntry = sourceLocations.find((entry) => entry.isWorldRoot) ?? null;
  const locationScopeRecords = (() => {
    const childrenByParent = new Map<string, string[]>();
    for (const entry of locationEntries) {
      if (!entry.containerId) continue;
      const childIds = childrenByParent.get(entry.containerId) ?? [];
      childIds.push(entry.id);
      childrenByParent.set(entry.containerId, childIds);
    }
    const byId = new Map(sourceLocations.map((entry) => [entry.id, entry]));
    const rootId = connection.contextSelection.selectedWorldId;
    const scopeIds = new Set([
      rootId,
      ...childrenByParent.keys(),
      ...(connection.locationDirectoryComplete === true ? locationEntries.map((entry) => entry.id) : []),
    ]);
    const derived = [...scopeIds].flatMap((id) => {
      const childIds = childrenByParent.get(id) ?? [];
      const owner = byId.get(id);
      if (!owner && id !== rootId) return [];
      return [{
        id,
        name: owner?.name ?? deriveWorldName(connection),
        parentId: owner?.containerId ?? null,
        childIds: childIds.sort((left, right) => left.localeCompare(right)),
        totalCount: childIds.length,
        complete: true,
        nextCursor: null,
        sourceRevisionFingerprint: null,
      }];
    });
    if (!Array.isArray(connection.locationScopes)) return derived;
    const explicit = connection.locationScopes.map((scope) => ({
      ...scope,
      childIds: [...scope.childIds],
    }));
    if (connection.locationDirectoryComplete !== true) return explicit;

    const explicitById = new Map(explicit.map((scope) => [scope.id, scope]));
    const complete = derived.map((scope) => {
      const loaded = explicitById.get(scope.id);
      if (!loaded) return scope;
      const sameChildren = loaded.complete && loaded.childIds.length === scope.childIds.length &&
        loaded.childIds.every((id) => scope.childIds.includes(id));
      return {
        ...scope,
        name: loaded.name,
        sourceRevisionFingerprint: sameChildren ? loaded.sourceRevisionFingerprint : null,
      };
    });
    const derivedIds = new Set(complete.map((scope) => scope.id));
    return [...complete, ...explicit.filter((scope) => !derivedIds.has(scope.id))];
  })();
  const hasSourceLocations = locationEntries.length > 0;
  const regionHints = hasLocationDirectory
    ? locationEntries
      .filter((entry) => normalizeKind(entry.kind)?.toLowerCase() === "region")
      .map((entry) => entry.name)
    : [];
  const directoryRegionMaps = hasLocationDirectory
    ? buildDirectoryRegionMaps(sourceLocations)
    : null;
  const liveMapTree = buildLiveMapTree(
    sourceLocations,
    connection.contextSelection.selectedWorldId,
    deriveWorldName(connection),
  );
  const rootMapId = liveMapTree.rootMapId;
  const liveKnowledgeOverlays: CampaignMapOverlay[] = (() => {
    // Only the registered, server-filtered party read can admit knowledge to DM Player view.
    // Retained legacy GM knowledge must never cross that perspective boundary.
    if (
      connection.knowledge.status !== "ready"
      || (connection.audience.seat === "dm" && perspective === "player" && connection.knowledge.audience !== "party")
    ) {
      return [];
    }

    const targetsByName = new Map<string, {
      locationIds: Set<string>;
      targets: Array<{ mapId: string; featureId: string }>;
    }>();
    const targetMaps = liveMapTree.maps.map((map) => ({ id: map.id, features: map.features }));
    for (const map of targetMaps) {
      for (const feature of map.features) {
        const name = normalizeExactName(feature.name);
        if (!name || !feature.locationId) continue;
        const group = targetsByName.get(name) ?? { locationIds: new Set<string>(), targets: [] };
        group.locationIds.add(feature.locationId);
        group.targets.push({ mapId: map.id, featureId: feature.id });
        targetsByName.set(name, group);
      }
    }

    const notesByName = new Map<string, { label: string; details: string[] }>();
    for (const location of knownLocations) {
      const name = normalizeExactName(location.name);
      if (!name) continue;
      const existing = notesByName.get(name) ?? { label: location.name.trim(), details: [] };
      for (const entry of location.entries) {
        const detail = entry.text.trim();
        if (detail && !existing.details.includes(detail)) existing.details.push(detail);
      }
      notesByName.set(name, existing);
    }

    return [...notesByName.entries()].flatMap(([name, notes]) => {
      const group = targetsByName.get(name);
      if (!group || group.locationIds.size !== 1 || notes.details.length === 0) return [];
      const detail = notes.details.join(" • ");
      return group.targets.map((target) => ({
        id: `overlay.live.knowledge.${target.featureId}`,
        mapId: target.mapId,
        featureId: target.featureId,
        kind: "note" as const,
        label: `${notes.label} knowledge`,
        detail,
        recordedOn: "Current campaign",
      }));
    });
  })();
  const baseWorldLocations = hasSourceLocations
    ? locationEntries.map((entry, index) => {
      const mapAnchor = validAnchor(entry.mapAnchor)
        ? { x: Math.round(entry.mapAnchor.x / 10), y: Math.round(entry.mapAnchor.y / 10) } : null;
      const notes = entry.sourceEntries.map((note) => note.text.trim()).filter(Boolean);
      const normalizedEntryKind = normalizeKind(entry.kind);
      const region = hasLocationDirectory
        ? inferRegionFromDirectoryEntry(entry, regionHints, directoryRegionMaps!)
        : inferRegionFromKnownLocation(entry.name);
      const label = hasLocationDirectory ? entry.id : `live-location-${index + 1}`;
      return {
        id: entry.id ?? label,
        parentId: entry.containerId,
        playerKnown: true,
        name: entry.name,
        region,
        kind: normalizedEntryKind ?? "Known place",
        status: "Known",
        summary: notes[0] ?? (entry.unavailableFields?.includes("summary") ? "Description unavailable." : `Known place: ${entry.name}.`),
        description: notes.join("\n\n") || (entry.unavailableFields?.includes("summary") ? "Description unavailable." : `Known place: ${entry.name}.`),
        atmosphere: "Observed from campaign knowledge.",
        landmarks: [],
        observations: notes.length
          ? notes
          : ["No additional campaign notes were recorded for this place."],
        routes: [],
        mapAnchor,
        ...(entry.unavailableFields ? { unavailableFields: entry.unavailableFields } : {}),
        people: [],
        ...(entry.media ? { media: entry.media } : {}),
      };
    })
    : [];
  const liveWorldDirectory = perspective === "dm" ? connection.worldDirectory : undefined;
  const baseLocationById = new Map(baseWorldLocations.map((location) => [location.id, location]));
  const worldPeople = (liveWorldDirectory?.people ?? []).flatMap((person) => {
    const location = baseLocationById.get(person.locationId);
    if (!location) return [];
    const unavailableFields = person.unavailableFields ?? [];
    const personName = person.name || "Name unavailable";
    const personKind = person.kind || "Unknown";
    const motiveUnavailable = unavailableFields.includes("motive");
    return [{
      id: person.id,
      initials: initials(personName),
      name: personName,
      kind: personKind,
      role: personKind === "Creature" ? "Recorded creature"
        : personKind === "NPC" ? "Recorded person" : "Classification unavailable",
      summary: person.motive?.summary ?? (motiveUnavailable
        ? "Motive unavailable."
        : `${personName} is recorded at ${location.name}.`),
      background: "Background unavailable.",
      disposition: motiveUnavailable ? "Unavailable"
        : person.motive ? displayStatus(person.motive.status) : "Not recorded",
      ...(person.media?.portrait ? { portrait: person.media.portrait } : {}),
      ...(person.motive ? { motive: person.motive.summary } : {}),
      ...(unavailableFields.length ? { unavailableFields } : {}),
      location: { id: location.id, name: location.name, region: location.region },
    }];
  });
  const peopleByLocation = new Map<string, typeof worldPeople>();
  for (const person of worldPeople) {
    const values = peopleByLocation.get(person.location.id) ?? [];
    values.push(person);
    peopleByLocation.set(person.location.id, values);
  }
  const holdingsByLocation = new Map<string, Array<{
    id: string;
    name: string;
    kind: string;
    status: string;
    summary: string;
    contents: never[];
    dmNote: string;
  }>>();
  for (const holding of liveWorldDirectory?.holdings ?? []) {
    if (!baseLocationById.has(holding.locationId)) continue;
    const values = holdingsByLocation.get(holding.locationId) ?? [];
    values.push({
      id: holding.id,
      name: holding.name,
      kind: displayStatus(holding.kind, "Holding"),
      status: "Recorded",
      summary: `${holding.name} is directly contained by this location in the live world.`,
      contents: [],
      dmNote: "No item contents have been projected for this holding.",
    });
    holdingsByLocation.set(holding.locationId, values);
  }
  const routeOriginId = connection.currentSituation?.status === "ready" &&
      connection.currentSituation.kind === "exploration"
    ? connection.currentSituation.locationId
    : null;
  const seenRouteIds = new Set<string>();
  const routesByLocation = new Map<string, Array<{ destination: string; detail: string }>>();
  for (const route of connection.knownRoutes ?? []) {
    if (seenRouteIds.has(route.id) || route.originId !== routeOriginId ||
        route.originId === route.destinationId || route.mode !== "on-foot" ||
        !Number.isInteger(route.durationMinutes) || route.durationMinutes < 1 ||
        route.durationMinutes > 1_440) continue;
    const origin = baseLocationById.get(route.originId);
    const destination = baseLocationById.get(route.destinationId);
    const detail = typeof route.detail === "string" ? route.detail.trim() : "";
    if (!origin || !destination || destination.name !== route.destinationName) continue;
    seenRouteIds.add(route.id);
    const values = routesByLocation.get(origin.id) ?? [];
    values.push({
      destination: destination.name,
      detail: `${detail || "Route details unavailable."} · On foot, ${route.durationMinutes} ${route.durationMinutes === 1 ? "minute" : "minutes"}.`,
    });
    routesByLocation.set(origin.id, values);
  }
  const worldLocations = baseWorldLocations.map((location) => ({
    ...location,
    routes: (routesByLocation.get(location.id) ?? []).sort((left, right) =>
      left.destination.localeCompare(right.destination) || left.detail.localeCompare(right.detail)),
    people: (peopleByLocation.get(location.id) ?? []).map(({ location: _, ...person }) => person),
    ...(perspective === "dm" ? { holdings: holdingsByLocation.get(location.id) ?? [] } : {}),
  }));
  const worldPersonById = new Map(worldPeople.map((person) => [person.id, person]));
  const factionById = new Map((liveWorldDirectory?.factions ?? []).map((faction) => [faction.id, faction]));
  const sovereignPowers = hasLocationDirectory
    ? sourceLocations
      .filter((location) => normalizeKind(location.kind)?.toLowerCase() === "region"
        && /\bkingdom of Thalorien\b/iu.test(location.summary ?? ""))
      .flatMap((location) => {
        const projected = baseLocationById.get(location.id);
        if (!projected || !location.summary) return [];
        return [{
          id: location.id,
          monogram: initials(location.name),
          name: location.name,
          kind: "Sovereign power" as const,
          influence: "Kingdom",
          status: "Recorded realm",
          summary: location.summary,
          goals: [],
          methods: [],
          assets: [],
          members: [],
          territories: [{ id: projected.id, name: projected.name, region: projected.region }],
          relationships: [],
        }];
      })
    : [];
  const organizations = (liveWorldDirectory?.factions ?? []).map((faction) => ({
    id: faction.id,
    monogram: initials(faction.name),
    name: faction.name,
    kind: "Organization" as const,
    influence: faction.territoryIds.length
      ? `${faction.territoryIds.length} recorded ${faction.territoryIds.length === 1 ? "territory" : "territories"}`
      : "No recorded territory",
    status: displayStatus(faction.status),
    summary: faction.summary,
    goals: faction.goals,
    methods: faction.methods,
    assets: faction.assets,
    members: faction.memberIds.flatMap((id) => {
      const person = worldPersonById.get(id);
      const reference = faction.memberReferences?.find((value) => value.id === id);
      return person ? [{ id: person.id, name: person.name, kind: person.kind }]
        : reference ? [{ ...reference, kind: "NPC" as const }] : [];
    }),
    territories: faction.territoryIds.flatMap((id) => {
      const location = baseLocationById.get(id);
      const reference = faction.territoryReferences?.find((value) => value.id === id);
      return location ? [{ id: location.id, name: location.name, region: location.region }]
        : reference ? [{ ...reference, region: "Recorded location" }] : [];
    }),
    relationships: [
      ...faction.alliedIds.map((id) => ({ id, stance: "Allied" })),
      ...faction.opposedIds.map((id) => ({ id, stance: "Opposed" })),
    ].flatMap((relationship) => {
      const target = factionById.get(relationship.id);
      const references = relationship.stance === "Allied" ? faction.alliedReferences : faction.opposedReferences;
      const reference = references?.find((value) => value.id === relationship.id);
      return target ? [{ id: target.id, name: target.name, stance: relationship.stance }]
        : reference ? [{ ...reference, stance: relationship.stance }] : [];
    }),
    dmAgenda: faction.agenda?.summary,
    ...(faction.unavailableFields?.length ? { unavailableFields: faction.unavailableFields } : {}),
  }));
  const worldFactions = [...sovereignPowers, ...organizations];
  const projectedFactionById = new Map(worldFactions.map((faction) => [faction.id, faction]));
  const campaignEntityLinks = (entityIds: readonly string[] = []) => {
    const locations: Array<{ id: string; name: string }> = [];
    const people: Array<{ id: string; name: string; kind: string }> = [];
    const factions: Array<{ id: string; name: string }> = [];
    for (const id of [...new Set(entityIds)]) {
      const location = baseLocationById.get(id);
      if (location) {
        locations.push({ id: location.id, name: location.name });
        continue;
      }
      const person = worldPersonById.get(id);
      if (person) {
        people.push({ id: person.id, name: person.name, kind: person.kind });
        continue;
      }
      const faction = projectedFactionById.get(id);
      if (faction) factions.push({ id: faction.id, name: faction.name });
    }
    return { locations, people, factions };
  };
  const currentLocationId = typeof connection.currentLocationId === "string"
    ? connection.currentLocationId
    : "";
  const currentSituation = connection.currentSituation
    ? connection.currentSituation
    : { status: "unavailable" as const,
      ...(currentLocationId ? { locationId: currentLocationId } : {}),
      message: "No authoritative current scene is available." };
  const worldName = deriveWorldName(connection);
  const contextSelection = connection.contextSelection;
  const knowledgeEntries = connection.knowledge.status === "ready" ? connection.knowledge.entries : [];
  const classifiedKnowledge = classifyThalorienKnowledge(
    knowledgeEntries.filter((entry) => entry.presentationKind !== "evidence"),
  );
  const knowledgeLore = [
    ...classifiedKnowledge.lore,
    ...classifiedKnowledge.history.map((entry) => ({
      id: `lore-${entry.id}`,
      title: entry.title,
      category: "World lore",
      status: entry.status,
      summary: entry.summary,
      body: [entry.summary, entry.consequence].filter(Boolean).join("\n\n"),
      linkedLocations: entry.linkedLocations,
      linkedPeople: entry.linkedPeople,
      linkedFactions: [],
      linkedHistory: [],
      ...(entry.admissions ? { admissions: entry.admissions } : {}),
    })),
  ];
  const chronologyHistory: WorldHistoryEvent[] = connection.chronology.status === "ready"
    ? connection.chronology.entries.map((entry) => {
      const subjects = entry.subjects ?? [];
      const linkedLocations = subjects.flatMap((subject) => {
        const location = baseLocationById.get(subject.id);
        return location ? [{ id: location.id, name: location.name }] : [];
      });
      const linkedPeople = subjects.flatMap((subject) => {
        const person = worldPersonById.get(subject.id);
        return person ? [{ id: person.id, name: person.name, kind: person.kind }] : [];
      });
      const firstLocation = linkedLocations.length > 0
        ? baseLocationById.get(linkedLocations[0]!.id)
        : null;
      return {
        id: entry.id,
        sortOrder: entry.occurredAtMinute,
        date: entry.dateLabel,
        era: displayStatus(entry.precision),
        title: entry.title,
        category: "World history",
        region: firstLocation?.region ?? "World",
        status: "Recorded",
        summary: entry.summary,
        linkedLocations,
        linkedPeople,
      };
    })
    : [];
  // Older connected envelopes predate field-local evidence. Their already-admitted values retain
  // the previous presentation semantics; new bootstraps carry explicit status instead of making
  // an absent or malformed field look authoritatively empty.
  const descriptiveFields = connection.campaign.descriptiveFields ?? {
    title: "ready",
    premise: connection.campaign.premise === null ? "empty" : "ready",
    partyGoals: connection.campaign.partyGoals.length === 0 ? "empty" : "ready",
    toneAndBoundaries: connection.campaign.toneAndBoundaries.length === 0 ? "empty" : "ready",
  } as const;
  const campaignGoals = connection.campaign.partyGoals;
  const premise = descriptiveFields.premise === "ready" && connection.campaign.premise
    ? connection.campaign.premise
    : descriptiveFields.premise === "empty"
      ? "No campaign premise has been recorded yet."
      : "Campaign premise is unavailable.";
  const partyGoalsAvailable = descriptiveFields.partyGoals === "ready" ||
    descriptiveFields.partyGoals === "partial" ||
    descriptiveFields.partyGoals === "empty";
  const partyObjective = partyGoalsAvailable
    ? campaignGoals[0] ?? "No party objective has been recorded yet."
    : "Party objectives are unavailable.";
  const chapters = connection.campaign.chapters ?? [];
  const arcs = connection.campaign.arcs ?? [];
  const sessions = perspective === "dm" ? connection.campaign.sessions ?? [] : [];
  const visits = perspective === "dm" ? connection.campaign.visits ?? [] : [];
  const campaignDetailFields: CampaignDetailFields = connection.campaign.detailFields ?? {
    chapters: detailStatus(connection.campaign, "chapters", chapters),
    arcs: detailStatus(connection.campaign, "arcs", arcs),
    sessions: detailStatus(connection.campaign, "sessions", sessions),
    visits: detailStatus(connection.campaign, "visits", visits),
  };
  const activeChapter = chapters.find((chapter) => chapter.status === "active") ?? null;
  const activeArc = arcs.find((arc) => arc.status === "active") ?? null;
  const preparedQuests = preparedQuestCards(knowledgeEntries, activeChapter?.title ?? null);
  const sessionAdventureLog = sessions
    .filter((session) => session.status === "ended" && session.recap)
    .map((session) => {
      const recap = session.recap!;
      const orderedMilestones = [...recap.milestones].sort(
        (left, right) => left.sequence - right.sequence || left.timestamp.localeCompare(right.timestamp),
      );
      return {
        id: `live-session-log-${session.ordinal}`,
        sortOrder: session.ordinal,
        session: `Session ${session.ordinal}`,
        date: campaignDate(orderedMilestones.at(-1)?.timestamp ?? session.updatedAtUtc),
        title: recap.chapter.title ?? "Chapter title unavailable",
        summary: recap.chapter.partyQuestion ?? "Chapter question unavailable.",
        result: orderedMilestones.length
          ? orderedMilestones.map((milestone) => milestone.closingSummary).join(" • ")
          : `The session ended with ${recap.arc.title ?? "an arc title unavailable"} still active.`,
        links: campaignEntityLinks(session.worldEntityIds),
      };
    });
  const chapterAdventureLog = chapters
    .filter((chapter) => chapter.status === "closed" && chapter.closingSummary)
    .map((chapter, index) => ({
      id: `live-chapter-log-${index + 1}`,
      sortOrder: index,
      session: `Chapter ${index + 1}`,
      date: campaignDate(chapter.updatedAtUtc ?? chapter.createdAtUtc),
      title: chapter.title ?? "Chapter title unavailable",
      summary: chapter.partyQuestion ?? "Chapter question unavailable.",
      result: chapter.closingSummary!,
      links: campaignEntityLinks(chapter.worldEntityIds),
      ...(chapter.gmContext ? { dmNote: chapter.gmContext } : {}),
    }));
  const campaignAdventureLog = sessionAdventureLog.length > 0
    ? sessionAdventureLog
    : chapterAdventureLog;
  const campaignOutcomes = arcs
    .filter((arc) => arc.status !== "active" && arc.closingSummary)
    .map((arc, index) => ({
      id: `live-arc-outcome-${index + 1}`,
      sortOrder: index,
      status: displayStatus(arc.status),
      title: arc.title ?? "Arc title unavailable",
      situation: arc.partyStake ?? "Arc stake unavailable.",
      result: arc.closingSummary!,
      consequence: `This campaign arc is recorded as ${arc.status}.`,
      links: campaignEntityLinks(arc.worldEntityIds),
      ...(arc.gmContext ? { dmRamification: arc.gmContext } : {}),
    }));
  const chapterThreads = chapters
    .filter((chapter) => chapter.status === "active")
    .map((chapter, index) => ({
      id: `live-chapter-thread-${index + 1}`,
      sortOrder: index,
      category: "Chapter question",
      status: "Active",
      pressure: "Current",
      title: chapter.title ?? "Chapter title unavailable",
      summary: chapter.partyQuestion ?? "Chapter question unavailable.",
      lastChanged: campaignDate(chapter.updatedAtUtc ?? chapter.createdAtUtc),
      links: { locations: [], people: [], factions: [] },
      ...(chapter.gmContext ? { dmTruth: chapter.gmContext } : {}),
    }));
  const arcThreads = arcs
    .filter((arc) => arc.status === "active")
    .map((arc, index) => ({
      id: `live-arc-thread-${index + 1}`,
      sortOrder: chapterThreads.length + index,
      category: "Campaign arc",
      status: "Active",
      pressure: "Long-term",
      title: arc.title ?? "Arc title unavailable",
      summary: arc.partyStake ?? "Arc stake unavailable.",
      lastChanged: campaignDate(arc.updatedAtUtc ?? arc.createdAtUtc),
      links: { locations: [], people: [], factions: [] },
      ...(arc.gmContext ? { dmTruth: arc.gmContext } : {}),
    }));
  const dmCampaignContext = perspective === "dm"
    ? [activeChapter?.gmContext, activeArc?.gmContext].filter(Boolean).join("\n\n")
    : "";
  const campaignClues = knowledgeEntries
    .filter((entry) => entry.presentationKind === "evidence")
    .map((entry, index) => {
      const content = splitKnowledgeText(entry.text, `Campaign evidence ${index + 1}`);
      return {
        id: entry.knowledgeId ?? entry.recognitionKey ?? `live-campaign-clue-${index + 1}`,
        sortOrder: index,
        mystery: "Campaign evidence",
        status: displayStatus(entry.stance, "Known"),
        title: content.title,
        detail: content.detail,
        partyConclusion: "No party conclusion has been recorded.",
        discoveredAt: "Current campaign knowledge",
        links: campaignEntityLinks(entry.subject ? [entry.subject.id] : []),
        ...(entry.media?.handout ? { handout: entry.media.handout } : {}),
        ...(entry.admissions ? { admissions: entry.admissions } : {}),
      };
    });
  const campaignVisits = visits.flatMap((visit) => {
    const location = baseLocationById.get(visit.locationId);
    if (!location) return [];
    return [{
      id: visit.id,
      location: { id: location.id, name: location.name, region: location.region },
      firstVisited: `Campaign minute ${visit.firstVisitedMinute}`,
      lastVisited: `Campaign minute ${visit.lastVisitedMinute}`,
      visitCount: visit.visitCount,
      status: displayStatus(visit.status),
      summary: visit.summary ?? "Visit summary unavailable.",
      memory: visit.memory ?? "Visit memory unavailable.",
      ...(visit.gmContext ? { dmContext: visit.gmContext } : {}),
    }];
  });
  const regionCounts = hasSourceLocations
    ? worldLocations.reduce((acc, location) => {
      acc.set(location.region, (acc.get(location.region) ?? 0) + 1);
      return acc;
    }, new Map<string, number>())
    : new Map<string, number>();
  const liveRegions = [...regionCounts.entries()].map(([name, count]) => ({
    name,
    detail: "Known campaign locations",
    count,
  }));
  const rootMap = liveMapTree.maps.find((map) => map.id === rootMapId) ?? liveMapTree.maps[0]!;
  const rootMapVisual = rootMap.base ?? { imageUrl: "", alt: "No reviewed map is available." };

  return {
    version: 1,
    status: "ready",
    applicationId: connection.applicationId,
    stateSpaceId: connection.stateSpaceId,
    revision: `live:${connection.applicationId}:${connection.stateSpaceId}:${connection.campaign.id}`,
    audience: {
      seat: connection.audience.seat,
      perspective,
      allowedPerspectives: connection.audience.allowedPerspectives,
      ...(connection.audience.websiteAccess === "shared" ? { websiteAccess: "shared" as const } : {}),
    },
    ...(connection.campaign.projection || connection.currentPlayProjection ? {
      objectQueries: { campaignSummary: connection.campaign.projection, currentPlay: connection.currentPlayProjection },
    } : {}),
    currentSituation,
    contextSelection,
    world: {
      id: contextSelection.selectedWorldId,
      name: worldName,
      era: "Live campaign",
      summary: worldRootEntry?.summary
        ?? "This world view is reading the campaign context currently available from the game server.",
      premise,
      currentLocationId,
      map: { ...rootMapVisual },
      mapOwnerId: liveMapTree.mapOwnerId,
      rootMapId,
      maps: liveMapTree.maps,
      regions: hasSourceLocations
        ? liveRegions
        : [],
      facts: [
        { label: "Campaign", value: connection.campaign.name, detail: "Current server-selected campaign" },
        {
          label: "Known places",
          value: String(worldLocations.length),
          detail: `Across ${liveRegions.length} inferred regions`,
        },
        {
          label: "Knowledge entries",
          value: String(knowledgeEntries.length),
          detail: "Player-safe campaign and world information",
        },
      ],
      history: chronologyHistory,
      historyCoverage: connection.chronology.coverage ?? "complete",
      locations: worldLocations,
      locationScopes: locationScopeRecords,
      people: worldPeople,
      ...(liveWorldDirectory ? {
        peopleDirectory: {
          totalCount: worldPeople.length,
          hierarchyComplete: liveWorldDirectory.peopleHierarchyComplete ?? true,
          coverage: liveWorldDirectory.peopleCoverage ?? "complete",
          sourceRevisionFingerprint: liveWorldDirectory.peopleSourceRevisionFingerprint ?? null,
        },
      } : {}),
      factions: worldFactions,
      lore: knowledgeLore,
      loreCoverage: connection.knowledge.coverage ?? "complete",
    },
    campaign: {
      title: connection.campaign.name,
      subtitle: "Connected live campaign",
      status: displayStatus(connection.campaign.status, "Active"),
      chapter: activeChapter?.title ?? detailFallback(campaignDetailFields.chapters,
        "No active chapter recorded", "Active chapter information unavailable."),
      question: activeChapter?.partyQuestion ?? detailFallback(campaignDetailFields.chapters,
        "No active chapter question has been recorded yet.", "Active chapter question unavailable."),
      premise,
      progress: chapters.length || arcs.length
        ? `${chapters.length} ${chapters.length === 1 ? "chapter" : "chapters"} · ${arcs.length} ${arcs.length === 1 ? "arc" : "arcs"}`
        : "Live campaign structure has not been recorded yet",
      objective: partyObjective,
      stakes: activeArc?.partyStake ?? detailFallback(campaignDetailFields.arcs,
        "No active campaign arc stake has been recorded yet.", "Active campaign arc stake unavailable."),
      nextMilestone: activeChapter?.partyQuestion
        ?? (partyGoalsAvailable ? campaignGoals[0] : undefined)
        ?? "No milestone has been recorded yet.",
      facts: [
        {
          label: "Current arc",
          value: activeArc?.title ?? "Not recorded",
          detail: activeArc ? displayStatus(activeArc.status) : "No active arc",
        },
        {
          label: "Campaign structure",
          value: `${chapters.length} / ${arcs.length}`,
          detail: "Chapters / arcs recorded by the live campaign",
        },
        {
          label: "Table role",
          value: connection.audience.seat === "dm" ? "Dungeon Master" : connection.actor.name,
          detail: connection.audience.seat === "dm"
            ? "Server-authorized local DM seat"
            : connection.actor.state ?? "Current player character",
        },
        {
          label: "Party goals",
          value: partyGoalsAvailable ? String(campaignGoals.length) : "Unavailable",
          detail: partyGoalsAvailable
            ? "Recorded in the campaign root"
            : "The campaign summary did not provide usable party goals.",
        },
      ],
      descriptiveFields,
      detailFields: campaignDetailFields,
      adventureLog: campaignAdventureLog,
      placesVisited: campaignVisits,
      outcomes: campaignOutcomes,
      mapOverlays: liveKnowledgeOverlays,
      quests: preparedQuests.length > 0 ? preparedQuests : campaignGoals.map((goal, index) => ({
        id: `live-goal-${index}`,
        sortOrder: index,
        kind: "Party goal",
        status: "Active",
        title: goal,
        summary: "Recorded in the current campaign context.",
        nextStep: goal,
        objectives: [],
        links: { locations: [], people: [], factions: [] },
      })),
      threads: [...chapterThreads, ...arcThreads],
      clues: campaignClues,
      cluesCoverage: connection.knowledge.status === "unavailable" ? "unavailable"
        : connection.knowledge.coverage === "partial" ? "partial" : "complete",
      ...(connection.knowledge.audience ? { knowledgeAudience: connection.knowledge.audience } : {}),
      ...(dmCampaignContext ? { dmContext: dmCampaignContext } : {}),
    },
    party: projectPartySummary(connection),
    rules: connection.rules ?? [],
  };
}

export function connectedCampaignToDeferredHubUpdate(
  connection: ConnectedCampaignEnvelope,
  section: DeferredHubSection,
): DeferredHubUpdate {
  const projected = connectedCampaignToHubEnvelope(connection);
  switch (section) {
    case "context":
      return { section, contextSelection: projected.contextSelection! };
    case "history":
      return { section, world: { history: projected.world.history, historyCoverage: projected.world.historyCoverage } };
    case "lore":
      return {
        section,
        world: { lore: projected.world.lore, loreCoverage: projected.world.loreCoverage },
        campaign: {
          quests: projected.campaign.quests,
          clues: projected.campaign.clues,
          cluesCoverage: projected.campaign.cluesCoverage,
          knowledgeAudience: projected.campaign.knowledgeAudience,
          mapOverlays: projected.campaign.mapOverlays,
        },
      };
    case "locations":
      return {
        section,
        world: {
          currentLocationId: projected.world.currentLocationId,
          map: projected.world.map,
          mapOwnerId: projected.world.mapOwnerId,
          rootMapId: projected.world.rootMapId,
          maps: projected.world.maps,
          regions: projected.world.regions,
          facts: projected.world.facts,
          locations: projected.world.locations,
          locationScopes: projected.world.locationScopes,
        },
        campaign: { mapOverlays: projected.campaign.mapOverlays },
      };
    case "people":
      return {
        section,
        world: {
          locations: projected.world.locations,
          people: projected.world.people,
          peopleDirectory: projected.world.peopleDirectory,
        },
      };
    case "current":
      return {
        section,
        currentSituation: projected.currentSituation!,
        ...(connection.currentPlayProjection ? { projection: connection.currentPlayProjection } : {}),
        world: {
          currentLocationId: projected.world.currentLocationId,
          locations: projected.world.locations,
        },
        campaign: { mapOverlays: projected.campaign.mapOverlays },
      };
  }
}

function detailStatus(
  campaign: ConnectedCampaignEnvelope["campaign"],
  key: keyof CampaignDetailFields,
  values: readonly unknown[],
): CampaignDetailFields[keyof CampaignDetailFields] {
  return campaign.detailFields?.[key] ?? (values.length > 0 ? "ready" : "absent");
}

function detailUnavailable(status: CampaignDetailFields[keyof CampaignDetailFields]): boolean {
  return status !== "ready" && status !== "empty";
}

function detailFallback(
  status: CampaignDetailFields[keyof CampaignDetailFields],
  empty: string,
  unavailable: string,
): string {
  return status === "empty" ? empty : detailUnavailable(status) ? unavailable : empty;
}

/**
 * Projects only one authorized location scope into the browser resource response.
 * The connected source may retain other visited scopes for admission and continuation,
 * but those unrelated records must not make each cached page grow with browsing history.
 */
export function connectedCampaignToWorldScopeUpdate(
  connection: ConnectedCampaignEnvelope,
  scopeId: string,
): Extract<DeferredHubUpdate, { section: "locations" }> & { scopePage: { id: string } } {
  const sourceScope = connection.locationScopes?.find((scope) => scope.id === scopeId);
  if (!sourceScope) throw new Error("The requested World scope was not projected.");
  const directory = connection.locationDirectory ?? [];
  const directoryById = new Map(directory.map((entry) => [entry.id, entry]));
  const pageIds = new Set([scopeId, ...sourceScope.childIds]);
  const projectionIds = new Set(pageIds);
  const worldId = connection.contextSelection.selectedWorldId;
  let ancestorId = directoryById.get(scopeId)?.containerId ?? null;
  const visitedAncestors = new Set<string>();
  while (ancestorId) {
    if (visitedAncestors.has(ancestorId) || visitedAncestors.size >= 16)
      throw new Error("The requested World scope hierarchy exceeds its supported bounds.");
    visitedAncestors.add(ancestorId);
    projectionIds.add(ancestorId);
    if (ancestorId === worldId) break;
    ancestorId = directoryById.get(ancestorId)?.containerId ?? null;
  }
  projectionIds.add(worldId);

  const scopedConnection: ConnectedCampaignEnvelope = {
    ...connection,
    locationDirectory: directory.filter((entry) => projectionIds.has(entry.id)),
    locationDirectoryComplete: false,
    locationScopes: [sourceScope],
    // People, holdings, Current routes, and knowledge overlays have independent owners.
    // A scope page must not copy their previously accumulated records into its cache entry.
    worldDirectory: undefined,
    knownRoutes: undefined,
    knowledge: { status: "empty", entries: [], locations: [] },
  };
  const projected = connectedCampaignToDeferredHubUpdate(scopedConnection, "locations");
  if (projected.section !== "locations") throw new Error("The World scope response is incompatible.");
  const pageScope = projected.world.locationScopes.find((scope) => scope.id === scopeId);
  if (!pageScope) throw new Error("The requested World scope metadata was not projected.");
  const maps = projected.world.maps.filter((map) =>
    pageIds.has(map.subject.id) || map.id === projected.world.rootMapId);
  const boundedMaps = maps.length > 0 ? maps : projected.world.maps.slice(0, 1);
  const mapIds = new Set(boundedMaps.map((map) => map.id));
  return {
    section: "locations",
    scopePage: { id: scopeId },
    world: {
      ...projected.world,
      maps: boundedMaps,
      regions: [],
      facts: [],
      locations: projected.world.locations.filter((location) => pageIds.has(location.id)),
      locationScopes: [pageScope],
    },
    campaign: {
      mapOverlays: projected.campaign.mapOverlays.filter((overlay) => mapIds.has(overlay.mapId)),
    },
  };
}
