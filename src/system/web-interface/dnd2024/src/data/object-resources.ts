import type { CampaignReadModel, DeferredHubUpdate, HubEnvelope, InventoryContainerResult, ObjectReadEvidence, PartyMemberReadModel, Perspective, ReadyHubEnvelope, WorldFaction } from "./hub-types";
import { ResourceStore, type KeyedResource } from "./resource-store";
import type { ResourceState } from "./resource-state";
import { ViewReadError } from "./view-read-client";
import { isCampaignReadModel } from "../state.js";

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
export type WorldScopeRequest = { envelope: ReadyHubEnvelope; scopeId: string };
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

function scopedCampaignResource({ envelope }: CampaignDetailsObjectRequest | CampaignContextObjectRequest) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const evidence = envelope.objectQueries?.campaignSummary;
  return [envelope.applicationId, envelope.stateSpaceId, campaignId, worldId,
    envelope.audience.seat, envelope.audience.perspective,
    evidence?.resolutionFingerprint ?? "no-resolution",
    evidence?.sourceRevisionFingerprint ?? "no-source-revision"].join(":");
}

function characterResourceScope({ envelope, actorId }: CharacterResourceRequest) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const evidence = envelope.objectQueries?.campaignSummary;
  return [envelope.applicationId, envelope.stateSpaceId, campaignId,
    envelope.audience.seat, envelope.audience.perspective,
    evidence?.resolutionFingerprint ?? "no-resolution",
    evidence?.sourceRevisionFingerprint ?? "no-source-revision", actorId].join(":");
}

function characterTableScope(envelope: ReadyHubEnvelope) {
  return characterResourceScope({ envelope, actorId: "all-characters" });
}

function worldScopeResource({ envelope, scopeId }: WorldScopeRequest) {
  const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
  const worldId = envelope.contextSelection?.selectedWorldId ?? envelope.world.id;
  const evidence = envelope.objectQueries?.campaignSummary;
  return [envelope.applicationId, envelope.stateSpaceId, campaignId, worldId,
    envelope.audience.seat, envelope.audience.perspective,
    evidence?.resolutionFingerprint ?? "no-resolution",
    evidence?.sourceRevisionFingerprint ?? "no-source-revision", scopeId].join(":");
}

function worldTableScope(envelope: ReadyHubEnvelope) {
  return worldScopeResource({ envelope, scopeId: "all-world-scopes" });
}

function worldInformationResource({ envelope, section }: WorldInformationRequest) {
  return `${worldTableScope(envelope)}:${section}`;
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
      world.locations.length + world.people.length <= 200;
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
    this.#campaignDetails = this.#store.define({
      name: "campaign-details",
      cacheKey: scopedCampaignResource,
      read: options.readCampaignDetails,
      validate: isCampaignDetails,
      maximumAgeMs,
      maximumEntryBytes: 2 * 1024 * 1024,
    });
    this.#campaignContext = this.#store.define({
      name: "campaign-context",
      cacheKey: scopedCampaignResource,
      read: options.readCampaignContext,
      validate: isCampaignContextUpdate,
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

  async loadCampaignDetails(request: CampaignDetailsObjectRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#campaignDetails.load(request, { preferCached, signal })).value;
  }

  async loadCampaignContext(request: CampaignContextObjectRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#campaignContext.load(request, { preferCached, signal })).value;
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId === CAMPAIGN_SUMMARY_OBJECT_ID) {
      this.#campaign.invalidate();
      this.#campaignDetails.invalidate();
      this.#campaignContext.invalidate();
      return true;
    }
    if (qualifiedId === FACTION_DIRECTORY_OBJECT_ID) {
      this.#factions.invalidate();
      return true;
    }
    if (qualifiedId === CAMPAIGN_LOCATION_VISITS_OBJECT_ID) {
      this.#campaignDetails.invalidate();
      return true;
    }
    if (qualifiedId === WORLD_CAMPAIGN_DIRECTORY_OBJECT_ID) {
      this.#campaignContext.invalidate();
      return true;
    }
    return false;
  }

  invalidateAll() {
    this.#store.invalidateAll();
  }
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

  constructor(options: WorldResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 20,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 2 * 1024 * 1024,
    });
    this.#scopes = this.#store.define({
      name: "world-location-scope",
      cacheKey: worldScopeResource,
      read: options.readScope,
      validate: isWorldScopeUpdate,
      maximumAgeMs: options.maximumAgeMs ?? 30_000,
      maximumEntryBytes: 524_288,
    });
    this.#information = this.#store.define({
      name: "world-information",
      cacheKey: worldInformationResource,
      read: options.readInformation,
      validate: isWorldInformationUpdate,
      maximumAgeMs: options.maximumAgeMs ?? 30_000,
      maximumEntryBytes: 1_100_000,
    });
  }

  replaceScope(envelope: ReadyHubEnvelope) {
    const scope = worldTableScope(envelope);
    if (this.#activeScope !== null && this.#activeScope !== scope) this.#store.invalidateAll();
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

  invalidateAll() {
    this.#store.invalidateAll();
  }
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

  constructor(options: CurrentViewResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 2,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 2 * 1024 * 1024,
    });
    this.#current = this.#store.define({
      name: "current-view",
      cacheKey: scopedCampaignResource,
      read: options.readCurrent,
      validate: isCurrentViewUpdate,
      maximumAgeMs: options.maximumAgeMs ?? 15_000,
      maximumEntryBytes: 1_100_000,
    });
  }

  replaceScope(envelope: ReadyHubEnvelope) {
    const scope = scopedCampaignResource({ envelope });
    if (this.#activeScope !== null && this.#activeScope !== scope) this.#store.invalidateAll();
    this.#activeScope = scope;
  }

  async loadCurrent(request: CurrentViewRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    return (await this.#current.load(request, { signal, preferCached })).value;
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId !== CAMPAIGN_SUMMARY_OBJECT_ID) return false;
    this.#current.invalidate();
    return true;
  }

  invalidateAll() {
    this.#store.invalidateAll();
  }
}

type CharacterResourceOwnerOptions = {
  readSheet: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  readDetails: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<PartyMemberReadModel>;
  readInventory: (request: CharacterResourceRequest, signal: AbortSignal) => Promise<InventoryContainerResult>;
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
  #activeScope: string | null = null;

  constructor(options: CharacterResourceOwnerOptions) {
    this.#store = new ResourceStore({
      maximumEntries: options.maximumEntries ?? 12,
      maximumRetainedBytes: options.maximumRetainedBytes ?? 4 * 1024 * 1024,
    });
    const maximumAgeMs = options.maximumAgeMs ?? 30_000;
    this.#sheet = this.#store.define({
      name: "character-sheet", cacheKey: characterResourceScope,
      read: options.readSheet, validate: isCharacterResource,
      maximumAgeMs, maximumEntryBytes: 1_100_000,
    });
    this.#details = this.#store.define({
      name: "character-details", cacheKey: characterResourceScope,
      read: options.readDetails, validate: isCharacterResource,
      maximumAgeMs, maximumEntryBytes: 1_100_000,
    });
    this.#inventory = this.#store.define({
      name: "character-inventory", cacheKey: characterResourceScope,
      read: options.readInventory,
      validate: (value): value is InventoryContainerResult => Boolean(value && typeof value === "object" &&
        "status" in value && typeof value.status === "string" &&
        ["ready", "error", "forbidden"].includes(value.status)),
      maximumAgeMs, maximumEntryBytes: 280_000,
    });
  }

  replaceScope(envelope: ReadyHubEnvelope) {
    const scope = characterTableScope(envelope);
    if (this.#activeScope !== null && this.#activeScope !== scope) this.#store.invalidateAll();
    this.#activeScope = scope;
  }

  async loadSheet(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    const value = (await this.#sheet.load(request, { signal, preferCached })).value;
    if (value.sheetState.status === "error") this.#sheet.invalidate();
    return value;
  }

  async loadDetails(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    const value = (await this.#details.load(request, { signal, preferCached })).value;
    if (value.sheetState.status === "error" || value.inventoryState.status === "error") this.#details.invalidate();
    return value;
  }

  async loadInventory(request: CharacterResourceRequest, signal?: AbortSignal, preferCached = true) {
    this.replaceScope(request.envelope);
    const value = (await this.#inventory.load(request, { signal, preferCached })).value;
    if (value.status === "error") this.#inventory.invalidate();
    return value;
  }

  invalidateObject(qualifiedId: string) {
    if (qualifiedId === CHARACTER_DOSSIER_OBJECT_ID || qualifiedId === CAMPAIGN_SUMMARY_OBJECT_ID) {
      this.#sheet.invalidate();
      this.#details.invalidate();
      this.#inventory.invalidate();
      return true;
    }
    if (qualifiedId.startsWith("dnd2024.object.inventory-item-")) {
      this.#details.invalidate();
      this.#inventory.invalidate();
      return true;
    }
    return false;
  }

  invalidateAll() {
    this.#store.invalidateAll();
  }
}
