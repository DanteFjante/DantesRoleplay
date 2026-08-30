export type Perspective = "player" | "dm";
export type MainTabId = "world" | "campaign" | "party" | "current" | "rules";
export type PartySectionId = "overview" | "sheet" | "knowledge" | "backstory" | "origin" | "inventory";
export type CampaignSectionId =
  | "overview"
  | "log"
  | "places"
  | "outcomes"
  | "quests"
  | "threads"
  | "clues";
export type WorldSectionId =
  | "overview"
  | "map"
  | "history"
  | "locations"
  | "people"
  | "factions"
  | "lore";
export type LocationSectionId = "details" | "people" | "holdings";

export type LocationPerson = {
  id: string;
  initials: string;
  name: string;
  kind: "NPC" | "Creature";
  role: string;
  summary: string;
  background: string;
  disposition: string;
  motive?: string;
  dmSecret?: string;
};

export type LocationHolding = {
  id: string;
  name: string;
  kind: string;
  status: string;
  summary: string;
  contents: Array<{ name: string; quantity: number; detail: string }>;
  dmNote: string;
};

export type WorldLocation = {
  id: string;
  name: string;
  region: string;
  kind: string;
  status: string;
  summary: string;
  description: string;
  atmosphere: string;
  landmarks: string[];
  observations: string[];
  routes: Array<{ destination: string; detail: string }>;
  mapAnchor: { x: number; y: number };
  people: LocationPerson[];
  holdings?: LocationHolding[];
  dmSecret?: string;
};

export type WorldHistoryEvent = {
  id: string;
  sortOrder: number;
  date: string;
  era: string;
  title: string;
  category: string;
  region: string;
  status: string;
  summary: string;
  consequence: string;
  linkedLocations: Array<{ id: string; name: string }>;
  linkedPeople: Array<{ id: string; name: string; kind: "NPC" | "Creature" }>;
  dmTruth?: string;
  dmConsequence?: string;
};

export type WorldPersonDirectoryEntry = LocationPerson & {
  location: { id: string; name: string; region: string };
};

export type WorldFaction = {
  id: string;
  monogram: string;
  name: string;
  kind?: "Organization" | "Sovereign power";
  influence: string;
  status: string;
  summary: string;
  goals: string[];
  methods: string[];
  assets?: string[];
  members: Array<{ id: string; name: string; kind: "NPC" | "Creature" }>;
  territories: Array<{ id: string; name: string; region: string }>;
  relationships: Array<{ id: string; name: string; stance: string }>;
  dmAgenda?: string;
  dmSecret?: string;
};

export type WorldLoreEntry = {
  id: string;
  title: string;
  category: string;
  status: string;
  summary: string;
  body: string;
  linkedLocations: Array<{ id: string; name: string }>;
  linkedPeople: Array<{ id: string; name: string; kind: "NPC" | "Creature" }>;
  linkedFactions: Array<{ id: string; name: string }>;
  linkedHistory: Array<{ id: string; title: string; date: string }>;
  dmTruth?: string;
  dmNote?: string;
};

export type MapScope = "world" | "region" | "city" | "location";

export type MapCoordinateSpace = {
  id: string;
  unit: string;
  width: number;
  height: number;
};

export type MapLayer = {
  id: string;
  kind: "base" | "terrain" | "labels" | "markers";
  order: number;
  label: string;
};

export type MapFeature = {
  id: string;
  kind: "point";
  layerId: string;
  coordinateSpaceId: string;
  geometry: { x: number; y: number };
  name: string;
  detail: string;
  locationId: string | null;
};

export type MapScopeLink = {
  id: string;
  childMapId: string;
  childScope: MapScope;
  childName: string;
  viaFeatureId: string | null;
};

export type MapDocument = {
  id: string;
  scope: MapScope;
  parentMapId: string | null;
  subject: { kind: string; id: string; name: string };
  coordinateSpace: MapCoordinateSpace;
  base: { imageUrl: string; alt: string } | null;
  layers: MapLayer[];
  features: MapFeature[];
  scopeLinks: MapScopeLink[];
};

export type CampaignMapOverlay = {
  id: string;
  mapId: string;
  featureId: string | null;
  kind: "note" | "reveal";
  label: string;
  detail: string;
  recordedOn: string;
};

export type MapBreadcrumb = { id: string; name: string; scope: MapScope };

export type MapChildScope = {
  id: string;
  mapId: string;
  name: string;
  scope: MapScope;
  viaFeatureId: string | null;
};

export type MapSearchResult = {
  mapId: string;
  mapName: string;
  mapScope: MapScope;
  featureId: string;
  locationId: string | null;
  name: string;
  detail: string;
};

export type MapFactionInfluence = {
  factionId: string;
  name: string;
  influence: string;
  featureIds: string[];
};

export type MapFeatureGroup = {
  layer: MapLayer;
  features: MapFeature[];
};

export type WorldReadModel = {
  id: string;
  name: string;
  era: string;
  summary: string;
  premise: string;
  currentLocationId: string;
  map: { imageUrl: string; alt: string };
  rootMapId: string;
  maps: MapDocument[];
  regions: Array<{ name: string; detail: string; count: number }>;
  facts: Array<{ label: string; value: string; detail: string }>;
  history: WorldHistoryEvent[];
  locations: WorldLocation[];
  people: WorldPersonDirectoryEntry[];
  factions: WorldFaction[];
  lore: WorldLoreEntry[];
};

export type CampaignEntityLinks = {
  locations: Array<{ id: string; name: string }>;
  people: Array<{ id: string; name: string; kind: "NPC" | "Creature" }>;
  factions: Array<{ id: string; name: string }>;
};

export type CampaignLogEntry = {
  id: string;
  sortOrder: number;
  session: string;
  date: string;
  title: string;
  summary: string;
  result: string;
  links: CampaignEntityLinks;
  dmNote?: string;
  dmThread?: string;
};

export type CampaignVisit = {
  id: string;
  location: { id: string; name: string; region: string };
  firstVisited: string;
  lastVisited: string;
  visitCount: number;
  status: string;
  summary: string;
  memory: string;
  dmContext?: string;
};

export type CampaignOutcome = {
  id: string;
  sortOrder: number;
  status: string;
  title: string;
  situation: string;
  result: string;
  consequence: string;
  links: CampaignEntityLinks;
  dmRamification?: string;
};

export type CampaignQuestObjective = {
  id: string;
  status: string;
  text: string;
};

export type CampaignQuest = {
  id: string;
  sortOrder: number;
  kind: string;
  status: string;
  title: string;
  summary: string;
  nextStep: string;
  objectives: CampaignQuestObjective[];
  links: CampaignEntityLinks;
  dmContext?: string;
};

export type CampaignThread = {
  id: string;
  sortOrder: number;
  category: string;
  status: string;
  pressure: string;
  title: string;
  summary: string;
  lastChanged: string;
  links: CampaignEntityLinks;
  dmTruth?: string;
  dmReveal?: string;
};

export type CampaignClue = {
  id: string;
  sortOrder: number;
  mystery: string;
  status: string;
  title: string;
  detail: string;
  partyConclusion: string;
  discoveredAt: string;
  links: CampaignEntityLinks;
  dmTruth?: string;
  dmConnection?: string;
};

export type CampaignReadModel = {
  title: string;
  subtitle: string;
  status: string;
  chapter: string;
  question: string;
  premise: string;
  progress: string;
  objective: string;
  stakes: string;
  nextMilestone: string;
  facts: Array<{ label: string; value: string; detail: string }>;
  adventureLog: CampaignLogEntry[];
  placesVisited: CampaignVisit[];
  outcomes: CampaignOutcome[];
  mapOverlays: CampaignMapOverlay[];
  quests: CampaignQuest[];
  threads: CampaignThread[];
  clues: CampaignClue[];
  dmContext?: string;
};

export type PartyDossierEntry = {
  id: string;
  kind: string;
  title: string;
  detail: string;
};

export type PartyKnowledgeEntry = {
  id: string;
  stance: string;
  kind: string;
  text: string;
};

export type PartyMemberReadModel = {
  id: string;
  initials: string;
  name: string;
  detail: string;
  status: string;
  isCurrent: boolean;
  recordStatus: string;
  sheet: PartyDossierEntry[];
  knowledge: PartyKnowledgeEntry[];
  backstory: PartyDossierEntry[];
  origin: PartyDossierEntry[];
  inventory: PartyDossierEntry[];
};

export type RuleCategory = "Action" | "Reaction";

export type RuleReadModel = {
  id: string;
  title: string;
  category: RuleCategory;
  summary: string;
  source: {
    id: "source.dnd2024.srd-5.2.1";
    locator: string;
  };
};

export type HubAudience = {
  seat: Perspective;
  perspective: Perspective;
  allowedPerspectives: Perspective[];
};

export type HubContextSelection = {
  selectedWorldId: string;
  selectedCampaignId: string;
  worlds: Array<{
    id: string;
    name: string;
    campaigns: Array<{ id: string; name: string }>;
  }>;
};

export type ReadyHubEnvelope = {
  version: 1;
  status: "ready";
  revision: string;
  audience: HubAudience;
  contextSelection?: HubContextSelection;
  world: WorldReadModel;
  campaign: CampaignReadModel;
  party: PartyMemberReadModel[];
  rules: RuleReadModel[];
};

export type DeniedHubEnvelope = {
  version: 1;
  status: "denied";
  message: string;
};

export type ConnectedCampaignEnvelope = {
  version: 1;
  status: "connected";
  applicationId: string;
  stateSpaceId: string;
  currentLocationId?: string;
  audience: {
    seat: Perspective;
    perspective?: Perspective;
    allowedPerspectives: Perspective[];
  };
  contextSelection: HubContextSelection;
  campaign: {
    id: string;
    name: string;
    status: string | null;
    premise: string | null;
    partyGoals: string[];
    toneAndBoundaries: string[];
    chapters: Array<{
      id: string;
      status: "active" | "closed";
      title: string;
      partyQuestion: string;
      createdAtUtc: string | null;
      updatedAtUtc: string | null;
      closingSummary?: string;
      gmContext?: string;
    }>;
    arcs: Array<{
      id: string;
      status: "active" | "resolved" | "abandoned";
      title: string;
      partyStake: string;
      createdAtUtc: string | null;
      updatedAtUtc: string | null;
      closingSummary?: string;
      gmContext?: string;
    }>;
    sessions: Array<{
      id: string;
      status: "active" | "ended";
      ordinal: number;
      updatedAtUtc: string | null;
      recap?: {
        chapter: { id: string; status: "active"; title: string; partyQuestion: string };
        arc: { id: string; status: "active"; title: string; partyStake: string };
        milestones: Array<{
          chapterId: string;
          title: string;
          closingSummary: string;
          timestamp: string;
          sequence: number;
        }>;
      };
    }>;
  };
  actor: {
    id: string;
    name: string;
    state: string | null;
    entries: Array<{ kind: string; key: string; label: string; details?: string }>;
  };
  party?: Array<{
    id: string;
    name: string;
    state: string | null;
    current: boolean;
    entries: Array<{ kind: string; key: string; label: string; details?: string }>;
  }>;
  knowledge: {
    status: "ready" | "empty" | "unavailable";
    entries: Array<{ text: string; stance: string; presentationKind: string }>;
    locations: Array<{
      name: string;
      entries: Array<{ text: string; stance: string; presentationKind: string }>;
    }>;
  };
  locationDirectoryAudience?: Perspective;
  locationDirectory?: Array<{
    id: string;
    name: string;
    kind?: string;
    summary?: string;
    containerId?: string;
    containmentSlot?: string;
    mapAnchor?: { x: number; y: number };
    mapVisual?: { assetKey: string; alt: string };
  }>;
  worldDirectory?: {
    people: Array<{
      id: string;
      name: string;
      kind: "NPC" | "Creature";
      locationId: string;
      motive?: { status: string; visibility: string; summary: string };
    }>;
    factions: Array<{
      id: string;
      name: string;
      status: string;
      visibility: string;
      summary: string;
      goals: string[];
      methods: string[];
      assets: string[];
      agenda: { state: string; summary: string };
      memberIds: string[];
      territoryIds: string[];
      alliedIds: string[];
      opposedIds: string[];
    }>;
    holdings: Array<{
      id: string;
      name: string;
      locationId: string;
      kind: string;
    }>;
  };
  rules?: RuleReadModel[];
};

export type CharacterCreationRequiredEnvelope = {
  version: 1;
  status: "character-creation-required";
  applicationId: string;
  stateSpaceId: string;
  campaignId: string;
  characterId: string;
  message: string;
};

export type UnavailableHubEnvelope = {
  version: 1;
  status: "unavailable";
  message: string;
};

export type HubEnvelope = ReadyHubEnvelope | DeniedHubEnvelope | ConnectedCampaignEnvelope |
  CharacterCreationRequiredEnvelope | UnavailableHubEnvelope;
