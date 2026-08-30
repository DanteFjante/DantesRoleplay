import { parseDmEmails, parseDmPrincipalIds, resolveAudience } from "./audience-policy.js";
import { HUB_SOURCE_REVISION, hubSource } from "./hub-source.js";

function projectPerson(person, perspective) {
  const safe = {
    id: person.id,
    initials: person.initials,
    name: person.name,
    kind: person.kind,
    role: person.role,
    summary: person.summary,
    background: person.background,
    disposition: person.disposition,
  };

  return perspective === "dm"
    ? { ...safe, motive: person.dm.motive, dmSecret: person.dm.secret }
    : safe;
}

function projectHolding(holding) {
  return {
    id: holding.id,
    name: holding.name,
    kind: holding.kind,
    status: holding.status,
    summary: holding.summary,
    contents: holding.contents.map((item) => ({ ...item })),
    dmNote: holding.dmNote,
  };
}

function projectLocation(location, perspective) {
  const safe = {
    id: location.id,
    name: location.name,
    region: location.region,
    kind: location.kind,
    status: location.status,
    summary: location.summary,
    description: location.description,
    atmosphere: location.atmosphere,
    landmarks: [...location.landmarks],
    observations: [...location.observations],
    routes: location.routes.map((route) => ({ ...route })),
    mapAnchor: { ...location.mapAnchor },
    people: location.people
      .filter((person) => perspective === "dm" || person.playerKnown)
      .map((person) => projectPerson(person, perspective)),
  };

  return perspective === "dm"
    ? {
        ...safe,
        dmSecret: location.dm.secret,
        holdings: location.holdings.map(projectHolding),
      }
    : safe;
}

function projectHistoryEvent(event, perspective, locationById, personById) {
  const safe = {
    id: event.id,
    sortOrder: event.sortOrder,
    date: event.date,
    era: event.era,
    title: event.title,
    category: event.category,
    region: event.region,
    status: event.status,
    summary: event.summary,
    consequence: event.consequence,
    linkedLocations: event.locationIds
      .map((locationId) => locationById.get(locationId))
      .filter(Boolean)
      .map((location) => ({ id: location.id, name: location.name })),
    linkedPeople: event.personIds
      .map((personId) => personById.get(personId))
      .filter(Boolean)
      .map((person) => ({ id: person.id, name: person.name, kind: person.kind })),
  };

  return perspective === "dm"
    ? {
        ...safe,
        dmTruth: event.dm.truth,
        dmConsequence: event.dm.consequence,
      }
    : safe;
}

function deriveWorldPeople(projectedLocations) {
  const personById = new Map();
  for (const location of projectedLocations) {
    for (const person of location.people) {
      if (!personById.has(person.id)) {
        personById.set(person.id, {
          ...person,
          location: { id: location.id, name: location.name, region: location.region },
        });
      }
    }
  }
  return [...personById.values()];
}

function projectFaction(
  faction,
  perspective,
  factionById,
  locationById,
  personById,
) {
  const linkVisible = (link) => perspective === "dm" || link.playerKnown;
  const safe = {
    id: faction.id,
    monogram: faction.monogram,
    name: faction.name,
    influence: faction.influence,
    status: faction.status,
    summary: faction.summary,
    goals: [...faction.goals],
    methods: [...faction.methods],
    members: faction.members
      .filter(linkVisible)
      .map((link) => personById.get(link.personId))
      .filter(Boolean)
      .map((person) => ({ id: person.id, name: person.name, kind: person.kind })),
    territories: faction.territories
      .filter(linkVisible)
      .map((link) => locationById.get(link.locationId))
      .filter(Boolean)
      .map((location) => ({ id: location.id, name: location.name, region: location.region })),
    relationships: faction.relationships
      .filter(linkVisible)
      .map((link) => ({ faction: factionById.get(link.factionId), stance: link.stance }))
      .filter((link) => Boolean(link.faction))
      .map((link) => ({ id: link.faction.id, name: link.faction.name, stance: link.stance })),
  };

  return perspective === "dm"
    ? { ...safe, dmAgenda: faction.dm.agenda, dmSecret: faction.dm.secret }
    : safe;
}

function projectLoreEntry(
  entry,
  perspective,
  locationById,
  personById,
  factionById,
  historyById,
) {
  const safe = {
    id: entry.id,
    title: entry.title,
    category: entry.category,
    status: entry.status,
    summary: entry.summary,
    body: entry.body,
    linkedLocations: entry.locationIds
      .map((id) => locationById.get(id))
      .filter(Boolean)
      .map((location) => ({ id: location.id, name: location.name })),
    linkedPeople: entry.personIds
      .map((id) => personById.get(id))
      .filter(Boolean)
      .map((person) => ({ id: person.id, name: person.name, kind: person.kind })),
    linkedFactions: entry.factionIds
      .map((id) => factionById.get(id))
      .filter(Boolean)
      .map((faction) => ({ id: faction.id, name: faction.name })),
    linkedHistory: entry.historyIds
      .map((id) => historyById.get(id))
      .filter(Boolean)
      .map((event) => ({ id: event.id, title: event.title, date: event.date })),
  };

  return perspective === "dm"
    ? { ...safe, dmTruth: entry.dm.truth, dmNote: entry.dm.note }
    : safe;
}

function projectCampaignLinks(record, locationById, personById, factionById) {
  return {
    locations: record.locationIds
      .map((id) => locationById.get(id))
      .filter(Boolean)
      .map((location) => ({ id: location.id, name: location.name })),
    people: record.personIds
      .map((id) => personById.get(id))
      .filter(Boolean)
      .map((person) => ({ id: person.id, name: person.name, kind: person.kind })),
    factions: record.factionIds
      .map((id) => factionById.get(id))
      .filter(Boolean)
      .map((faction) => ({ id: faction.id, name: faction.name })),
  };
}

function projectCampaign(campaign, perspective, locationById, personById, factionById, projectedMaps) {
  const adventureLog = campaign.adventureLog
    .filter((entry) => perspective === "dm" || entry.playerKnown)
    .map((entry) => {
      const safe = {
        id: entry.id,
        sortOrder: entry.sortOrder,
        session: entry.session,
        date: entry.date,
        title: entry.title,
        summary: entry.summary,
        result: entry.result,
        links: projectCampaignLinks(entry, locationById, personById, factionById),
      };
      return perspective === "dm"
        ? { ...safe, dmNote: entry.dm.note, dmThread: entry.dm.thread }
        : safe;
    });
  const placesVisited = campaign.placesVisited
    .map((visit) => ({ visit, location: locationById.get(visit.locationId) }))
    .filter(({ location }) => Boolean(location))
    .map(({ visit, location }) => {
      const safe = {
        id: visit.id,
        location: { id: location.id, name: location.name, region: location.region },
        firstVisited: visit.firstVisited,
        lastVisited: visit.lastVisited,
        visitCount: visit.visitCount,
        status: visit.status,
        summary: visit.summary,
        memory: visit.memory,
      };
      return perspective === "dm" ? { ...safe, dmContext: visit.dm } : safe;
    });
  const outcomes = campaign.outcomes
    .filter((outcome) => perspective === "dm" || outcome.playerKnown)
    .map((outcome) => {
      const safe = {
        id: outcome.id,
        sortOrder: outcome.sortOrder,
        status: outcome.status,
        title: outcome.title,
        situation: outcome.situation,
        result: outcome.result,
        consequence: outcome.consequence,
        links: projectCampaignLinks(outcome, locationById, personById, factionById),
      };
      return perspective === "dm"
        ? { ...safe, dmRamification: outcome.dm }
        : safe;
    });
  const quests = campaign.quests
    .filter((quest) => perspective === "dm" || quest.playerKnown)
    .map((quest) => {
      const safe = {
        id: quest.id,
        sortOrder: quest.sortOrder,
        kind: quest.kind,
        status: quest.status,
        title: quest.title,
        summary: quest.summary,
        nextStep: quest.nextStep,
        objectives: quest.objectives
          .filter((objective) => perspective === "dm" || objective.playerKnown)
          .map((objective) => ({ id: objective.id, status: objective.status, text: objective.text })),
        links: projectCampaignLinks(quest, locationById, personById, factionById),
      };
      return perspective === "dm" ? { ...safe, dmContext: quest.dm.context } : safe;
    });
  const threads = campaign.threads
    .filter((thread) => perspective === "dm" || thread.playerKnown)
    .map((thread) => {
      const safe = {
        id: thread.id,
        sortOrder: thread.sortOrder,
        category: thread.category,
        status: thread.status,
        pressure: thread.pressure,
        title: thread.title,
        summary: thread.summary,
        lastChanged: thread.lastChanged,
        links: projectCampaignLinks(thread, locationById, personById, factionById),
      };
      return perspective === "dm"
        ? { ...safe, dmTruth: thread.dm.truth, dmReveal: thread.dm.reveal }
        : safe;
    });
  const clues = campaign.clues
    .filter((clue) => perspective === "dm" || clue.playerKnown)
    .map((clue) => {
      const safe = {
        id: clue.id,
        sortOrder: clue.sortOrder,
        mystery: clue.mystery,
        status: clue.status,
        title: clue.title,
        detail: clue.detail,
        partyConclusion: clue.partyConclusion,
        discoveredAt: clue.discoveredAt,
        links: projectCampaignLinks(clue, locationById, personById, factionById),
      };
      return perspective === "dm"
        ? { ...safe, dmTruth: clue.dm.truth, dmConnection: clue.dm.connection }
        : safe;
    });
  const visibleRegionCount = new Set(placesVisited.map((visit) => visit.location.region)).size;
  const facts = campaign.facts.map((fact) => {
    if (fact.label === "Sessions") {
      return { ...fact, value: String(adventureLog.length), detail: "Recorded campaign entries" };
    }
    if (fact.label === "Places visited") {
      return {
        ...fact,
        value: String(placesVisited.length),
        detail: `Across ${visibleRegionCount} world regions`,
      };
    }
    if (fact.label === "Open outcomes") {
      return {
        ...fact,
        value: String(outcomes.filter((outcome) => outcome.status !== "Resolved").length),
        detail: "Visible situations still changing",
      };
    }
    if (fact.label === "Active quests") {
      return {
        ...fact,
        value: String(quests.filter((quest) => quest.status !== "Complete").length),
        detail: "Visible pursuits with an authored next step",
      };
    }
    return { ...fact };
  });
  const safe = {
    title: campaign.title,
    subtitle: campaign.subtitle,
    status: campaign.status,
    chapter: campaign.chapter,
    question: campaign.question,
    premise: campaign.premise,
    progress: campaign.progress,
    objective: campaign.objective,
    stakes: campaign.stakes,
    nextMilestone: campaign.nextMilestone,
    facts,
    adventureLog,
    placesVisited,
    outcomes,
    quests,
    threads,
    clues,
    mapOverlays: projectCampaignOverlays(campaign.mapOverlays, perspective, projectedMaps),
  };
  return perspective === "dm" ? { ...safe, dmContext: campaign.dm.context } : safe;
}

function isGeometryInSpace(geometry, space) {
  return (
    Boolean(geometry) &&
    Number.isFinite(geometry.x) &&
    Number.isFinite(geometry.y) &&
    geometry.x >= 0 &&
    geometry.y >= 0 &&
    geometry.x <= space.width &&
    geometry.y <= space.height
  );
}

const MAP_AUDIENCES = new Set(["player", "dm"]);

/** A layer is emitted only when it declares an audience this perspective may read. */
function projectMapLayers(map, perspective) {
  return map.layers
    .filter((layer) => {
      // A missing or unrecognised audience is omitted, never defaulted: defaulting to "player"
      // would make an unlabelled layer silently visible to everyone.
      if (!MAP_AUDIENCES.has(layer.audience)) return false;
      return perspective === "dm" || layer.audience === "player";
    })
    .map((layer) => ({ id: layer.id, kind: layer.kind, order: layer.order, label: layer.label }))
    .sort((left, right) => left.order - right.order);
}

/**
 * The base is resolved to the variant this audience may see. With no such variant the map is
 * emitted without a base and renders the unavailable state; another audience's asset is never
 * substituted, and its absence is never explained.
 */
function projectMapBase(map, perspective) {
  const variant = (map.base?.variants ?? []).find((candidate) => candidate.audience === perspective);
  return variant ? { imageUrl: variant.imageUrl, alt: variant.alt } : null;
}

function projectMapFeature(feature, map, perspective, locationById, visibleLayerIds) {
  if (perspective !== "dm" && feature.playerKnown === false) return null;

  // A feature outlives neither its layer's policy nor its layer's existence.
  if (!visibleLayerIds.has(feature.layerId)) return null;

  const location = feature.locationId ? locationById.get(feature.locationId) : null;
  if (feature.locationId && !location) return null;

  const geometry =
    feature.placement === "location-anchor"
      ? location
        ? { x: location.mapAnchor.x, y: location.mapAnchor.y }
        : null
      : feature.geometry
        ? { x: feature.geometry.x, y: feature.geometry.y }
        : null;

  // Geometry outside the declaring map's own coordinate space is rejected, never clamped.
  if (!isGeometryInSpace(geometry, map.coordinateSpace)) return null;

  return {
    id: feature.id,
    kind: feature.kind,
    layerId: feature.layerId,
    coordinateSpaceId: map.coordinateSpace.id,
    geometry,
    name: feature.placement === "location-anchor" ? location.name : feature.name,
    detail: feature.placement === "location-anchor" ? location.summary : feature.detail,
    locationId: location ? location.id : null,
  };
}

function projectMaps(sourceMaps, perspective, locationById) {
  const audienceVisible = sourceMaps.filter(
    (map) => perspective === "dm" || map.playerKnown !== false,
  );

  // A map whose parent is not itself visible is unreachable: it can carry no breadcrumb trail, so
  // it is dropped rather than rendered from a partial ancestry.
  let reachable = audienceVisible;
  for (;;) {
    const byId = new Set(reachable.map((map) => map.id));
    const next = reachable.filter((map) => map.parentMapId === null || byId.has(map.parentMapId));
    if (next.length === reachable.length) break;
    reachable = next;
  }

  const reachableById = new Map(reachable.map((map) => [map.id, map]));

  return reachable.map((map) => {
    const layers = projectMapLayers(map, perspective);
    const visibleLayerIds = new Set(layers.map((layer) => layer.id));
    const features = map.features
      .map((feature) => projectMapFeature(feature, map, perspective, locationById, visibleLayerIds))
      .filter(Boolean);
    const featureIds = new Set(features.map((feature) => feature.id));

    return {
      id: map.id,
      scope: map.scope,
      parentMapId: map.parentMapId,
      subject: { ...map.subject },
      coordinateSpace: { ...map.coordinateSpace },
      base: projectMapBase(map, perspective),
      layers,
      features,
      scopeLinks: map.scopeLinks
        .filter((link) => perspective === "dm" || link.playerKnown)
        .filter((link) => reachableById.has(link.childMapId))
        .map((link) => {
          const child = reachableById.get(link.childMapId);
          return {
            id: link.id,
            childMapId: child.id,
            childScope: child.scope,
            childName: child.subject.name,
            viaFeatureId: link.viaFeatureId && featureIds.has(link.viaFeatureId) ? link.viaFeatureId : null,
          };
        }),
    };
  });
}

/**
 * An overlay is campaign-owned annotation pointing at a World map. It is emitted only when the
 * audience may see the overlay itself AND may already see its target. A dropped overlay leaves no
 * trace: no count, no placeholder, nothing that would reveal what the target's policy protects.
 */
function projectCampaignOverlays(overlays, perspective, projectedMaps) {
  const mapById = new Map(projectedMaps.map((map) => [map.id, map]));

  return overlays
    .filter((overlay) => perspective === "dm" || overlay.playerKnown)
    .filter((overlay) => {
      const map = mapById.get(overlay.mapId);
      if (!map) return false;
      if (overlay.featureId === null) return true;
      return map.features.some((feature) => feature.id === overlay.featureId);
    })
    .map((overlay) => ({
      id: overlay.id,
      mapId: overlay.mapId,
      featureId: overlay.featureId,
      kind: overlay.kind,
      label: overlay.label,
      detail: overlay.detail,
      recordedOn: overlay.recordedOn,
    }))
    .sort(
      (left, right) =>
        left.mapId.localeCompare(right.mapId) ||
        String(left.featureId).localeCompare(String(right.featureId)) ||
        left.id.localeCompare(right.id),
    );
}

export function projectHubEnvelope(source, sourceRevision, audience) {
  if (audience.status !== "ready") {
    return {
      version: 1,
      status: "denied",
      message: "This table view is not available for the current visitor.",
    };
  }

  const visibleLocations = source.world.locations.filter(
    (location) => audience.perspective === "dm" || location.playerKnown !== false,
  );
  const projectedRegions = source.world.regions.map((region) => ({
    name: region.name,
    detail: region.detail,
    count: visibleLocations.filter((location) => location.region === region.name).length,
  }));
  const projectedLocations = visibleLocations.map((location) =>
    projectLocation(location, audience.perspective),
  );
  const locationById = new Map(projectedLocations.map((location) => [location.id, location]));
  const personById = new Map(
    projectedLocations.flatMap((location) =>
      location.people.map((person) => [person.id, person]),
    ),
  );
  const projectedHistory = source.world.history
    .filter((event) => audience.perspective === "dm" || event.playerKnown)
    .map((event) =>
      projectHistoryEvent(event, audience.perspective, locationById, personById),
    );
  const visibleFactions = source.world.factions.filter(
    (faction) => audience.perspective === "dm" || faction.playerKnown,
  );
  const visibleFactionById = new Map(
    visibleFactions.map((faction) => [faction.id, { id: faction.id, name: faction.name }]),
  );
  const projectedFactions = visibleFactions.map((faction) =>
    projectFaction(
      faction,
      audience.perspective,
      visibleFactionById,
      locationById,
      personById,
    ),
  );
  const historyById = new Map(projectedHistory.map((event) => [event.id, event]));
  const projectedLore = source.world.lore
    .filter((entry) => audience.perspective === "dm" || entry.playerKnown)
    .map((entry) =>
      projectLoreEntry(
        entry,
        audience.perspective,
        locationById,
        personById,
        visibleFactionById,
        historyById,
      ),
    );
  const projectedPeople = deriveWorldPeople(projectedLocations);
  const projectedMaps = projectMaps(source.world.maps, audience.perspective, locationById);
  const projectedFacts = source.world.facts.map((fact) => {
    if (fact.label === "Known places") {
      return {
        ...fact,
        value: String(visibleLocations.length),
        detail: `Across ${projectedRegions.filter((region) => region.count > 0).length} regions`,
      };
    }
    if (fact.label === "Active factions") {
      return {
        ...fact,
        value: String(projectedFactions.filter((faction) => faction.status === "Active").length),
        detail: "Visible organizations in this perspective",
      };
    }
    return { ...fact };
  });

  return {
    version: 1,
    status: "ready",
    revision: sourceRevision,
    audience: {
      seat: audience.seat,
      perspective: audience.perspective,
      allowedPerspectives: [...audience.allowedPerspectives],
    },
    world: {
      id: source.world.id,
      name: source.world.name,
      era: source.world.era,
      summary: source.world.summary,
      premise: source.world.premise,
      currentLocationId: source.world.currentLocationId,
      map: { ...source.world.map },
      rootMapId: source.world.rootMapId,
      maps: projectedMaps,
      regions: projectedRegions,
      facts: projectedFacts,
      history: projectedHistory,
      locations: projectedLocations,
      people: projectedPeople,
      factions: projectedFactions,
      lore: projectedLore,
    },
    campaign: projectCampaign(
      source.campaign,
      audience.perspective,
      locationById,
      personById,
      visibleFactionById,
      projectedMaps,
    ),
    party: source.party.map((member) => ({ ...member })),
    rules: source.rules.map((rule) => ({ ...rule })),
  };
}

export function readHubEnvelope({
  authenticatedUserId,
  authenticatedUserEmail,
  requestedPerspective,
  environment,
}) {
  const audience = resolveAudience({
    authenticatedUserId,
    authenticatedUserEmail,
    requestedPerspective,
    dmPrincipalIds: parseDmPrincipalIds(environment.DND2024_DM_USER_IDS),
    dmEmails: parseDmEmails(environment.DND2024_DM_EMAILS),
    nodeEnvironment: environment.NODE_ENV,
    localSeat: environment.DND2024_LOCAL_SEAT,
  });

  return projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, audience);
}
