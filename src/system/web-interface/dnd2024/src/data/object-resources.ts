import type { CampaignReadModel, DeferredHubUpdate, HubEnvelope, InventoryContainerPageResult, InventoryContainerResult, ObjectReadEvidence, PartyMemberReadModel, Perspective, ReadyHubEnvelope, WorldFaction } from "./hub-types";
import type { ResourceInvalidationReason } from "./resource-store";
import { RESOURCE_FRESHNESS_MS, resourceCacheKey, resourceContractToken } from "./resource-policy";
import { ViewReadError } from "./view-read-client";
import { RequestCoordinator, type CoordinatedRequest } from "./request-coordinator";
import { contract as campaignSummaryContract } from "../server/campaign-summary-contract.js";
import { contract as factionDirectoryContract } from "../server/faction-directory-contract.js";
import { contract as campaignDetailsContract } from "../server/campaign-details-contract.js";
import { contract as campaignContextContract } from "../server/campaign-context-contract.js";
import { contract as characterSheetContract } from "../server/character-sheet-contract.js";
import { contract as characterDossierContract } from "../server/character-dossier-contract.js";
import { contract as inventoryContainerContract } from "../server/inventory-container-contract.js";
import { contract as inventoryWalletContract } from "../server/inventory-wallet-contract.js";
import { contract as worldLocationScopeContract } from "../server/world-location-scope-contract.js";
import { contract as worldLocationScopePageContract } from "../server/world-location-scope-page-contract.js";
import { contract as worldPeopleHoldingsPageContract } from "../server/world-people-holdings-page-contract.js";

export const CAMPAIGN_SUMMARY_OBJECT_ID = "dnd2024.object.campaign-summary";
export const FACTION_DIRECTORY_OBJECT_ID = "dnd2024.object.faction-directory-page";
export const CAMPAIGN_LOCATION_VISITS_OBJECT_ID = "dnd2024.object.campaign-location-visits";
export const WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID = "dnd2024.object.world-campaign-directory";
export const CHARACTER_DOSSIER_OBJECT_ID = "dnd2024.object.character-dossier-records";

export type CampaignObjectRequest = {
  perspective: Perspective;
  campaignId?: string;
};

export type FactionDirectoryPage = {
  coverage?: "complete" | "partial";
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

export type CampaignDetailsObjectRequest = { envelope: ReadyHubEnvelope };
export type CampaignContextObjectRequest = { envelope: ReadyHubEnvelope };
export type CampaignContextUpdate = Extract<DeferredHubUpdate, { section: "context" }>;
export type CharacterResourceRequest = { envelope: ReadyHubEnvelope; actorId: string };
export type InventoryContainerResourceRequest = CharacterResourceRequest & { containerId: string };
const characterReadOutcome = Symbol("character-read-outcome");

/**
 * Internal provenance for a character read. Completed values stay in Redux;
 * this only tells the hub whether it must commit a fresh transport result or
 * merely finish a request which was satisfied by that same canonical value.
 */
export type CharacterReadOutcome<T> = {
  readonly [characterReadOutcome]: true;
  readonly cacheHit: boolean;
  readonly value: T;
};

function freshCharacterRead<T>(value: T): CharacterReadOutcome<T> {
  return { [characterReadOutcome]: true, cacheHit: false, value };
}

function cachedCharacterRead<T>(value: T): CharacterReadOutcome<T> {
  return { [characterReadOutcome]: true, cacheHit: true, value };
}

function throwIfCharacterReadAborted(signal?: AbortSignal) {
  if (signal?.aborted)
    throw new ViewReadError("cancelled", "The character request is no longer current.");
}

function throwIfWorldReadAborted(signal?: AbortSignal) {
  if (signal?.aborted)
    throw new ViewReadError("cancelled", "The World request is no longer current.");
}

/** Only the local resource owner can produce this marker; response values cannot spoof it. */
export function isCharacterReadOutcome<T>(value: unknown): value is CharacterReadOutcome<T> {
  return Boolean(value && typeof value === "object" &&
    (value as CharacterReadOutcome<T>)[characterReadOutcome] === true);
}
export type WorldScopeRequest = {
  envelope: ReadyHubEnvelope;
  scopeId: string;
  cursor?: string | null;
};
export type WorldScopeUpdate = Extract<DeferredHubUpdate, { section: "locations" }> & {
  scopePage: { id: string };
};
export type WorldInformationSection = "people" | "lore" | "history";
export type WorldInformationRequest = { envelope: ReadyHubEnvelope; section: WorldInformationSection };
export type WorldInformationUpdate = Extract<DeferredHubUpdate, { section: WorldInformationSection }>;
export type CurrentViewRequest = { envelope: ReadyHubEnvelope };
export type CurrentViewUpdate = Extract<DeferredHubUpdate, { section: "current" }>;

type TableResourceOwnerOptions = {
  readCampaign: (request: CampaignObjectRequest, signal: AbortSignal) => Promise<HubEnvelope>;
  readFactionPage: (request: FactionObjectRequest, signal: AbortSignal) => Promise<FactionDirectoryPage>;
  readCampaignDetails: (request: CampaignDetailsObjectRequest, signal: AbortSignal) => Promise<CampaignReadModel>;
  readCampaignContext: (request: CampaignContextObjectRequest, signal: AbortSignal) => Promise<CampaignContextUpdate>;
  validateCampaign: (value: unknown) => value is HubEnvelope;
  maximumAgeMs?: number;
};

function campaignSelection({ perspective, campaignId }: CampaignObjectRequest) {
  return resourceCacheKey("dnd2024", resourceContractToken(campaignSummaryContract),
    perspective, campaignId ?? "bound");
}

function factionScope({ envelope, cursor }: FactionObjectRequest, generation: number) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const campaignEvidence = envelope.objectQueries?.campaignSummary;
  const continuationRevision = cursor === null ? null
    : envelope.world.factionDirectory?.sourceRevisionFingerprint ?? null;
  return resourceCacheKey(resourceContractToken(factionDirectoryContract), generation,
    envelope.applicationId, envelope.stateSpaceId, campaignId,
    envelope.audience.seat, envelope.audience.perspective, worldId,
    campaignEvidence?.resolutionFingerprint ?? "no-resolution",
    cursor, continuationRevision);
}

function scopedCampaignResource({ envelope }: CampaignDetailsObjectRequest | CampaignContextObjectRequest,
  generation: number, contractToken: string) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const evidence = envelope.objectQueries?.campaignSummary;
  return resourceCacheKey(contractToken, generation,
    envelope.applicationId, envelope.stateSpaceId, campaignId, worldId,
    envelope.audience.seat, envelope.audience.perspective,
    evidence?.resolutionFingerprint ?? "no-resolution");
}

function characterResourceScope({ envelope, actorId }: CharacterResourceRequest,
  generation: number, contractToken: string) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const evidence = envelope.objectQueries?.campaignSummary;
  return resourceCacheKey(contractToken, generation,
    envelope.applicationId, envelope.stateSpaceId, campaignId,
    envelope.audience.seat, envelope.audience.perspective,
    evidence?.resolutionFingerprint ?? "no-resolution", actorId);
}

function inventoryContainerResourceScope(request: InventoryContainerResourceRequest,
  generation: number, contractToken: string) {
  return resourceCacheKey(characterResourceScope(request, generation, contractToken), request.containerId);
}

function characterTableScope(envelope: ReadyHubEnvelope) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const currentActorIds = (envelope.party ?? []).filter((member) => member.isCurrent).map((member) => member.id).sort();
  return resourceCacheKey(envelope.applicationId, envelope.stateSpaceId, campaignId,
    worldId, envelope.audience.seat, envelope.audience.perspective, currentActorIds.join(","),
    envelope.objectQueries?.campaignSummary?.resolutionFingerprint ?? "no-resolution");
}

function worldScopeResource({ envelope, scopeId, cursor }: WorldScopeRequest, generation: number) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const evidence = envelope.objectQueries?.campaignSummary;
  const continuationRevision = cursor == null ? null
    : envelope.world.locationScopes.find((scope) => scope.id === scopeId)?.sourceRevisionFingerprint ?? null;
  return resourceCacheKey(cursor == null ? resourceContractToken(worldLocationScopeContract)
    : resourceContractToken(worldLocationScopePageContract), generation,
    envelope.applicationId, envelope.stateSpaceId, campaignId, worldId,
    envelope.audience.seat, envelope.audience.perspective,
    evidence?.resolutionFingerprint ?? "no-resolution", scopeId, cursor ?? null, continuationRevision);
}

function worldTableScope(envelope: ReadyHubEnvelope) {
  return characterTableScope(envelope);
}

function worldInformationResource({ envelope, section }: WorldInformationRequest, generation: number) {
  const token = section === "people" ? resourceContractToken(worldPeopleHoldingsPageContract)
    : `dnd2024-${section}-adapter-v1`;
  return resourceCacheKey(token, generation, worldTableScope(envelope), section);
}

function isCharacterResource(value: unknown): value is PartyMemberReadModel {
  if (!value || typeof value !== "object") return false;
  const member = value as Partial<PartyMemberReadModel>;
  return validText(member.id, 200) && validText(member.name, 400) &&
    Array.isArray(member.sheet) && Array.isArray(member.inventory) &&
    Boolean(member.sheetState && typeof member.sheetState === "object") &&
    Boolean(member.inventoryState && typeof member.inventoryState === "object");
}

function isCampaignDetails(value: unknown): value is CampaignReadModel {
  return Boolean(value && typeof value === "object" &&
    typeof (value as Partial<CampaignReadModel>).title === "string" &&
    Array.isArray((value as Partial<CampaignReadModel>).adventureLog) &&
    Array.isArray((value as Partial<CampaignReadModel>).placesVisited) &&
    Array.isArray((value as Partial<CampaignReadModel>).outcomes));
}

function isCampaignContextUpdate(value: unknown): value is CampaignContextUpdate {
  if (!value || typeof value !== "object") return false;
  const update = value as Record<string, unknown>;
  if (Object.keys(update).sort().join("|") !== "contextSelection|section" || update.section !== "context" ||
      !update.contextSelection || typeof update.contextSelection !== "object") return false;
  const selection = update.contextSelection as Record<string, unknown>;
  return validText(selection.selectedCampaignId, 200) && validText(selection.selectedWorldId, 200) &&
    Array.isArray(selection.worlds) && selection.worlds.length > 0;
}

function isWorldScopeUpdate(value: unknown): value is WorldScopeUpdate {
  if (!value || typeof value !== "object") return false;
  const update = value as Record<string, unknown>;
  if (update.section !== "locations" || !update.world || typeof update.world !== "object" ||
      !update.campaign || typeof update.campaign !== "object") return false;
  const world = update.world as Record<string, unknown>;
  const scopePage = update.scopePage as Record<string, unknown> | undefined;
  const locationScopes = world.locationScopes as unknown[] | undefined;
  const pageScope = locationScopes?.[0] as Record<string, unknown> | undefined;
  const childIds = pageScope?.childIds as unknown[] | undefined;
  if (!scopePage || !validText(scopePage.id, 400) || !Array.isArray(childIds) || childIds.length > 200 ||
      childIds.some((id) => !validText(id, 400)) || new Set(childIds).size !== childIds.length) return false;
  const allowedLocationIds = new Set([scopePage.id, ...childIds]);
  const locations = world.locations as unknown[] | undefined;
  if (!Array.isArray(locations) || locations.length > 201 || locations.some((location) =>
    !location || typeof location !== "object" ||
    !allowedLocationIds.has((location as Record<string, unknown>).id))) return false;
  const locationIds = locations.map((location) => (location as Record<string, unknown>).id);
  if (new Set(locationIds).size !== locationIds.length) return false;
  return Boolean(scopePage) && validText(scopePage?.id, 400) &&
    Array.isArray(world.maps) && world.maps.length > 0 && world.maps.length <= 202 &&
    locationScopes?.length === 1 && pageScope?.id === scopePage?.id &&
    validText(world.rootMapId, 400);
}

function isWorldInformationUpdate(value: unknown): value is WorldInformationUpdate {
  if (!value || typeof value !== "object") return false;
  const update = value as Record<string, unknown>;
  if (!["people", "lore", "history"].includes(String(update.section)) ||
      !update.world || typeof update.world !== "object") return false;
  const world = update.world as Record<string, unknown>;
  if (update.section === "people") {
    return Array.isArray(world.locations) && Array.isArray(world.people) &&
      world.locations.length <= 1_000 && world.people.length <= 2_000 &&
      world.locations.length + world.people.length <= 3_000 &&
      (!world.peopleDirectory || typeof world.peopleDirectory === "object");
  }
  if (update.section === "history")
    return Array.isArray(world.history) && world.history.length <= 500;
  if (!Array.isArray(world.lore) || world.lore.length > 500 ||
      !update.campaign || typeof update.campaign !== "object") return false;
  const campaign = update.campaign as Record<string, unknown>;
  return Array.isArray(campaign.quests) && Array.isArray(campaign.clues) &&
    Array.isArray(campaign.mapOverlays);
}

function validText(value: unknown, maximumLength: number) {
  return typeof value === "string" && value.length > 0 && value.length <= maximumLength && value === value.trim();
}

export function isFactionDirectoryPage(value: unknown): value is FactionDirectoryPage {
  if (!value || typeof value !== "object") return false;
  const page = value as Record<string, unknown>;
  const pageKeys = Object.keys(page).sort().join("|");
  if (pageKeys !== "complete|factions|nextCursor|projection|sourceRevisionFingerprint|totalCount" &&
      pageKeys !== "complete|coverage|factions|nextCursor|projection|sourceRevisionFingerprint|totalCount") return false;
  if (!Array.isArray(page.factions) || page.factions.length > 25 ||
      !Number.isInteger(page.totalCount) || (page.totalCount as number) < page.factions.length ||
      (page.totalCount as number) > 100 || typeof page.complete !== "boolean" ||
      !(page.nextCursor === null || validText(page.nextCursor, 2_048)) ||
      page.complete !== (page.nextCursor === null) ||
      !validText(page.sourceRevisionFingerprint, 128) ||
      (page.coverage !== undefined && !["complete", "partial"].includes(page.coverage as string))) return false;
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

/** Coordinates Campaign/World-table transport only. Confirmed values live in Redux. */
export class TableResourceOwner {
  readonly #coordinator = new RequestCoordinator();
  readonly #campaign: CoordinatedRequest<CampaignObjectRequest, HubEnvelope>;
  readonly #factions: CoordinatedRequest<FactionObjectRequest, FactionDirectoryPage>;
  readonly #campaignDetails: CoordinatedRequest<CampaignDetailsObjectRequest, CampaignReadModel>;
  readonly #campaignContext: CoordinatedRequest<CampaignContextObjectRequest, CampaignContextUpdate>;

  constructor(options: TableResourceOwnerOptions) {
    void options.maximumAgeMs;
    this.#campaign = {
      key: (request, generation) => resourceCacheKey(campaignSelection(request), generation),
      read: options.readCampaign,
      validate: options.validateCampaign,
      maximumBytes: 2 * 1024 * 1024,
    };
    this.#factions = {
      key: (request, generation) => factionScope(request, generation),
      read: options.readFactionPage,
      validate: isFactionDirectoryPage,
      maximumBytes: 524_288,
    };
    this.#campaignDetails = {
      key: (request, generation) => scopedCampaignResource(request, generation,
        resourceContractToken(campaignDetailsContract)),
      read: options.readCampaignDetails,
      validate: isCampaignDetails,
      maximumBytes: 2 * 1024 * 1024,
    };
    this.#campaignContext = {
      key: (request, generation) => scopedCampaignResource(request, generation,
        resourceContractToken(campaignContextContract)),
      read: options.readCampaignContext,
      validate: isCampaignContextUpdate,
      maximumBytes: 524_288,
    };
  }

  async loadCampaign(request: CampaignObjectRequest, preferCached = false, signal?: AbortSignal) {
    void preferCached;
    const selection = campaignSelection(request);
    this.#coordinator.replaceScope(selection);
    return this.#coordinator.load(this.#campaign, request, signal);
  }

  async loadFactionPage(request: FactionObjectRequest, signal?: AbortSignal, preferCached = true) {
    if (signal?.aborted) throw new DOMException("Faction page replaced", "AbortError");
    const acceptRevision = (page: FactionDirectoryPage) => {
      const expectedRevision = request.cursor === null
        ? null
        : request.envelope.world.factionDirectory?.sourceRevisionFingerprint ?? null;
      if (expectedRevision && page.sourceRevisionFingerprint !== expectedRevision) {
        this.#coordinator.invalidate();
        throw new ViewReadError("stale-data", "The faction directory changed while it was being paged.");
      }
      return page;
    };
    void preferCached;
    this.#coordinator.replaceScope(characterTableScope(request.envelope));
    return acceptRevision(await this.#coordinator.load(this.#factions, request, signal));
  }

  async loadCampaignDetails(request: CampaignDetailsObjectRequest, signal?: AbortSignal, preferCached = true) {
    void preferCached;
    this.#coordinator.replaceScope(characterTableScope(request.envelope));
    return this.#coordinator.load(this.#campaignDetails, request, signal);
  }

  async loadCampaignContext(request: CampaignContextObjectRequest, signal?: AbortSignal, preferCached = true) {
    void preferCached;
    this.#coordinator.replaceScope(characterTableScope(request.envelope));
    return this.#coordinator.load(this.#campaignContext, request, signal);
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId === CAMPAIGN_SUMMARY_OBJECT_ID) {
      this.#coordinator.invalidate();
      return true;
    }
    if (qualifiedId === FACTION_DIRECTORY_OBJECT_ID) {
      this.#coordinator.invalidate();
      return true;
    }
    if (qualifiedId === CAMPAIGN_LOCATION_VISITS_OBJECT_ID) {
      this.#coordinator.invalidate();
      return true;
    }
    if (qualifiedId === WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID) {
      this.#coordinator.invalidate();
      return true;
    }
    return false;
  }

  invalidateAll(reason: ResourceInvalidationReason = "manual") {
    void reason;
    this.#coordinator.invalidate();
  }

  cacheMetrics() { return { retainedEntries: 0, retainedBytes: 0 }; }
}

type WorldResourceOwnerOptions = {
  readScope: (request: WorldScopeRequest, signal: AbortSignal) => Promise<WorldScopeUpdate>;
  readInformation: (request: WorldInformationRequest, signal: AbortSignal) => Promise<WorldInformationUpdate>;
  maximumEntries?: number;
  maximumRetainedBytes?: number;
  maximumAgeMs?: number;
};

/** Owns independently loaded map/location scopes and fences them to one authorized table view. */
export class WorldResourceOwner {
  readonly #coordinator = new RequestCoordinator();
  readonly #scopes: CoordinatedRequest<WorldScopeRequest, WorldScopeUpdate>;
  readonly #information: CoordinatedRequest<WorldInformationRequest, WorldInformationUpdate>;
  #activeScope: string | null = null;

  constructor(options: WorldResourceOwnerOptions) {
    void options.maximumEntries; void options.maximumRetainedBytes; void options.maximumAgeMs;
    this.#scopes = {
      key: (request, generation) => worldScopeResource(request, generation),
      read: options.readScope,
      validate: isWorldScopeUpdate,
      maximumBytes: 524_288,
    };
    this.#information = {
      key: (request, generation) => worldInformationResource(request, generation),
      read: options.readInformation,
      validate: isWorldInformationUpdate,
      maximumBytes: 4 * 1024 * 1024,
    };
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false,
    reason: ResourceInvalidationReason = "scope-replaced") {
    const scope = worldTableScope(envelope);
    void reason;
    this.#coordinator.replaceScope(scope, force || this.#activeScope !== null && this.#activeScope !== scope);
    this.#activeScope = scope;
  }

  async loadScope(request: WorldScopeRequest, signal?: AbortSignal, preferCached = true) {
    throwIfWorldReadAborted(signal);
    this.replaceScope(request.envelope);
    throwIfWorldReadAborted(signal);
    void preferCached;
    const value = await this.#coordinator.load(this.#scopes, request, signal);
    throwIfWorldReadAborted(signal);
    return value;
  }

  async loadInformation(request: WorldInformationRequest, signal?: AbortSignal, preferCached = true) {
    throwIfWorldReadAborted(signal);
    this.replaceScope(request.envelope);
    throwIfWorldReadAborted(signal);
    void preferCached;
    const value = await this.#coordinator.load(this.#information, request, signal);
    throwIfWorldReadAborted(signal);
    return value;
  }

  invalidateAll(reason: ResourceInvalidationReason = "manual") {
    void reason;
    this.#coordinator.invalidate();
  }

  cacheMetrics() { return { retainedEntries: 0, retainedBytes: 0 }; }
}

type CharacterResourceOwnerOptions = {
  readSheet: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  readDetails: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  readInventory: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<InventoryContainerResult>;
  readInventoryContainer?: (request: InventoryContainerResourceRequest, signal: AbortSignal) => Promise<InventoryContainerPageResult>;
  maximumEntries?: number;
  maximumRetainedBytes?: number;
  maximumAgeMs?: number;
  /** Redux is the sole owner of completed character values. This callback is a scoped, bounded lookup. */
  readConfirmed?: <T extends PartyMemberReadModel | InventoryContainerResult>(
    facet: "sheet" | "details" | "inventory",
    request: CharacterResourceRequest,
    maximumAgeMs: number,
  ) => T | null;
  clearConfirmed?: () => void;
  clearScope?: () => void;
};

/**
 * Coordinates independent selected-character reads. It intentionally retains no response
 * values: a confirmed value is read from Redux, while this owner only deduplicates active
 * requests, retries transport failures, bounds bodies, and fences obsolete scopes.
 */
export class CharacterResourceOwner {
  readonly #coordinator = new RequestCoordinator();
  readonly #sheet: CoordinatedRequest<CharacterResourceRequest, PartyMemberReadModel>;
  readonly #details: CoordinatedRequest<CharacterResourceRequest, PartyMemberReadModel>;
  readonly #inventory: CoordinatedRequest<CharacterResourceRequest, InventoryContainerResult>;
  readonly #inventoryContainers: CoordinatedRequest<InventoryContainerResourceRequest, InventoryContainerPageResult> | null;
  readonly #readConfirmed: CharacterResourceOwnerOptions["readConfirmed"];
  readonly #clearConfirmed: (() => void) | undefined;
  readonly #clearScope: (() => void) | undefined;
  readonly #maximumAgeMs: { sheet: number; details: number; inventory: number };
  #activeScope: string | null = null;
  #hits = 0;
  #misses = 0;

  constructor(options: CharacterResourceOwnerOptions) {
    this.#readConfirmed = options.readConfirmed;
    this.#clearConfirmed = options.clearConfirmed;
    this.#clearScope = options.clearScope;
    const maximumAgeMs = options.maximumAgeMs;
    this.#maximumAgeMs = {
      sheet: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.characterSheet,
      details: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.characterDetails,
      inventory: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.characterInventory,
    };
    this.#sheet = {
      key: (request, generation) => characterResourceScope(request,
        generation, resourceContractToken(characterSheetContract)),
      read: options.readSheet, validate: isCharacterResource,
      maximumBytes: 1_100_000,
    };
    this.#details = {
      key: (request, generation) => characterResourceScope(request,
        generation, resourceContractToken(characterDossierContract)),
      read: options.readDetails, validate: isCharacterResource,
      maximumBytes: 1_100_000,
    };
    this.#inventory = {
      key: (request, generation) => characterResourceScope(request,
        generation, resourceCacheKey(resourceContractToken(inventoryContainerContract),
          resourceContractToken(inventoryWalletContract))),
      read: options.readInventory,
      validate: (value): value is InventoryContainerResult => Boolean(value && typeof value === "object" &&
        "status" in value && typeof value.status === "string" &&
        ["ready", "error", "forbidden"].includes(value.status)),
      maximumBytes: 5_500_000,
    };
    this.#inventoryContainers = options.readInventoryContainer ? {
      key: (request, generation) => inventoryContainerResourceScope(request,
        generation, resourceContractToken(inventoryContainerContract)),
      read: options.readInventoryContainer,
      validate: (value): value is InventoryContainerPageResult => Boolean(value && typeof value === "object" &&
        "status" in value && typeof value.status === "string" &&
        ["ready", "error", "forbidden"].includes(value.status)),
      maximumBytes: 5_500_000,
    } : null;
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false,
    reason: ResourceInvalidationReason = "scope-replaced") {
    void reason;
    const scope = characterTableScope(envelope);
    const scopeChanged = this.#activeScope !== null && this.#activeScope !== scope;
    if (this.#activeScope !== null && (force || scopeChanged)) {
      this.#coordinator.replaceScope(scope, true);
      if (scopeChanged) this.#clearScope?.();
    } else {
      this.#coordinator.replaceScope(scope);
    }
    this.#activeScope = scope;
  }

  async loadSheet(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.loadSheetOutcome(request, signal, preferCached)).value;
  }

  async loadSheetOutcome(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true): Promise<CharacterReadOutcome<PartyMemberReadModel>> {
    throwIfCharacterReadAborted(signal);
    this.replaceScope(request.envelope);
    const cached = preferCached ? this.#readConfirmed?.<PartyMemberReadModel>("sheet", request,
      this.#maximumAgeMs.sheet) : null;
    throwIfCharacterReadAborted(signal);
    if (cached) { this.#hits += 1; return cachedCharacterRead(cached); }
    this.#misses += 1;
    return freshCharacterRead(await this.#coordinator.load(this.#sheet, request, signal));
  }

  async loadDetails(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.loadDetailsOutcome(request, signal, preferCached)).value;
  }

  async loadDetailsOutcome(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true): Promise<CharacterReadOutcome<PartyMemberReadModel>> {
    throwIfCharacterReadAborted(signal);
    this.replaceScope(request.envelope);
    const cached = preferCached ? this.#readConfirmed?.<PartyMemberReadModel>("details", request,
      this.#maximumAgeMs.details) : null;
    throwIfCharacterReadAborted(signal);
    if (cached) { this.#hits += 1; return cachedCharacterRead(cached); }
    this.#misses += 1;
    return freshCharacterRead(await this.#coordinator.load(this.#details, request, signal));
  }

  async loadInventory(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.loadInventoryOutcome(request, signal, preferCached)).value;
  }

  async loadInventoryOutcome(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true): Promise<CharacterReadOutcome<InventoryContainerResult>> {
    throwIfCharacterReadAborted(signal);
    this.replaceScope(request.envelope);
    const cached = preferCached ? this.#readConfirmed?.<InventoryContainerResult>("inventory", request,
      this.#maximumAgeMs.inventory) : null;
    throwIfCharacterReadAborted(signal);
    if (cached) { this.#hits += 1; return cachedCharacterRead(cached); }
    this.#misses += 1;
    return freshCharacterRead(await this.#coordinator.load(this.#inventory, request, signal));
  }

  async loadInventoryContainer(request: InventoryContainerResourceRequest, signal?: AbortSignal,
    preferCached = true) {
    if (!this.#inventoryContainers) throw new Error("Scoped inventory loading is unavailable.");
    this.replaceScope(request.envelope);
    // Container pages remain query-scoped. Until coverage proves a complete, compatible
    // collection, their confirmed values are deliberately not reused across queries.
    void preferCached;
    this.#misses += 1;
    return this.#coordinator.load(this.#inventoryContainers, request, signal);
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId === CHARACTER_DOSSIER_OBJECT_ID || qualifiedId === CAMPAIGN_SUMMARY_OBJECT_ID) {
      this.#coordinator.invalidate();
      this.#clearConfirmed?.();
      return true;
    }
    if (qualifiedId.startsWith("dnd2024.object.inventory-item-")) {
      this.#coordinator.invalidate();
      this.#clearConfirmed?.();
      return true;
    }
    return false;
  }

  invalidateAll(reason: ResourceInvalidationReason = "manual") {
    void reason;
    this.#coordinator.invalidate();
    this.#clearConfirmed?.();
  }

  cacheMetrics() {
    return { hits: this.#hits, misses: this.#misses, expiries: 0, inFlightShares: 0,
      retainedEntries: 0, retainedBytes: 0, activeRequests: 0, evictions: 0, invalidationsByReason: {} };
  }
}
