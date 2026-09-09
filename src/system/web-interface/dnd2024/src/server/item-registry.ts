import { RESOURCE_FRESHNESS_MS, resourceCacheKey } from "../data/resource-policy.ts";
import { ResourceStore, type ResourceInvalidationReason, type ResourceStoreMetrics } from "../data/resource-store.ts";
import { ViewReadError } from "../data/view-read-client.ts";
import type { ItemDetailsData, ItemSource } from "./item-view-client.ts";
import { normalizeGameServerOrigin } from "./game-server-context.js";
import { readBoundedJson } from "./read-model-response.js";

const APPLICATION_ID = "dnd2024";
const ITEM_DEFINITION_COMPONENT = "dnd2024.item-definition";
const ITEM_ARCHETYPES = [
  "dnd2024.archetype.item-definition",
  "dnd2024.archetype.weapon-definition",
  "dnd2024.archetype.tool-definition",
  "dnd2024.archetype.consumable-definition",
] as const;
const PAGE_SIZE = 40;
const MAXIMUM_RECORDS = 20_000;
const MAXIMUM_PAGE_BYTES = 524_288;
// The catalog caps the unescaped ContentJson string at one million characters.
// Its JSON envelope can be larger because quotes, slashes, and Unicode are escaped.
const MAXIMUM_DETAIL_BYTES = 8 * 1_048_576;
const MAXIMUM_CONTENT_CHARACTERS = 1_000_000;
const MAXIMUM_CURSOR_LENGTH = 4_096;
const FINGERPRINT = /^[A-F0-9]{64}$/iu;

export type ItemRegistryRecord = {
  id: string;
  collection: string;
  name: string;
  status: string;
  version: number;
  contentFingerprint: string;
  sourceId: string;
  sourceLabel: string;
  classification: "core" | "homebrew" | "compatibility" | "third-party";
};

export type ItemRegistryRequest = {
  query: string;
  cursor: string | null;
  expectedResolutionFingerprint: string | null;
};

export type ItemRegistryPage = {
  resolutionFingerprint: string;
  records: ItemRegistryRecord[];
  totalCount: number;
  nextCursor: string | null;
};

export type ItemDefinitionRequest = {
  id: string;
  collection: string;
  expectedContentFingerprint: string | null;
  sourceLabel: string | null;
};

export type ItemDefinition = {
  record: ItemRegistryRecord;
  details: ItemDetailsData;
};

export const EMPTY_ITEM_REGISTRY_REQUEST: ItemRegistryRequest = Object.freeze({
  query: "",
  cursor: null,
  expectedResolutionFingerprint: null,
});

function object(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown> : null;
}

function text(value: unknown, maximum: number, allowEmpty = false): string | null {
  return typeof value === "string" && value.length <= maximum && value.trim() === value
    && (allowEmpty || value.length > 0) && !/[\u0000-\u001F\u007F]/u.test(value) ? value : null;
}

function optionalText(value: unknown, maximum: number): string | null {
  return value === null ? null : text(value, maximum);
}

function summary(value: unknown, sourceLabel: unknown, classification: unknown): ItemRegistryRecord | null {
  const record = object(value);
  const id = text(record?.qualifiedId, 400);
  const collection = text(record?.collection, 63);
  const name = text(record?.name, 400);
  const status = text(record?.status, 63);
  const contentFingerprint = text(record?.contentFingerprint, 64)?.toUpperCase() ?? null;
  const sourceId = text(record?.sourceId, 200);
  const label = text(sourceLabel, 120);
  const kind = classification;
  if (!id || !collection || !name || !status || !contentFingerprint || !FINGERPRINT.test(contentFingerprint)
      || !sourceId || !label || !["core", "homebrew", "compatibility", "third-party"].includes(String(kind))
      || !Number.isSafeInteger(record?.version) || (record!.version as number) < 1) return null;
  return { id, collection, name, status, version: record!.version as number,
    contentFingerprint, sourceId, sourceLabel: label,
    classification: kind as ItemRegistryRecord["classification"] };
}

function normalizeRegistryRequest(request: ItemRegistryRequest): ItemRegistryRequest {
  const query = request.query.trim();
  const expected = request.expectedResolutionFingerprint?.toUpperCase() ?? null;
  if (query.length > 256 || /[\u0000-\u001F\u007F]/u.test(query)
      || (request.cursor !== null && optionalText(request.cursor, MAXIMUM_CURSOR_LENGTH) === null)
      || (expected !== null && expected !== "none" && !FINGERPRINT.test(expected)))
    throw new ViewReadError("incompatible-data", "The item-registry request is invalid.");
  return { query, cursor: request.cursor, expectedResolutionFingerprint: expected };
}

function isRegistryPage(value: unknown): value is ItemRegistryPage {
  const page = object(value);
  return Boolean(page && (page.resolutionFingerprint === "none" || FINGERPRINT.test(String(page.resolutionFingerprint)))
    && Array.isArray(page.records) && page.records.length <= PAGE_SIZE
    && page.records.every((record) => summary({
      qualifiedId: object(record)?.id,
      collection: object(record)?.collection,
      name: object(record)?.name,
      status: object(record)?.status,
      version: object(record)?.version,
      contentFingerprint: object(record)?.contentFingerprint,
      sourceId: object(record)?.sourceId,
    }, object(record)?.sourceLabel, object(record)?.classification) !== null)
    && new Set((page.records as ItemRegistryRecord[]).map((record) => record.id)).size === page.records.length
    && Number.isSafeInteger(page.totalCount) && (page.totalCount as number) >= page.records.length
    && (page.totalCount as number) <= MAXIMUM_RECORDS
    && optionalText(page.nextCursor, MAXIMUM_CURSOR_LENGTH) === page.nextCursor);
}

export async function readItemRegistry({
  serverOrigin,
  applicationId,
  request = EMPTY_ITEM_REGISTRY_REQUEST,
  signal,
  fetchImpl = fetch,
}: {
  serverOrigin: string;
  applicationId: string;
  request?: ItemRegistryRequest;
  signal?: AbortSignal;
  fetchImpl?: typeof fetch;
}): Promise<ItemRegistryPage> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID)
    throw new ViewReadError("incompatible-data", "The item registry is unavailable.");
  const normalized = normalizeRegistryRequest(request);
  const url = new URL(`/api/applications/${APPLICATION_ID}/content`, `${origin}/`);
  url.searchParams.set("limit", String(PAGE_SIZE));
  url.searchParams.append("kind", "entity");
  url.searchParams.append("componentAny", ITEM_DEFINITION_COMPONENT);
  for (const archetype of ITEM_ARCHETYPES) url.searchParams.append("archetype", archetype);
  if (normalized.query) url.searchParams.set("query", normalized.query);
  if (normalized.cursor) url.searchParams.set("cursor", normalized.cursor);
  const response = await fetchImpl(url, { headers: { Accept: "application/json" }, cache: "no-store", signal });
  if (!response.ok) throw new ViewReadError(response.status === 409 ? "stale-data" : "transport",
    response.status === 409 ? "The item registry changed while this page was loading." : "The item registry is unavailable.");
  const decoded = await readBoundedJson(response, MAXIMUM_PAGE_BYTES);
  const page = decoded.status === "ready" ? object(decoded.value) : null;
  const fingerprint = page?.resolutionFingerprint === "none" ? "none"
    : text(page?.resolutionFingerprint, 64)?.toUpperCase() ?? null;
  const rawRecords = Array.isArray(page?.resolvedWinners) ? page.resolvedWinners : null;
  const records = rawRecords?.map((value) => {
    const candidate = object(value);
    return summary(candidate?.record, candidate?.sourceLabel, candidate?.classification);
  }) ?? null;
  const nextCursor = optionalText(page?.nextCursor, MAXIMUM_CURSOR_LENGTH);
  if (page?.applicationId !== APPLICATION_ID || !fingerprint || (fingerprint !== "none" && !FINGERPRINT.test(fingerprint))
      || !records || records.some((record) => !record) || records.length > PAGE_SIZE
      || new Set(records.map((record) => record!.id)).size !== records.length
      || !Number.isSafeInteger(page?.totalCount) || (page!.totalCount as number) < records.length
      || (page!.totalCount as number) > MAXIMUM_RECORDS || nextCursor !== page?.nextCursor)
    throw new ViewReadError("incompatible-data", "The item-registry response is invalid.");
  if (normalized.expectedResolutionFingerprint && normalized.expectedResolutionFingerprint !== fingerprint)
    throw new ViewReadError("stale-data", "The item registry changed while this page was loading.");
  const result = { resolutionFingerprint: fingerprint, records: records as ItemRegistryRecord[],
    totalCount: page.totalCount as number, nextCursor };
  if (!isRegistryPage(result)) throw new ViewReadError("incompatible-data", "The item-registry response is invalid.");
  return result;
}

function fraction(value: unknown): string | number | null {
  const input = object(value);
  if (!input || !Number.isSafeInteger(input.numerator) || !Number.isSafeInteger(input.denominator)
      || (input.numerator as number) < 0 || (input.denominator as number) < 1) return null;
  return input.denominator === 1 ? input.numerator as number : `${input.numerator}/${input.denominator}`;
}

function display(value: string) {
  return value.split(/[.-]/u).filter(Boolean).map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(" ");
}

function referenceLabel(value: unknown): string | null {
  const id = text(object(value)?.entityId, 200);
  return id ? display(id.split(".").at(-1) ?? id) : null;
}

function property(label: string, value: string | number | boolean | null, unit: string | null,
  source: ItemSource): ItemDetailsData["properties"][number] | null {
  return value === null ? null : { label, value, unit, sources: [source], observerKnowledge: null };
}

function definitionDetails(record: ItemRegistryRecord, contentJson: string): ItemDetailsData {
  let parsed: unknown;
  try { parsed = JSON.parse(contentJson); } catch { throw new ViewReadError("incompatible-data", "The item definition is invalid."); }
  const root = object(parsed);
  const components = object(root?.components);
  const definition = object(components?.[ITEM_DEFINITION_COMPONENT]);
  const archetype = text(root?.archetype, 200);
  const currentDefinition = archetype !== null && ITEM_ARCHETYPES.includes(archetype as typeof ITEM_ARCHETYPES[number]);
  if (!root || !components || (!definition && !currentDefinition) || text(root.id, 400) === null || text(root.name, 400) === null)
    throw new ViewReadError("incompatible-data", "The item definition is invalid.");
  const source: ItemSource = { label: record.sourceLabel, knowledgeState: "known" };
  const properties: Array<ItemDetailsData["properties"][number] | null> = [];
  const kind = text(definition?.kind, 80) ?? (archetype === "dnd2024.archetype.weapon-definition" ? "weapon"
    : archetype === "dnd2024.archetype.tool-definition" ? "tool"
      : archetype === "dnd2024.archetype.consumable-definition" ? "consumable" : currentDefinition ? "item" : null);
  const physical = object(components["dnd2024.item.physical"]);
  const weight = object(physical?.weight);
  const mass = fraction(definition?.massPounds) ?? fraction(weight?.value);
  const massUnit = definition?.massPounds ? "Pound" : referenceLabel(weight?.unit);
  properties.push(property("Kind", kind ? display(kind) : null, null, source));
  properties.push(property("Weight per item", mass, mass === null ? null : massUnit, source));
  properties.push(property("Stack policy", text(definition?.stackPolicy, 80), null, source));
  const modes = Array.isArray(definition?.equipmentModes)
    ? definition!.equipmentModes.map((value) => text(value, 80)).filter(Boolean) as string[] : [];
  if (modes.length) properties.push(property("Equipment modes", modes.map(display).join(", "), null, source));
  const coreVersion = object(components["dnd2024.core.version"]);
  const status = text(coreVersion?.status, 80) ?? record.status;
  properties.push(property("Record status", display(status), null, source));
  const version = Number.isSafeInteger(definition?.definitionVersion) ? definition!.definitionVersion as number
    : Number.isSafeInteger(coreVersion?.revision) ? coreVersion!.revision as number : record.version;
  properties.push(property("Definition version", version, null, source));
  const sourceRef = object(definition?.sourceRef);
  const coreSource = object(components["dnd2024.core.source"]);
  const citation = Array.isArray(coreSource?.citations) ? object(coreSource.citations[0]) : null;
  const locator = text(sourceRef?.locator, 2_048) ?? text(citation?.locator, 2_048);
  const description = text(definition?.description, 2_048) ?? locator;
  const weapon = object(components["dnd2024.item.weapon"]);
  properties.push(property("Weapon category", referenceLabel(weapon?.category), null, source));
  const weaponProperties = Array.isArray(weapon?.properties)
    ? weapon.properties.map(referenceLabel).filter(Boolean) as string[] : [];
  if (weaponProperties.length) properties.push(property("Weapon properties", weaponProperties.join(", "), null, source));
  const tool = object(components["dnd2024.item.tool"]);
  properties.push(property("Tool category", referenceLabel(tool?.category), null, source));
  const valid = properties.filter((value): value is ItemDetailsData["properties"][number] => value !== null).slice(0, 32);
  return {
    version: 1,
    observerId: "shared-table",
    itemId: record.id,
    perspective: "dm",
    state: kind && (mass !== null || currentDefinition) ? "ready" : "partial",
    name: text(root.name, 160) ?? record.name.slice(0, 160),
    description,
    definitionId: record.id,
    quantity: null,
    container: null,
    equipmentSlots: [],
    properties: valid,
    sources: [source],
    media: [],
    reasons: kind && (mass !== null || currentDefinition) ? [] : ["source-incomplete"],
    observerKnowledge: null,
  };
}

function isItemDefinition(value: unknown): value is ItemDefinition {
  const candidate = object(value);
  const record = object(candidate?.record);
  const details = object(candidate?.details);
  return Boolean(record && details && typeof record.id === "string" && typeof details.name === "string"
    && details.itemId === record.id && details.quantity === null && details.container === null);
}

export async function readItemDefinition({
  serverOrigin,
  applicationId,
  request,
  signal,
  fetchImpl = fetch,
}: {
  serverOrigin: string;
  applicationId: string;
  request: ItemDefinitionRequest;
  signal?: AbortSignal;
  fetchImpl?: typeof fetch;
}): Promise<ItemDefinition> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID || !text(request.id, 400) || !text(request.collection, 63)
      || (request.expectedContentFingerprint !== null && !FINGERPRINT.test(request.expectedContentFingerprint)))
    throw new ViewReadError("incompatible-data", "The item-definition request is invalid.");
  const url = new URL(`/api/applications/${APPLICATION_ID}/catalog/records/${encodeURIComponent(request.id)}`, `${origin}/`);
  url.searchParams.set("collection", request.collection);
  const response = await fetchImpl(url, { headers: { Accept: "application/json" }, cache: "no-store", signal });
  if (!response.ok) throw new ViewReadError(response.status === 404 ? "stale-data" : "transport",
    response.status === 404 ? "This item definition is no longer in the registry." : "The item definition is unavailable.");
  const decoded = await readBoundedJson(response, MAXIMUM_DETAIL_BYTES);
  const body = decoded.status === "ready" ? object(decoded.value) : null;
  const contentJson = text(body?.contentJson, MAXIMUM_CONTENT_CHARACTERS, true);
  const record = summary(body?.summary, request.sourceLabel ?? "Catalog", "core");
  if (!contentJson || !record || record.id !== request.id || record.collection !== request.collection)
    throw new ViewReadError("incompatible-data", "The item-definition response is invalid.");
  if (request.expectedContentFingerprint && request.expectedContentFingerprint !== record.contentFingerprint)
    throw new ViewReadError("stale-data", "This item definition changed after the registry page loaded.");
  const result = { record, details: definitionDetails(record, contentJson) };
  if (!isItemDefinition(result)) throw new ViewReadError("incompatible-data", "The item-definition response is invalid.");
  return result;
}

export class ItemRegistryClient {
  readonly #store: ResourceStore;
  readonly #page;
  readonly #definition;

  constructor({ serverOrigin, applicationId = APPLICATION_ID, fetchImpl = fetch }: {
    serverOrigin: string; applicationId?: string; fetchImpl?: typeof fetch;
  }) {
    this.#store = new ResourceStore({ maximumEntries: 32, maximumRetainedBytes: 10 * 1024 * 1024,
      diagnosticName: "item-registry-resources" });
    this.#page = this.#store.define<ItemRegistryRequest, ItemRegistryPage>({
      name: "item-registry-page",
      cacheKey: (value) => {
        const request = normalizeRegistryRequest(value);
        return resourceCacheKey("item-registry-v1", applicationId, request.query, request.cursor,
          request.expectedResolutionFingerprint);
      },
      read: (request, signal) => readItemRegistry({ serverOrigin, applicationId, request, signal, fetchImpl }),
      validate: isRegistryPage,
      maximumAgeMs: RESOURCE_FRESHNESS_MS.itemRegistry,
      maximumEntryBytes: MAXIMUM_PAGE_BYTES,
    });
    this.#definition = this.#store.define<ItemDefinitionRequest, ItemDefinition>({
      name: "item-registry-definition",
      cacheKey: (request) => resourceCacheKey("item-definition-v1", applicationId, request.collection,
        request.id, request.expectedContentFingerprint, request.sourceLabel),
      read: (request, signal) => readItemDefinition({ serverOrigin, applicationId, request, signal, fetchImpl }),
      validate: isItemDefinition,
      maximumAgeMs: RESOURCE_FRESHNESS_MS.itemDefinition,
      maximumEntryBytes: MAXIMUM_DETAIL_BYTES,
    });
  }

  async loadPage(request: ItemRegistryRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#page.load(normalizeRegistryRequest(request), { signal, preferCached })).value;
  }

  async loadDefinition(request: ItemDefinitionRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#definition.load(request, { signal, preferCached })).value;
  }

  invalidate(reason: ResourceInvalidationReason = "manual") { this.#store.invalidateAll(reason); }
  metrics(): ResourceStoreMetrics { return this.#store.metrics(); }
}
