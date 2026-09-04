export const VALID_PERSPECTIVES = ["player", "dm"];

export const MAIN_TABS = [
  { id: "world", label: "World", icon: "Map" },
  { id: "campaign", label: "Campaign", icon: "ScrollText" },
  { id: "party", label: "Party", icon: "UsersRound" },
  { id: "current", label: "Current View", icon: "Compass" },
  { id: "rules", label: "Rules", icon: "BookOpen" },
  { id: "content", label: "Installed Content", icon: "PackageOpen" },
];

export const CAMPAIGN_SECTIONS = [
  { id: "overview", label: "Overview" },
  { id: "log", label: "Adventure Log" },
  { id: "places", label: "Places Visited" },
  { id: "outcomes", label: "Outcomes" },
  { id: "quests", label: "Quests" },
  { id: "threads", label: "Open Threads" },
  { id: "clues", label: "Clues" },
];

export const WORLD_SECTIONS = [
  { id: "overview", label: "Overview" },
  { id: "map", label: "Map" },
  { id: "history", label: "History" },
  { id: "locations", label: "Locations" },
  { id: "people", label: "People" },
  { id: "factions", label: "Factions" },
  { id: "lore", label: "Lore" },
];

export const LOCATION_SECTIONS = [
  { id: "details", label: "Details" },
  { id: "people", label: "People & Creatures" },
  { id: "holdings", label: "Holdings", dmOnly: true },
];

export function normalizePerspective(value) {
  if (value === "client") {
    return "player";
  }

  return VALID_PERSPECTIVES.includes(value) ? value : "player";
}

export function normalizeMainTab(value) {
  return MAIN_TABS.some((tab) => tab.id === value) ? value : "world";
}

export function normalizeCampaignSection(value) {
  return CAMPAIGN_SECTIONS.some((section) => section.id === value) ? value : "overview";
}

export function normalizeWorldSection(value) {
  return WORLD_SECTIONS.some((section) => section.id === value) ? value : "overview";
}

export function normalizeLocationSection(value, perspective = "player") {
  const section = LOCATION_SECTIONS.find((candidate) => candidate.id === value);
  if (!section || (section.dmOnly && perspective !== "dm")) return "details";
  return section.id;
}

export function filterLocations(locations, query) {
  const normalizedQuery = String(query ?? "").trim().toLocaleLowerCase();
  const stableLocations = [...locations].sort((left, right) => left.name.localeCompare(right.name));

  if (!normalizedQuery) {
    return stableLocations;
  }

  return stableLocations.filter((location) =>
    [location.name, location.region, location.kind, location.summary]
      .filter(Boolean)
      .some((value) => value.toLocaleLowerCase().includes(normalizedQuery)),
  );
}

export function filterWorldHistory(
  events,
  { query = "", region = "all", category = "all", order = "newest" } = {},
) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  const filtered = events.filter((event) => {
    if (region !== "all" && event.region !== region) return false;
    if (category !== "all" && event.category !== category) return false;
    if (!normalizedQuery) return true;

    return [
      event.title,
      event.date,
      event.era,
      event.category,
      event.region,
      event.status,
      event.summary,
      event.consequence,
      ...event.linkedLocations.map((location) => location.name),
      ...event.linkedPeople.map((person) => person.name),
    ]
      .filter(Boolean)
      .some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  });

  const direction = order === "oldest" ? 1 : -1;
  return [...filtered].sort(
    (left, right) =>
      (left.sortOrder - right.sortOrder) * direction || left.title.localeCompare(right.title),
  );
}

export function filterWorldPeople(
  people,
  { query = "", kind = "all", region = "all" } = {},
) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...people]
    .filter((person) => {
      if (kind !== "all" && person.kind !== kind) return false;
      if (region !== "all" && person.location.region !== region) return false;
      if (!normalizedQuery) return true;
      return [
        person.name,
        person.kind,
        person.role,
        person.summary,
        person.background,
        person.disposition,
        person.location.name,
        person.location.region,
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort((left, right) => left.name.localeCompare(right.name));
}

export function filterWorldFactions(
  factions,
  { query = "", influence = "all" } = {},
) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...factions]
    .filter((faction) => {
      if (influence !== "all" && faction.influence !== influence) return false;
      if (!normalizedQuery) return true;
      return [
        faction.name,
        faction.kind ?? "Organization",
        faction.influence,
        faction.status,
        faction.summary,
        ...faction.goals,
        ...faction.methods,
        ...faction.members.map((member) => member.name),
        ...faction.territories.map((territory) => territory.name),
        ...faction.relationships.flatMap((relationship) => [relationship.name, relationship.stance]),
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort((left, right) => left.name.localeCompare(right.name));
}

export function filterWorldLore(
  entries,
  { query = "", category = "all", status = "all" } = {},
) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...entries]
    .filter((entry) => {
      if (category !== "all" && entry.category !== category) return false;
      if (status !== "all" && entry.status !== status) return false;
      if (!normalizedQuery) return true;
      return [
        entry.title,
        entry.category,
        entry.status,
        entry.summary,
        entry.body,
        ...entry.linkedLocations.map((location) => location.name),
        ...entry.linkedPeople.map((person) => person.name),
        ...entry.linkedFactions.map((faction) => faction.name),
        ...entry.linkedHistory.map((event) => event.title),
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort((left, right) => left.title.localeCompare(right.title));
}

export function filterCampaignLog(entries, { query = "", order = "newest" } = {}) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  const filtered = entries.filter((entry) => {
    if (!normalizedQuery) return true;
    return [
      entry.session,
      entry.date,
      entry.title,
      entry.summary,
      entry.result,
      ...entry.links.locations.map((location) => location.name),
      ...entry.links.people.map((person) => person.name),
      ...entry.links.factions.map((faction) => faction.name),
    ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
  });
  const direction = order === "oldest" ? 1 : -1;
  return [...filtered].sort(
    (left, right) =>
      (left.sortOrder - right.sortOrder) * direction || left.title.localeCompare(right.title),
  );
}

export function filterCampaignPlaces(
  places,
  { query = "", region = "all" } = {},
) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...places]
    .filter((place) => {
      if (region !== "all" && place.location.region !== region) return false;
      if (!normalizedQuery) return true;
      return [
        place.location.name,
        place.location.region,
        place.status,
        place.summary,
        place.memory,
        place.firstVisited,
        place.lastVisited,
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort(
      (left, right) =>
        right.visitCount - left.visitCount || left.location.name.localeCompare(right.location.name),
    );
}

export function filterCampaignOutcomes(
  outcomes,
  { query = "", status = "all" } = {},
) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...outcomes]
    .filter((outcome) => {
      if (status !== "all" && outcome.status !== status) return false;
      if (!normalizedQuery) return true;
      return [
        outcome.status,
        outcome.title,
        outcome.situation,
        outcome.result,
        outcome.consequence,
        ...outcome.links.locations.map((location) => location.name),
        ...outcome.links.people.map((person) => person.name),
        ...outcome.links.factions.map((faction) => faction.name),
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort(
      (left, right) =>
        right.sortOrder - left.sortOrder || left.title.localeCompare(right.title),
    );
}

export function filterCampaignQuests(quests, { query = "", status = "all", kind = "all" } = {}) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...quests]
    .filter((quest) => {
      if (status !== "all" && quest.status !== status) return false;
      if (kind !== "all" && quest.kind !== kind) return false;
      if (!normalizedQuery) return true;
      return [
        quest.kind,
        quest.status,
        quest.title,
        quest.summary,
        quest.nextStep,
        ...quest.objectives.flatMap((objective) => [objective.status, objective.text]),
        ...quest.links.locations.map((location) => location.name),
        ...quest.links.people.map((person) => person.name),
        ...quest.links.factions.map((faction) => faction.name),
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort((left, right) => right.sortOrder - left.sortOrder || left.title.localeCompare(right.title));
}

export function filterCampaignThreads(threads, { query = "", status = "all", category = "all" } = {}) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...threads]
    .filter((thread) => {
      if (status !== "all" && thread.status !== status) return false;
      if (category !== "all" && thread.category !== category) return false;
      if (!normalizedQuery) return true;
      return [
        thread.category,
        thread.status,
        thread.pressure,
        thread.title,
        thread.summary,
        thread.lastChanged,
        ...thread.links.locations.map((location) => location.name),
        ...thread.links.people.map((person) => person.name),
        ...thread.links.factions.map((faction) => faction.name),
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort((left, right) => right.sortOrder - left.sortOrder || left.title.localeCompare(right.title));
}

export function filterCampaignClues(clues, { query = "", mystery = "all", status = "all" } = {}) {
  const normalizedQuery = String(query).trim().toLocaleLowerCase();
  return [...clues]
    .filter((clue) => {
      if (mystery !== "all" && clue.mystery !== mystery) return false;
      if (status !== "all" && clue.status !== status) return false;
      if (!normalizedQuery) return true;
      return [
        clue.mystery,
        clue.status,
        clue.title,
        clue.detail,
        clue.partyConclusion,
        clue.discoveredAt,
        ...clue.links.locations.map((location) => location.name),
        ...clue.links.people.map((person) => person.name),
        ...clue.links.factions.map((faction) => faction.name),
      ].some((value) => value.toLocaleLowerCase().includes(normalizedQuery));
    })
    .sort((left, right) => right.sortOrder - left.sortOrder || left.title.localeCompare(right.title));
}

export const MAP_SCOPES = ["world", "region", "city", "location"];

export function isGeometryInCoordinateSpace(geometry, space) {
  return (
    Boolean(geometry) &&
    Boolean(space) &&
    Number.isFinite(space.width) &&
    Number.isFinite(space.height) &&
    space.width > 0 &&
    space.height > 0 &&
    Number.isFinite(geometry.x) &&
    Number.isFinite(geometry.y) &&
    geometry.x >= 0 &&
    geometry.y >= 0 &&
    geometry.x <= space.width &&
    geometry.y <= space.height
  );
}

export function resolveMapDocument(maps, mapId) {
  return maps.find((map) => map.id === mapId) ?? null;
}

export function resolveRootMapId(maps, rootMapId) {
  const declared = resolveMapDocument(maps, rootMapId);
  if (declared && declared.parentMapId === null) return declared.id;
  return maps.find((map) => map.parentMapId === null)?.id ?? "";
}

export function normalizeMapId(maps, value, rootMapId) {
  return resolveMapDocument(maps, value) ? value : resolveRootMapId(maps, rootMapId);
}

/**
 * Breadcrumbs are derived only from declared parent links, ordered root to current.
 * An unknown ancestor or a cycle yields no trail at all rather than a partial one.
 */
export function buildMapBreadcrumbs(maps, mapId) {
  const trail = [];
  const seen = new Set();
  let current = resolveMapDocument(maps, mapId);

  while (current) {
    if (seen.has(current.id)) return [];
    seen.add(current.id);
    trail.unshift({ id: current.id, name: current.subject.name, scope: current.scope });
    if (current.parentMapId === null) return trail;
    const parent = resolveMapDocument(maps, current.parentMapId);
    if (!parent) return [];
    current = parent;
  }

  return [];
}

export function resolveMapChildScopes(maps, mapId) {
  const map = resolveMapDocument(maps, mapId);
  if (!map) return [];
  return map.scopeLinks
    .filter((link) => resolveMapDocument(maps, link.childMapId))
    .map((link) => ({
      id: link.id,
      mapId: link.childMapId,
      name: link.childName,
      scope: link.childScope,
      viaFeatureId: link.viaFeatureId,
    }));
}

export function resolveSelectedMapFeature(map, featureId) {
  if (!map) return null;
  return map.features.find((feature) => feature.id === featureId) ?? null;
}

export function filterMapFeaturesByLayers(map, visibleLayerIds) {
  if (!map || !Array.isArray(map.features)) return [];
  const visible = visibleLayerIds instanceof Set
    ? visibleLayerIds
    : new Set(Array.isArray(visibleLayerIds) ? visibleLayerIds : []);
  return map.features.filter((feature) => visible.has(feature.layerId));
}

export function searchMapFeatures(maps, query, activeMapId = "") {
  const normalizedQuery = String(query ?? "").trim().toLocaleLowerCase();
  if (!normalizedQuery || !Array.isArray(maps)) return [];

  const scopeRank = { world: 0, region: 1, city: 2, location: 3 };
  const candidates = [];
  maps.forEach((map, mapIndex) => {
    if (!map || !Array.isArray(map.features)) return;
    map.features.forEach((feature, featureIndex) => {
      if (![feature.name, feature.detail]
        .filter((value) => typeof value === "string")
        .some((value) => value.toLocaleLowerCase().includes(normalizedQuery))) return;
      candidates.push({
        mapId: map.id,
        mapName: map.subject.name,
        mapScope: map.scope,
        featureId: feature.id,
        locationId: feature.locationId,
        name: feature.name,
        detail: feature.detail,
        _mapIndex: mapIndex,
        _featureIndex: featureIndex,
      });
    });
  });

  const winners = new Map();
  for (const candidate of candidates) {
    const key = candidate.locationId ?? `${candidate.mapId}:${candidate.featureId}`;
    const current = winners.get(key);
    const candidateActive = candidate.mapId === activeMapId;
    const currentActive = current?.mapId === activeMapId;
    const candidateRank = scopeRank[candidate.mapScope] ?? -1;
    const currentRank = current ? (scopeRank[current.mapScope] ?? -1) : -1;
    if (
      !current
      || (candidateActive && !currentActive)
      || (candidateActive === currentActive && candidateRank > currentRank)
    ) {
      winners.set(key, candidate);
    }
  }

  return [...winners.values()]
    .sort((left, right) =>
      left.name.localeCompare(right.name)
      || left._mapIndex - right._mapIndex
      || left._featureIndex - right._featureIndex)
    .map(({ _mapIndex, _featureIndex, ...result }) => result);
}

export function resolveMapFactionInfluences(factions, map) {
  if (!Array.isArray(factions) || !map || !Array.isArray(map.features)) return [];
  return factions.flatMap((faction) => {
    if (!faction || !Array.isArray(faction.territories)) return [];
    const territoryIds = new Set(
      faction.territories
        .map((territory) => territory?.id)
        .filter((id) => typeof id === "string"),
    );
    const featureIds = map.features
      .filter((feature) => feature.locationId !== null && territoryIds.has(feature.locationId))
      .map((feature) => feature.id);
    return featureIds.length === 0 ? [] : [{
      factionId: faction.id,
      name: faction.name,
      influence: faction.influence,
      featureIds,
    }];
  });
}

export function groupMapFeaturesByLayers(map) {
  if (!map || !Array.isArray(map.layers) || !Array.isArray(map.features)) return [];
  return [...map.layers]
    .sort((left, right) => left.order - right.order || left.id.localeCompare(right.id))
    .map((layer) => ({
      layer,
      features: map.features.filter((feature) => feature.layerId === layer.id),
    }))
    .filter((group) => group.features.length > 0);
}

function isMapFeature(value, space, layerIds) {
  const preview = value?.preview;
  return (
    value &&
    typeof value.id === "string" &&
    typeof value.kind === "string" &&
    typeof value.layerId === "string" &&
    layerIds.has(value.layerId) &&
    typeof value.name === "string" &&
    (value.locationId === null || typeof value.locationId === "string") &&
    (preview === undefined || (
      preview &&
      typeof preview.imageUrl === "string" &&
      typeof preview.alt === "string" &&
      Number.isInteger(preview.width) && preview.width > 0 &&
      Number.isInteger(preview.height) && preview.height > 0
    )) &&
    isGeometryInCoordinateSpace(value.geometry, space)
  );
}

function isMapDocument(value) {
  return (
    value &&
    typeof value.id === "string" &&
    MAP_SCOPES.includes(value.scope) &&
    (value.parentMapId === null || typeof value.parentMapId === "string") &&
    value.subject &&
    typeof value.subject.name === "string" &&
    value.coordinateSpace &&
    typeof value.coordinateSpace.id === "string" &&
    Number.isFinite(value.coordinateSpace.width) &&
    Number.isFinite(value.coordinateSpace.height) &&
    value.coordinateSpace.width > 0 &&
    value.coordinateSpace.height > 0 &&
    (value.base === null ||
      (value.base &&
        typeof value.base.imageUrl === "string" &&
        typeof value.base.alt === "string")) &&
    Array.isArray(value.layers) &&
    value.layers.every(
      (layer) =>
        layer && typeof layer.id === "string" && typeof layer.kind === "string" && Number.isFinite(layer.order),
    ) &&
    Array.isArray(value.features) &&
    value.features.every((feature) =>
      isMapFeature(feature, value.coordinateSpace, new Set(value.layers.map((layer) => layer.id))),
    ) &&
    Array.isArray(value.scopeLinks) &&
    value.scopeLinks.every(
      (link) =>
        link &&
        typeof link.id === "string" &&
        typeof link.childMapId === "string" &&
        typeof link.childName === "string" &&
        MAP_SCOPES.includes(link.childScope) &&
        (link.viaFeatureId === null || typeof link.viaFeatureId === "string"),
    )
  );
}

export function isValidMapHierarchy(maps, rootMapId) {
  if (!Array.isArray(maps) || maps.length === 0) return false;
  if (!maps.every(isMapDocument)) return false;

  const byId = new Map(maps.map((map) => [map.id, map]));
  if (byId.size !== maps.length) return false;

  const root = byId.get(rootMapId);
  if (!root || root.scope !== "world" || root.parentMapId !== null) return false;

  return maps.every((map) => {
    if (map.scopeLinks.some((link) => !byId.has(link.childMapId))) return false;
    if (
      map.scopeLinks.some(
        (link) => link.viaFeatureId !== null && !map.features.some((feature) => feature.id === link.viaFeatureId),
      )
    ) {
      return false;
    }
    return buildMapBreadcrumbs(maps, map.id).length > 0;
  });
}

export const MAP_OVERLAY_KINDS = ["note", "reveal"];

export function resolveMapOverlays(overlays, mapId) {
  if (!Array.isArray(overlays)) return [];
  return overlays.filter((overlay) => overlay.mapId === mapId);
}

export function resolveFeatureOverlays(overlays, mapId, featureId) {
  return resolveMapOverlays(overlays, mapId).filter((overlay) => overlay.featureId === featureId);
}

function isMapOverlay(value) {
  return (
    value &&
    typeof value.id === "string" &&
    typeof value.mapId === "string" &&
    (value.featureId === null || typeof value.featureId === "string") &&
    MAP_OVERLAY_KINDS.includes(value.kind) &&
    typeof value.label === "string" &&
    typeof value.detail === "string" &&
    typeof value.recordedOn === "string" &&
    // A campaign annotation points at World geography; it never carries any of its own.
    value.geometry === undefined &&
    value.coordinateSpaceId === undefined &&
    value.layerId === undefined &&
    value.base === undefined
  );
}

/** Every projected overlay must resolve to a projected map, and to a projected feature when it names one. */
export function overlaysResolveAgainstMaps(overlays, maps) {
  if (!Array.isArray(overlays) || !overlays.every(isMapOverlay)) return false;
  const mapById = new Map(maps.map((map) => [map.id, map]));
  return overlays.every((overlay) => {
    const map = mapById.get(overlay.mapId);
    if (!map) return false;
    return (
      overlay.featureId === null ||
      map.features.some((feature) => feature.id === overlay.featureId)
    );
  });
}

export function resolveSelectedLocation(locations, selectedLocationId, currentLocationId) {
  return (
    locations.find((location) => location.id === selectedLocationId) ??
    locations.find((location) => location.id === currentLocationId) ??
    locations[0] ??
    null
  );
}

export function resolveCurrentSceneLocation(locations, currentLocationId) {
  if (typeof currentLocationId !== "string" || currentLocationId.length === 0) return null;
  return locations.find((location) => location.id === currentLocationId) ?? null;
}

function isHistoryLocationLink(value) {
  return value && typeof value.id === "string" && typeof value.name === "string";
}

function isHistoryPersonLink(value) {
  return (
    isHistoryLocationLink(value) &&
    (value.kind === "NPC" || value.kind === "Creature")
  );
}

function isLocationPerson(value) {
  return (
    value &&
    typeof value.id === "string" &&
    typeof value.initials === "string" &&
    typeof value.name === "string" &&
    (value.kind === "NPC" || value.kind === "Creature") &&
    typeof value.role === "string" &&
    typeof value.summary === "string" &&
    typeof value.background === "string" &&
    typeof value.disposition === "string" &&
    (value.motive === undefined || typeof value.motive === "string") &&
    (value.dmSecret === undefined || typeof value.dmSecret === "string")
  );
}

function isWorldPersonEntry(value) {
  return (
    isLocationPerson(value) &&
    isHistoryLocationLink(value.location) &&
    typeof value.location.region === "string"
  );
}

function isWorldFaction(value) {
  return (
    value &&
    typeof value.id === "string" &&
    typeof value.monogram === "string" &&
    typeof value.name === "string" &&
    typeof value.influence === "string" &&
    typeof value.status === "string" &&
    typeof value.summary === "string" &&
    Array.isArray(value.goals) &&
    value.goals.every((goal) => typeof goal === "string") &&
    Array.isArray(value.methods) &&
    value.methods.every((method) => typeof method === "string") &&
    Array.isArray(value.members) &&
    value.members.every(isHistoryPersonLink) &&
    Array.isArray(value.territories) &&
    value.territories.every(
      (territory) => isHistoryLocationLink(territory) && typeof territory.region === "string",
    ) &&
    Array.isArray(value.relationships) &&
    value.relationships.every(
      (relationship) =>
        isHistoryLocationLink(relationship) && typeof relationship.stance === "string",
    ) &&
    (value.dmAgenda === undefined || typeof value.dmAgenda === "string") &&
    (value.dmSecret === undefined || typeof value.dmSecret === "string")
  );
}

function isWorldLoreEntry(value) {
  return (
    value &&
    typeof value.id === "string" &&
    typeof value.title === "string" &&
    typeof value.category === "string" &&
    typeof value.status === "string" &&
    typeof value.summary === "string" &&
    typeof value.body === "string" &&
    Array.isArray(value.linkedLocations) &&
    value.linkedLocations.every(isHistoryLocationLink) &&
    Array.isArray(value.linkedPeople) &&
    value.linkedPeople.every(isHistoryPersonLink) &&
    Array.isArray(value.linkedFactions) &&
    value.linkedFactions.every(isHistoryLocationLink) &&
    Array.isArray(value.linkedHistory) &&
    value.linkedHistory.every(
      (event) =>
        event &&
        typeof event.id === "string" &&
        typeof event.title === "string" &&
        typeof event.date === "string",
    ) &&
    (value.dmTruth === undefined || typeof value.dmTruth === "string") &&
    (value.dmNote === undefined || typeof value.dmNote === "string")
  );
}

function isWorldHistoryEvent(event) {
  return (
    event &&
    typeof event.id === "string" &&
    Number.isFinite(event.sortOrder) &&
    typeof event.date === "string" &&
    typeof event.era === "string" &&
    typeof event.title === "string" &&
    typeof event.category === "string" &&
    typeof event.region === "string" &&
    typeof event.status === "string" &&
    typeof event.summary === "string" &&
    (event.consequence === undefined || typeof event.consequence === "string") &&
    Array.isArray(event.linkedLocations) &&
    event.linkedLocations.every(isHistoryLocationLink) &&
    Array.isArray(event.linkedPeople) &&
    event.linkedPeople.every(isHistoryPersonLink) &&
    (event.dmTruth === undefined || typeof event.dmTruth === "string") &&
    (event.dmConsequence === undefined || typeof event.dmConsequence === "string")
  );
}

function isCampaignLinks(value) {
  return (
    value &&
    Array.isArray(value.locations) &&
    value.locations.every(isHistoryLocationLink) &&
    Array.isArray(value.people) &&
    value.people.every(isHistoryPersonLink) &&
    Array.isArray(value.factions) &&
    value.factions.every(isHistoryLocationLink)
  );
}

function isCampaignQuest(value) {
  return (
    value &&
    typeof value.id === "string" &&
    Number.isFinite(value.sortOrder) &&
    typeof value.kind === "string" &&
    typeof value.status === "string" &&
    typeof value.title === "string" &&
    typeof value.summary === "string" &&
    typeof value.nextStep === "string" &&
    Array.isArray(value.objectives) &&
    value.objectives.every(
      (objective) =>
        objective &&
        typeof objective.id === "string" &&
        typeof objective.status === "string" &&
        typeof objective.text === "string",
    ) &&
    isCampaignLinks(value.links) &&
    (value.dmContext === undefined || typeof value.dmContext === "string")
  );
}

function isCampaignThread(value) {
  return (
    value &&
    typeof value.id === "string" &&
    Number.isFinite(value.sortOrder) &&
    typeof value.category === "string" &&
    typeof value.status === "string" &&
    typeof value.pressure === "string" &&
    typeof value.title === "string" &&
    typeof value.summary === "string" &&
    typeof value.lastChanged === "string" &&
    isCampaignLinks(value.links) &&
    (value.dmTruth === undefined || typeof value.dmTruth === "string") &&
    (value.dmReveal === undefined || typeof value.dmReveal === "string")
  );
}

function isCampaignClue(value) {
  return (
    value &&
    typeof value.id === "string" &&
    Number.isFinite(value.sortOrder) &&
    typeof value.mystery === "string" &&
    typeof value.status === "string" &&
    typeof value.title === "string" &&
    typeof value.detail === "string" &&
    typeof value.partyConclusion === "string" &&
    typeof value.discoveredAt === "string" &&
    isCampaignLinks(value.links) &&
    (value.dmTruth === undefined || typeof value.dmTruth === "string") &&
    (value.dmConnection === undefined || typeof value.dmConnection === "string")
  );
}

function isCampaign(value) {
  return (
    value &&
    typeof value.title === "string" &&
    typeof value.subtitle === "string" &&
    typeof value.status === "string" &&
    typeof value.chapter === "string" &&
    typeof value.question === "string" &&
    typeof value.premise === "string" &&
    typeof value.progress === "string" &&
    typeof value.objective === "string" &&
    typeof value.stakes === "string" &&
    typeof value.nextMilestone === "string" &&
    Array.isArray(value.facts) &&
    value.facts.every(
      (fact) =>
        fact &&
        typeof fact.label === "string" &&
        typeof fact.value === "string" &&
        typeof fact.detail === "string",
    ) &&
    Array.isArray(value.adventureLog) &&
    value.adventureLog.every(
      (entry) =>
        entry &&
        typeof entry.id === "string" &&
        Number.isFinite(entry.sortOrder) &&
        typeof entry.session === "string" &&
        typeof entry.date === "string" &&
        typeof entry.title === "string" &&
        typeof entry.summary === "string" &&
        typeof entry.result === "string" &&
        isCampaignLinks(entry.links) &&
        (entry.dmNote === undefined || typeof entry.dmNote === "string") &&
        (entry.dmThread === undefined || typeof entry.dmThread === "string"),
    ) &&
    Array.isArray(value.placesVisited) &&
    value.placesVisited.every(
      (place) =>
        place &&
        typeof place.id === "string" &&
        isHistoryLocationLink(place.location) &&
        typeof place.location.region === "string" &&
        typeof place.firstVisited === "string" &&
        typeof place.lastVisited === "string" &&
        Number.isInteger(place.visitCount) &&
        place.visitCount > 0 &&
        typeof place.status === "string" &&
        typeof place.summary === "string" &&
        typeof place.memory === "string" &&
        (place.dmContext === undefined || typeof place.dmContext === "string"),
    ) &&
    Array.isArray(value.outcomes) &&
    value.outcomes.every(
      (outcome) =>
        outcome &&
        typeof outcome.id === "string" &&
        Number.isFinite(outcome.sortOrder) &&
        typeof outcome.status === "string" &&
        typeof outcome.title === "string" &&
        typeof outcome.situation === "string" &&
        typeof outcome.result === "string" &&
        typeof outcome.consequence === "string" &&
        isCampaignLinks(outcome.links) &&
        (outcome.dmRamification === undefined || typeof outcome.dmRamification === "string"),
    ) &&
    Array.isArray(value.quests) &&
    value.quests.every(isCampaignQuest) &&
    Array.isArray(value.threads) &&
    value.threads.every(isCampaignThread) &&
    Array.isArray(value.clues) &&
    value.clues.every(isCampaignClue) &&
    (value.dmContext === undefined || typeof value.dmContext === "string")
  );
}

function isVisualMedia(value) {
  return value &&
    typeof value.imageUrl === "string" &&
    typeof value.alt === "string" &&
    Number.isInteger(value.width) && value.width > 0 && value.width <= 10_000 &&
    Number.isInteger(value.height) && value.height > 0 && value.height <= 10_000;
}

function isPartyDossierEntry(value) {
  return value &&
    typeof value.id === "string" &&
    typeof value.kind === "string" &&
    typeof value.title === "string" &&
    typeof value.detail === "string" &&
    (value.media === undefined || isVisualMedia(value.media));
}

function isPartyKnowledgeEntry(value) {
  return value &&
    typeof value.id === "string" &&
    typeof value.stance === "string" &&
    typeof value.kind === "string" &&
    typeof value.text === "string";
}

function isNamedCharacterReference(value) {
  return value && typeof value === "object" && !Array.isArray(value) &&
    Object.keys(value).length === 2 && typeof value.id === "string" && value.id.length > 0 &&
    typeof value.label === "string" && value.label.length > 0;
}

function isCharacterSheetV2(value) {
  if (!value || typeof value !== "object" || value.version !== 2 ||
      !isNamedCharacterReference(value.subject) || !Array.isArray(value.inventory?.items) ||
      value.inventory.contentsDepth !== 4 || value.inventory.mayOmitDeeperContents !== true ||
      !value.wallet || !Array.isArray(value.wallet.denominations)) return false;
  const ids = new Set();
  const byId = new Map();
  for (const item of value.inventory.items) {
    if (!item || typeof item.id !== "string" || ids.has(item.id) ||
        !isNamedCharacterReference(item.definition) || !Number.isSafeInteger(item.quantity) || item.quantity < 1 ||
        !(item.parentItemId === null || typeof item.parentItemId === "string") ||
        !Number.isInteger(item.order) || !Number.isInteger(item.depth) || item.depth < 1 || item.depth > 4 ||
        !Number.isInteger(item.childCount) || typeof item.deeperContentsOmitted !== "boolean" ||
        !Array.isArray(item.equipmentSlots) || !item.equipmentSlots.every(isNamedCharacterReference)) return false;
    ids.add(item.id);
    byId.set(item.id, item);
  }
  for (const item of value.inventory.items) {
    if (item.parentItemId === null ? item.depth !== 1 :
      !byId.has(item.parentItemId) || item.depth !== byId.get(item.parentItemId).depth + 1) return false;
  }
  return Number.isSafeInteger(value.wallet.coinCount) && value.wallet.coinCount >= 0 &&
    Number.isSafeInteger(value.wallet.copperValue) && value.wallet.copperValue >= 0 &&
    Number.isSafeInteger(value.wallet.gpCount) && value.wallet.gpCount >= 0;
}

function isDossierSectionState(value) {
  if (!value || typeof value !== "object" || !["idle", "loading", "ready", "empty", "stale", "error", "forbidden"].includes(value.status)) {
    return false;
  }
  const dataIsValid = value.data === null ||
    (Array.isArray(value.data) && value.data.every(isPartyDossierEntry));
  if (!dataIsValid) return false;
  if (value.status === "idle") return value.data === null;
  if (value.status === "loading") return true;
  if (value.status === "ready" || value.status === "empty") {
    return Array.isArray(value.data) && ["canonical", "provisional"].includes(value.source);
  }
  if (value.status === "stale") {
    return Array.isArray(value.data) && ["canonical", "provisional"].includes(value.source) &&
      ["transport", "http", "incompatible-data", "unknown"].includes(value.failureCategory) &&
      typeof value.diagnosticId === "string";
  }
  if (value.status === "error") {
    return value.data === null &&
      ["transport", "http", "incompatible-data", "unknown"].includes(value.failureCategory) &&
      typeof value.diagnosticId === "string";
  }
  return value.data === null && value.failureCategory === "authorization" &&
    typeof value.diagnosticId === "string";
}

function isPartyMember(value) {
  return value &&
    typeof value.id === "string" &&
    typeof value.initials === "string" &&
    typeof value.name === "string" &&
    typeof value.detail === "string" &&
    typeof value.status === "string" &&
    typeof value.isCurrent === "boolean" &&
    (value.portrait === undefined || isVisualMedia(value.portrait)) &&
    typeof value.recordStatus === "string" &&
    ["canonical", "provisional", "unavailable", "empty"].includes(value.sheetStatus) &&
    ["canonical", "provisional", "unavailable", "empty"].includes(value.inventoryStatus) &&
    isDossierSectionState(value.sheetState) &&
    isDossierSectionState(value.inventoryState) &&
    Array.isArray(value.sheet) && value.sheet.every(isPartyDossierEntry) &&
    Array.isArray(value.knowledge) && value.knowledge.every(isPartyKnowledgeEntry) &&
    Array.isArray(value.backstory) && value.backstory.every(isPartyDossierEntry) &&
    Array.isArray(value.origin) && value.origin.every(isPartyDossierEntry) &&
    Array.isArray(value.inventory) && value.inventory.every(isPartyDossierEntry) &&
    (value.characterSheet === undefined || isCharacterSheetV2(value.characterSheet));
}

function isRuleReference(value) {
  const section = value?.section;
  const source = value?.source;
  const authority = value?.authority;
  return Boolean(
    value &&
    typeof value.id === "string" && value.id.length > 0 &&
    typeof value.resolutionKey === "string" && value.resolutionKey.length > 0 &&
    typeof value.title === "string" && value.title.length > 0 &&
    typeof value.summary === "string" && value.summary.length > 0 &&
    Number.isInteger(value.order) && value.order >= 0 &&
    section && typeof section.id === "string" && section.id.length > 0 &&
    typeof section.label === "string" && section.label.length > 0 &&
    Number.isInteger(section.order) && section.order >= 0 &&
    Array.isArray(value.blocks) && value.blocks.length > 0 && value.blocks.every((block) =>
      block && ["paragraph", "steps", "list", "callout"].includes(block.kind) &&
      (block.heading === null || typeof block.heading === "string") &&
      (block.body === null || typeof block.body === "string") &&
      Array.isArray(block.items) && block.items.every((item) => typeof item === "string") &&
      (typeof block.body === "string" || block.items.length > 0)) &&
    Array.isArray(value.examples) && value.examples.every((example) =>
      example && typeof example.title === "string" && typeof example.body === "string") &&
    Array.isArray(value.relatedRuleIds) && value.relatedRuleIds.every((id) => typeof id === "string") &&
    Array.isArray(value.citations) && value.citations.length > 0 && value.citations.every((citation) =>
      citation && typeof citation.sourceId === "string" && typeof citation.locator === "string") &&
    authority && Array.isArray(authority.mechanicIds) && Array.isArray(authority.procedureIds) &&
    [...authority.mechanicIds, ...authority.procedureIds].every((id) => typeof id === "string") &&
    authority.mechanicIds.length + authority.procedureIds.length > 0 &&
    ["public", "dm"].includes(value.visibility) &&
    source && typeof source.ownerId === "string" && typeof source.label === "string" &&
    ["core", "homebrew", "compatibility", "third-party"].includes(source.classification),
  );
}

function hasExactBoardKeys(value, keys) {
  return value && typeof value === "object" && !Array.isArray(value) &&
    Object.keys(value).length === keys.length && keys.every((key) => Object.hasOwn(value, key));
}

function validBoardArea(value, columns, rows) {
  return value && hasExactBoardKeys(value, ["x", "y", "width", "height"]) &&
    Number.isInteger(value.x) && value.x >= 0 && Number.isInteger(value.y) && value.y >= 0 &&
    Number.isInteger(value.width) && value.width > 0 && Number.isInteger(value.height) && value.height > 0 &&
    value.x + value.width <= columns && value.y + value.height <= rows;
}

function boundedBoardInteger(value, minimum, maximum) {
  return Number.isInteger(value) && value >= minimum && value <= maximum;
}

function validTacticalBoard(value) {
  if (!value || !hasExactBoardKeys(value, ["revision", "columns", "rows", "feetPerSquare", "terrain", "obstacles", "participants", ...(value.turn === undefined ? [] : ["turn"])]) ||
      !boundedBoardInteger(value.revision, 1, 2147483647) || !boundedBoardInteger(value.columns, 1, 64) ||
      !boundedBoardInteger(value.rows, 1, 64) || !boundedBoardInteger(value.feetPerSquare, 1, 30) ||
      !Array.isArray(value.terrain) || value.terrain.length > 256 ||
      !Array.isArray(value.obstacles) || value.obstacles.length > 256 ||
      !Array.isArray(value.participants) || value.participants.length > 100) return false;
  const ids = new Set();
  for (const item of value.terrain) {
    if (!item || !hasExactBoardKeys(item, ["id", "label", "area", "movementCost"]) ||
        typeof item.id !== "string" || ids.has(item.id) || typeof item.label !== "string" ||
        !validBoardArea(item.area, value.columns, value.rows) || !boundedBoardInteger(item.movementCost, 1, 4)) return false;
    ids.add(item.id);
  }
  for (const item of value.obstacles) {
    if (!item || !hasExactBoardKeys(item, ["id", "label", "area"]) || typeof item.id !== "string" ||
        ids.has(item.id) || typeof item.label !== "string" || !validBoardArea(item.area, value.columns, value.rows)) return false;
    ids.add(item.id);
  }
  const participantIds = new Set();
  for (const participant of value.participants) {
    const position = participant?.position;
    if (!participant || !hasExactBoardKeys(participant, ["id", "name", "initiative", "active", "position"]) ||
        typeof participant.id !== "string" || participantIds.has(participant.id) ||
        typeof participant.name !== "string" || !Number.isInteger(participant.initiative) ||
        typeof participant.active !== "boolean" || !position ||
        !hasExactBoardKeys(position, ["x", "y", "width", "height", "elevationFeet", "revision"]) ||
        !validBoardArea({ x: position.x, y: position.y, width: position.width, height: position.height }, value.columns, value.rows) ||
        !boundedBoardInteger(position.width, 1, 8) || !boundedBoardInteger(position.height, 1, 8) ||
        !boundedBoardInteger(position.elevationFeet, -1000, 1000) || !boundedBoardInteger(position.revision, 1, 2147483647)) return false;
    participantIds.add(participant.id);
  }
  return value.turn === undefined || (
    value.turn && hasExactBoardKeys(value.turn, ["id", "participationId", "actorId", "actorName", "ordinal"]) &&
    typeof value.turn.id === "string" && typeof value.turn.participationId === "string" &&
    typeof value.turn.actorId === "string" && typeof value.turn.actorName === "string" &&
    boundedBoardInteger(value.turn.ordinal, 0, 99)
  );
}

function isCurrentSituation(value, locations) {
  if (!value || typeof value !== "object" || !["ready", "unavailable"].includes(value.status)) return false;
  if (value.status === "unavailable") {
    return typeof value.message === "string" &&
      (value.locationId === undefined || locations.some((location) => location.id === value.locationId));
  }
  if (value.kind === "recorded") {
    if (value.locationId !== undefined && (typeof value.locationId !== "string" ||
        !locations.some((location) => location.id === value.locationId))) return false;
    const recorded = value.recorded;
    const knownKinds = ["out-of-character", "conversation", "combat", "exploration", "investigation",
      "travel", "rest", "downtime", "other"];
    return recorded && typeof recorded.id === "string" && recorded.id.length > 0 &&
      knownKinds.includes(recorded.kind) && typeof recorded.summary === "string" && recorded.summary.length > 0 &&
      Array.isArray(recorded.participants) && recorded.participants.length <= 32 &&
      recorded.participants.every((participant) => participant && typeof participant.id === "string" &&
        typeof participant.name === "string" &&
        (participant.entityId === undefined || typeof participant.entityId === "string")) &&
      Array.isArray(recorded.interactions) && recorded.interactions.length <= 12 &&
      recorded.interactions.every((message) => message && typeof message.id === "string" &&
        Number.isInteger(message.ordinal) && message.ordinal > 0 && ["player", "assistant"].includes(message.role) &&
        typeof message.text === "string" && message.text.length > 0) &&
      (recorded.location === undefined || (recorded.location && typeof recorded.location.name === "string" &&
        (recorded.location.id === undefined || recorded.location.id === value.locationId)));
  }
  if (typeof value.locationId !== "string" ||
      !locations.some((location) => location.id === value.locationId) ||
      !["exploration", "conversation", "combat"].includes(value.kind)) return false;
  if (value.affordances !== undefined) {
    if (!Array.isArray(value.affordances) || value.affordances.length > 24) return false;
    const keys = new Set();
    for (const item of value.affordances) {
      if (!item || typeof item !== "object" || typeof item.key !== "string" ||
          !/^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/u.test(item.key) || item.key.length > 64 ||
          typeof item.label !== "string" || !/\S/u.test(item.label) || item.label.length > 120 ||
          typeof item.summary !== "string" || !/\S/u.test(item.summary) || item.summary.length > 500 ||
          keys.has(item.key)) return false;
      keys.add(item.key);
    }
  }
  if (value.kind === "exploration") return true;
  if (value.kind === "conversation") {
    return value.conversation && typeof value.conversation.id === "string" &&
      typeof value.conversation.name === "string" &&
      (value.conversation.summary === undefined || typeof value.conversation.summary === "string") &&
      Array.isArray(value.conversation.participants) &&
      value.conversation.participants.every((participant) =>
        participant && typeof participant.id === "string" && typeof participant.name === "string");
  }
  return value.combat && typeof value.combat.id === "string" && typeof value.combat.name === "string" &&
    Array.isArray(value.combat.participants) && value.combat.participants.every((participant) =>
      participant && typeof participant.id === "string" && typeof participant.name === "string" &&
      Number.isInteger(participant.initiative) && typeof participant.active === "boolean") &&
    (value.combat.round === undefined || (typeof value.combat.round.id === "string" &&
      Number.isInteger(value.combat.round.number) && value.combat.round.number > 0)) &&
    (value.combat.turn === undefined || (typeof value.combat.turn.id === "string" &&
      typeof value.combat.turn.participationId === "string" && typeof value.combat.turn.actorId === "string" &&
      typeof value.combat.turn.actorName === "string" && Number.isInteger(value.combat.turn.ordinal) &&
      value.combat.turn.ordinal >= 0 && (value.combat.turn.budget === undefined ||
        (value.combat.turn.budget && ["actions", "bonusActions", "reactions"].every((key) =>
          Number.isInteger(value.combat.turn.budget[key]) && value.combat.turn.budget[key] >= 0))))) &&
    (value.combat.board === undefined || validTacticalBoard(value.combat.board));
}

export function isReadyHubEnvelope(value) {
  if (!value || typeof value !== "object" || value.version !== 1 || value.status !== "ready") {
    return false;
  }

  const { audience, contextSelection, world, campaign, party, rules } = value;
  const validContextSelection = contextSelection === undefined || (
    contextSelection &&
    typeof contextSelection.selectedWorldId === "string" &&
    typeof contextSelection.selectedCampaignId === "string" &&
    Array.isArray(contextSelection.worlds) &&
    contextSelection.worlds.length > 0 &&
    contextSelection.worlds.every((candidateWorld) =>
      candidateWorld &&
      typeof candidateWorld.id === "string" &&
      typeof candidateWorld.name === "string" &&
      Array.isArray(candidateWorld.campaigns) &&
      candidateWorld.campaigns.length > 0 &&
      candidateWorld.campaigns.every((candidateCampaign) =>
        candidateCampaign &&
        typeof candidateCampaign.id === "string" &&
        typeof candidateCampaign.name === "string")
    ) &&
    contextSelection.worlds.some((candidateWorld) =>
      candidateWorld.id === contextSelection.selectedWorldId &&
      candidateWorld.campaigns.some((candidateCampaign) =>
        candidateCampaign.id === contextSelection.selectedCampaignId))
  );
  return (
    validContextSelection &&
    typeof value.applicationId === "string" &&
    /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/u.test(value.applicationId) &&
    typeof value.stateSpaceId === "string" &&
    /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/u.test(value.stateSpaceId) &&
    audience &&
    VALID_PERSPECTIVES.includes(audience.seat) &&
    VALID_PERSPECTIVES.includes(audience.perspective) &&
    Array.isArray(audience.allowedPerspectives) &&
    audience.allowedPerspectives.length > 0 &&
    audience.allowedPerspectives.every((perspective) => VALID_PERSPECTIVES.includes(perspective)) &&
    world &&
    typeof world.name === "string" &&
    typeof world.currentLocationId === "string" &&
    world.map &&
    typeof world.map.imageUrl === "string" &&
    typeof world.map.alt === "string" &&
    Array.isArray(world.history) &&
    world.history.every(isWorldHistoryEvent) &&
    Array.isArray(world.people) &&
    world.people.every(isWorldPersonEntry) &&
    Array.isArray(world.factions) &&
    world.factions.every(isWorldFaction) &&
    Array.isArray(world.lore) &&
    world.lore.every(isWorldLoreEntry) &&
    Array.isArray(world.locations) &&
    world.locations.length > 0 &&
    world.locations.every(
      (location) =>
        Array.isArray(location.people) &&
        location.people.every(isLocationPerson) &&
        (location.holdings === undefined || Array.isArray(location.holdings)) &&
        location.mapAnchor &&
        Number.isFinite(location.mapAnchor.x) &&
        Number.isFinite(location.mapAnchor.y) &&
        location.mapAnchor.x >= 0 &&
        location.mapAnchor.x <= 100 &&
        location.mapAnchor.y >= 0 &&
        location.mapAnchor.y <= 100,
    ) &&
    (world.currentLocationId.length === 0 ||
      world.locations.some((location) => location.id === world.currentLocationId)) &&
    typeof world.rootMapId === "string" &&
    isValidMapHierarchy(world.maps, world.rootMapId) &&
    isCampaign(campaign) &&
    overlaysResolveAgainstMaps(campaign.mapOverlays, world.maps) &&
    Array.isArray(party) &&
    party.every(isPartyMember) &&
    Array.isArray(rules) &&
    rules.every(isRuleReference) &&
    (value.currentSituation === undefined || isCurrentSituation(value.currentSituation, world.locations))
  );
}
