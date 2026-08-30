import type {
  CampaignMapOverlay,
  ConnectedCampaignEnvelope,
  MapDocument,
  MapFeature,
  MapLayer,
  MapScope,
  PartyDossierEntry,
  PartyMemberReadModel,
  ReadyHubEnvelope,
} from "../data/hub-types";
import { resolveMapAssetUrl } from "../data/map-assets.ts";
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
  const idParts = connection.campaign.id?.split(".")?.filter(Boolean) ?? [];
  const worldFromId = idParts.length >= 3 && idParts[0] === "campaign"
    ? normalizeSlugWords(idParts[1])
    : null;
  return worldFromId ?? normalizeSlugWords(connection.campaign.id) ?? connection.campaign.name ?? "Unprojected world";
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

function partyEntries(
  member: ConnectedPartyMember,
  kinds: readonly string[],
): PartyDossierEntry[] {
  const allowedKinds = new Set(kinds);
  return member.entries
    .filter((entry) => allowedKinds.has(entry.kind))
    .map((entry) => ({
      id: `${member.id}:${entry.kind}:${entry.key}`,
      kind: entry.kind,
      title: entry.label,
      detail: entry.details ?? "No further detail has been recorded.",
    }));
}

function projectParty(connection: ConnectedCampaignEnvelope): PartyMemberReadModel[] {
  const members: ConnectedPartyMember[] = connection.party ?? [{
    ...connection.actor,
    current: true,
  }];
  const canAttachBoundKnowledge = connection.audience.seat === "player" &&
    connection.knowledge.status === "ready";

  return members.map((member) => {
    const sheet = partyEntries(member, ["class", "feature"]);
    const backstory = partyEntries(member, ["background", "note"]);
    const origin = partyEntries(member, ["class", "background"]);
    const inventory = partyEntries(member, ["equipment"]);
    const primaryDirection = origin[0]?.title ?? sheet[0]?.title ?? "Character details not yet recorded";
    return {
      id: member.id,
      initials: initials(member.name),
      name: member.name,
      detail: primaryDirection,
      status: member.state ? displayStatus(member.state) : "Active participant",
      isCurrent: member.current,
      recordStatus: member.entries.length > 0 ? "Provisional character record" : "Identity only",
      sheet,
      knowledge: canAttachBoundKnowledge && member.current
        ? connection.knowledge.entries.map((entry, index) => ({
          id: `${member.id}:knowledge:${index + 1}`,
          stance: displayStatus(entry.stance, "Known"),
          kind: displayStatus(entry.presentationKind, "Knowledge"),
          text: entry.text,
        }))
        : [],
      backstory,
      origin,
      inventory,
    };
  });
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
  assetBaseUrl = "/",
): { imageUrl: string; alt: string } | null {
  if (!value.mapVisual) return null;
  const imageUrl = resolveMapAssetUrl(value.mapVisual.assetKey, assetBaseUrl);
  return imageUrl ? { imageUrl, alt: value.mapVisual.alt } : null;
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

function buildLiveMapTree(entries: readonly LiveDirectoryEntry[], assetBaseUrl = "/"): {
  rootMapId: string;
  maps: MapDocument[];
} {
  const byId = new Map(entries.map((entry) => [entry.id, entry]));
  const mapOwners = new Map(entries
    .filter((entry) => resolvedMapBase(entry, assetBaseUrl) !== null)
    .map((entry) => [entry.id, entry]));
  const rootOwner = [...mapOwners.values()]
    .filter((entry) => !entry.containerId || !byId.has(entry.containerId) || !mapOwners.has(entry.containerId))
    .sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id))[0] ?? null;
  if (!rootOwner) {
    const rootMapId = "map.live.world.unavailable";
    return {
      rootMapId,
      maps: [{
        id: rootMapId,
        scope: "world",
        parentMapId: null,
        subject: { kind: "world", id: "world.unavailable", name: "Map unavailable" },
        coordinateSpace: {
          id: "space.live.world.unavailable",
          unit: "normalized",
          width: LIVE_MAP_SPACE_SIZE,
          height: LIVE_MAP_SPACE_SIZE,
        },
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
    const base = resolvedMapBase(owner, assetBaseUrl);
    if (!base) return;
    visiting.add(owner.id);
    const scope = scopeForMap(owner, isRoot);
    const mapId = mapIdForLocation(owner.id);
    const coordinateSpaceId = `space.live.${owner.id}`;
    const children = entries
      .filter((entry) => entry.containerId === owner.id && validAnchor(entry.mapAnchor))
      .sort((left, right) => left.name.localeCompare(right.name) || left.id.localeCompare(right.id));
    const features: MapFeature[] = children.map((child) => ({
      id: `feature.live.${owner.id}.${child.id}`,
      kind: "point",
      layerId: liveLayerId(scope, liveLayerCategory(child.kind)),
      coordinateSpaceId,
      geometry: { x: child.mapAnchor!.x, y: child.mapAnchor!.y },
      name: child.name,
      detail: child.summary ?? `Known information about ${child.name}.`,
      locationId: child.id,
    }));
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
      base,
      layers: liveLayersForFeatures(scope, features),
      features,
      scopeLinks: [],
    };
    maps.push(document);
    for (const child of children) {
      if (!mapOwners.has(child.id)) continue;
      visit(child, mapId, false);
      const childMapId = mapIdForLocation(child.id);
      if (!maps.some((candidate) => candidate.id === childMapId && candidate.parentMapId === mapId)) continue;
      document.scopeLinks.push({
        id: `scopelink.live.${owner.id}.${child.id}`,
        childMapId,
        childScope: scopeForMap(child, false),
        childName: child.name,
        viaFeatureId: `feature.live.${owner.id}.${child.id}`,
      });
    }
    visiting.delete(owner.id);
    visited.add(owner.id);
  };
  visit(rootOwner, null, true);
  return { rootMapId: mapIdForLocation(rootOwner.id), maps };
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


export function connectedCampaignToHubEnvelope(
  connection: ConnectedCampaignEnvelope,
  options: { assetBaseUrl?: string } = {},
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
        containerId: entry.containerId ?? null,
        containmentSlot: entry.containmentSlot,
        mapAnchor: entry.mapAnchor,
        mapVisual: entry.mapVisual,
      };
    })
    : knownLocations.map((entry, index) => ({
      id: `live-location-${index + 1}`,
      name: entry.name,
      summary: null,
      containerId: null,
      sourceEntries: entry.entries,
    }));
  const hasSourceLocations = sourceLocations.length > 0;
  const regionHints = hasLocationDirectory
    ? sourceLocations
      .filter((entry) => normalizeKind(entry.kind)?.toLowerCase() === "region")
      .map((entry) => entry.name)
    : [];
  const directoryRegionMaps = hasLocationDirectory
    ? buildDirectoryRegionMaps(sourceLocations)
    : null;
  const liveMapTree = buildLiveMapTree(sourceLocations, options.assetBaseUrl);
  const rootMapId = liveMapTree.rootMapId;
  const liveMapFeatures = liveMapTree.maps.find((map) => map.id === rootMapId)?.features ?? [];
  const liveKnowledgeOverlays: CampaignMapOverlay[] = (() => {
    // A DM's Player toggle is a local rehearsal over a GM-authorized server request. Until the
    // server can issue a perspective-bound knowledge read, emitting that knowledge in preview
    // could put GM notes into Player-shaped bytes, so this path fails closed.
    if (
      connection.knowledge.status !== "ready"
      || (connection.audience.seat === "dm" && perspective === "player")
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
    ? sourceLocations.map((entry, index) => {
      const x = validAnchor(entry.mapAnchor) ? Math.round(entry.mapAnchor.x / 10) : 0;
      const y = validAnchor(entry.mapAnchor) ? Math.round(entry.mapAnchor.y / 10) : 0;
      const notes = entry.sourceEntries.map((note) => note.text.trim()).filter(Boolean);
      const normalizedEntryKind = normalizeKind(entry.kind);
      const region = hasLocationDirectory
        ? inferRegionFromDirectoryEntry(entry, regionHints, directoryRegionMaps!)
        : inferRegionFromKnownLocation(entry.name);
      const label = hasLocationDirectory ? entry.id : `live-location-${index + 1}`;
      return {
        id: entry.id ?? label,
        playerKnown: true,
        name: entry.name,
        region,
        kind: normalizedEntryKind ?? "Known place",
        status: "Known",
        summary: notes[0] ?? `Known place: ${entry.name}.`,
        description: notes.join("\n\n") || `Known place: ${entry.name}.`,
        atmosphere: "Observed from campaign knowledge.",
        landmarks: [],
        observations: notes.length
          ? notes
          : ["No additional campaign notes were recorded for this place."],
        routes: [],
        mapAnchor: { x, y },
        people: [],
      };
    })
    : [{
      id: "live-current-location-unavailable",
      playerKnown: true,
      name: "Current location not recorded",
      region: "Live campaign context",
      kind: "Unprojected location",
      status: "Unavailable",
      summary: "The connected database has not supplied a current-location projection.",
      description: "A location will appear here once the game server records one for this campaign.",
      atmosphere: "Unavailable",
      landmarks: [],
      observations: ["No current location has been recorded."],
      routes: [],
      mapAnchor: { x: 0, y: 0 },
      people: [],
    }];
  const liveWorldDirectory = perspective === "dm" ? connection.worldDirectory : undefined;
  const baseLocationById = new Map(baseWorldLocations.map((location) => [location.id, location]));
  const worldPeople = (liveWorldDirectory?.people ?? []).flatMap((person) => {
    const location = baseLocationById.get(person.locationId);
    if (!location) return [];
    return [{
      id: person.id,
      initials: initials(person.name),
      name: person.name,
      kind: person.kind,
      role: person.kind === "Creature" ? "Recorded creature" : "Recorded person",
      summary: person.motive?.summary ?? `${person.name} is recorded at ${location.name}.`,
      background: "No background has been recorded.",
      disposition: person.motive ? displayStatus(person.motive.status) : "Not recorded",
      ...(person.motive ? { motive: person.motive.summary } : {}),
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
  const worldLocations = baseWorldLocations.map((location) => ({
    ...location,
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
      return person ? [{ id: person.id, name: person.name, kind: person.kind }] : [];
    }),
    territories: faction.territoryIds.flatMap((id) => {
      const location = baseLocationById.get(id);
      return location ? [{ id: location.id, name: location.name, region: location.region }] : [];
    }),
    relationships: [
      ...faction.alliedIds.map((id) => ({ id, stance: "Allied" })),
      ...faction.opposedIds.map((id) => ({ id, stance: "Opposed" })),
    ].flatMap((relationship) => {
      const target = factionById.get(relationship.id);
      return target ? [{ id: target.id, name: target.name, stance: relationship.stance }] : [];
    }),
    dmAgenda: faction.agenda.summary,
  }));
  const worldFactions = [...sovereignPowers, ...organizations];
  const currentLocationId = connection.currentLocationId && baseLocationById.has(connection.currentLocationId)
    ? connection.currentLocationId
    : "";
  const worldName = deriveWorldName(connection);
  const contextSelection = connection.contextSelection ?? {
    selectedWorldId: `world.${connection.campaign.id.split(".")[1] ?? "live"}`,
    selectedCampaignId: connection.campaign.id,
    worlds: [{
      id: `world.${connection.campaign.id.split(".")[1] ?? "live"}`,
      name: worldName,
      campaigns: [{ id: connection.campaign.id, name: connection.campaign.name }],
    }],
  };
  const knowledgeEntries = connection.knowledge.status === "ready" ? connection.knowledge.entries : [];
  const classifiedKnowledge = classifyThalorienKnowledge(
    knowledgeEntries.filter((entry) => entry.presentationKind !== "evidence"),
  );
  const campaignGoals = connection.campaign.partyGoals;
  const premise = connection.campaign.premise ?? "No campaign premise has been recorded yet.";
  const chapters = connection.campaign.chapters ?? [];
  const arcs = connection.campaign.arcs ?? [];
  const sessions = perspective === "dm" ? connection.campaign.sessions ?? [] : [];
  const activeChapter = chapters.find((chapter) => chapter.status === "active") ?? null;
  const activeArc = arcs.find((arc) => arc.status === "active") ?? null;
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
        title: recap.chapter.title,
        summary: recap.chapter.partyQuestion,
        result: orderedMilestones.length
          ? orderedMilestones.map((milestone) => milestone.closingSummary).join(" • ")
          : `The session ended with ${recap.arc.title} still active.`,
        links: { locations: [], people: [], factions: [] },
      };
    });
  const chapterAdventureLog = chapters
    .filter((chapter) => chapter.status === "closed" && chapter.closingSummary)
    .map((chapter, index) => ({
      id: `live-chapter-log-${index + 1}`,
      sortOrder: index,
      session: `Chapter ${index + 1}`,
      date: campaignDate(chapter.updatedAtUtc ?? chapter.createdAtUtc),
      title: chapter.title,
      summary: chapter.partyQuestion,
      result: chapter.closingSummary!,
      links: { locations: [], people: [], factions: [] },
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
      title: arc.title,
      situation: arc.partyStake,
      result: arc.closingSummary!,
      consequence: `This campaign arc is recorded as ${arc.status}.`,
      links: { locations: [], people: [], factions: [] },
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
      title: chapter.title,
      summary: chapter.partyQuestion,
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
      title: arc.title,
      summary: arc.partyStake,
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
        id: `live-campaign-clue-${index + 1}`,
        sortOrder: index,
        mystery: "Campaign evidence",
        status: displayStatus(entry.stance, "Known"),
        title: content.title,
        detail: content.detail,
        partyConclusion: "No party conclusion has been recorded.",
        discoveredAt: "Current campaign knowledge",
        links: { locations: [], people: [], factions: [] },
      };
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
  const legacyWorldMap = rootMap.base ?? { imageUrl: "", alt: "No reviewed map is available." };

  return {
    version: 1,
    status: "ready",
    revision: `live:${connection.applicationId}:${connection.stateSpaceId}:${connection.campaign.id}`,
    audience: {
      seat: connection.audience.seat,
      perspective,
      allowedPerspectives: connection.audience.allowedPerspectives,
    },
    contextSelection,
    world: {
      id: contextSelection.selectedWorldId,
      name: worldName,
      era: "Live campaign",
      summary: "This world view is reading the campaign context currently available from the game server.",
      premise,
      currentLocationId,
      map: { ...legacyWorldMap },
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
      history: classifiedKnowledge.history,
      locations: worldLocations,
      people: worldPeople,
      factions: worldFactions,
      lore: classifiedKnowledge.lore,
    },
    campaign: {
      title: connection.campaign.name,
      subtitle: "Connected live campaign",
      status: displayStatus(connection.campaign.status, "Active"),
      chapter: activeChapter?.title ?? "No active chapter recorded",
      question: activeChapter?.partyQuestion ?? "No active chapter question has been recorded yet.",
      premise,
      progress: chapters.length || arcs.length
        ? `${chapters.length} ${chapters.length === 1 ? "chapter" : "chapters"} · ${arcs.length} ${arcs.length === 1 ? "arc" : "arcs"}`
        : "Live campaign structure has not been recorded yet",
      objective: campaignGoals[0] ?? "No party objective has been recorded yet.",
      stakes: activeArc?.partyStake ?? "No active campaign arc stake has been recorded yet.",
      nextMilestone: activeChapter?.partyQuestion
        ?? campaignGoals[0]
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
        { label: "Party goals", value: String(campaignGoals.length), detail: "Recorded in the campaign root" },
      ],
      adventureLog: campaignAdventureLog,
      placesVisited: [],
      outcomes: campaignOutcomes,
      mapOverlays: liveKnowledgeOverlays,
      quests: campaignGoals.map((goal, index) => ({
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
      ...(dmCampaignContext ? { dmContext: dmCampaignContext } : {}),
    },
    party: projectParty(connection),
    rules: connection.rules ?? [],
  };
}
