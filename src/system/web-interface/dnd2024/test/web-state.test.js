import assert from "node:assert/strict";
import test from "node:test";

import {
  CAMPAIGN_SECTIONS,
  LOCATION_SECTIONS,
  MAIN_TABS,
  WORLD_SECTIONS,
  filterCampaignLog,
  filterCampaignOutcomes,
  filterCampaignPlaces,
  filterMapFeaturesByLayers,
  groupMapFeaturesByLayers,
  filterLocations,
  filterWorldFactions,
  filterWorldHistory,
  filterWorldLore,
  filterWorldPeople,
  buildMapBreadcrumbs,
  isGeometryInCoordinateSpace,
  overlaysResolveAgainstMaps,
  resolveFeatureOverlays,
  resolveMapOverlays,
  isReadyHubEnvelope,
  isValidMapHierarchy,
  normalizeCampaignSection,
  normalizeMapId,
  resolveMapChildScopes,
  resolveMapDocument,
  resolveMapFactionInfluences,
  resolveCurrentSceneLocation,
  resolveSelectedMapFeature,
  searchMapFeatures,
  normalizeMainTab,
  normalizeLocationSection,
  normalizePerspective,
  normalizeWorldSection,
  resolveSelectedLocation,
} from "../src/state.js";

const locations = [
  { id: "archive", name: "Sunken Archive", region: "Ash March", kind: "ruin", summary: "A flooded vault." },
  { id: "beacon", name: "Hollow Beacon", region: "Crown Coast", kind: "tower", summary: "A silent light." },
  { id: "crossing", name: "Greyfen Crossing", region: "Ash March", kind: "settlement", summary: "A guarded bridge." },
];

const history = [
  {
    id: "newest",
    sortOrder: 30,
    title: "The Beacon Woke",
    date: "Today",
    era: "Ashen Reckoning",
    category: "Omen",
    region: "Crown Coast",
    status: "Ongoing",
    summary: "A light crossed the sea.",
    consequence: "Captains avoid the northern water.",
    linkedLocations: [{ id: "beacon", name: "Hollow Beacon" }],
    linkedPeople: [{ id: "elian", name: "Elian Voss", kind: "NPC" }],
  },
  {
    id: "middle",
    sortOrder: 20,
    title: "Greyfen Held",
    date: "Yesterday",
    era: "Ashen Reckoning",
    category: "Conflict",
    region: "Ash March",
    status: "Resolved",
    summary: "The bridge survived.",
    consequence: "The crossing remains open.",
    linkedLocations: [{ id: "crossing", name: "Greyfen Crossing" }],
    linkedPeople: [],
  },
  {
    id: "oldest",
    sortOrder: 10,
    title: "The Archive Sank",
    date: "Long ago",
    era: "Ashen Reckoning",
    category: "World change",
    region: "Ash March",
    status: "Established",
    summary: "Rain filled the vault.",
    consequence: "The lower shelves remain flooded.",
    linkedLocations: [{ id: "archive", name: "Sunken Archive" }],
    linkedPeople: [],
  },
];

const people = [
  {
    id: "maelin",
    initials: "MQ",
    name: "Maelin Quill",
    kind: "NPC",
    role: "Scholar",
    summary: "Studies old wards.",
    background: "Former royal archivist.",
    disposition: "Curious",
    location: { id: "archive", name: "Sunken Archive", region: "Ash March" },
  },
  {
    id: "raven",
    initials: "TR",
    name: "Tollhouse Raven",
    kind: "Creature",
    role: "Messenger",
    summary: "Watches the tollhouse.",
    background: "Arrived after the raid.",
    disposition: "Bold",
    location: { id: "crossing", name: "Greyfen Crossing", region: "Ash March" },
  },
];

const factions = [
  {
    id: "wardens",
    monogram: "AW",
    name: "Ash Wardens",
    influence: "Major",
    status: "Active",
    summary: "Guard the ridge.",
    goals: ["Keep the road open"],
    methods: ["Patrols"],
    members: [{ id: "captain", name: "Captain Hale", kind: "NPC" }],
    territories: [{ id: "emberwatch", name: "Emberwatch", region: "Ash March" }],
    relationships: [{ id: "fox", name: "White Fox Company", stance: "Opposed" }],
  },
  {
    id: "fox",
    monogram: "WF",
    name: "White Fox Company",
    influence: "Regional",
    status: "Active",
    summary: "Moves messages.",
    goals: ["Keep routes free"],
    methods: ["Hidden paths"],
    members: [],
    territories: [{ id: "crossing", name: "Greyfen Crossing", region: "Ash March" }],
    relationships: [{ id: "wardens", name: "Ash Wardens", stance: "Opposed" }],
  },
];

const lore = [
  {
    id: "bells",
    title: "Greyfen Bells",
    category: "Place",
    status: "Rumour",
    summary: "The bells count promises.",
    body: "The lowest bell rings under clear water.",
    linkedLocations: [{ id: "crossing", name: "Greyfen Crossing" }],
    linkedPeople: [],
    linkedFactions: [{ id: "fox", name: "White Fox Company" }],
    linkedHistory: [{ id: "flood", title: "Flood winter", date: "319 AR" }],
  },
  {
    id: "key",
    title: "Bronze Key",
    category: "Relic",
    status: "Established",
    summary: "A key without teeth.",
    body: "It resonates with the Archive wards.",
    linkedLocations: [{ id: "archive", name: "Sunken Archive" }],
    linkedPeople: [{ id: "maelin", name: "Maelin Quill", kind: "NPC" }],
    linkedFactions: [],
    linkedHistory: [],
  },
];

const emptyCampaignLinks = { locations: [], people: [], factions: [] };
const campaignLog = [
  { id: "recent", sortOrder: 20, session: "Session 2", date: "Today", title: "Archive oath", summary: "The ward woke.", result: "A door opened.", links: { ...emptyCampaignLinks, locations: [{ id: "archive", name: "Sunken Archive" }] } },
  { id: "first", sortOrder: 10, session: "Session 1", date: "Yesterday", title: "Bridge raid", summary: "Greyfen held.", result: "The road stayed open.", links: { ...emptyCampaignLinks, people: [{ id: "mara", name: "Mara Vell", kind: "NPC" }] } },
];
const campaignPlaces = [
  { id: "visit-archive", location: { id: "archive", name: "Sunken Archive", region: "Ash March" }, firstVisited: "Today", lastVisited: "Today", visitCount: 1, status: "Current", summary: "A flooded vault.", memory: "The ward woke." },
  { id: "visit-crossing", location: { id: "crossing", name: "Greyfen Crossing", region: "Ash March" }, firstVisited: "Yesterday", lastVisited: "Yesterday", visitCount: 3, status: "Allied", summary: "A guarded bridge.", memory: "The bells survived." },
];
const campaignOutcomes = [
  { id: "open", sortOrder: 20, status: "Ongoing", title: "The ward woke", situation: "The door was sealed.", result: "An oath opened it.", consequence: "Another oath is required.", links: { ...emptyCampaignLinks, locations: [{ id: "archive", name: "Sunken Archive" }] } },
  { id: "closed", sortOrder: 10, status: "Resolved", title: "The bridge held", situation: "Raiders attacked.", result: "The party won.", consequence: "The road remains open.", links: emptyCampaignLinks },
];
const campaignQuests = [
  { id: "reliquary", sortOrder: 20, kind: "Main quest", status: "Active", title: "Open the reliquary", summary: "A ward waits.", nextStep: "Speak the oath.", objectives: [{ id: "oath", status: "Active", text: "Speak the second oath." }], links: { ...emptyCampaignLinks, locations: [{ id: "archive", name: "Sunken Archive" }] } },
  { id: "warden", sortOrder: 10, kind: "Faction quest", status: "Open", title: "Choose a route", summary: "Wardens are watching.", nextStep: "Ask Hale.", objectives: [{ id: "hale", status: "Open", text: "Respond to Hale." }], links: { ...emptyCampaignLinks, people: [{ id: "hale", name: "Captain Hale", kind: "NPC" }] } },
];
const campaignThreads = [
  { id: "oath", sortOrder: 20, category: "Threat", status: "Unresolved", pressure: "Dawn approaches", title: "The second oath", summary: "The ward wants a promise.", lastChanged: "Session 2", links: { ...emptyCampaignLinks, locations: [{ id: "archive", name: "Sunken Archive" }] } },
  { id: "name", sortOrder: 10, category: "Mystery", status: "Open", pressure: "Quiet", title: "An older name", summary: "The beacon spoke.", lastChanged: "Session 1", links: emptyCampaignLinks },
];
const campaignClues = [
  { id: "intent", sortOrder: 20, mystery: "Reliquary", status: "Established", title: "The ward answered intent", detail: "A promise woke it.", partyConclusion: "Promises matter.", discoveredAt: "Session 2", links: { ...emptyCampaignLinks, locations: [{ id: "archive", name: "Sunken Archive" }] } },
  { id: "name", sortOrder: 10, mystery: "Oath-keepers", status: "Lead", title: "The beacon named Seraphine", detail: "An older name sounded.", partyConclusion: "A family connection exists.", discoveredAt: "Session 1", links: emptyCampaignLinks },
];
const campaign = {
  title: "The Cinder Crown",
  subtitle: "An Eldervale campaign",
  status: "In progress",
  chapter: "Chapter III",
  question: "Who carries the crown?",
  premise: "A broken oath-road adventure.",
  progress: "The party reached the Archive.",
  objective: "Open the reliquary.",
  stakes: "The Wardens are approaching.",
  nextMilestone: "Speak the second oath.",
  facts: [{ label: "Sessions", value: "2", detail: "Recorded entries" }],
  adventureLog: campaignLog,
  placesVisited: campaignPlaces,
  outcomes: campaignOutcomes,
  quests: campaignQuests,
  threads: campaignThreads,
  clues: campaignClues,
  mapOverlays: [],
};

test("web table accepts Player and DM while migrating the legacy Client preference", () => {
  assert.equal(normalizePerspective("dm"), "dm");
  assert.equal(normalizePerspective("player"), "player");
  assert.equal(normalizePerspective("client"), "player");
  assert.equal(normalizePerspective(null), "player");
});

test("shared navigation has the confirmed identity and order in every perspective", () => {
  assert.deepEqual(
    MAIN_TABS.map(({ id, label }) => [id, label]),
    [
      ["world", "World"],
      ["campaign", "Campaign"],
      ["party", "Party"],
      ["current", "Current View"],
      ["rules", "Rules"],
      ["content", "Installed Content"],
    ],
  );
});

test("unknown navigation state fails to the World overview", () => {
  assert.deepEqual(WORLD_SECTIONS.map(({ id }) => id), [
    "overview",
    "map",
    "history",
    "locations",
    "people",
    "factions",
    "lore",
  ]);
  assert.equal(normalizeMainTab("party"), "party");
  assert.equal(normalizeMainTab("debug"), "world");
  assert.equal(normalizeWorldSection("locations"), "locations");
  assert.equal(normalizeWorldSection("map"), "map");
  assert.equal(normalizeWorldSection("history"), "history");
  assert.equal(normalizeWorldSection("factions"), "factions");
});

test("Campaign sections are stable and unknown state fails to Overview", () => {
  assert.deepEqual(CAMPAIGN_SECTIONS.map(({ id }) => id), ["overview", "log", "places", "outcomes", "quests", "threads", "clues"]);
  assert.equal(normalizeCampaignSection("places"), "places");
  assert.equal(normalizeCampaignSection("clues"), "clues");
  assert.equal(normalizeCampaignSection("secrets"), "overview");
});

test("location subsections are stable and Holdings fails closed outside DM", () => {
  assert.deepEqual(LOCATION_SECTIONS.map(({ id }) => id), ["details", "people", "holdings"]);
  assert.equal(normalizeLocationSection("people", "player"), "people");
  assert.equal(normalizeLocationSection("holdings", "dm"), "holdings");
  assert.equal(normalizeLocationSection("holdings", "player"), "details");
  assert.equal(normalizeLocationSection("secrets", "dm"), "details");
});

test("location search is stable, case-insensitive, and searches user-facing fields", () => {
  assert.deepEqual(filterLocations(locations, "").map(({ id }) => id), ["crossing", "beacon", "archive"]);
  assert.deepEqual(filterLocations(locations, "ASH").map(({ id }) => id), ["crossing", "archive"]);
  assert.deepEqual(filterLocations(locations, "silent").map(({ id }) => id), ["beacon"]);
  assert.deepEqual(filterLocations(locations, "missing"), []);
});

test("location selection is exact and falls back to the current place", () => {
  assert.equal(resolveSelectedLocation(locations, "beacon", "archive")?.id, "beacon");
  assert.equal(resolveSelectedLocation(locations, "unknown", "archive")?.id, "archive");
  assert.equal(resolveSelectedLocation([], "unknown", "archive"), null);
});

test("Current View requires an exact current location and never falls back to the first place", () => {
  assert.equal(resolveCurrentSceneLocation(locations, "archive")?.id, "archive");
  assert.equal(resolveCurrentSceneLocation(locations, "unknown"), null);
  assert.equal(resolveCurrentSceneLocation(locations, ""), null);
});

test("World History filters compose and ordering is stable without mutating input", () => {
  const originalOrder = history.map(({ id }) => id);

  assert.deepEqual(filterWorldHistory(history).map(({ id }) => id), ["newest", "middle", "oldest"]);
  assert.deepEqual(
    filterWorldHistory(history, { order: "oldest" }).map(({ id }) => id),
    ["oldest", "middle", "newest"],
  );
  assert.deepEqual(
    filterWorldHistory(history, { region: "Ash March", category: "Conflict" }).map(({ id }) => id),
    ["middle"],
  );
  assert.deepEqual(filterWorldHistory(history, { query: "ELIAN" }).map(({ id }) => id), ["newest"]);
  assert.deepEqual(filterWorldHistory(history, { query: "sunken" }).map(({ id }) => id), ["oldest"]);
  assert.deepEqual(filterWorldHistory(history, { query: "missing" }), []);
  assert.deepEqual(history.map(({ id }) => id), originalOrder);
});

test("World People search composes with kind and region filters", () => {
  assert.deepEqual(filterWorldPeople(people).map(({ id }) => id), ["maelin", "raven"]);
  assert.deepEqual(filterWorldPeople(people, { kind: "Creature" }).map(({ id }) => id), ["raven"]);
  assert.deepEqual(filterWorldPeople(people, { query: "former" }).map(({ id }) => id), ["maelin"]);
  assert.deepEqual(filterWorldPeople(people, { region: "missing" }), []);
});

test("World Faction search covers goals, members, and relationships without mutation", () => {
  const original = factions.map(({ id }) => id);
  assert.deepEqual(filterWorldFactions(factions, { influence: "Regional" }).map(({ id }) => id), ["fox"]);
  assert.deepEqual(filterWorldFactions(factions, { query: "captain" }).map(({ id }) => id), ["wardens"]);
  assert.deepEqual(filterWorldFactions(factions, { query: "opposed" }).map(({ id }) => id), ["wardens", "fox"]);
  assert.deepEqual(factions.map(({ id }) => id), original);
});

test("World Lore search composes with category and status filters", () => {
  assert.deepEqual(filterWorldLore(lore).map(({ id }) => id), ["key", "bells"]);
  assert.deepEqual(filterWorldLore(lore, { category: "Relic" }).map(({ id }) => id), ["key"]);
  assert.deepEqual(filterWorldLore(lore, { status: "Rumour", query: "white fox" }).map(({ id }) => id), ["bells"]);
  assert.deepEqual(filterWorldLore(lore, { query: "maelin" }).map(({ id }) => id), ["key"]);
});

test("Campaign filters search user-facing continuity and preserve stable ordering", () => {
  assert.deepEqual(filterCampaignLog(campaignLog).map(({ id }) => id), ["recent", "first"]);
  assert.deepEqual(filterCampaignLog(campaignLog, { order: "oldest" }).map(({ id }) => id), ["first", "recent"]);
  assert.deepEqual(filterCampaignLog(campaignLog, { query: "mara" }).map(({ id }) => id), ["first"]);
  assert.deepEqual(filterCampaignPlaces(campaignPlaces).map(({ id }) => id), ["visit-crossing", "visit-archive"]);
  assert.deepEqual(filterCampaignPlaces(campaignPlaces, { query: "ward" }).map(({ id }) => id), ["visit-archive"]);
  assert.deepEqual(filterCampaignOutcomes(campaignOutcomes, { status: "Resolved" }).map(({ id }) => id), ["closed"]);
  assert.deepEqual(filterCampaignOutcomes(campaignOutcomes, { query: "reliquary" }), []);
});

function mapFixture(overrides = {}) {
  return {
    id: "map.world",
    scope: "world",
    parentMapId: null,
    subject: { kind: "world", id: "world.test", name: "Testvale" },
    coordinateSpace: { id: "space.world", unit: "percent", width: 100, height: 100 },
    base: { imageUrl: "/map.png", alt: "Unlabelled map" },
    layers: [{ id: "layer.markers", kind: "markers", order: 1, label: "Places" }],
    features: [
      {
        id: "feature.archive",
        kind: "point",
        layerId: "layer.markers",
        coordinateSpaceId: "space.world",
        geometry: { x: 50, y: 50 },
        name: "Archive",
        detail: "A place.",
        locationId: "archive",
      },
    ],
    scopeLinks: [],
    ...overrides,
  };
}

const worldMapFixture = mapFixture();

const regionMapFixture = mapFixture({
  id: "map.region",
  scope: "region",
  parentMapId: "map.world",
  subject: { kind: "region", id: "region.test", name: "The March" },
  coordinateSpace: { id: "space.region", unit: "grid", width: 1000, height: 700 },
  features: [
    {
      id: "feature.hold",
      kind: "point",
      layerId: "layer.markers",
      coordinateSpaceId: "space.region",
      geometry: { x: 900, y: 600 },
      name: "The Hold",
      detail: "A settlement.",
      locationId: null,
    },
  ],
  scopeLinks: [],
});

const linkedWorldMapFixture = mapFixture({
  scopeLinks: [
    {
      id: "scopelink.region",
      childMapId: "map.region",
      childScope: "region",
      childName: "The March",
      viaFeatureId: "feature.archive",
    },
  ],
});

const scopedMaps = [linkedWorldMapFixture, regionMapFixture];

test("a map id normalizes to the declared world root and unknown features clear the selection", () => {
  assert.equal(normalizeMapId(scopedMaps, "map.region", "map.world"), "map.region");
  assert.equal(normalizeMapId(scopedMaps, "map.absent", "map.world"), "map.world");
  assert.equal(normalizeMapId(scopedMaps, "", "map.world"), "map.world");
  assert.equal(normalizeMapId(scopedMaps, "map.region", "map.absent"), "map.region");
  assert.equal(resolveMapDocument(scopedMaps, "map.absent"), null);
  assert.equal(resolveSelectedMapFeature(regionMapFixture, "feature.absent"), null);
  assert.equal(resolveSelectedMapFeature(null, "feature.hold"), null);
  assert.equal(resolveSelectedMapFeature(regionMapFixture, "feature.hold").name, "The Hold");
});

test("atlas search finds only projected features and collapses duplicate location scopes", () => {
  const regionArchive = {
    ...regionMapFixture,
    features: [{
      ...regionMapFixture.features[0],
      id: "feature.region.archive",
      name: "Archive",
      detail: "A closer view of the flooded archive.",
      locationId: "archive",
    }],
  };
  const maps = [linkedWorldMapFixture, regionArchive];
  const originalWorldFeatures = [...linkedWorldMapFixture.features];

  assert.deepEqual(searchMapFeatures(maps, "", "map.world"), []);
  assert.equal(searchMapFeatures(maps, "missing", "map.world").length, 0);
  assert.equal(searchMapFeatures(maps, "flooded", "map.world")[0].mapId, "map.region");
  assert.equal(searchMapFeatures(maps, "archive", "map.world")[0].mapId, "map.world");
  assert.equal(searchMapFeatures(maps, "archive", "map.region")[0].mapId, "map.region");
  assert.equal(searchMapFeatures(maps, "archive", "map.absent")[0].mapId, "map.region");
  assert.deepEqual(linkedWorldMapFixture.features, originalWorldFeatures);
});

test("faction influence resolves only exact projected territory location IDs", () => {
  const projectedFactions = [
    { ...factions[0], territories: [{ id: "archive", name: "Different display name", region: "Elsewhere" }] },
    { ...factions[1], territories: [{ id: "archive-lookalike", name: "Archive", region: "Ash March" }] },
  ];
  const originalFeatures = [...worldMapFixture.features];
  const influences = resolveMapFactionInfluences(projectedFactions, worldMapFixture);

  assert.deepEqual(influences, [{
    factionId: "wardens",
    name: "Ash Wardens",
    influence: "Major",
    featureIds: ["feature.archive"],
  }]);
  assert.deepEqual(resolveMapFactionInfluences([], worldMapFixture), []);
  assert.deepEqual(resolveMapFactionInfluences(projectedFactions, null), []);
  assert.deepEqual(worldMapFixture.features, originalFeatures);
});

test("map list groups the same projected features by declared layer order", () => {
  const map = mapFixture({
    layers: [
      { id: "layer.empty", kind: "markers", order: 3, label: "Empty" },
      { id: "layer.sites", kind: "markers", order: 2, label: "Sites" },
      { id: "layer.regions", kind: "markers", order: 1, label: "Regions" },
    ],
    features: [
      { ...worldMapFixture.features[0], id: "feature.site", layerId: "layer.sites" },
      { ...worldMapFixture.features[0], id: "feature.region", layerId: "layer.regions" },
    ],
  });
  const originalFeatures = [...map.features];
  const groups = groupMapFeaturesByLayers(map);

  assert.deepEqual(groups.map((group) => group.layer.label), ["Regions", "Sites"]);
  assert.deepEqual(groups.map((group) => group.features.map((feature) => feature.id)), [
    ["feature.region"],
    ["feature.site"],
  ]);
  assert.deepEqual(groupMapFeaturesByLayers(null), []);
  assert.deepEqual(map.features, originalFeatures);
});

test("breadcrumbs walk declared parent links root to current and refuse a broken ancestry", () => {
  assert.deepEqual(
    buildMapBreadcrumbs(scopedMaps, "map.region").map(({ name }) => name),
    ["Testvale", "The March"],
  );
  assert.deepEqual(buildMapBreadcrumbs(scopedMaps, "map.world").map(({ id }) => id), ["map.world"]);
  assert.deepEqual(buildMapBreadcrumbs(scopedMaps, "map.absent"), []);

  const orphan = mapFixture({ id: "map.orphan", scope: "city", parentMapId: "map.missing" });
  assert.deepEqual(buildMapBreadcrumbs([...scopedMaps, orphan], "map.orphan"), []);

  const cycleA = mapFixture({ id: "map.a", scope: "region", parentMapId: "map.b" });
  const cycleB = mapFixture({ id: "map.b", scope: "city", parentMapId: "map.a" });
  assert.deepEqual(buildMapBreadcrumbs([cycleA, cycleB], "map.a"), []);
});

test("child scopes come only from declared scope links, never from proximity", () => {
  assert.deepEqual(
    resolveMapChildScopes(scopedMaps, "map.world").map(({ mapId }) => mapId),
    ["map.region"],
  );
  assert.deepEqual(resolveMapChildScopes(scopedMaps, "map.region"), []);
  assert.deepEqual(resolveMapChildScopes(scopedMaps, "map.absent"), []);

  const danglingLink = mapFixture({
    scopeLinks: [
      {
        id: "scopelink.gone",
        childMapId: "map.gone",
        childScope: "city",
        childName: "Absent",
        viaFeatureId: null,
      },
    ],
  });
  assert.deepEqual(resolveMapChildScopes([danglingLink], "map.world"), []);
});

test("geometry is validated against its own declaring space and never clamped", () => {
  const space = { id: "space.region", unit: "grid", width: 1000, height: 700 };
  assert.equal(isGeometryInCoordinateSpace({ x: 0, y: 0 }, space), true);
  assert.equal(isGeometryInCoordinateSpace({ x: 1000, y: 700 }, space), true);
  assert.equal(isGeometryInCoordinateSpace({ x: 1001, y: 700 }, space), false);
  assert.equal(isGeometryInCoordinateSpace({ x: 500, y: -1 }, space), false);
  assert.equal(isGeometryInCoordinateSpace({ x: 500 }, space), false);
  assert.equal(isGeometryInCoordinateSpace({ x: 500, y: 100 }, { ...space, width: 0 }), false);

  // The same numbers are valid in the region space and invalid in the world space: the two
  // coordinate systems are unrelated and nothing converts between them.
  assert.equal(isGeometryInCoordinateSpace({ x: 900, y: 600 }, worldMapFixture.coordinateSpace), false);
});

test("a map hierarchy is rejected when a relationship or a placement cannot be trusted", () => {
  assert.equal(isValidMapHierarchy(scopedMaps, "map.world"), true);
  assert.equal(isValidMapHierarchy([], "map.world"), false);
  assert.equal(isValidMapHierarchy(scopedMaps, "map.region"), false);
  assert.equal(isValidMapHierarchy([regionMapFixture], "map.region"), false);
  assert.equal(isValidMapHierarchy([worldMapFixture, worldMapFixture], "map.world"), false);

  const outOfSpace = mapFixture({
    features: [{ ...worldMapFixture.features[0], geometry: { x: 140, y: 50 } }],
  });
  assert.equal(isValidMapHierarchy([outOfSpace], "map.world"), false);

  const danglingLink = mapFixture({
    scopeLinks: [
      { id: "l", childMapId: "map.gone", childScope: "city", childName: "Absent", viaFeatureId: null },
    ],
  });
  assert.equal(isValidMapHierarchy([danglingLink], "map.world"), false);

  const featureWithoutLayer = mapFixture({
    features: [{ ...worldMapFixture.features[0], layerId: "layer.absent" }],
  });
  assert.equal(isValidMapHierarchy([featureWithoutLayer], "map.world"), false);

  const unknownViaFeature = mapFixture({
    scopeLinks: [
      {
        id: "l",
        childMapId: "map.region",
        childScope: "region",
        childName: "The March",
        viaFeatureId: "feature.absent",
      },
    ],
  });
  assert.equal(isValidMapHierarchy([unknownViaFeature, regionMapFixture], "map.world"), false);
});

const overlayFixtures = [
  { id: "overlay.a", mapId: "map.world", featureId: "feature.archive", kind: "note", label: "A", detail: "a", recordedOn: "day" },
  { id: "overlay.b", mapId: "map.world", featureId: null, kind: "reveal", label: "B", detail: "b", recordedOn: "day" },
  { id: "overlay.c", mapId: "map.region", featureId: "feature.hold", kind: "note", label: "C", detail: "c", recordedOn: "day" },
];

test("overlays are selected by the map and feature they point at", () => {
  assert.deepEqual(resolveMapOverlays(overlayFixtures, "map.world").map(({ id }) => id), [
    "overlay.a",
    "overlay.b",
  ]);
  assert.deepEqual(resolveMapOverlays(overlayFixtures, "map.absent"), []);
  assert.deepEqual(resolveMapOverlays(undefined, "map.world"), []);
  assert.deepEqual(
    resolveFeatureOverlays(overlayFixtures, "map.world", "feature.archive").map(({ id }) => id),
    ["overlay.a"],
  );
  // A map-level overlay belongs to no feature and is never attached to one by accident.
  assert.deepEqual(resolveFeatureOverlays(overlayFixtures, "map.world", null).map(({ id }) => id), [
    "overlay.b",
  ]);
});

test("map marker layers filter features without changing the map document", () => {
  const originalFeatures = [...worldMapFixture.features];
  const visible = filterMapFeaturesByLayers(
    worldMapFixture,
    new Set([worldMapFixture.features[0].layerId]),
  );

  assert.deepEqual(visible.map((feature) => feature.id), [worldMapFixture.features[0].id]);
  assert.deepEqual(worldMapFixture.features, originalFeatures);
  assert.deepEqual(filterMapFeaturesByLayers(worldMapFixture, new Set()), []);
  assert.deepEqual(filterMapFeaturesByLayers(null, new Set()), []);
});

test("an overlay must resolve to a projected target and may carry no geography of its own", () => {
  assert.equal(overlaysResolveAgainstMaps(overlayFixtures, scopedMaps), true);
  assert.equal(overlaysResolveAgainstMaps([], scopedMaps), true);
  assert.equal(overlaysResolveAgainstMaps(undefined, scopedMaps), false);

  const absentMap = [{ ...overlayFixtures[0], mapId: "map.gone" }];
  const absentFeature = [{ ...overlayFixtures[0], featureId: "feature.gone" }];
  const unknownKind = [{ ...overlayFixtures[0], kind: "placement" }];
  assert.equal(overlaysResolveAgainstMaps(absentMap, scopedMaps), false);
  assert.equal(overlaysResolveAgainstMaps(absentFeature, scopedMaps), false);
  assert.equal(overlaysResolveAgainstMaps(unknownKind, scopedMaps), false);

  // A campaign annotation that tries to place something is invalid, not merely ignored.
  for (const forbidden of ["geometry", "coordinateSpaceId", "layerId", "base"]) {
    const placing = [{ ...overlayFixtures[0], [forbidden]: { x: 1, y: 1 } }];
    assert.equal(overlaysResolveAgainstMaps(placing, scopedMaps), false, forbidden);
  }
});

test("client envelope validation accepts only the closed ready shape", () => {
  const ready = {
    version: 1,
    status: "ready",
    applicationId: "dnd2024",
    stateSpaceId: "dnd2024-main",
    audience: { seat: "player", perspective: "player", allowedPerspectives: ["player"] },
    world: {
      name: "Eldervale",
      currentLocationId: "archive",
      map: { imageUrl: "/map.png", alt: "Unlabelled map" },
      history: [],
      people: [],
      factions: [],
      lore: [],
      locations: [{ id: "archive", mapAnchor: { x: 50, y: 50 }, people: [] }],
      rootMapId: "map.world",
      maps: [worldMapFixture],
    },
    campaign,
    party: [],
    rules: [],
  };

  assert.equal(isReadyHubEnvelope(ready), true);
  assert.equal(isReadyHubEnvelope({ ...ready, applicationId: undefined }), false);
  assert.equal(isReadyHubEnvelope({ ...ready, stateSpaceId: "../another-space" }), false);
  assert.equal(isReadyHubEnvelope({
    ...ready,
    world: {
      ...ready.world,
      history: [{ ...history[0], consequence: undefined }],
    },
  }), true);
  assert.equal(isReadyHubEnvelope({
    ...ready,
    currentSituation: { status: "ready", kind: "exploration", locationId: "archive" },
  }), true);
  assert.equal(isReadyHubEnvelope({
    ...ready,
    currentSituation: {
      status: "ready",
      kind: "exploration",
      locationId: "archive",
      affordances: [{ key: "inspect-door", label: "Inspect the door", summary: "Study its runes." }],
    },
  }), true);
  assert.equal(isReadyHubEnvelope({
    ...ready,
    currentSituation: {
      status: "ready",
      kind: "exploration",
      locationId: "archive",
      affordances: [
        { key: "inspect-door", label: "Inspect the door", summary: "Study its runes." },
        { key: "inspect-door", label: "Inspect it again", summary: "Duplicate the key." },
      ],
    },
  }), false);
  assert.equal(isReadyHubEnvelope({
    ...ready,
    currentSituation: { status: "ready", kind: "exploration", locationId: "hidden" },
  }), false);
  assert.equal(isReadyHubEnvelope({
    ...ready,
    currentSituation: {
      status: "ready",
      kind: "combat",
      locationId: "archive",
      combat: {
        id: "encounter.archive.ambush",
        name: "Archive ambush",
        participants: [{ id: "actor.hero", name: "Hero", initiative: 17, active: true }],
        turn: {
          id: "turn.archive.1",
          participationId: "participation.hero",
          actorId: "actor.hero",
          actorName: "Hero",
          ordinal: 0,
          budget: { actions: -1, bonusActions: 1, reactions: 1 },
        },
      },
    },
  }), false);
  assert.equal(isReadyHubEnvelope({ ...ready, status: "denied" }), false);
  assert.equal(isReadyHubEnvelope({ ...ready, audience: { ...ready.audience, seat: "admin" } }), false);
  assert.equal(isReadyHubEnvelope({ ...ready, world: { ...ready.world, locations: [] } }), false);
  assert.equal(
    isReadyHubEnvelope({ ...ready, world: { ...ready.world, history: [{ id: "broken" }] } }),
    false,
  );
  assert.equal(
    isReadyHubEnvelope({
      ...ready,
      world: {
        ...ready.world,
        history: [{ ...history[0], linkedPeople: [{ id: "unknown", name: "Unknown", kind: "Secret" }] }],
      },
    }),
    false,
  );
  assert.equal(
    isReadyHubEnvelope({ ...ready, world: { ...ready.world, factions: [{ id: "broken" }] } }),
    false,
  );
  assert.equal(
    isReadyHubEnvelope({ ...ready, world: { ...ready.world, lore: [{ id: "broken" }] } }),
    false,
  );
  assert.equal(isReadyHubEnvelope({ ...ready, world: { ...ready.world, maps: [] } }), false);
  assert.equal(isReadyHubEnvelope({ ...ready, world: { ...ready.world, rootMapId: "map.absent" } }), false);
  assert.equal(
    isReadyHubEnvelope({ ...ready, rules: [{ id: "dnd2024.shared.action.search", title: "Search" }] }),
    false,
  );
  assert.equal(
    isReadyHubEnvelope({
      ...ready,
      contextSelection: {
        selectedWorldId: "world.elders",
        selectedCampaignId: "campaign.absent",
        worlds: [{
          id: "world.elders",
          name: "The Elder World",
          campaigns: [{ id: "campaign.elders.first", name: "The First Campaign" }],
        }],
      },
    }),
    false,
  );
  assert.equal(
    isReadyHubEnvelope({
      ...ready,
      campaign: { ...campaign, mapOverlays: [{ ...overlayFixtures[0], mapId: "map.gone" }] },
    }),
    false,
  );
});
