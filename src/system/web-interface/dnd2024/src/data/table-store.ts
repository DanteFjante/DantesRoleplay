import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

import type {
  CampaignClue,
  CampaignLogEntry,
  CampaignMapOverlay,
  CampaignOutcome,
  CampaignQuest,
  CampaignReadModel,
  CampaignThread,
  CampaignVisit,
  DeferredHubUpdate,
  MapDocument,
  PartyMemberReadModel,
  ReadyHubEnvelope,
  WorldFaction,
  WorldHistoryEvent,
  WorldLocation,
  WorldLocationScope,
  WorldLoreEntry,
  WorldPersonDirectoryEntry,
  WorldReadModel,
} from "./hub-types";
import { applyDeferredHubUpdate } from "./section-state";

type Identified = { id: string };
type Indexed<T extends Identified> = { ids: string[]; byId: Record<string, T> };
type CampaignScalars = Omit<CampaignReadModel,
  "adventureLog" | "placesVisited" | "outcomes" | "mapOverlays" | "quests" | "threads" | "clues">;
type WorldScalars = Omit<WorldReadModel,
  "maps" | "history" | "locations" | "locationScopes" | "people" | "factions" | "lore">;

type NormalizedCampaign = {
  scalars: CampaignScalars;
  adventureLog: Indexed<CampaignLogEntry>;
  placesVisited: Indexed<CampaignVisit>;
  outcomes: Indexed<CampaignOutcome>;
  mapOverlays: Indexed<CampaignMapOverlay>;
  quests: Indexed<CampaignQuest>;
  threads: Indexed<CampaignThread>;
  clues: Indexed<CampaignClue>;
};

type NormalizedWorld = {
  scalars: WorldScalars;
  maps: Indexed<MapDocument>;
  history: Indexed<WorldHistoryEvent>;
  locations: Indexed<WorldLocation>;
  locationScopes: Indexed<WorldLocationScope>;
  people: Indexed<WorldPersonDirectoryEntry>;
  factions: Indexed<WorldFaction>;
  lore: Indexed<WorldLoreEntry>;
};

type TableShell = Pick<ReadyHubEnvelope,
  "version" | "status" | "applicationId" | "stateSpaceId" | "revision" | "audience" | "contextSelection" |
  "objectQueries">;

export type TableFacetMetadata = {
  generation: number;
  requestToken: number;
  bytes: number;
  confirmedAt: number;
  fresh: boolean;
};

export type ConfirmedFactionPage = {
  coverage?: "complete" | "partial";
  factions: WorldFaction[];
  totalCount: number;
  complete: boolean;
  nextCursor: string | null;
  sourceRevisionFingerprint: string;
};

export type TableState = {
  scope: string | null;
  generation: number;
  shell: TableShell | null;
  campaign: NormalizedCampaign | null;
  world: NormalizedWorld | null;
  campaignDetailsLoaded: boolean;
  requests: Record<string, { generation: number; requestToken: number }>;
  facets: Record<string, TableFacetMetadata>;
  requestTokenFloor: number;
  retainedBytes: number;
};

export const MAX_CONFIRMED_TABLE_BYTES = 16 * 1024 * 1024;
export const MAX_CONFIRMED_TABLE_FACETS = 32;
const safeId = /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,399}$/u;
const initialState: TableState = {
  scope: null,
  generation: 0,
  shell: null,
  campaign: null,
  world: null,
  campaignDetailsLoaded: false,
  requests: {},
  facets: {},
  requestTokenFloor: 0,
  retainedBytes: 0,
};

let nextTableRequestToken = 0;
export function allocateTableRequestToken() { nextTableRequestToken += 1; return nextTableRequestToken; }

export function tableScope(envelope: ReadyHubEnvelope) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const currentActorIds = (envelope.party ?? []).filter((member) => member.isCurrent).map((member) => member.id).sort();
  return JSON.stringify([envelope.applicationId, envelope.stateSpaceId, campaignId, worldId,
    envelope.audience.seat, envelope.audience.perspective, currentActorIds,
    envelope.objectQueries?.campaignSummary?.resolutionFingerprint ?? "no-resolution"]);
}

function indexed<T extends Identified>(values: readonly T[]): Indexed<T> | null {
  const ids: string[] = [];
  const byId: Record<string, T> = {};
  for (const value of values) {
    // Ambiguous or unsafe identity cannot enter a normalized collection. The
    // whole facet commit is withheld so its completeness metadata cannot lie.
    if (!safeId.test(value.id) || Object.hasOwn(byId, value.id)) return null;
    byId[value.id] = value;
    ids.push(value.id);
  }
  return { ids, byId };
}

function values<T extends Identified>(value: Indexed<T>) {
  return value.ids.flatMap((id) => Object.hasOwn(value.byId, id) ? [value.byId[id]!] : []);
}

function normalizeCampaign(value: CampaignReadModel): NormalizedCampaign | null {
  const { adventureLog, placesVisited, outcomes, mapOverlays, quests, threads, clues, ...scalars } = value;
  const normalized = {
    scalars,
    adventureLog: indexed(adventureLog),
    placesVisited: indexed(placesVisited),
    outcomes: indexed(outcomes),
    mapOverlays: indexed(mapOverlays),
    quests: indexed(quests),
    threads: indexed(threads),
    clues: indexed(clues),
  };
  return Object.values(normalized).some((entry) => entry === null) ? null : normalized as NormalizedCampaign;
}

function materializeCampaign(value: NormalizedCampaign): CampaignReadModel {
  return {
    ...value.scalars,
    adventureLog: values(value.adventureLog),
    placesVisited: values(value.placesVisited),
    outcomes: values(value.outcomes),
    mapOverlays: values(value.mapOverlays),
    quests: values(value.quests),
    threads: values(value.threads),
    clues: values(value.clues),
  };
}

function normalizeWorld(value: WorldReadModel): NormalizedWorld | null {
  const { maps, history, locations, locationScopes, people, factions, lore, ...scalars } = value;
  const normalized = {
    scalars,
    maps: indexed(maps),
    history: indexed(history),
    locations: indexed(locations),
    locationScopes: indexed(locationScopes),
    people: indexed(people),
    factions: indexed(factions),
    lore: indexed(lore),
  };
  return Object.values(normalized).some((entry) => entry === null) ? null : normalized as NormalizedWorld;
}

function materializeWorld(value: NormalizedWorld): WorldReadModel {
  return {
    ...value.scalars,
    maps: values(value.maps),
    history: values(value.history),
    locations: values(value.locations),
    locationScopes: values(value.locationScopes),
    people: values(value.people),
    factions: values(value.factions),
    lore: values(value.lore),
  };
}

function shellFrom(envelope: ReadyHubEnvelope): TableShell {
  const { world: _world, campaign: _campaign, party: _party, rules: _rules,
    currentSituation: _currentSituation, ...shell } = envelope;
  return shell;
}

export function materializeTableEnvelope(state: TableState, party: PartyMemberReadModel[]): ReadyHubEnvelope | null {
  if (!state.shell || !state.world || !state.campaign) return null;
  return {
    ...state.shell,
    world: materializeWorld(state.world),
    campaign: materializeCampaign(state.campaign),
    party,
    // Current and Rules have independent Redux owners. Their bootstrap bodies
    // are staging inputs only and are never retained in this table slice.
    rules: [],
  };
}

function byteLength(value: unknown) {
  try { return new TextEncoder().encode(JSON.stringify(value)).byteLength; }
  catch { return MAX_CONFIRMED_TABLE_BYTES + 1; }
}

function summaryFields(campaign: CampaignReadModel) {
  return {
    title: campaign.title,
    subtitle: campaign.subtitle,
    status: campaign.status,
    premise: campaign.premise,
    objective: campaign.objective,
    ...(Object.hasOwn(campaign, "descriptiveFields") ? { descriptiveFields: campaign.descriptiveFields } : {}),
  };
}

function normalizedEnvelope(state: TableState, envelope: ReadyHubEnvelope, preserveDetails: boolean) {
  const previousCampaign = state.campaign && preserveDetails && state.campaignDetailsLoaded
    ? materializeCampaign(state.campaign)
    : null;
  const shell = shellFrom(envelope);
  const world = normalizeWorld(envelope.world);
  const campaign = normalizeCampaign(previousCampaign
    ? { ...previousCampaign, ...summaryFields(envelope.campaign) }
    : envelope.campaign);
  if (!world || !campaign) return null;
  const retainedBytes = byteLength({ shell, world, campaign });
  if (retainedBytes > MAX_CONFIRMED_TABLE_BYTES) return null;
  return { shell, world, campaign, campaignDetailsLoaded: Boolean(previousCampaign), retainedBytes };
}

function commitMetadata(state: TableState, key: string, metadata: TableFacetMetadata) {
  state.facets[key] = { ...metadata, fresh: true };
  const keys = Object.keys(state.facets);
  if (keys.length <= MAX_CONFIRMED_TABLE_FACETS) return;
  keys.sort((left, right) => state.facets[left]!.requestToken - state.facets[right]!.requestToken);
  for (const retired of keys.slice(0, keys.length - MAX_CONFIRMED_TABLE_FACETS)) delete state.facets[retired];
}

const table = createSlice({
  name: "table",
  initialState,
  reducers: {
    bootstrapCommitted: {
      prepare(payload: { scope: string; envelope: ReadyHubEnvelope }) {
        return { payload: { ...payload, requestToken: allocateTableRequestToken() } };
      },
      reducer(state, action: PayloadAction<{ scope: string; envelope: ReadyHubEnvelope; requestToken: number }>) {
        const bytes = byteLength(action.payload.envelope);
        if (bytes > MAX_CONFIRMED_TABLE_BYTES || action.payload.requestToken <= state.requestTokenFloor) return;
        const changed = state.scope !== action.payload.scope;
        const normalized = normalizedEnvelope(state as unknown as TableState, action.payload.envelope, !changed);
        if (!normalized) return;
        state.scope = action.payload.scope;
        // A bootstrap replaces collection bodies even in the same authorized
        // scope. Earlier facet freshness cannot describe those replacement
        // bodies (which may contain deliberately unloaded placeholders).
        state.generation += 1;
        state.requestTokenFloor = action.payload.requestToken;
        state.requests = {};
        state.facets = {};
        state.shell = normalized.shell;
        state.world = normalized.world;
        state.campaign = normalized.campaign;
        state.campaignDetailsLoaded = normalized.campaignDetailsLoaded;
        state.retainedBytes = normalized.retainedBytes;
      },
    },
    requestStarted(state, action: PayloadAction<{ scope: string; key: string; requestToken: number }>) {
      if (state.scope !== action.payload.scope || action.payload.requestToken <= state.requestTokenFloor) return;
      state.requestTokenFloor = action.payload.requestToken;
      state.requests[action.payload.key] = { generation: state.generation, requestToken: action.payload.requestToken };
    },
    requestFinished(state, action: PayloadAction<{ scope: string; key: string; requestToken: number }>) {
      const request = state.requests[action.payload.key];
      if (state.scope === action.payload.scope && request?.generation === state.generation &&
          request.requestToken === action.payload.requestToken) {
        if (state.facets[action.payload.key]) state.facets[action.payload.key]!.fresh = false;
        delete state.requests[action.payload.key];
      }
    },
    campaignDetailsCommitted(state, action: PayloadAction<{ scope: string; key: string; value: CampaignReadModel }>) {
      const metadata = (action as PayloadAction<unknown, string, TableFacetMetadata>).meta;
      const request = state.requests[action.payload.key];
      if (!state.campaign || state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
          request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
          metadata.bytes < 0 || metadata.bytes > MAX_CONFIRMED_TABLE_BYTES) return;
      const current = materializeCampaign(state.campaign);
      // Details owns continuity, not the independently requested knowledge and map facets.
      // Its source envelope may still contain unloaded placeholders for those collections.
      const campaign = normalizeCampaign({ ...action.payload.value, ...summaryFields(current),
        quests: current.quests, clues: current.clues, cluesCoverage: current.cluesCoverage,
        knowledgeAudience: current.knowledgeAudience,
        mapOverlays: current.mapOverlays });
      const retainedBytes = campaign ? byteLength({ shell: state.shell, world: state.world, campaign })
        : MAX_CONFIRMED_TABLE_BYTES + 1;
      if (!campaign || retainedBytes > MAX_CONFIRMED_TABLE_BYTES) { delete state.requests[action.payload.key]; return; }
      state.campaign = campaign;
      state.campaignDetailsLoaded = true;
      delete state.requests[action.payload.key];
      commitMetadata(state, action.payload.key, metadata);
      state.retainedBytes = retainedBytes;
    },
    factionPageCommitted(state, action: PayloadAction<{ scope: string; key: string; cursor: string | null; value: ConfirmedFactionPage }>) {
      const metadata = (action as PayloadAction<unknown, string, TableFacetMetadata>).meta;
      const request = state.requests[action.payload.key];
      if (!state.world || state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
          request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
          metadata.bytes < 0 || metadata.bytes > MAX_CONFIRMED_TABLE_BYTES) return;
      const existing = action.payload.cursor === null ? [] : values(state.world.factions);
      const incomingIds = new Set(action.payload.value.factions.map((entry) => entry.id));
      const merged = [...existing.filter((entry) => !incomingIds.has(entry.id)), ...action.payload.value.factions];
      const factions = indexed(merged);
      if (!factions) { delete state.requests[action.payload.key]; return; }
      const world = { ...state.world, scalars: { ...state.world.scalars, factionDirectory: {
        totalCount: action.payload.value.totalCount,
        complete: action.payload.value.complete,
        coverage: action.payload.value.coverage === "partial" || action.payload.cursor !== null &&
          state.world.scalars.factionDirectory?.coverage === "partial" ? "partial" : "complete",
        nextCursor: action.payload.value.nextCursor,
        sourceRevisionFingerprint: action.payload.value.sourceRevisionFingerprint,
      } }, factions } as NormalizedWorld;
      const retainedBytes = byteLength({ shell: state.shell, world, campaign: state.campaign });
      if (retainedBytes > MAX_CONFIRMED_TABLE_BYTES) { delete state.requests[action.payload.key]; return; }
      state.world = world;
      delete state.requests[action.payload.key];
      commitMetadata(state, action.payload.key, metadata);
      state.retainedBytes = retainedBytes;
    },
    deferredCommitted(state, action: PayloadAction<{ scope: string; key: string; value: DeferredHubUpdate }>) {
      const metadata = (action as PayloadAction<unknown, string, TableFacetMetadata>).meta;
      const request = state.requests[action.payload.key];
      if (!state.shell || !state.world || !state.campaign || state.scope !== action.payload.scope || !request ||
          request.generation !== state.generation || request.generation !== metadata?.generation ||
          request.requestToken !== metadata.requestToken || metadata.bytes < 0 ||
          metadata.bytes > MAX_CONFIRMED_TABLE_BYTES) return;
      const envelope = materializeTableEnvelope(state as unknown as TableState, []);
      if (!envelope) return;
      const updated = applyDeferredHubUpdate(envelope, action.payload.value);
      const shell = shellFrom(updated);
      const world = normalizeWorld(updated.world);
      const campaign = normalizeCampaign(updated.campaign);
      const nextBytes = world && campaign ? byteLength({ shell, world, campaign }) : MAX_CONFIRMED_TABLE_BYTES + 1;
      if (!world || !campaign || nextBytes > MAX_CONFIRMED_TABLE_BYTES) {
        delete state.requests[action.payload.key]; return;
      }
      state.shell = shell;
      state.world = world;
      state.campaign = campaign;
      delete state.requests[action.payload.key];
      commitMetadata(state, action.payload.key, metadata);
      state.retainedBytes = nextBytes;
    },
    /** A visible prefix is source-fenced but its owned continuation still holds this request lease. */
    deferredProgressCommitted(state, action: PayloadAction<{ scope: string; key: string; value: DeferredHubUpdate }>) {
      const metadata = (action as PayloadAction<unknown, string, TableFacetMetadata>).meta;
      const request = state.requests[action.payload.key];
      if (!state.shell || !state.world || !state.campaign || state.scope !== action.payload.scope || !request ||
          request.generation !== state.generation || request.generation !== metadata?.generation ||
          request.requestToken !== metadata.requestToken || metadata.bytes < 0 ||
          metadata.bytes > MAX_CONFIRMED_TABLE_BYTES) return;
      const envelope = materializeTableEnvelope(state as unknown as TableState, []);
      if (!envelope) return;
      const updated = applyDeferredHubUpdate(envelope, action.payload.value);
      const shell = shellFrom(updated);
      const world = normalizeWorld(updated.world);
      const campaign = normalizeCampaign(updated.campaign);
      const nextBytes = world && campaign ? byteLength({ shell, world, campaign }) : MAX_CONFIRMED_TABLE_BYTES + 1;
      if (!world || !campaign || nextBytes > MAX_CONFIRMED_TABLE_BYTES) return;
      state.shell = shell;
      state.world = world;
      state.campaign = campaign;
      // Do not retire the matching flight here: later prefixes and the terminal response must
      // prove the same request token before replacing this bounded Redux value.
      commitMetadata(state, action.payload.key, metadata);
      state.retainedBytes = nextBytes;
    },
    invalidated(state) {
      state.generation += 1;
      state.requests = {};
      for (const facet of Object.values(state.facets)) facet.fresh = false;
    },
    scopeCleared(state) {
      const generation = state.generation + 1;
      Object.assign(state, { ...initialState, generation, requestTokenFloor: state.requestTokenFloor });
    },
  },
});

export const tableActions = table.actions;
export const tableReducer = table.reducer;

export function commitCampaignDetails(payload: { scope: string; key: string; value: CampaignReadModel },
  meta: Omit<TableFacetMetadata, "fresh">) {
  return { type: tableActions.campaignDetailsCommitted.type, payload, meta };
}

export function commitDeferredTable(payload: { scope: string; key: string; value: DeferredHubUpdate },
  meta: Omit<TableFacetMetadata, "fresh">) {
  return { type: tableActions.deferredCommitted.type, payload, meta };
}

export function commitDeferredTableProgress(payload: { scope: string; key: string; value: DeferredHubUpdate },
  meta: Omit<TableFacetMetadata, "fresh">) {
  return { type: tableActions.deferredProgressCommitted.type, payload, meta };
}

export function commitFactionPage(payload: { scope: string; key: string; cursor: string | null; value: ConfirmedFactionPage },
  meta: Omit<TableFacetMetadata, "fresh">) {
  return { type: tableActions.factionPageCommitted.type, payload, meta };
}

export function tableFacetFresh(state: TableState, scope: string, key: string, maximumAgeMs: number) {
  if (state.scope !== scope) return false;
  const facet = state.facets[key];
  return Boolean(facet?.fresh && Date.now() - facet.confirmedAt <= maximumAgeMs);
}
