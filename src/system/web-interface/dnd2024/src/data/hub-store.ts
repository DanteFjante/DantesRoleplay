import { combineReducers, configureStore, createSelector, createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { useDispatch, useSelector, type TypedUseSelectorHook } from "react-redux";

import type { CurrentPlayReadEvidence, CurrentSituationReadModel, InventoryContainerResult, PartyMemberReadModel, ReadyHubEnvelope, RulesReferencePublication, WorldLocation } from "./hub-types";
import { preserveMemberOnTransientFailure } from "./section-state";
import type { ItemDetailsRequest, ItemDetailsResult } from "../server/item-view-client";
import type { ItemUsesRequest, ItemUsesResult } from "../server/item-uses-client";
import type { ItemRecipesRequest, ItemRecipesResult } from "../server/item-recipes-client";
import type { InstalledContentPage } from "../server/effective-content";
import {
  materializeTableEnvelope,
  tableReducer,
} from "./table-store";
import { connectedSourceActions, connectedSourceReducer, selectConnectedSource } from "./connected-source-state";
import { collectionActions, collectionReducer } from "./collection-state";

export {
  allocateTableRequestToken,
  commitCampaignDetails,
  commitDeferredTable,
  commitFactionPage,
  tableActions,
  tableFacetFresh,
  tableScope,
  MAX_CONFIRMED_TABLE_BYTES,
  MAX_CONFIRMED_TABLE_FACETS,
} from "./table-store";

export type CharacterFacet = "sheet" | "details";
export type ConfirmedFacetMetadata = { generation: number; requestToken: number; bytes: number; confirmedAt: number };
type ConfirmedFacet<T> = ConfirmedFacetMetadata & { value: T };
type CharacterFacets = Partial<Record<CharacterFacet, ConfirmedFacet<PartyMemberReadModel>>> & {
  inventory?: ConfirmedFacet<InventoryContainerResult>;
};
type PortraitAuthority = {
  portrait: PartyMemberReadModel["portrait"];
  coverage: "confirmed" | "denied";
  requestToken: number;
  bytes: number;
};

export type HubConfirmedState = {
  scope: string | null;
  generation: number;
  rosterIds: string[];
  rosterById: Record<string, PartyMemberReadModel>;
  facetsById: Record<string, CharacterFacets>;
  portraitById: Record<string, PortraitAuthority>;
  requestsByKey: Record<string, { generation: number; requestToken: number }>;
  facetOrder: Record<string, { requestToken: number; bytes: number }>;
  retainedBytes: number;
};

const addressableActorId = /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/u;

export function characterScope(envelope: ReadyHubEnvelope) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const currentActorIds = (envelope.party ?? []).filter((member) => member.isCurrent).map((member) => member.id).sort();
  return JSON.stringify([envelope.applicationId, envelope.stateSpaceId, campaignId,
    worldId, envelope.audience.seat, envelope.audience.perspective, currentActorIds,
    envelope.objectQueries?.campaignSummary?.resolutionFingerprint ?? "no-resolution"]);
}

/**
 * Current is private to the same authoritative table binding as a character
 * read. In particular, the current actor set is part of the key: changing
 * player identity under the same campaign must not reuse a previous scene.
 */
export function currentScope(envelope: ReadyHubEnvelope) { return characterScope(envelope); }

export type ItemFacet = "details" | "uses" | "recipes";
export type ItemFacetRequest = ItemDetailsRequest | ItemUsesRequest | ItemRecipesRequest;
export type ItemFacetResult = ItemDetailsResult | ItemUsesResult | ItemRecipesResult;
type ItemEntry = ConfirmedFacetMetadata & { value: ItemFacetResult };
type ItemState = {
  scope: string | null;
  generation: number;
  /** Restarts active facet reads after an explicit invalidation, not initial scope admission. */
  refreshEpoch: number;
  entries: Record<string, ItemEntry>;
  requests: Record<string, { generation: number; requestToken: number }>;
  order: Record<string, { requestToken: number; bytes: number }>;
  retainedBytes: number;
};

/** Item reads are scoped to the current authorized table binding, never just the visible route. */
export function itemScope(envelope: ReadyHubEnvelope) { return characterScope(envelope); }

/** Exact query identity. Results from different pages, revisions, observers, or facets never merge. */
export function itemFacetKey(facet: ItemFacet, request: ItemFacetRequest) {
  // Keep the query identity explicit. A page from a differently shaped object
  // query is not interchangeable merely because the route and item match.
  const query = facet === "details" ? "dnd2024.query.inventory-item-details"
    : facet === "uses" ? "dnd2024.query.inventory-item-uses"
      : "dnd2024.query.inventory-item-recipes";
  const base = [query, request.applicationId, request.stateSpaceId, request.campaignId, request.observerId,
    request.perspective, request.itemId, request.contextRevision];
  if (facet === "uses") {
    const value = request as ItemUsesRequest;
    return JSON.stringify([...base, value.offset, value.expectedSourceRevision]);
  }
  if (facet === "recipes") {
    const value = request as ItemRecipesRequest;
    return JSON.stringify([...base, value.makesOffset, value.usesOffset, value.expectedSourceRevision]);
  }
  return JSON.stringify(base);
}

export const MAX_CONFIRMED_ITEM_ENTRIES = 24;
export const MAX_CONFIRMED_ITEM_BYTES = 6 * 1024 * 1024;
const initialItemState: ItemState = { scope: null, generation: 0, refreshEpoch: 0, entries: {}, requests: {}, order: {}, retainedBytes: 0 };
let nextItemRequestToken = 0;
export function allocateItemRequestToken() { nextItemRequestToken += 1; return nextItemRequestToken; }

function discardItem(state: ItemState, key: string) {
  state.retainedBytes -= state.order[key]?.bytes ?? 0;
  delete state.entries[key]; delete state.order[key]; delete state.requests[key];
}

function enforceItemCapacity(state: ItemState) {
  while (Object.keys(state.order).length > MAX_CONFIRMED_ITEM_ENTRIES || state.retainedBytes > MAX_CONFIRMED_ITEM_BYTES) {
    const oldest = Object.entries(state.order).sort(([, left], [, right]) => left.requestToken - right.requestToken)[0]?.[0];
    if (!oldest) break;
    discardItem(state, oldest);
  }
}

function itemEntryBytes(value: ItemFacetResult) {
  try { return new TextEncoder().encode(JSON.stringify(value)).byteLength; }
  catch { return MAX_CONFIRMED_ITEM_BYTES + 1; }
}

const initialState: HubConfirmedState = {
  scope: null, generation: 0, rosterIds: [], rosterById: {}, facetsById: {}, portraitById: {}, requestsByKey: {},
  facetOrder: {}, retainedBytes: 0,
};
export const MAX_CONFIRMED_CHARACTER_FACETS = 10;
export const MAX_CONFIRMED_CHARACTER_BYTES = 6 * 1024 * 1024;
const requestKey = (actorId: string, facet: CharacterFacet | "inventory" | "portrait") => `${actorId}|${facet}`;
let nextRequestToken = 0;
/** Module-monotonic across hub remounts; reducer fencing still binds it to an active scope/generation. */
export function allocateCharacterRequestToken() { nextRequestToken += 1; return nextRequestToken; }

function discardFacet(state: HubConfirmedState, key: string) {
  const [actorId, facet] = key.split("|", 2) as [string, CharacterFacet | "inventory" | "portrait"];
  if (facet === "portrait") {
    const authority = Object.hasOwn(state.portraitById, actorId) ? state.portraitById[actorId] : undefined;
    // A retired image must never reveal the base roster's older portrait. Keep a tiny
    // zero-byte authority floor (including denial/known absence) until roster or scope removal.
    if (authority) state.portraitById[actorId] = { ...authority, portrait: undefined, bytes: 0 };
  }
  const facets = Object.hasOwn(state.facetsById, actorId) ? state.facetsById[actorId] : undefined;
  if (facets && facet !== "portrait") {
    delete facets[facet];
    if (!Object.keys(facets).length) delete state.facetsById[actorId];
  }
  state.retainedBytes -= state.facetOrder[key]?.bytes ?? 0;
  delete state.facetOrder[key];
}

function enforceCapacity(state: HubConfirmedState) {
  while (Object.keys(state.facetOrder).length > MAX_CONFIRMED_CHARACTER_FACETS ||
    state.retainedBytes > MAX_CONFIRMED_CHARACTER_BYTES) {
    const oldest = Object.entries(state.facetOrder).sort(([, left], [, right]) => left.requestToken - right.requestToken)[0]?.[0];
    if (!oldest) break;
    discardFacet(state, oldest);
  }
}

function portraitBytes(value: PartyMemberReadModel) {
  try { return new TextEncoder().encode(JSON.stringify({ portrait: value.portrait, coverage: value.portraitCoverage })).byteLength; }
  catch { return MAX_CONFIRMED_CHARACTER_BYTES; }
}

function memberBytes(value: PartyMemberReadModel) {
  try { return new TextEncoder().encode(JSON.stringify(value)).byteLength; }
  catch { return MAX_CONFIRMED_CHARACTER_BYTES + 1; }
}

function retainPortraitAuthority(state: HubConfirmedState, actorId: string, value: PartyMemberReadModel,
  requestToken: number) {
  const coverage = value.portraitCoverage;
  if (coverage !== "confirmed" && coverage !== "denied") return;
  const previous = Object.hasOwn(state.portraitById, actorId) ? state.portraitById[actorId] : undefined;
  // Media is shared by sheet/details reads, so facet-local sequencing is not
  // enough: a late sheet response must not overturn a newer details denial.
  if (previous && previous.requestToken >= requestToken) return;
  const key = requestKey(actorId, "portrait");
  const portrait = coverage === "denied" ? undefined : value.portrait;
  const bytes = portraitBytes({ ...value, portrait });
  state.portraitById[actorId] = { portrait, coverage, requestToken, bytes };
  state.retainedBytes += bytes - (state.facetOrder[key]?.bytes ?? 0);
  state.facetOrder[key] = { requestToken, bytes };
}

function retainBootstrapPortraitAuthority(state: HubConfirmedState, actorId: string, member: PartyMemberReadModel,
  requestToken: number) {
  const coverage = member.portraitCoverage;
  const explicitPortrait = Object.hasOwn(member, "portrait") && coverage !== "unavailable";
  if (!explicitPortrait && coverage !== "confirmed" && coverage !== "denied") return;
  for (const key of Object.keys(state.requestsByKey)) {
    if (key.startsWith(`${actorId}|`)) delete state.requestsByKey[key];
  }
  const authorityCoverage: PortraitAuthority["coverage"] = coverage === "denied" ? "denied" : "confirmed";
  const key = requestKey(actorId, "portrait");
  const portrait = authorityCoverage === "denied" ? undefined : member.portrait;
  const bytes = portraitBytes({ ...member, portrait, portraitCoverage: authorityCoverage });
  // A bootstrap with explicit media evidence is authoritative for its member.
  // Its request token is prepared before dispatch; do not derive a token while
  // reducing state (which would make replay non-deterministic).
  state.portraitById[actorId] = { portrait, coverage: authorityCoverage, requestToken, bytes };
  state.retainedBytes += bytes - (state.facetOrder[key]?.bytes ?? 0);
  state.facetOrder[key] = { requestToken, bytes };
}

function retainPortraitFloors(state: HubConfirmedState) {
  for (const actorId of Object.keys(state.portraitById)) {
    const authority = state.portraitById[actorId]!;
    state.portraitById[actorId] = { ...authority, portrait: undefined, bytes: 0 };
  }
}

const confirmed = createSlice({
  name: "confirmed",
  initialState,
  reducers: {
    bootstrapCommitted: {
      prepare(payload: { scope: string; party: PartyMemberReadModel[] }) {
        return { payload: { ...payload, requestToken: allocateCharacterRequestToken() } };
      },
      reducer(state, action: PayloadAction<{ scope: string; party: PartyMemberReadModel[]; requestToken: number }>) {
      const changed = state.scope !== action.payload.scope;
      const nextRoster: Record<string, PartyMemberReadModel> = {};
      const ids: string[] = [];
      for (const member of action.payload.party) {
        if (!addressableActorId.test(member.id) || Object.hasOwn(nextRoster, member.id)) continue;
        // Authorization denial is an explicit media clear. Never admit a
        // malformed/copied image alongside it into the base roster, including
        // the first bootstrap where no portrait authority table exists yet.
        const incoming = member.portraitCoverage === "denied" ? { ...member, portrait: undefined } : member;
        const previous = !changed && Object.hasOwn(state.rosterById, member.id)
          ? state.rosterById[member.id] : undefined;
        // Bootstrap owns the current roster only. It may preserve its preceding
        // roster projection through a transient failure, but never copies a
        // bounded, query-specific facet into the unmetered roster record.
        const preserved = previous
          ? preserveMemberOnTransientFailure(previous, incoming)
          : incoming;
        const authoritativePortrait = (Object.hasOwn(incoming, "portrait") && incoming.portraitCoverage !== "unavailable") ||
          incoming.portraitCoverage === "confirmed" || incoming.portraitCoverage === "denied";
        // The roster owns membership. A same-scope bootstrap which omits portrait coverage
        // cannot erase a confirmed value, while an explicit own property (including undefined)
        // is an authoritative update. Previous scopes are never read here.
        nextRoster[member.id] = previous && !authoritativePortrait
          ? { ...preserved, portrait: previous.portrait }
          : preserved;
        ids.push(member.id);
      }
      state.scope = action.payload.scope;
      state.generation += changed ? 1 : 0;
      state.rosterIds = ids;
      state.rosterById = nextRoster;
      if (changed) {
        state.facetsById = {}; state.portraitById = {}; state.requestsByKey = {}; state.facetOrder = {}; state.retainedBytes = 0;
      } else for (const id of Object.keys(state.facetsById)) if (!Object.hasOwn(nextRoster, id)) {
        for (const key of Object.keys(state.facetOrder)) if (key.startsWith(`${id}|`)) discardFacet(state, key);
      }
      if (!changed) for (const id of Object.keys(state.portraitById)) if (!Object.hasOwn(nextRoster, id)) {
        discardFacet(state, requestKey(id, "portrait"));
        delete state.portraitById[id];
      }
      if (!changed) for (const key of Object.keys(state.requestsByKey)) {
        const actorId = key.split("|", 1)[0]!;
        if (!Object.hasOwn(nextRoster, actorId)) delete state.requestsByKey[key];
      }
      if (!changed) for (const member of action.payload.party) {
        if (Object.hasOwn(nextRoster, member.id)) {
          retainBootstrapPortraitAuthority(state, member.id, member, action.payload.requestToken);
        }
      }
      enforceCapacity(state);
      },
    },
    characterRequestStarted(state, action: PayloadAction<{
      scope: string; actorId: string; facet: CharacterFacet | "inventory"; requestToken: number;
    }>) {
      if (state.scope !== action.payload.scope || !Object.hasOwn(state.rosterById, action.payload.actorId)) return;
      state.requestsByKey[requestKey(action.payload.actorId, action.payload.facet)] = {
        generation: state.generation, requestToken: action.payload.requestToken,
      };
    },
    /** A same-scope Redux hit completes only its own request; it never renews retained evidence. */
    characterRequestFinished(state, action: PayloadAction<{
      scope: string; actorId: string; facet: CharacterFacet | "inventory"; requestToken: number;
    }>) {
      const key = requestKey(action.payload.actorId, action.payload.facet);
      const request = state.requestsByKey[key];
      if (state.scope === action.payload.scope && request?.generation === state.generation &&
          request.requestToken === action.payload.requestToken)
        delete state.requestsByKey[key];
    },
    characterFacetCommitted(state, action: PayloadAction<{
      scope: string; actorId: string; facet: CharacterFacet; value: PartyMemberReadModel;
    }>) {
      const key = requestKey(action.payload.actorId, action.payload.facet);
      const request = state.requestsByKey[key];
      const metadata = (action as PayloadAction<unknown, string, ConfirmedFacetMetadata>).meta;
      if (state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
        request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
        !Object.hasOwn(state.rosterById, action.payload.actorId) || metadata.bytes < 0 ||
        metadata.bytes > MAX_CONFIRMED_CHARACTER_BYTES) return;
      const facets = Object.hasOwn(state.facetsById, action.payload.actorId)
        ? state.facetsById[action.payload.actorId]! : {};
      const previousFacet = facets[action.payload.facet];
      const incoming = action.payload.value.portraitCoverage === "denied"
        ? { ...action.payload.value, portrait: undefined } : action.payload.value;
      const value = previousFacet
        ? preserveMemberOnTransientFailure(previousFacet.value, incoming)
        : incoming;
      // A transient failure can retain a larger canonical value than the small
      // error response that triggered it. Account for the stored projection,
      // not only transport metadata supplied by the completed read.
      const bytes = Math.max(metadata.bytes, memberBytes(value));
      if (bytes > MAX_CONFIRMED_CHARACTER_BYTES) {
        delete state.requestsByKey[key];
        return;
      }
      const retained = { ...metadata, bytes };
      facets[action.payload.facet] = { value, ...retained };
      state.facetsById[action.payload.actorId] = facets;
      state.retainedBytes += bytes - (state.facetOrder[key]?.bytes ?? 0);
      state.facetOrder[key] = { requestToken: metadata.requestToken, bytes };
      retainPortraitAuthority(state, action.payload.actorId, value, metadata.requestToken);
      delete state.requestsByKey[key];
      enforceCapacity(state);
    },
    inventoryCommitted(state, action: PayloadAction<{
      scope: string; actorId: string; value: InventoryContainerResult;
    }>) {
      const key = requestKey(action.payload.actorId, "inventory");
      const request = state.requestsByKey[key];
      const metadata = (action as PayloadAction<unknown, string, ConfirmedFacetMetadata>).meta;
      if (state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
        request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
        !Object.hasOwn(state.rosterById, action.payload.actorId) || metadata.bytes < 0 ||
        metadata.bytes > MAX_CONFIRMED_CHARACTER_BYTES) return;
      const facets = Object.hasOwn(state.facetsById, action.payload.actorId)
        ? state.facetsById[action.payload.actorId]! : {};
      facets.inventory = { value: action.payload.value, ...metadata };
      state.facetsById[action.payload.actorId] = facets;
      state.retainedBytes += metadata.bytes - (state.facetOrder[key]?.bytes ?? 0);
      state.facetOrder[key] = { requestToken: metadata.requestToken, bytes: metadata.bytes };
      delete state.requestsByKey[key];
      enforceCapacity(state);
    },
    characterFacetsCleared(state, _action: PayloadAction<void>) {
      state.generation += 1; state.facetsById = {}; retainPortraitFloors(state); state.requestsByKey = {}; state.facetOrder = {}; state.retainedBytes = 0;
    },
    scopeCleared(state, _action: PayloadAction<void>) {
      state.scope = null; state.generation += 1; state.rosterIds = []; state.rosterById = {}; state.facetsById = {}; state.portraitById = {};
      state.requestsByKey = {}; state.facetOrder = {}; state.retainedBytes = 0;
    },
  },
});

const items = createSlice({
  name: "items",
  initialState: initialItemState,
  reducers: {
    scopeReplaced(state, action: PayloadAction<{ scope: string; force?: boolean }>) {
      if (state.scope === action.payload.scope && !action.payload.force) return;
      const replacingSameScope = state.scope === action.payload.scope;
      state.scope = action.payload.scope;
      state.generation += 1;
      if (replacingSameScope && action.payload.force) state.refreshEpoch += 1;
      state.entries = {}; state.requests = {}; state.order = {}; state.retainedBytes = 0;
    },
    requestStarted(state, action: PayloadAction<{ scope: string; key: string; requestToken: number }>) {
      if (state.scope !== action.payload.scope) return;
      state.requests[action.payload.key] = { generation: state.generation, requestToken: action.payload.requestToken };
    },
    committed(state, action: PayloadAction<{ scope: string; key: string; value: ItemFacetResult }>) {
      const metadata = (action as PayloadAction<unknown, string, ConfirmedFacetMetadata>).meta;
      const request = state.requests[action.payload.key];
      if (state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
          request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
          metadata.bytes < 0 || metadata.bytes > MAX_CONFIRMED_ITEM_BYTES) return;
      const previous = Object.hasOwn(state.entries, action.payload.key) ? state.entries[action.payload.key] : undefined;
      // A same-query transport/staleness result does not prove the confirmed
      // projection was removed. Retain that exact, bounded value; denial does
      // prove it must disappear and therefore replaces it.
      const retainedFallback = previous?.value.status === "ready" &&
        (action.payload.value.status === "unavailable" || action.payload.value.status === "stale")
      const value = retainedFallback ? previous.value : action.payload.value;
      const bytes = Math.max(metadata.bytes, itemEntryBytes(value));
      if (bytes > MAX_CONFIRMED_ITEM_BYTES) { delete state.requests[action.payload.key]; return; }
      // A transport fallback is still the old confirmation. Its request has
      // finished, but it must not renew cache age or the response expiry.
      state.entries[action.payload.key] = { value, ...metadata,
        confirmedAt: retainedFallback ? previous.confirmedAt : metadata.confirmedAt, bytes };
      state.retainedBytes += bytes - (state.order[action.payload.key]?.bytes ?? 0);
      state.order[action.payload.key] = { requestToken: metadata.requestToken, bytes };
      delete state.requests[action.payload.key];
      enforceItemCapacity(state);
    },
    requestFinished(state, action: PayloadAction<{ scope: string; key: string; requestToken: number }>) {
      const request = state.requests[action.payload.key];
      if (state.scope === action.payload.scope && request?.requestToken === action.payload.requestToken)
        delete state.requests[action.payload.key];
    },
    invalidated(state, _action: PayloadAction<void>) {
      state.generation += 1;
      state.refreshEpoch += 1;
      state.entries = {}; state.requests = {}; state.order = {}; state.retainedBytes = 0;
    },
  },
});

type ReferenceEntry<T> = ConfirmedFacetMetadata & { value: T };
type ReferenceState = {
  scope: string | null;
  generation: number;
  rules: ReferenceEntry<RulesReferencePublication> | null;
  content: Record<string, ReferenceEntry<InstalledContentPage>>;
  requests: Record<string, { generation: number; requestToken: number }>;
  contentOrder: Record<string, { requestToken: number; bytes: number }>;
  retainedBytes: number;
};

export const MAX_CONFIRMED_CONTENT_PAGES = 24;
export const MAX_CONFIRMED_REFERENCE_BYTES = 12 * 1024 * 1024;
const MAX_CONFIRMED_RULES_BYTES = 4 * 1024 * 1024;
const MAX_CONFIRMED_CONTENT_BYTES = 8 * 1024 * 1024;
const initialReferenceState: ReferenceState = {
  scope: null, generation: 0, rules: null, content: {}, requests: {}, contentOrder: {}, retainedBytes: 0,
};
let nextReferenceRequestToken = 0;
export function allocateReferenceRequestToken() { nextReferenceRequestToken += 1; return nextReferenceRequestToken; }

function discardContentPage(state: ReferenceState, key: string) {
  state.retainedBytes -= state.contentOrder[key]?.bytes ?? 0;
  delete state.content[key]; delete state.contentOrder[key]; delete state.requests[`content:${key}`];
}

function enforceReferenceCapacity(state: ReferenceState) {
  while (Object.keys(state.contentOrder).length > MAX_CONFIRMED_CONTENT_PAGES ||
    state.retainedBytes - (state.rules?.bytes ?? 0) > MAX_CONFIRMED_CONTENT_BYTES ||
    state.retainedBytes > MAX_CONFIRMED_REFERENCE_BYTES) {
    const oldest = Object.entries(state.contentOrder)
      .sort(([, left], [, right]) => left.requestToken - right.requestToken)[0]?.[0];
    if (!oldest) break;
    discardContentPage(state, oldest);
  }
}

const references = createSlice({
  name: "references",
  initialState: initialReferenceState,
  reducers: {
    scopeReplaced(state, action: PayloadAction<{ scope: string; force?: boolean }>) {
      if (!action.payload.force && state.scope === action.payload.scope) return;
      state.scope = action.payload.scope; state.generation += 1;
      state.rules = null; state.content = {}; state.requests = {}; state.contentOrder = {}; state.retainedBytes = 0;
    },
    rulesRequestStarted(state, action: PayloadAction<{ scope: string; requestToken: number }>) {
      if (state.scope !== action.payload.scope) return;
      state.requests.rules = { generation: state.generation, requestToken: action.payload.requestToken };
    },
    rulesCommitted(state, action: PayloadAction<{ scope: string; value: RulesReferencePublication }>) {
      const request = state.requests.rules;
      const metadata = (action as PayloadAction<unknown, string, ConfirmedFacetMetadata>).meta;
      if (state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
        request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
        metadata.bytes < 0 || metadata.bytes > MAX_CONFIRMED_RULES_BYTES) return;
      state.retainedBytes += metadata.bytes - (state.rules?.bytes ?? 0);
      state.rules = { value: action.payload.value, ...metadata };
      delete state.requests.rules;
      enforceReferenceCapacity(state);
    },
    rulesDenied(state, action: PayloadAction<{ scope: string; requestToken: number }>) {
      const request = state.requests.rules;
      if (state.scope !== action.payload.scope || request?.requestToken !== action.payload.requestToken) return;
      state.retainedBytes -= state.rules?.bytes ?? 0;
      state.rules = null; delete state.requests.rules;
    },
    contentRequestStarted(state, action: PayloadAction<{ scope: string; key: string; requestToken: number }>) {
      if (state.scope !== action.payload.scope) return;
      state.requests[`content:${action.payload.key}`] = { generation: state.generation, requestToken: action.payload.requestToken };
    },
    contentCommitted(state, action: PayloadAction<{ scope: string; key: string; value: InstalledContentPage }>) {
      const requestKey = `content:${action.payload.key}`;
      const request = state.requests[requestKey];
      const metadata = (action as PayloadAction<unknown, string, ConfirmedFacetMetadata>).meta;
      if (state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
        request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
        metadata.bytes < 0 || metadata.bytes > 524_288) return;
      state.retainedBytes += metadata.bytes - (state.contentOrder[action.payload.key]?.bytes ?? 0);
      state.content[action.payload.key] = { value: action.payload.value, ...metadata };
      state.contentOrder[action.payload.key] = { requestToken: metadata.requestToken, bytes: metadata.bytes };
      delete state.requests[requestKey];
      enforceReferenceCapacity(state);
    },
    contentDenied(state, action: PayloadAction<{ scope: string; key: string; requestToken: number }>) {
      const requestKey = `content:${action.payload.key}`;
      const request = state.requests[requestKey];
      if (state.scope !== action.payload.scope || request?.requestToken !== action.payload.requestToken) return;
      // A page-level denial proves this scope no longer authorizes the content
      // collection. Do not leave an earlier page visible while later pages are
      // denied; fence every content request in the same partition.
      for (const key of Object.keys(state.contentOrder)) discardContentPage(state, key);
      for (const key of Object.keys(state.requests)) if (key.startsWith("content:")) delete state.requests[key];
    },
    requestFinished(state, action: PayloadAction<{ scope: string; key: "rules" | `content:${string}`; requestToken: number }>) {
      const request = state.requests[action.payload.key];
      if (state.scope === action.payload.scope && request?.requestToken === action.payload.requestToken)
        delete state.requests[action.payload.key];
    },
    invalidated(state) {
      state.generation += 1; state.rules = null; state.content = {}; state.requests = {}; state.contentOrder = {}; state.retainedBytes = 0;
    },
  },
});

/**
 * Current intentionally retains just the selected scene and selected location.
 * A deferred Current response is not a World-directory patch: keeping it here
 * prevents a partial scene response from reviving or clobbering World data.
 */
export type CurrentDisplay = {
  situation: CurrentSituationReadModel;
  location: WorldLocation | null;
  projection?: CurrentPlayReadEvidence;
};
type CurrentEntry = ConfirmedFacetMetadata & { value: CurrentDisplay; fresh: boolean };
type CurrentState = {
  scope: string | null;
  generation: number;
  entry: CurrentEntry | null;
  request: { generation: number; requestToken: number } | null;
  /** Scalar ordering fence survives denial/eviction; it retains no display data. */
  requestTokenFloor: number;
  retainedBytes: number;
};
export const MAX_CONFIRMED_CURRENT_BYTES = 2 * 1024 * 1024;
const initialCurrentState: CurrentState = {
  scope: null, generation: 0, entry: null, request: null, requestTokenFloor: 0, retainedBytes: 0,
};
let nextCurrentRequestToken = 0;
export function allocateCurrentRequestToken() { nextCurrentRequestToken += 1; return nextCurrentRequestToken; }

function currentBytes(value: CurrentDisplay) {
  try { return new TextEncoder().encode(JSON.stringify(value)).byteLength; }
  catch { return MAX_CONFIRMED_CURRENT_BYTES + 1; }
}

const current = createSlice({
  name: "current",
  initialState: initialCurrentState,
  reducers: {
    scopeReplaced(state, action: PayloadAction<{ scope: string; force?: boolean }>) {
      if (state.scope === action.payload.scope && !action.payload.force) return;
      state.scope = action.payload.scope;
      state.generation += 1;
      state.entry = null;
      state.request = null;
      state.retainedBytes = 0;
    },
    currentRequestStarted(state, action: PayloadAction<{ scope: string; requestToken: number }>) {
      if (state.scope !== action.payload.scope || action.payload.requestToken <= state.requestTokenFloor) return;
      state.requestTokenFloor = action.payload.requestToken;
      state.request = { generation: state.generation, requestToken: action.payload.requestToken };
    },
    currentCommitted(state, action: PayloadAction<{ scope: string; value: CurrentDisplay }>) {
      const metadata = (action as PayloadAction<unknown, string, ConfirmedFacetMetadata>).meta;
      const request = state.request;
      if (state.scope !== action.payload.scope || !request || request.generation !== state.generation ||
          request.generation !== metadata?.generation || request.requestToken !== metadata.requestToken ||
          metadata.bytes < 0 || metadata.bytes > MAX_CONFIRMED_CURRENT_BYTES) return;
      const bytes = Math.max(metadata.bytes, currentBytes(action.payload.value));
      if (bytes > MAX_CONFIRMED_CURRENT_BYTES) { state.request = null; return; }
      state.entry = { value: action.payload.value, ...metadata, bytes, fresh: true };
      state.requestTokenFloor = Math.max(state.requestTokenFloor, metadata.requestToken);
      state.retainedBytes = bytes;
      state.request = null;
    },
    /** Authorization revokes the complete Current projection for this scope. */
    currentDenied(state, action: PayloadAction<{ scope: string; requestToken: number }>) {
      if (state.scope !== action.payload.scope || state.request?.requestToken !== action.payload.requestToken) return;
      state.requestTokenFloor = Math.max(state.requestTokenFloor, action.payload.requestToken);
      state.entry = null;
      state.request = null;
      state.retainedBytes = 0;
    },
    currentRequestFinished(state, action: PayloadAction<{ scope: string; requestToken: number }>) {
      if (state.scope === action.payload.scope && state.request?.generation === state.generation &&
          state.request.requestToken === action.payload.requestToken) {
        // A completed transport failure does not revoke the displayable last
        // scene, but it cannot make it a fresh cache hit after recovery.
        if (state.entry) state.entry.fresh = false;
        state.request = null;
      }
    },
    /** Bootstrap is already an authorized table response, but must match its captured scope/generation. */
    bootstrapCommitted(state, action: PayloadAction<{ scope: string; value: CurrentDisplay }>) {
        const metadata = (action as PayloadAction<unknown, string, ConfirmedFacetMetadata>).meta;
        if (!metadata || state.scope !== action.payload.scope || metadata.generation !== state.generation ||
            metadata.requestToken < 1 || metadata.bytes < 0 || metadata.bytes > MAX_CONFIRMED_CURRENT_BYTES) return;
        // A delayed staging projection must not replace an active or newer
        // scoped Current read. Bootstrap tokens are allocated outside reducers
        // from the same monotonic source as transport requests.
        const previous = state.entry;
        if (state.request || metadata.requestToken <= state.requestTokenFloor ||
            (previous && previous.requestToken >= metadata.requestToken)) return;
        const bytes = currentBytes(action.payload.value);
        if (bytes > MAX_CONFIRMED_CURRENT_BYTES) return;
        state.entry = { value: action.payload.value, generation: state.generation,
          requestToken: metadata.requestToken, bytes, confirmedAt: metadata.confirmedAt, fresh: true };
        state.request = null;
        state.requestTokenFloor = metadata.requestToken;
        state.retainedBytes = bytes;
    },
    invalidated(state) {
      state.generation += 1;
      // Object/change-stream invalidation is recoverable: retain the last
      // authorized scene for display, but make it ineligible for cache reuse
      // or action prerequisites until a fresh read commits. Scope replacement
      // and explicit denial still clear the entry above.
      if (state.entry) state.entry.fresh = false;
      state.request = null;
    },
  },
});

export const hubActions = confirmed.actions;
export const itemActions = items.actions;
export const referenceActions = references.actions;
export const currentActions = current.actions;
export function commitCharacterFacet(payload: { scope: string; actorId: string; facet: CharacterFacet; value: PartyMemberReadModel },
  meta: ConfirmedFacetMetadata) {
  return { type: hubActions.characterFacetCommitted.type, payload, meta };
}
export function commitInventory(payload: { scope: string; actorId: string; value: InventoryContainerResult },
  meta: ConfirmedFacetMetadata) {
  return { type: hubActions.inventoryCommitted.type, payload, meta };
}
export function commitItemFacet(payload: { scope: string; key: string; value: ItemFacetResult }, meta: ConfirmedFacetMetadata) {
  return { type: itemActions.committed.type, payload, meta };
}
export function commitCurrent(payload: { scope: string; value: CurrentDisplay }, meta: ConfirmedFacetMetadata) {
  return { type: currentActions.currentCommitted.type, payload, meta };
}
export function commitCurrentBootstrap(payload: { scope: string; value: CurrentDisplay }, meta: ConfirmedFacetMetadata) {
  return { type: currentActions.bootstrapCommitted.type, payload, meta };
}
const combinedReducer = combineReducers({
  confirmed: confirmed.reducer, items: items.reducer, references: references.reducer, current: current.reducer,
  table: tableReducer, connectedSource: connectedSourceReducer, collections: collectionReducer,
});
export function createHubStore() { return configureStore({ reducer: (state: ReturnType<typeof combinedReducer> | undefined, action) => {
  const next = combinedReducer(state, action);
  // Retire dependent collection facets in the same transaction as their owner.
  // No render can observe a former audience's results under a new scope.
  const retire = next.confirmed.scope !== state?.confirmed.scope || next.confirmed.generation !== state?.confirmed.generation ||
    action.type === referenceActions.invalidated.type;
  const collections = retire ? collectionReducer(next.collections, collectionActions.scopeReplaced({
    scope: next.confirmed.scope, generation: next.collections.generation + 1,
  })) : next.collections;
  return collections === next.collections ? next : { ...next, collections };
} }); }
export type HubStore = ReturnType<typeof createHubStore>;
export type RootState = ReturnType<HubStore["getState"]>;
export type HubDispatch = HubStore["dispatch"];
/** The source workspace receives this adapter only after this real Hub store exists. */
export function createConnectedSourceOwner(store: HubStore) {
  return {
    get: (scope: string) => selectConnectedSource(store.getState(), scope),
    replace: (scope: string, value: import("./hub-types").ConnectedCampaignEnvelope, bytes: number) => {
      store.dispatch(connectedSourceActions.replaced({ scope, value, bytes }));
    },
    update: (scope: string, value: import("./hub-types").ConnectedCampaignEnvelope, bytes: number) => {
      store.dispatch(connectedSourceActions.updated({ scope, value, bytes }));
    },
    clear: () => { store.dispatch(connectedSourceActions.cleared()); },
    metrics: () => ({ retainedBytes: store.getState().connectedSource.retainedBytes }),
  };
}
export const useHubDispatch = () => useDispatch<HubDispatch>();
export const useHubSelector: TypedUseSelectorHook<RootState> = useSelector;

function newer<T>(left: ConfirmedFacet<T>, right: ConfirmedFacet<T>) {
  return left.requestToken > right.requestToken;
}

const withoutFacets = new WeakMap<PartyMemberReadModel, PartyMemberReadModel>();
const emptyFacets: CharacterFacets = {};
const noPortraitAuthority = {};
const composed = new WeakMap<PartyMemberReadModel, WeakMap<CharacterFacets, WeakMap<object, PartyMemberReadModel>>>();
function composeMember(base: PartyMemberReadModel, facets: CharacterFacets | undefined,
  portraitAuthority: PortraitAuthority | undefined): PartyMemberReadModel {
  if (!facets && !portraitAuthority) {
    const cached = withoutFacets.get(base);
    if (cached) return cached;
    withoutFacets.set(base, base);
    return base;
  }
  const facetKey = facets ?? emptyFacets;
  const byFacet = composed.get(base) ?? new WeakMap<CharacterFacets, WeakMap<object, PartyMemberReadModel>>();
  composed.set(base, byFacet);
  const byPortrait = byFacet.get(facetKey) ?? new WeakMap<object, PartyMemberReadModel>();
  byFacet.set(facetKey, byPortrait);
  const cached = byPortrait.get(portraitAuthority ?? noPortraitAuthority);
  if (cached) return cached;
  const sources = [facets?.sheet, facets?.details].filter((facet): facet is ConfirmedFacet<PartyMemberReadModel> => Boolean(facet))
    .sort((left, right) => newer(left, right) ? -1 : newer(right, left) ? 1 : 0);
  if (!sources.length && !facets?.inventory && !portraitAuthority) { byPortrait.set(noPortraitAuthority, base); return base; }
  const field = <K extends keyof PartyMemberReadModel>(key: K,
    allowed: ReadonlyArray<ConfirmedFacet<PartyMemberReadModel>> = sources): PartyMemberReadModel[K] => {
    for (const source of allowed) if (Object.hasOwn(source.value, key)) return source.value[key];
    return base[key];
  };
  const sheetSources = sources.filter((source) => source === facets?.sheet || source === facets?.details);
  const detailSources = sources.filter((source) => source === facets?.details);
  // This is a named display projection, not a spread merge. Inventory remains dossier-owned:
  // a sheet projection includes summary fields for rendering convenience but has no inventory
  // coverage. Portrait coverage is explicit so unavailable media never overwrites a confirmed
  // same-scope image, while confirmed absence and denial clear it.
  const value: PartyMemberReadModel = {
    ...base,
    portrait: portraitAuthority ? portraitAuthority.portrait : base.portrait,
    portraitCoverage: portraitAuthority?.coverage ?? base.portraitCoverage,
    detail: field("detail", sheetSources), recordStatus: field("recordStatus", sheetSources),
    sheetStatus: field("sheetStatus", sheetSources), sheetState: field("sheetState", sheetSources),
    sheet: field("sheet", sheetSources), characterSheet: field("characterSheet", sheetSources),
    backstory: field("backstory", detailSources), origin: field("origin", detailSources),
    inventoryStatus: field("inventoryStatus", detailSources), inventoryState: field("inventoryState", detailSources),
    inventory: field("inventory", detailSources), inventoryResource: facets?.inventory?.value ?? base.inventoryResource,
  };
  byPortrait.set(portraitAuthority ?? noPortraitAuthority, value);
  return value;
}

export const selectCharacterParty = createSelector(
  [(state: RootState) => state.confirmed.rosterIds, (state: RootState) => state.confirmed.rosterById,
    (state: RootState) => state.confirmed.facetsById, (state: RootState) => state.confirmed.portraitById],
  (ids, rosterById, facetsById, portraitById) => ids
    .filter((id) => Object.hasOwn(rosterById, id))
    .map((id) => composeMember(rosterById[id]!, Object.hasOwn(facetsById, id) ? facetsById[id] : undefined,
      Object.hasOwn(portraitById, id) ? portraitById[id] : undefined)),
);

export const selectTableEnvelope = createSelector(
  [(state: RootState) => state.table, selectCharacterParty],
  (table, party) => materializeTableEnvelope(table, party),
);

export const selectCharacterScope = (state: RootState) => state.confirmed.scope;
export const selectCharacterGeneration = (state: RootState) => state.confirmed.generation;
const memberSelectors = new Map<string, ReturnType<typeof createSelector>>();
export const selectCharacterMember = (actorId: string) => {
  const existing = memberSelectors.get(actorId);
  if (existing) return existing as (state: RootState) => PartyMemberReadModel | null;
  if (memberSelectors.size >= 256) memberSelectors.delete(memberSelectors.keys().next().value!);
  const selector = createSelector(
    [(state: RootState) => Object.hasOwn(state.confirmed.rosterById, actorId) ? state.confirmed.rosterById[actorId] : undefined,
      (state: RootState) => Object.hasOwn(state.confirmed.facetsById, actorId) ? state.confirmed.facetsById[actorId] : undefined,
      (state: RootState) => Object.hasOwn(state.confirmed.portraitById, actorId) ? state.confirmed.portraitById[actorId] : undefined],
    (base, facets, portraitAuthority) => base ? composeMember(base, facets, portraitAuthority) : null,
  );
  memberSelectors.set(actorId, selector);
  return selector;
};

export function peekCharacterFacet<T>(state: RootState, scope: string, actorId: string,
  facet: CharacterFacet | "inventory", maximumAgeMs: number): T | null {
  if (state.confirmed.scope !== scope || !Object.hasOwn(state.confirmed.facetsById, actorId)) return null;
  const confirmedFacet = state.confirmed.facetsById[actorId]?.[facet];
  if (!confirmedFacet || Date.now() - confirmedFacet.confirmedAt > maximumAgeMs) return null;
  // Failure is useful display state, not a fresh successful read. Reusing it
  // after reconnect used to suppress the request that could recover the view.
  // This only governs cache reuse: partial domain fields remain displayable.
  if (facet === "inventory") {
    if ((confirmedFacet.value as InventoryContainerResult).status !== "ready") return null;
  } else {
    const value = confirmedFacet.value as PartyMemberReadModel;
    if (!["ready", "empty"].includes(value.sheetState.status) || value.portraitCoverage === "unavailable") return null;
  }
  if (facet !== "inventory" && Object.hasOwn(state.confirmed.portraitById, actorId)) {
    const authority = state.confirmed.portraitById[actorId]!;
    const value = confirmedFacet.value as PartyMemberReadModel;
    // A cached sheet is not a fresh media read. Reusing it must not reissue an
    // older portrait as a newer request after another facet/bootstrap revoked it.
    if (value.portrait !== authority.portrait || value.portraitCoverage !== authority.coverage)
      return { ...value, portrait: authority.portrait, portraitCoverage: authority.coverage } as T;
  }
  return confirmedFacet.value as T;
}

export const selectItemScope = (state: RootState) => state.items.scope;
export const selectItemGeneration = (state: RootState) => state.items.generation;
export const selectItemRefreshEpoch = (state: RootState) => state.items.refreshEpoch;
const itemSelectors = new Map<string, ReturnType<typeof createSelector>>();
export const selectItemFacet = (scope: string, key: string) => {
  const selectorKey = `${scope}\u0000${key}`;
  const existing = itemSelectors.get(selectorKey);
  if (existing) return existing as (state: RootState) => ItemFacetResult | null;
  if (itemSelectors.size >= 512) itemSelectors.delete(itemSelectors.keys().next().value!);
  const selector = createSelector(
    [(state: RootState) => state.items.scope, (state: RootState) =>
      Object.hasOwn(state.items.entries, key) ? state.items.entries[key] : undefined],
    (activeScope, entry) => activeScope === scope ? entry?.value ?? null : null,
  );
  itemSelectors.set(selectorKey, selector);
  return selector;
};

export function peekItemFacet<T extends ItemFacetResult>(state: RootState, scope: string, key: string,
  maximumAgeMs: number): T | null {
  if (state.items.scope !== scope || !Object.hasOwn(state.items.entries, key)) return null;
  const entry = state.items.entries[key]!;
  return Date.now() - entry.confirmedAt < maximumAgeMs ? entry.value as T : null;
}

const referenceSelectors = new Map<string, ReturnType<typeof createSelector>>();
export const selectRulesPublication = (scope: string) => {
  const existing = referenceSelectors.get(`rules\u0000${scope}`);
  if (existing) return existing as (state: RootState) => RulesReferencePublication | null;
  if (referenceSelectors.size >= 512) referenceSelectors.delete(referenceSelectors.keys().next().value!);
  const selector = createSelector(
    [(state: RootState) => state.references.scope, (state: RootState) => state.references.rules],
    (activeScope, entry) => activeScope === scope ? entry?.value ?? null : null,
  );
  referenceSelectors.set(`rules\u0000${scope}`, selector);
  return selector;
};

export const selectInstalledContentPage = (scope: string, key: string) => {
  const selectorKey = `content\u0000${scope}\u0000${key}`;
  const existing = referenceSelectors.get(selectorKey);
  if (existing) return existing as (state: RootState) => InstalledContentPage | null;
  if (referenceSelectors.size >= 512) referenceSelectors.delete(referenceSelectors.keys().next().value!);
  const selector = createSelector(
    [(state: RootState) => state.references.scope, (state: RootState) =>
      Object.hasOwn(state.references.content, key) ? state.references.content[key] : undefined],
    (activeScope, entry) => activeScope === scope ? entry?.value ?? null : null,
  );
  referenceSelectors.set(selectorKey, selector);
  return selector;
};

export const selectInstalledContentPages = (scope: string, keys: readonly string[]) => {
  const selectorKey = `content-pages\u0000${scope}\u0000${JSON.stringify(keys)}`;
  const existing = referenceSelectors.get(selectorKey);
  if (existing) return existing as (state: RootState) => InstalledContentPage[];
  if (referenceSelectors.size >= 512) referenceSelectors.delete(referenceSelectors.keys().next().value!);
  const selector = createSelector(
    [(state: RootState) => state.references.scope, (state: RootState) => state.references.content],
    (activeScope, content) => activeScope === scope
      ? keys.flatMap((key) => Object.hasOwn(content, key) ? [content[key]!.value] : []) : [],
  );
  referenceSelectors.set(selectorKey, selector);
  return selector;
};

export function peekRulesPublication(state: RootState, scope: string, maximumAgeMs: number) {
  const entry = state.references.scope === scope ? state.references.rules : null;
  return entry && Date.now() - entry.confirmedAt < maximumAgeMs ? entry.value : null;
}

export function peekInstalledContentPage(state: RootState, scope: string, key: string, maximumAgeMs: number) {
  const entry = state.references.scope === scope && Object.hasOwn(state.references.content, key)
    ? state.references.content[key] : null;
  return entry && Date.now() - entry.confirmedAt < maximumAgeMs ? entry.value : null;
}

const currentSelectors = new Map<string, ReturnType<typeof createSelector>>();
export const selectCurrentDisplay = (scope: string) => {
  const existing = currentSelectors.get(scope);
  if (existing) return existing as (state: RootState) => CurrentDisplay | null;
  if (currentSelectors.size >= 256) currentSelectors.delete(currentSelectors.keys().next().value!);
  const selector = createSelector(
    [(state: RootState) => state.current.scope, (state: RootState) => state.current.entry],
    (activeScope, entry) => activeScope === scope ? entry?.value ?? null : null,
  );
  currentSelectors.set(scope, selector);
  return selector;
};

/** A hit returns the exact retained evidence; it never restamps confirmation time. */
export function peekCurrentDisplay(state: RootState, scope: string, maximumAgeMs: number) {
  const entry = state.current.scope === scope ? state.current.entry : null;
  return entry?.fresh && Date.now() - entry.confirmedAt < maximumAgeMs ? entry.value : null;
}
export const selectCurrentScope = (state: RootState) => state.current.scope;
export const selectCurrentGeneration = (state: RootState) => state.current.generation;
export const selectCurrentFresh = (scope: string) => (state: RootState) =>
  state.current.scope === scope && state.current.entry?.fresh === true;
