import type { HubEnvelope, ObjectReadEvidence, Perspective, ReadyHubEnvelope, WorldFaction } from "./hub-types";
import { ViewReadClient, ViewReadError } from "./view-read-client";

export const CAMPAIGN_SUMMARY_OBJECT_ID = "dnd2024.object.campaign-summary";
export const FACTION_DIRECTORY_OBJECT_ID = "dnd2024.object.faction-directory-page";

export type CampaignObjectRequest = {
  perspective: Perspective;
  campaignId?: string;
};

export type FactionDirectoryPage = {
  factions: WorldFaction[];
  totalCount: number;
  complete: boolean;
  nextCursor: string | null;
  sourceRevisionFingerprint: string;
  projection: ObjectReadEvidence;
};

export type FactionObjectRequest = {
  envelope: ReadyHubEnvelope;
  cursor: string | null;
};

type BrowserObjectQueryOptions = {
  readCampaign: (request: CampaignObjectRequest, signal: AbortSignal) => Promise<HubEnvelope>;
  readFactionPage: (request: FactionObjectRequest, signal: AbortSignal) => Promise<FactionDirectoryPage>;
  validateCampaign: (value: unknown) => value is HubEnvelope;
  maximumCachedScopes?: number;
  maximumCacheAgeMs?: number;
};

function factionScope({ envelope, cursor }: FactionObjectRequest) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  return [envelope.applicationId, envelope.stateSpaceId, campaignId,
    envelope.audience.seat, envelope.audience.perspective, worldId, cursor ?? "first"].join(":");
}

function validText(value: unknown, maximumLength: number) {
  return typeof value === "string" && value.length > 0 && value.length <= maximumLength && value === value.trim();
}

export function isFactionDirectoryPage(value: unknown): value is FactionDirectoryPage {
  if (!value || typeof value !== "object") return false;
  const page = value as Record<string, unknown>;
  if (Object.keys(page).sort().join("|") !==
      "complete|factions|nextCursor|projection|sourceRevisionFingerprint|totalCount") return false;
  if (!Array.isArray(page.factions) || page.factions.length > 25 ||
      !Number.isInteger(page.totalCount) || (page.totalCount as number) < page.factions.length ||
      (page.totalCount as number) > 100 || typeof page.complete !== "boolean" ||
      !(page.nextCursor === null || validText(page.nextCursor, 2_048)) ||
      page.complete !== (page.nextCursor === null) ||
      !validText(page.sourceRevisionFingerprint, 128)) return false;
  const projection = page.projection as Record<string, unknown> | null;
  const evidenceKeys = "outputSchemaHash|qualifiedQueryId|resolutionFingerprint|resultFingerprint|sourceRevisionFingerprint|stateSpaceFingerprint";
  if (!projection || Object.keys(projection).sort().join("|") !== evidenceKeys ||
      projection.qualifiedQueryId !== "dnd2024.query.faction-directory-page" ||
      ![projection.stateSpaceFingerprint, projection.resolutionFingerprint, projection.outputSchemaHash,
        projection.resultFingerprint, projection.sourceRevisionFingerprint]
        .every((fingerprint) => typeof fingerprint === "string" && /^[0-9A-F]{64}$/iu.test(fingerprint)) ||
      page.sourceRevisionFingerprint !== projection.sourceRevisionFingerprint) return false;
  const identities = page.factions.map((entry) => {
    if (!entry || typeof entry !== "object") return null;
    const faction = entry as Record<string, unknown>;
    return validText(faction.id, 200) && validText(faction.name, 400) ? faction.id : null;
  });
  return identities.every((identity) => identity !== null) && new Set(identities).size === identities.length;
}

/**
 * The one browser cache owner for the migrated Campaign and Factions object queries. It keeps
 * response bodies in bounded memory only, isolates every audience/scope in its cache key and
 * delegates validation/cancellation/fingerprinting to ViewReadClient.
 */
export class BrowserObjectQueryState {
  readonly #campaign: ViewReadClient<CampaignObjectRequest, HubEnvelope>;
  readonly #factions: ViewReadClient<FactionObjectRequest, FactionDirectoryPage>;

  constructor(options: BrowserObjectQueryOptions) {
    const retention = {
      maximumCachedScopes: options.maximumCachedScopes ?? 8,
      maximumCacheAgeMs: options.maximumCacheAgeMs ?? 30_000,
    };
    this.#campaign = new ViewReadClient({
      ...retention,
      cacheKey: ({ perspective, campaignId }) => `${perspective}:${campaignId ?? "bound"}`,
      read: options.readCampaign,
      validate: options.validateCampaign,
    });
    this.#factions = new ViewReadClient({
      ...retention,
      cacheKey: factionScope,
      read: options.readFactionPage,
      validate: isFactionDirectoryPage,
    });
  }

  async loadCampaign(request: CampaignObjectRequest, preferCached = false) {
    const cached = preferCached ? this.#campaign.peek(request) : null;
    return cached?.value ?? (await this.#campaign.load(request)).value;
  }

  peekCampaign(request: CampaignObjectRequest) {
    return this.#campaign.peek(request);
  }

  async loadFactionPage(request: FactionObjectRequest, signal: AbortSignal, preferCached = true) {
    if (signal.aborted) throw new DOMException("Faction page replaced", "AbortError");
    const acceptRevision = (page: FactionDirectoryPage) => {
      const expectedRevision = request.cursor === null
        ? null
        : request.envelope.world.factionDirectory?.sourceRevisionFingerprint ?? null;
      if (expectedRevision && page.sourceRevisionFingerprint !== expectedRevision) {
        this.#factions.invalidate();
        throw new ViewReadError("stale-data", "The faction directory changed while it was being paged.");
      }
      return page;
    };
    const cached = preferCached ? this.#factions.peek(request) : null;
    if (cached) return acceptRevision(cached.value);
    const cancel = () => this.#factions.cancel();
    signal.addEventListener("abort", cancel, { once: true });
    try {
      return acceptRevision((await this.#factions.load(request)).value);
    } finally {
      signal.removeEventListener("abort", cancel);
    }
  }

  peekFactionPage(request: FactionObjectRequest) {
    return this.#factions.peek(request);
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId === CAMPAIGN_SUMMARY_OBJECT_ID) {
      this.#campaign.invalidate();
      return true;
    }
    if (qualifiedId === FACTION_DIRECTORY_OBJECT_ID) {
      this.#factions.invalidate();
      return true;
    }
    return false;
  }

  invalidateAll() {
    this.#campaign.invalidate();
    this.#factions.invalidate();
  }
}

export type ObjectEdit = {
  draft: unknown;
  status: "editing" | "pending" | "failed";
  error?: string;
};

export type BrowserObjectUiState = {
  selectedFactionId: string;
  campaignDetailsLoaded: boolean;
  edits: Record<string, ObjectEdit>;
};

export type BrowserObjectUiAction =
  | { type: "faction-selected"; factionId: string }
  | { type: "scope-replaced"; factionId: string }
  | { type: "campaign-details-loaded" }
  | { type: "edit-staged"; objectId: string; draft: unknown }
  | { type: "write-submitted"; objectId: string }
  | { type: "write-failed"; objectId: string; error: string }
  | { type: "write-confirmed"; objectId: string };

export function createBrowserObjectUiState(selectedFactionId: string): BrowserObjectUiState {
  return { selectedFactionId, campaignDetailsLoaded: false, edits: {} };
}

/** Local drafts never become authoritative object data. A confirmation only retires the draft;
 * the separately validated server response is what callers may merge into the visible object. */
export function browserObjectUiReducer(
  state: BrowserObjectUiState,
  action: BrowserObjectUiAction,
): BrowserObjectUiState {
  switch (action.type) {
    case "faction-selected":
      return action.factionId === state.selectedFactionId ? state : { ...state, selectedFactionId: action.factionId };
    case "scope-replaced":
      return createBrowserObjectUiState(action.factionId);
    case "campaign-details-loaded":
      return state.campaignDetailsLoaded ? state : { ...state, campaignDetailsLoaded: true };
    case "edit-staged":
      return { ...state, edits: { ...state.edits, [action.objectId]: { draft: action.draft, status: "editing" } } };
    case "write-submitted": {
      const edit = state.edits[action.objectId];
      return !edit ? state : { ...state, edits: { ...state.edits,
        [action.objectId]: { draft: edit.draft, status: "pending" },
      } };
    }
    case "write-failed": {
      const edit = state.edits[action.objectId];
      return !edit || edit.status !== "pending" ? state : { ...state, edits: { ...state.edits,
        [action.objectId]: { draft: edit.draft, status: "failed", error: action.error },
      } };
    }
    case "write-confirmed": {
      const edit = state.edits[action.objectId];
      if (!edit || edit.status !== "pending") return state;
      const edits = { ...state.edits };
      delete edits[action.objectId];
      return { ...state, edits };
    }
  }
}
