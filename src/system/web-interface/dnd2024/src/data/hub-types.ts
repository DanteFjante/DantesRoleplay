export type Perspective = "player" | "dm";
export type MainTabId = "world" | "campaign" | "party" | "current" | "rules" | "content";
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

export type VisualMedia = {
  imageUrl: string;
  alt: string;
  width: number;
  height: number;
};

export type VisualMediaAttachment = VisualMedia & {
  mediaId: string;
  role: "portrait" | "setting" | "map" | "illustration" | "icon" | "scene" | "handout";
  caption: string;
};

export type EntityVisualMedia = {
  portrait?: VisualMedia;
  setting?: VisualMedia;
  map?: VisualMedia;
  illustration?: VisualMedia;
  icon?: VisualMedia;
  scene?: VisualMedia;
  handout?: VisualMedia;
  gallery?: VisualMediaAttachment[];
};

export type LocationPerson = {
  id: string;
  initials: string;
  name: string;
  kind: "NPC" | "Creature";
  role: string;
  summary: string;
  background: string;
  disposition: string;
  portrait?: VisualMedia;
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
  media?: EntityVisualMedia;
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
  consequence?: string;
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
  preview?: VisualMedia;
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
  handout?: VisualMedia;
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
  media?: VisualMedia;
};

export type PartyKnowledgeEntry = {
  id: string;
  stance: string;
  kind: string;
  text: string;
};

export type SectionFailureCategory = "authorization" | "stale-data" | "transport" | "http" | "incompatible-data" | "unknown";

export type SectionState<T> =
  | { status: "idle"; data: null }
  | { status: "loading"; data: T | null }
  | { status: "ready" | "empty"; data: T; source: "canonical" | "provisional" }
  | {
      status: "stale";
      data: T;
      source: "canonical" | "provisional";
      failureCategory: Exclude<SectionFailureCategory, "authorization">;
      diagnosticId: string;
      errorCode?: string;
      httpStatus?: number;
    }
  | {
      status: "error";
      data: null;
      failureCategory: Exclude<SectionFailureCategory, "authorization">;
      diagnosticId: string;
      errorCode?: string;
      httpStatus?: number;
    }
  | {
      status: "forbidden";
      data: null;
      failureCategory: "authorization";
      diagnosticId: string;
      errorCode?: string;
    };

export type CharacterSheetProjection = {
  version: 1;
  subject: { id: string; name: string };
  identity?: { pronouns?: string; appearance?: string; biography?: string; playerNotes?: string };
  origin?: { speciesId: string; backgroundId: string };
  experience?: { total: number };
  classes?: Array<{ id: string; name: string; classId: string; level: number; subclassId: string | null }>;
  level?: number;
  proficiencyBonus?: number;
  abilities?: Array<{ id: string; score: number; modifier: number }>;
  savingThrows?: Array<{ ability: string; proficient: boolean; modifier: number }>;
  skills?: Array<{ id: string; ability: string; proficient: boolean; expertise: boolean; modifier: number }>;
  initiative?: { ability: "dex"; modifier: number };
  hitPoints?: { current: number; maximum: number; maximumReduction: number };
  temporaryHitPoints?: { amount: number };
  armorClass?: { value: number };
  body?: { sizeId: string };
  movement?: Array<{ id: string; numerator: number; denominator: number; unitId: string }>;
  senses?: Array<{ id: string; numerator?: number; denominator?: number; unitId?: string }>;
  conditions?: Array<{ id: string; level: number | null }>;
  proficiencies?: Array<{ id: string; rankId: string }>;
  features?: Array<{ featureId: string; grantedById: string; grantKind: string; classLevel: number | null }>;
  resources?: Array<{ id: string; name: string; definitionId: string; expended: number }>;
  spellcasting?: Array<{
    id: string;
    name: string;
    sourceDefinitionId: string;
    abilityId: string;
    preparedSpellIds: string[];
    availableSpellIds: string[];
  }>;
  actions?: Array<{ id: string; name: string; activityIds: string[] }>;
};

export type NamedCharacterReference = { id: string; label: string };

export type CharacterInventoryItemV2 = {
  id: string;
  name: string;
  definition: NamedCharacterReference;
  quantity: number;
  slot: string;
  parentItemId: string | null;
  order: number;
  depth: number;
  childCount: number;
  deeperContentsOmitted: boolean;
  equipmentSlots: NamedCharacterReference[];
  media?: EntityVisualMedia;
};

export type CharacterWalletV2 = {
  coinCount: number;
  copperValue: number;
  gpCount: number;
  denominations: Array<{
    denomination: NamedCharacterReference;
    code: "cp" | "sp" | "ep" | "gp" | "pp";
    count: number;
    copperValuePerCoin: 1 | 10 | 50 | 100 | 1000;
    totalCopperValue: number;
  }>;
};

export type CharacterSheetProjectionV2 = {
  version: 2;
  subject: NamedCharacterReference;
  identity?: { pronouns?: string; appearance?: string; biography?: string; playerNotes?: string };
  origin?: { species: NamedCharacterReference; background: NamedCharacterReference };
  experience?: { total: number };
  classes?: Array<{
    id: string;
    name: string;
    class: NamedCharacterReference;
    level: number;
    subclass: NamedCharacterReference | null;
  }>;
  level?: number;
  proficiencyBonus?: number;
  abilities?: Array<{ ability: NamedCharacterReference; score: number; modifier: number }>;
  savingThrows?: Array<{ ability: NamedCharacterReference; proficient: boolean; modifier: number }>;
  skills?: Array<{
    skill: NamedCharacterReference;
    ability: NamedCharacterReference;
    proficient: boolean;
    expertise: boolean;
    modifier: number;
  }>;
  initiative?: { ability: NamedCharacterReference; modifier: number };
  hitPoints?: { current: number; maximum: number; maximumReduction: number };
  temporaryHitPoints?: { amount: number };
  armorClass?: { value: number };
  body?: { size: NamedCharacterReference };
  movement?: Array<{
    kind: NamedCharacterReference;
    numerator: number;
    denominator: number;
    unit: NamedCharacterReference;
  }>;
  senses?: Array<{
    sense: NamedCharacterReference;
    numerator?: number;
    denominator?: number;
    unit?: NamedCharacterReference;
  }>;
  conditions?: Array<{ condition: NamedCharacterReference; level: number | null }>;
  proficiencies?: Array<{ proficiency: NamedCharacterReference; rank: NamedCharacterReference }>;
  features?: Array<{
    feature: NamedCharacterReference;
    grantedBy: NamedCharacterReference;
    grantKind: NamedCharacterReference;
    classLevel: number | null;
  }>;
  resources?: Array<{ id: string; name: string; definition: NamedCharacterReference; expended: number }>;
  spellcasting?: Array<{
    id: string;
    name: string;
    sourceDefinition: NamedCharacterReference;
    ability: NamedCharacterReference;
    preparedSpells: NamedCharacterReference[];
    availableSpells: NamedCharacterReference[];
  }>;
  actions?: Array<{ id: string; name: string; activities: NamedCharacterReference[] }>;
  inventory: {
    items: CharacterInventoryItemV2[];
    contentsDepth: 4;
    mayOmitDeeperContents: true;
  };
  wallet: CharacterWalletV2;
};

export type CharacterDossierSource = { sourceId: string; locator: string };

export type CharacterDossierDefinition = {
  id: string;
  label: string;
  canonicalName: string;
  kind: string;
  status: "active" | "identity-only";
  summary: string | null;
  source: CharacterDossierSource | null;
};

export type CharacterDossierMetadata = {
  origin: {
    species: CharacterDossierDefinition;
    background: CharacterDossierDefinition;
    traits: Array<{
      key: string;
      label: string;
      status: "active" | "pending";
      reason: string | null;
      mechanicId: string | null;
      source: CharacterDossierSource | null;
    }>;
  };
  classes: Array<{
    id: string;
    name: string;
    definition: CharacterDossierDefinition;
    level: number;
    subclass: NamedCharacterReference | null;
  }>;
  features: Array<{
    definition: CharacterDossierDefinition;
    grantedBy: CharacterDossierDefinition;
    grantKind: string;
    classLevel: number | null;
    configurationKey: string | null;
    implementation: {
      status: "recorded" | "executable" | "pending";
      reason: string | null;
      entitlementKey: string | null;
      nextCapabilityId: string | null;
    };
  }>;
  inventory: {
    definitions: CharacterDossierDefinition[];
    contentsDepth: 4;
    mayOmitDeeperContents: true;
  };
  levelOneRules: {
    test: "character-level-one-rules-project";
    subjectId: string;
    armorClass: Record<string, unknown>;
    attacks: Array<Record<string, unknown>>;
    senses: Array<Record<string, unknown>>;
    savingThrowCircumstances: Array<Record<string, unknown>>;
    spellAccess: Record<string, unknown>;
    equipment: Record<string, unknown>;
    entitlements: Array<{
      ownerDefinitionId: string;
      entitlementKey: string;
      status: "active" | "pending";
      reason: string | null;
      mechanicId: string | null;
      nextCapabilityId: string | null;
      knownValues: Record<string, unknown>;
      missingValues: string[];
      source: CharacterDossierSource;
    }>;
  };
  definitions: CharacterDossierDefinition[];
  provenance: {
    sheetQueryId: "dnd2024.query.character-sheet-v2";
    sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project";
    dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project";
    definitionCount: number;
    inventoryDepth: 4;
    ruleTextPolicy: "canonical-only";
  };
};

export type CanonicalCharacterData = CharacterSheetProjectionV2 & {
  dossier: CharacterDossierMetadata;
  projection?: {
    stateSpaceFingerprint: string;
    resolutionFingerprint: string;
    resultFingerprint: string;
    sourceRevisionFingerprint: string;
  };
};

export type CanonicalCharacterResult =
  | { status: "ready"; data: CanonicalCharacterData; failureCategory: null; diagnosticId: string }
  | {
      status: "error";
      data: null;
      failureCategory: Exclude<SectionFailureCategory, "authorization">;
      diagnosticId: string;
      errorCode?: string;
      httpStatus?: number;
    }
  | {
      status: "forbidden";
      data: null;
      failureCategory: "authorization";
      diagnosticId: string;
      errorCode?: string;
      httpStatus?: number;
    };

export type PartyMemberReadModel = {
  id: string;
  initials: string;
  name: string;
  detail: string;
  status: string;
  isCurrent: boolean;
  portrait?: VisualMedia;
  recordStatus: string;
  sheetStatus: "canonical" | "provisional" | "unavailable" | "empty";
  inventoryStatus: "canonical" | "provisional" | "unavailable" | "empty";
  sheetState: SectionState<PartyDossierEntry[]>;
  inventoryState: SectionState<PartyDossierEntry[]>;
  sheet: PartyDossierEntry[];
  knowledge: PartyKnowledgeEntry[];
  backstory: PartyDossierEntry[];
  origin: PartyDossierEntry[];
  inventory: PartyDossierEntry[];
  characterSheet?: CanonicalCharacterData;
};

export type RuleReadModel = {
  id: string;
  resolutionKey: string;
  title: string;
  summary: string;
  order: number;
  section: {
    id: string;
    label: string;
    order: number;
  };
  blocks: Array<{
    kind: "paragraph" | "steps" | "list" | "callout";
    heading: string | null;
    body: string | null;
    items: string[];
  }>;
  examples: Array<{
    title: string;
    body: string;
  }>;
  relatedRuleIds: string[];
  citations: Array<{
    sourceId: string;
    locator: string;
  }>;
  authority: {
    mechanicIds: string[];
    procedureIds: string[];
  };
  visibility: "public" | "dm";
  source: {
    ownerId: string;
    label: string;
    classification: "core" | "homebrew" | "compatibility" | "third-party";
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

export type CurrentSceneAffordance = {
  key: string;
  label: string;
  summary: string;
};

export type TacticalEncounterBoard = {
  revision: number;
  columns: number;
  rows: number;
  feetPerSquare: number;
  terrain: Array<{
    id: string;
    label: string;
    area: { x: number; y: number; width: number; height: number };
    movementCost: number;
  }>;
  obstacles: Array<{
    id: string;
    label: string;
    area: { x: number; y: number; width: number; height: number };
  }>;
  participants: Array<{
    id: string;
    name: string;
    initiative: number;
    active: boolean;
    position: { x: number; y: number; width: number; height: number; elevationFeet: number; revision: number };
  }>;
  turn?: { id: string; participationId: string; actorId?: string; actorName: string; ordinal: number };
};

export type CurrentSituationReadModel =
  | {
      status: "unavailable";
      locationId?: string;
      message: string;
    }
  | {
      status: "ready";
      kind: "recorded";
      locationId?: string;
      recorded: {
        id: string;
        kind: "out-of-character" | "conversation" | "combat" | "exploration" | "investigation" |
          "travel" | "rest" | "downtime" | "other";
        summary: string;
        participants: Array<{ id: string; name: string; entityId?: string }>;
        interactions: Array<{ id: string; ordinal: number; role: "player" | "assistant"; text: string }>;
        location?: { id?: string; name: string };
      };
    }
  | {
      status: "ready";
      kind: "exploration";
      locationId: string;
      scene?: VisualMedia;
      affordances?: CurrentSceneAffordance[];
    }
  | {
      status: "ready";
      kind: "conversation";
      locationId: string;
      scene?: VisualMedia;
      affordances?: CurrentSceneAffordance[];
      conversation: {
        id: string;
        name: string;
        summary?: string;
        participants: Array<{ id: string; name: string; portrait?: VisualMedia }>;
      };
    }
  | {
      status: "ready";
      kind: "combat";
      locationId: string;
      scene?: VisualMedia;
      affordances?: CurrentSceneAffordance[];
      combat: {
        id: string;
        name: string;
        board?: TacticalEncounterBoard;
        round?: { id: string; number: number };
        turn?: {
          id: string;
          participationId: string;
          actorId: string;
          actorName: string;
          ordinal: number;
          budget?: { actions: number; bonusActions: number; reactions: number };
        };
        participants: Array<{
          id: string;
          name: string;
          initiative: number;
          active: boolean;
          portrait?: VisualMedia;
        }>;
      };
    };

export type KnownRouteReadModel = {
  id: string;
  originId: string;
  destinationId: string;
  destinationName: string;
  detail: string;
  mode: "on-foot";
  durationMinutes: number;
};

export type ReadyHubEnvelope = {
  version: 1;
  status: "ready";
  applicationId: string;
  stateSpaceId: string;
  revision: string;
  audience: HubAudience;
  contextSelection?: HubContextSelection;
  world: WorldReadModel;
  campaign: CampaignReadModel;
  party: PartyMemberReadModel[];
  rules: RuleReadModel[];
  currentSituation?: CurrentSituationReadModel;
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
  currentSituation?: CurrentSituationReadModel;
  knownRoutes?: KnownRouteReadModel[];
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
      worldEntityIds?: string[];
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
      worldEntityIds?: string[];
    }>;
    sessions: Array<{
      id: string;
      status: "active" | "ended";
      ordinal: number;
      updatedAtUtc: string | null;
      worldEntityIds?: string[];
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
    visits: Array<{
      id: string;
      locationId: string;
      firstVisitedMinute: number;
      lastVisitedMinute: number;
      visitCount: number;
      status: "current" | "departed";
      summary: string;
      memory: string;
      gmContext?: string;
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
    media?: EntityVisualMedia;
    entries: Array<{ kind: string; key: string; label: string; details?: string }>;
    canonical?: CanonicalCharacterData;
    canonicalResult?: CanonicalCharacterResult;
  }>;
  knowledge: {
    status: "ready" | "empty" | "unavailable";
    entries: Array<{
      text: string;
      stance: string;
      presentationKind: string;
      subject?: { id: string; name: string };
      media?: EntityVisualMedia;
    }>;
    locations: Array<{
      name: string;
      entries: Array<{
        text: string;
        stance: string;
        presentationKind: string;
        subject?: { id: string; name: string };
      }>;
    }>;
  };
  chronology: {
    status: "ready" | "empty" | "unavailable";
    perspective: Perspective;
    entries: Array<{
      id: string;
      occurredAtMinute: number;
      dateLabel: string;
      precision: "exact" | "approximate" | "era";
      title: string;
      summary: string;
      subjects?: Array<{ id: string; name: string }>;
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
    mapVisual?: { imageUrl: string; alt: string };
    media?: EntityVisualMedia;
  }>;
  worldDirectory?: {
    people: Array<{
      id: string;
      name: string;
      kind: "NPC" | "Creature";
      locationId: string;
      media?: EntityVisualMedia;
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
