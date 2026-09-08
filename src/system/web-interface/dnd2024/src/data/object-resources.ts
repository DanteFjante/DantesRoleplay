import type { HubEnvelope, ObjectReadEvidence, Perspective, ReadyHubEnvelope, WorldFaction } from "./hub-types";
import { ResourceStore } from "./resource-store";
import type { ResourceState } from "./resource-state";
import { ViewReadError } from "./view-read-client";

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

type TableResourceOwnerOptions = {
  readCampaign: (request: CampaignObjectRequest, signal: AbortSignal) => Promise<HubEnvelope>;
  readFactionPage: (request: FactionObjectRequest, signal: AbortSignal) => Promise<FactionDirectoryPage>;
  validateCampaign: (value: unknown) => value is HubEnvelope;
  maximumEntries?: number;
  maximumRetainedBytes?: number;
  maximumAgeMs?: number;
};

function campaignSelection({ perspective, campaignId }: CampaignObjectRequest) {
  return `${perspective}:${campaignId ?? "bound"}`;
}

function factionScope({ envelope, cursor }: FactionObjectRequest) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const campaignEvidence = envelope.objectQueries?.campaignSummary;
  return [envelope.applicationId, envelope.stateSpaceId, campaignId,
    envelope.audience.seat, envelope.audience.perspective, worldId,
    campaignEvidence?.resolutionFingerprint ?? "no-resolution",
    campaignEvidence?.sourceRevisionFingerprint ?? "no-source-revision",
    cursor ?? "first"].join(":");
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
 * Production owner for Campaign and Faction resources. Campaign selection remains shell state;
 * validated results and pages live in the shared bounded store. Replacing that selection clears
 * and fences every prior-scope resource before the new read begins.
 */
export class TableResourceOwner {
  readonly #store: ResourceStore;
  readonly #campaign;
  readonly #factions;
  #activeSelection: string | null = null;

  constructor(options: TableResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 16,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 4 * 1024 * 1024,
    });
    const maximumAgeMs = options.maximumAgeMs ?? 30_000;
    this.#campaign = this.#store.define({
      name: "campaign-summary",
      cacheKey: campaignSelection,
      read: options.readCampaign,
      validate: options.validateCampaign,
      maximumAgeMs,
      maximumEntryBytes: 2 * 1024 * 1024,
    });
    this.#factions = this.#store.define({
      name: "faction-directory-page",
      cacheKey: factionScope,
      read: options.readFactionPage,
      validate: isFactionDirectoryPage,
      maximumAgeMs,
      maximumEntryBytes: 524_288,
    });
  }

  async loadCampaign(request: CampaignObjectRequest, preferCached = false, signal?: AbortSignal) {
    const selection = campaignSelection(request);
    if (this.#activeSelection !== null && selection !== this.#activeSelection) this.#store.invalidateAll();
    this.#activeSelection = selection;
    return (await this.#campaign.load(request, { preferCached, signal })).value;
  }

  peekCampaign(request: CampaignObjectRequest) {
    return this.#campaign.peek(request);
  }

  campaignState(request: CampaignObjectRequest): ResourceState<HubEnvelope> {
    return this.#campaign.state(request);
  }

  subscribeCampaign(request: CampaignObjectRequest, listener: (state: ResourceState<HubEnvelope>) => void) {
    return this.#campaign.subscribe(request, listener);
  }

  async loadFactionPage(request: FactionObjectRequest, signal?: AbortSignal, preferCached = true) {
    if (signal?.aborted) throw new DOMException("Faction page replaced", "AbortError");
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
    const result = await this.#factions.load(request, { preferCached, signal });
    return acceptRevision(result.value);
  }

  peekFactionPage(request: FactionObjectRequest) {
    return this.#factions.peek(request);
  }

  factionPageState(request: FactionObjectRequest): ResourceState<FactionDirectoryPage> {
    return this.#factions.state(request);
  }

  subscribeFactionPage(request: FactionObjectRequest, listener: (state: ResourceState<FactionDirectoryPage>) => void) {
    return this.#factions.subscribe(request, listener);
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
    this.#store.invalidateAll();
  }
}
