import type { CampaignReadModel, DeferredHubUpdate, HubEnvelope, InventoryContainerPageResult, InventoryContainerResult, ObjectReadEvidence, PartyMemberReadModel, Perspective, ReadyHubEnvelope, WorldFaction } from "./hub-types";
import { ResourceStore, type KeyedResource, type ResourceInvalidationReason } from "./resource-store";
import type { ResourceState } from "./resource-state";
import { RESOURCE_FRESHNESS_MS, resourceCacheKey, resourceContractToken } from "./resource-policy";
import { ViewReadError } from "./view-read-client";
import { isCampaignReadModel } from "../state.js";
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
import { contract as campaignResumeContract } from "../server/campaign-resume-contract.js";
import { contract as currentSceneContract } from "../server/current-scene-contract.js";

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
export type WorldScopeRequest = { envelope: ReadyHubEnvelope; scopeId: string; cursor?: string | null };
export type WorldScopeUpdate = Extract<DeferredHubUpdate, { section: "locations" }>;
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
  maximumEntries?: number;
  maximumRetainedBytes?: number;
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
  return resourceCacheKey(envelope.applicationId, envelope.stateSpaceId, campaignId,
    envelope.audience.seat, envelope.audience.perspective,
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
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  return resourceCacheKey(envelope.applicationId, envelope.stateSpaceId, campaignId,
    envelope.contextSelection?.selectedWorldId ?? envelope.world.id,
    envelope.audience.seat, envelope.audience.perspective,
    envelope.objectQueries?.campaignSummary?.resolutionFingerprint ?? "no-resolution");
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
  return isCampaignReadModel(value);
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
  return Array.isArray(world.maps) && world.maps.length > 0 && world.maps.length <= 1_001 &&
    Array.isArray(world.locations) && world.locations.length <= 1_000 &&
    Array.isArray(world.locationScopes) && world.locationScopes.length <= 1_001 &&
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

function isCurrentViewUpdate(value: unknown): value is CurrentViewUpdate {
  if (!value || typeof value !== "object") return false;
  const update = value as Record<string, unknown>;
  if (update.section !== "current" || !update.currentSituation ||
      typeof update.currentSituation !== "object" || !update.world || typeof update.world !== "object" ||
      !update.campaign || typeof update.campaign !== "object") return false;
  const situation = update.currentSituation as Record<string, unknown>;
  const world = update.world as Record<string, unknown>;
  const campaign = update.campaign as Record<string, unknown>;
  return ["ready", "unavailable"].includes(String(situation.status)) &&
    typeof world.currentLocationId === "string" && Array.isArray(world.locations) &&
    world.locations.length <= 1_000 && Array.isArray(campaign.mapOverlays);
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
  readonly #campaignDetails: KeyedResource<CampaignDetailsObjectRequest, CampaignReadModel>;
  readonly #campaignContext: KeyedResource<CampaignContextObjectRequest, CampaignContextUpdate>;
  #activeSelection: string | null = null;
  #generation = 0;

  constructor(options: TableResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 16,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 4 * 1024 * 1024,
      diagnosticName: "table-resources",
    });
    const maximumAgeMs = options.maximumAgeMs;
    this.#campaign = this.#store.define({
      name: "campaign-summary",
      cacheKey: campaignSelection,
      read: options.readCampaign,
      validate: options.validateCampaign,
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.campaignSummary,
      maximumEntryBytes: 2 * 1024 * 1024,
    });
    this.#factions = this.#store.define({
      name: "faction-directory-page",
      cacheKey: (request) => factionScope(request, this.#generation),
      read: options.readFactionPage,
      validate: isFactionDirectoryPage,
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.factionDirectoryPage,
      maximumEntryBytes: 524_288,
    });
    this.#campaignDetails = this.#store.define({
      name: "campaign-details",
      cacheKey: (request) => scopedCampaignResource(request, this.#generation,
        resourceContractToken(campaignDetailsContract)),
      read: options.readCampaignDetails,
      validate: isCampaignDetails,
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.campaignDetails,
      maximumEntryBytes: 2 * 1024 * 1024,
    });
    this.#campaignContext = this.#store.define({
      name: "campaign-context",
      cacheKey: (request) => scopedCampaignResource(request, this.#generation,
        resourceContractToken(campaignContextContract)),
      read: options.readCampaignContext,
      validate: isCampaignContextUpdate,
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.campaignContext,
      maximumEntryBytes: 524_288,
    });
  }

  async loadCampaign(request: CampaignObjectRequest, preferCached = false, signal?: AbortSignal) {
    const selection = campaignSelection(request);
    const changed = this.#activeSelection !== null && selection !== this.#activeSelection;
    if (changed) this.#store.invalidateAll("scope-replaced");
    this.#activeSelection = selection;
    const cached = preferCached ? this.#campaign.peek(request) : null;
    const value = (await this.#campaign.load(request, { preferCached, signal })).value;
    if (!cached) {
      this.#generation += 1;
      this.#factions.invalidate(undefined, "workspace-replaced");
      this.#campaignDetails.invalidate(undefined, "workspace-replaced");
      this.#campaignContext.invalidate(undefined, "workspace-replaced");
    }
    return value;
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
        this.#factions.invalidate(undefined, "workspace-replaced");
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

  async loadCampaignDetails(request: CampaignDetailsObjectRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#campaignDetails.load(request, { preferCached, signal })).value;
  }

  async loadCampaignContext(request: CampaignContextObjectRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#campaignContext.load(request, { preferCached, signal })).value;
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId === CAMPAIGN_SUMMARY_OBJECT_ID) {
      this.#campaign.invalidate(undefined, "object-change");
      this.#campaignDetails.invalidate(undefined, "object-change");
      this.#campaignContext.invalidate(undefined, "object-change");
      return true;
    }
    if (qualifiedId === FACTION_DIRECTORY_OBJECT_ID) {
      this.#factions.invalidate(undefined, "object-change");
      return true;
    }
    if (qualifiedId === CAMPAIGN_LOCATION_VISITS_OBJECT_ID) {
      this.#campaignDetails.invalidate(undefined, "object-change");
      return true;
    }
    if (qualifiedId === WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID) {
      this.#campaignContext.invalidate(undefined, "object-change");
      return true;
    }
    return false;
  }

  invalidateAll(reason: ResourceInvalidationReason = "manual") {
    this.#generation += 1;
    this.#store.invalidateAll(reason);
  }

  cacheMetrics() { return this.#store.metrics(); }
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
  readonly #store: ResourceStore;
  readonly #scopes: KeyedResource<WorldScopeRequest, WorldScopeUpdate>;
  readonly #information: KeyedResource<WorldInformationRequest, WorldInformationUpdate>;
  #activeScope: string | null = null;
  #generation = 0;

  constructor(options: WorldResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 20,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 8 * 1024 * 1024,
      diagnosticName: "world-resources",
    });
    this.#scopes = this.#store.define({
      name: "world-location-scope",
      cacheKey: (request) => worldScopeResource(request, this.#generation),
      read: options.readScope,
      validate: isWorldScopeUpdate,
      maximumAgeMs: options.maximumAgeMs ?? RESOURCE_FRESHNESS_MS.worldLocationScope,
      maximumEntryBytes: 524_288,
    });
    this.#information = this.#store.define({
      name: "world-information",
      cacheKey: (request) => worldInformationResource(request, this.#generation),
      read: options.readInformation,
      validate: isWorldInformationUpdate,
      maximumAgeMs: options.maximumAgeMs ?? RESOURCE_FRESHNESS_MS.worldInformation,
      maximumEntryBytes: 4 * 1024 * 1024,
    });
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false,
    reason: ResourceInvalidationReason = "scope-replaced") {
    const scope = worldTableScope(envelope);
    if (this.#activeScope !== null && (force || this.#activeScope !== scope)) {
      this.#generation += 1;
      this.#store.invalidateAll(reason);
    }
    this.#activeScope = scope;
  }

  async loadScope(request: WorldScopeRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    return (await this.#scopes.load(request, { signal, preferCached })).value;
  }

  async loadInformation(request: WorldInformationRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    return (await this.#information.load(request, { signal, preferCached })).value;
  }

  invalidateAll(reason: ResourceInvalidationReason = "manual") {
    this.#generation += 1;
    this.#store.invalidateAll(reason);
  }

  cacheMetrics() { return this.#store.metrics(); }
}

type CurrentViewResourceOwnerOptions = {
  readCurrent: (request: CurrentViewRequest, signal: AbortSignal) => Promise<CurrentViewUpdate>;
  maximumEntries?: number;
  maximumRetainedBytes?: number;
  maximumAgeMs?: number;
};

/** Owns the composed scene/resume/board resource and fences late results by observer and campaign. */
export class CurrentViewResourceOwner {
  readonly #store: ResourceStore;
  readonly #current: KeyedResource<CurrentViewRequest, CurrentViewUpdate>;
  #activeScope: string | null = null;
  #generation = 0;

  constructor(options: CurrentViewResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 2,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 2 * 1024 * 1024,
      diagnosticName: "current-view-resources",
    });
    this.#current = this.#store.define({
      name: "current-view",
      cacheKey: (request) => scopedCampaignResource(request, this.#generation,
        resourceCacheKey(resourceContractToken(campaignResumeContract), resourceContractToken(currentSceneContract),
          "current-view-adapter-v1")),
      read: options.readCurrent,
      validate: isCurrentViewUpdate,
      maximumAgeMs: options.maximumAgeMs ?? RESOURCE_FRESHNESS_MS.currentView,
      maximumEntryBytes: 1_100_000,
    });
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false,
    reason: ResourceInvalidationReason = "scope-replaced") {
    const scope = worldTableScope(envelope);
    if (this.#activeScope !== null && (force || this.#activeScope !== scope)) {
      this.#generation += 1;
      this.#store.invalidateAll(reason);
    }
    this.#activeScope = scope;
  }

  async loadCurrent(request: CurrentViewRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    return (await this.#current.load(request, { signal, preferCached })).value;
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId !== CAMPAIGN_SUMMARY_OBJECT_ID) return false;
    this.#current.invalidate(undefined, "object-change");
    return true;
  }

  invalidateAll(reason: ResourceInvalidationReason = "manual") {
    this.#generation += 1;
    this.#store.invalidateAll(reason);
  }

  cacheMetrics() { return this.#store.metrics(); }
}

type CharacterResourceOwnerOptions = {
  readSheet: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  readDetails: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  readInventory: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<InventoryContainerResult>;
  readInventoryContainer?: (request: InventoryContainerResourceRequest, signal: AbortSignal) => Promise<InventoryContainerPageResult>;
  maximumEntries?: number;
  maximumRetainedBytes?: number;
  maximumAgeMs?: number;
};

/** Independent selected-character resources sharing the bounded table cache and scope fence. */
export class CharacterResourceOwner {
  readonly #store: ResourceStore;
  readonly #sheet: KeyedResource<CharacterResourceRequest, PartyMemberReadModel>;
  readonly #details: KeyedResource<CharacterResourceRequest, PartyMemberReadModel>;
  readonly #inventory: KeyedResource<CharacterResourceRequest, InventoryContainerResult>;
  readonly #inventoryContainers: KeyedResource<InventoryContainerResourceRequest, InventoryContainerPageResult> | null;
  #activeScope: string | null = null;
  #generation = 0;

  constructor(options: CharacterResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 64,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 24 * 1024 * 1024,
      diagnosticName: "character-resources",
    });
    const maximumAgeMs = options.maximumAgeMs;
    this.#sheet = this.#store.define({
      name: "character-sheet", cacheKey: (request) => characterResourceScope(request,
        this.#generation, resourceContractToken(characterSheetContract)),
      read: options.readSheet, validate: isCharacterResource,
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.characterSheet, maximumEntryBytes: 1_100_000,
    });
    this.#details = this.#store.define({
      name: "character-details", cacheKey: (request) => characterResourceScope(request,
        this.#generation, resourceContractToken(characterDossierContract)),
      read: options.readDetails, validate: isCharacterResource,
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.characterDetails, maximumEntryBytes: 1_100_000,
    });
    this.#inventory = this.#store.define({
      name: "character-inventory", cacheKey: (request) => characterResourceScope(request,
        this.#generation, resourceCacheKey(resourceContractToken(inventoryContainerContract),
          resourceContractToken(inventoryWalletContract))),
      read: options.readInventory,
      validate: (value): value is InventoryContainerResult => Boolean(value && typeof value === "object" &&
        "status" in value && typeof value.status === "string" &&
        ["ready", "error", "forbidden"].includes(value.status)),
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.characterInventory, maximumEntryBytes: 5_500_000,
    });
    this.#inventoryContainers = options.readInventoryContainer ? this.#store.define({
      name: "inventory-container-page",
      cacheKey: (request) => inventoryContainerResourceScope(request,
        this.#generation, resourceContractToken(inventoryContainerContract)),
      read: options.readInventoryContainer,
      validate: (value): value is InventoryContainerPageResult => Boolean(value && typeof value === "object" &&
        "status" in value && typeof value.status === "string" &&
        ["ready", "error", "forbidden"].includes(value.status)),
      maximumAgeMs: maximumAgeMs ?? RESOURCE_FRESHNESS_MS.characterInventory, maximumEntryBytes: 5_500_000,
    }) : null;
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false,
    reason: ResourceInvalidationReason = "scope-replaced") {
    const scope = characterTableScope(envelope);
    if (this.#activeScope !== null && (force || this.#activeScope !== scope)) {
      this.#generation += 1;
      this.#store.invalidateAll(reason);
    }
    this.#activeScope = scope;
  }

  async loadSheet(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    const value = (await this.#sheet.load(request, { signal, preferCached })).value;
    if (value.sheetState.status === "error") this.#sheet.invalidate(undefined, "manual");
    return value;
  }

  async loadDetails(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    const value = (await this.#details.load(request, { signal, preferCached })).value;
    if (value.sheetState.status === "error" || value.inventoryState.status === "error")
      this.#details.invalidate(undefined, "manual");
    return value;
  }

  async loadInventory(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    const value = (await this.#inventory.load(request, { signal, preferCached })).value;
    if (value.status === "error") this.#inventory.invalidate(undefined, "manual");
    return value;
  }

  async loadInventoryContainer(request: InventoryContainerResourceRequest, signal?: AbortSignal,
    preferCached = true) {
    if (!this.#inventoryContainers) throw new Error("Scoped inventory loading is unavailable.");
    this.replaceScope(request.envelope);
    const value = (await this.#inventoryContainers.load(request, { signal, preferCached })).value;
    if (value.status === "error") this.#inventoryContainers.invalidate(undefined, "manual");
    return value;
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId === CHARACTER_DOSSIER_OBJECT_ID || qualifiedId === CAMPAIGN_SUMMARY_OBJECT_ID) {
      this.#sheet.invalidate(undefined, "object-change");
      this.#details.invalidate(undefined, "object-change");
      this.#inventory.invalidate(undefined, "object-change");
      this.#inventoryContainers?.invalidate(undefined, "object-change");
      return true;
    }
    if (qualifiedId.startsWith("dnd2024.object.inventory-item-")) {
      this.#details.invalidate(undefined, "object-change");
      this.#inventory.invalidate(undefined, "object-change");
      this.#inventoryContainers?.invalidate(undefined, "object-change");
      return true;
    }
    return false;
  }

  invalidateAll(reason: ResourceInvalidationReason = "manual") {
    this.#generation += 1;
    this.#store.invalidateAll(reason);
  }

  cacheMetrics() { return this.#store.metrics(); }
}
