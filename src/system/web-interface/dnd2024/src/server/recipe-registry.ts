import { RESOURCE_FRESHNESS_MS, resourceCacheKey } from "../data/resource-policy.ts";
import { ResourceStore, type ResourceInvalidationReason, type ResourceStoreMetrics } from "../data/resource-store.ts";
import { ViewReadError } from "../data/view-read-client.ts";
import type { RecipeEntry } from "./item-recipes-client.ts";
import type { ItemRegistryRecord } from "./item-registry.ts";
import { normalizeGameServerOrigin } from "./game-server-context.js";
import { readBoundedJson } from "./read-model-response.js";

const APPLICATION_ID = "dnd2024";
const RECIPE_COMPONENT = "dnd2024.crafting.recipe";
const RECIPE_ARCHETYPE = "dnd2024.archetype.crafting-recipe";
const ITEM_COMPONENT = "dnd2024.item-definition";
const ITEM_ARCHETYPES = [
  "dnd2024.archetype.item-definition",
  "dnd2024.archetype.weapon-definition",
  "dnd2024.archetype.tool-definition",
  "dnd2024.archetype.consumable-definition",
] as const;
const PAGE_SIZE = 12;
const MAXIMUM_RECORDS = 2_000;
const MAXIMUM_LINKS = 32;
const MAXIMUM_PAGE_BYTES = 524_288;
const MAXIMUM_DETAIL_BYTES = 8 * 1_048_576;
const MAXIMUM_CONTENT_CHARACTERS = 1_000_000;
const MAXIMUM_CURSOR_LENGTH = 4_096;
const FINGERPRINT = /^[A-F0-9]{64}$/iu;
const ID = /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/u;

export type RecipeRegistryRecord = ItemRegistryRecord;
export type RecipeRegistryRequest = {
  query: string;
  cursor: string | null;
  expectedResolutionFingerprint: string | null;
  relatedItemId: string | null;
};
export type RecipeRegistryPage = {
  resolutionFingerprint: string;
  records: RecipeRegistryRecord[];
  totalCount: number;
  nextCursor: string | null;
};
export type RecipeDefinitionRequest = {
  id: string;
  collection: string;
  expectedContentFingerprint: string | null;
  sourceLabel: string | null;
};
export type RecipeDefinition = {
  record: RecipeRegistryRecord;
  entry: RecipeEntry;
  linkedItems: ItemRegistryRecord[];
};

export const EMPTY_RECIPE_REGISTRY_REQUEST: RecipeRegistryRequest = Object.freeze({
  query: "", cursor: null, expectedResolutionFingerprint: null, relatedItemId: null,
});

function object(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown> : null;
}

function text(value: unknown, maximum: number, allowEmpty = false): string | null {
  return typeof value === "string" && value.length <= maximum && value.trim() === value
    && (allowEmpty || value.length > 0) && !/[\u0000-\u001F\u007F]/u.test(value) ? value : null;
}

function jsonText(value: unknown, maximum: number): string | null {
  return typeof value === "string" && value.trim().length > 0 && value.length <= maximum
    && !/[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/u.test(value) ? value : null;
}

function optionalText(value: unknown, maximum: number): string | null {
  return value === null ? null : text(value, maximum);
}

function summary(value: unknown, sourceLabel: unknown, classification: unknown): RecipeRegistryRecord | null {
  const record = object(value);
  const id = text(record?.qualifiedId, 400);
  const collection = text(record?.collection, 63);
  const name = text(record?.name, 400);
  const status = text(record?.status, 63);
  const contentFingerprint = text(record?.contentFingerprint, 64)?.toUpperCase() ?? null;
  const sourceId = text(record?.sourceId, 200);
  const label = text(sourceLabel, 120);
  if (!id || !collection || !name || !status || !contentFingerprint || !FINGERPRINT.test(contentFingerprint)
      || !sourceId || !label || !["core", "homebrew", "compatibility", "third-party"].includes(String(classification))
      || !Number.isSafeInteger(record?.version) || (record!.version as number) < 1) return null;
  return { id, collection, name, status, version: record!.version as number,
    contentFingerprint, sourceId, sourceLabel: label,
    classification: classification as RecipeRegistryRecord["classification"] };
}

function normalizeRequest(request: RecipeRegistryRequest): RecipeRegistryRequest {
  const query = request.query.trim();
  const expected = request.expectedResolutionFingerprint?.toUpperCase() ?? null;
  if (query.length > 256 || /[\u0000-\u001F\u007F]/u.test(query)
      || (request.cursor !== null && optionalText(request.cursor, MAXIMUM_CURSOR_LENGTH) === null)
      || (expected !== null && expected !== "none" && !FINGERPRINT.test(expected))
      || (request.relatedItemId !== null && !ID.test(request.relatedItemId)))
    throw new ViewReadError("incompatible-data", "The recipe-registry request is invalid.");
  return { query, cursor: request.cursor, expectedResolutionFingerprint: expected,
    relatedItemId: request.relatedItemId };
}

function isRegistryPage(value: unknown): value is RecipeRegistryPage {
  const page = object(value);
  return Boolean(page && (page.resolutionFingerprint === "none" || FINGERPRINT.test(String(page.resolutionFingerprint)))
    && Array.isArray(page.records) && page.records.length <= PAGE_SIZE
    && page.records.every((record) => summary({
      qualifiedId: object(record)?.id, collection: object(record)?.collection, name: object(record)?.name,
      status: object(record)?.status, version: object(record)?.version,
      contentFingerprint: object(record)?.contentFingerprint, sourceId: object(record)?.sourceId,
    }, object(record)?.sourceLabel, object(record)?.classification) !== null)
    && new Set((page.records as RecipeRegistryRecord[]).map((record) => record.id)).size === page.records.length
    && Number.isSafeInteger(page.totalCount) && (page.totalCount as number) >= page.records.length
    && (page.totalCount as number) <= MAXIMUM_RECORDS
    && optionalText(page.nextCursor, MAXIMUM_CURSOR_LENGTH) === page.nextCursor);
}

export async function readRecipeRegistry({ serverOrigin, applicationId, request = EMPTY_RECIPE_REGISTRY_REQUEST,
  signal, fetchImpl = fetch }: { serverOrigin: string; applicationId: string; request?: RecipeRegistryRequest;
    signal?: AbortSignal; fetchImpl?: typeof fetch }): Promise<RecipeRegistryPage> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID)
    throw new ViewReadError("incompatible-data", "The recipe registry is unavailable.");
  const normalized = normalizeRequest(request);
  const url = new URL(`/api/applications/${APPLICATION_ID}/content`, `${origin}/`);
  url.searchParams.set("limit", String(PAGE_SIZE));
  url.searchParams.append("kind", "entity");
  url.searchParams.append("componentAny", RECIPE_COMPONENT);
  url.searchParams.append("archetype", RECIPE_ARCHETYPE);
  if (normalized.relatedItemId) url.searchParams.append("referenceAny", normalized.relatedItemId);
  if (normalized.query) url.searchParams.set("query", normalized.query);
  if (normalized.cursor) url.searchParams.set("cursor", normalized.cursor);
  const response = await fetchImpl(url, { headers: { Accept: "application/json" }, cache: "no-store", signal });
  if (!response.ok) throw new ViewReadError(response.status === 409 ? "stale-data" : "transport",
    response.status === 409 ? "The recipe registry changed while this page was loading." : "The recipe registry is unavailable.");
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
    throw new ViewReadError("incompatible-data", "The recipe-registry response is invalid.");
  if (normalized.expectedResolutionFingerprint && normalized.expectedResolutionFingerprint !== fingerprint)
    throw new ViewReadError("stale-data", "The recipe registry changed while this page was loading.");
  const result = { resolutionFingerprint: fingerprint, records: records as RecipeRegistryRecord[],
    totalCount: page.totalCount as number, nextCursor };
  if (!isRegistryPage(result)) throw new ViewReadError("incompatible-data", "The recipe-registry response is invalid.");
  return result;
}

function display(value: string) {
  return value.split(/[._:-]/u).filter(Boolean).map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(" ");
}

function referenceId(value: unknown): string | null {
  const id = text(object(value)?.entityId, 200);
  return id && ID.test(id) ? id : null;
}

function referenceLabel(id: string) { return display(id.split(".").at(-1) ?? id).slice(0, 160); }

function quantityReferences(value: unknown): { values: RecipeEntry["outputs"]; incomplete: boolean } {
  if (!Array.isArray(value) || value.length > 16) return { values: [], incomplete: true };
  const values: RecipeEntry["outputs"] = [];
  let incomplete = false;
  for (const candidate of value) {
    const row = object(candidate);
    const id = referenceId(row?.definition);
    const quantity = row?.quantity;
    if (!id || !Number.isSafeInteger(quantity) || (quantity as number) < 1) { incomplete = true; continue; }
    values.push({ name: referenceLabel(id), definitionId: id, quantity: quantity as number });
  }
  return { values, incomplete };
}

function amount(value: unknown): string | null {
  if (Number.isSafeInteger(value) && (value as number) >= 0) return String(value);
  const input = object(value);
  if (!input) return null;
  if (Number.isSafeInteger(input.average)) {
    const roll = amount(input.roll);
    return roll ? `${input.average} on average (${roll})` : String(input.average);
  }
  if (Number.isSafeInteger(input.count) && (input.count as number) >= 1) {
    const die = referenceId(input.dieRef);
    if (!die || !Number.isSafeInteger(input.modifier)) return null;
    const modifier = input.modifier as number;
    return `${input.count} ${referenceLabel(die)}${modifier === 0 ? "" : modifier > 0 ? ` + ${modifier}` : ` − ${Math.abs(modifier)}`}`;
  }
  const mechanic = text(input.mechanicId, 200);
  return mechanic ? `Calculated by ${display(mechanic)}` : null;
}

function duration(value: unknown): string | null {
  const input = object(value);
  const kind = text(input?.kind, 40);
  if (!input || !kind) return null;
  if (kind === "instantaneous") return "Instantaneous";
  if (kind === "permanent") return "Permanent";
  if (kind === "special") return "Special; see the recorded source";
  if (kind === "measured") {
    const measured = amount(input.amount);
    const unit = referenceId(input.unit);
    return measured && unit ? `${measured} ${referenceLabel(unit)}` : null;
  }
  if (kind === "until-event") {
    const event = text(object(input.event)?.event, 160);
    return event ? `Until ${display(event)}` : null;
  }
  return null;
}

type RequirementDescription = { text: string; supported: boolean; references: string[]; complete: boolean };
function requirement(value: unknown, depth = 0): RequirementDescription {
  const input = object(value);
  if (!input || depth > 8) return { text: "Requirement description unavailable", supported: false,
    references: [], complete: false };
  const operator = text(input.operator, 40);
  if (operator === "predicate") {
    const predicate = text(input.predicateId, 200);
    const args = Array.isArray(input.arguments) && input.arguments.length <= 16 ? input.arguments : null;
    if (!predicate || !args) return { text: "Requirement description unavailable", supported: false,
      references: [], complete: false };
    const references = args.map(referenceId).filter((id): id is string => id !== null);
    const labels = references.map(referenceLabel);
    if (predicate === "predicate.proficiency.tool") return { text: labels.length
      ? `Proficiency with ${labels.join(", ")}` : "Tool proficiency (specific tool not recorded)",
    supported: true, references, complete: true };
    if (predicate === "predicate.proficiency.crafter") return { text: labels.length
      ? `Crafter proficiency: ${labels.join(", ")}` : "Required crafter proficiency (details not recorded)",
    supported: true, references, complete: true };
    return { text: `Unsupported requirement: ${display(predicate)}`, supported: false, references, complete: true };
  }
  if (operator === "not") {
    const child = requirement(input.child, depth + 1);
    return { ...child, text: `Not (${child.text})` };
  }
  if (operator === "all" || operator === "any") {
    const children = Array.isArray(input.children) && input.children.length >= 1 && input.children.length <= 16
      ? input.children.map((child) => requirement(child, depth + 1)) : null;
    if (!children) return { text: "Requirement description unavailable", supported: false,
      references: [], complete: false };
    return { text: children.map((child) => child.text).join(operator === "all" ? "; and " : "; or "),
      supported: children.every((child) => child.supported),
      references: [...new Set(children.flatMap((child) => child.references))],
      complete: children.every((child) => child.complete) };
  }
  return { text: "Unsupported requirement description", supported: false, references: [], complete: true };
}

function fraction(value: unknown): string | null {
  const input = object(value);
  if (!input || !Number.isSafeInteger(input.numerator) || !Number.isSafeInteger(input.denominator)
      || (input.numerator as number) < 0 || (input.denominator as number) < 1) return null;
  return input.denominator === 1 ? String(input.numerator) : `${input.numerator}/${input.denominator}`;
}

function recipeEntry(record: RecipeRegistryRecord, contentJson: string): { entry: RecipeEntry; references: string[] } {
  let parsed: unknown;
  try { parsed = JSON.parse(contentJson); } catch {
    throw new ViewReadError("incompatible-data", "The recipe definition is invalid.");
  }
  const root = object(parsed);
  const components = object(root?.components);
  const recipe = object(components?.[RECIPE_COMPONENT]);
  if (!root || !components || !recipe || text(root.id, 200) === null || text(root.name, 160) === null)
    throw new ViewReadError("incompatible-data", "The recipe definition is invalid.");
  const outputs = quantityReferences(recipe.outputs);
  const materials = recipe.materialRequirements === undefined
    ? { values: [] as RecipeEntry["outputs"], incomplete: false }
    : quantityReferences(recipe.materialRequirements);
  const workDuration = duration(recipe.workDuration);
  const tool = requirement(recipe.toolRequirement);
  const crafter = requirement(recipe.crafterRequirement);
  const source = { label: record.sourceLabel, knowledgeState: "known" as const };
  const requirements: RecipeEntry["requirements"] = [
    { label: "Tool requirement", value: tool.text.slice(0, 512), unit: null, sources: [source], observerKnowledge: null },
    { label: "Crafter requirement", value: crafter.text.slice(0, 512), unit: null, sources: [source], observerKnowledge: null },
  ];
  const materialCost = object(recipe.materialCost);
  if (materialCost) {
    const value = fraction(materialCost.amount);
    const denomination = referenceId(materialCost.denomination);
    requirements.push({ label: "Material cost", value: value && denomination
      ? `${value} ${referenceLabel(denomination)}` : "Recorded cost is incomplete",
    unit: null, sources: [source], observerKnowledge: null });
  }
  const effects = Array.isArray(recipe.completionEffects) && recipe.completionEffects.length <= 12
    ? recipe.completionEffects.map((candidate) => referenceId(object(candidate)?.effect)).filter((id): id is string => id !== null)
    : [];
  if (effects.length) requirements.push({ label: "Completion effects", value: effects.map(referenceLabel).join(", ").slice(0, 512),
    unit: null, sources: [source], observerKnowledge: null });
  const coreSource = object(components["dnd2024.core.source"]);
  const citation = Array.isArray(coreSource?.citations) ? object(coreSource.citations[0]) : null;
  const locator = text(citation?.locator, 1_024);
  const incomplete = outputs.incomplete || materials.incomplete || outputs.values.length === 0 || workDuration === null
    || !tool.complete || !crafter.complete;
  const references = [...new Set([
    ...outputs.values.flatMap((value) => value.definitionId ?? []),
    ...materials.values.flatMap((value) => value.definitionId ?? []),
    ...tool.references,
    ...crafter.references,
  ])].slice(0, MAXIMUM_LINKS);
  return { entry: {
    id: record.id, name: text(root.name, 160)!, description: locator,
    knowledgeState: "known", sources: [source], requirements,
    availability: incomplete ? "definition-incomplete" : "not-evaluated",
    outputs: outputs.values, materials: materials.values,
    tools: tool.references.length ? tool.references.slice(0, 8).map(referenceLabel) : [tool.text.slice(0, 160)],
    duration: workDuration, observerKnowledge: null,
  }, references };
}

async function readLinkedItems(origin: string, ids: string[], signal: AbortSignal | undefined,
  fetchImpl: typeof fetch): Promise<ItemRegistryRecord[]> {
  if (!ids.length) return [];
  const url = new URL(`/api/applications/${APPLICATION_ID}/content`, `${origin}/`);
  url.searchParams.set("limit", String(ids.length));
  url.searchParams.append("kind", "entity");
  url.searchParams.append("componentAny", ITEM_COMPONENT);
  for (const archetype of ITEM_ARCHETYPES) url.searchParams.append("archetype", archetype);
  for (const id of ids) url.searchParams.append("id", id);
  const response = await fetchImpl(url, { headers: { Accept: "application/json" }, cache: "no-store", signal });
  if (!response.ok) throw new ViewReadError("transport", "Supporting item definitions are unavailable.");
  const decoded = await readBoundedJson(response, MAXIMUM_PAGE_BYTES);
  const page = decoded.status === "ready" ? object(decoded.value) : null;
  const rawRecords = Array.isArray(page?.resolvedWinners) ? page.resolvedWinners : null;
  const records = rawRecords?.map((value) => {
    const candidate = object(value);
    return summary(candidate?.record, candidate?.sourceLabel, candidate?.classification);
  }) ?? null;
  if (page?.applicationId !== APPLICATION_ID || !records || records.some((record) => !record)
      || records.length > ids.length || page?.nextCursor !== null || page?.totalCount !== records.length
      || records.some((record) => !ids.includes(record!.id)))
    throw new ViewReadError("incompatible-data", "Supporting item definitions are invalid.");
  return records as ItemRegistryRecord[];
}

function isDefinition(value: unknown): value is RecipeDefinition {
  const candidate = object(value);
  const record = object(candidate?.record);
  const entry = object(candidate?.entry);
  return Boolean(record && entry && typeof record.id === "string" && entry.id === record.id
    && Array.isArray(candidate?.linkedItems) && candidate!.linkedItems.length <= MAXIMUM_LINKS);
}

export async function readRecipeDefinition({ serverOrigin, applicationId, request, signal, fetchImpl = fetch }:
  { serverOrigin: string; applicationId: string; request: RecipeDefinitionRequest;
    signal?: AbortSignal; fetchImpl?: typeof fetch }): Promise<RecipeDefinition> {
  const origin = normalizeGameServerOrigin(serverOrigin);
  if (!origin || applicationId !== APPLICATION_ID || !ID.test(request.id) || !ID.test(request.collection)
      || (request.expectedContentFingerprint !== null && !FINGERPRINT.test(request.expectedContentFingerprint)))
    throw new ViewReadError("incompatible-data", "The recipe-definition request is invalid.");
  const url = new URL(`/api/applications/${APPLICATION_ID}/catalog/records/${encodeURIComponent(request.id)}`, `${origin}/`);
  url.searchParams.set("collection", request.collection);
  const response = await fetchImpl(url, { headers: { Accept: "application/json" }, cache: "no-store", signal });
  if (!response.ok) throw new ViewReadError(response.status === 404 ? "stale-data" : "transport",
    response.status === 404 ? "This recipe is no longer in the registry." : "The recipe definition is unavailable.");
  const decoded = await readBoundedJson(response, MAXIMUM_DETAIL_BYTES);
  const body = decoded.status === "ready" ? object(decoded.value) : null;
  const contentJson = jsonText(body?.contentJson, MAXIMUM_CONTENT_CHARACTERS);
  const record = summary(body?.summary, request.sourceLabel ?? "Catalog", "core");
  if (!contentJson || !record || record.id !== request.id || record.collection !== request.collection)
    throw new ViewReadError("incompatible-data", "The recipe-definition response is invalid.");
  if (request.expectedContentFingerprint && request.expectedContentFingerprint.toUpperCase() !== record.contentFingerprint)
    throw new ViewReadError("stale-data", "This recipe changed after the registry page loaded.");
  const parsed = recipeEntry(record, contentJson);
  let linkedItems: ItemRegistryRecord[] = [];
  try {
    linkedItems = await readLinkedItems(origin, parsed.references, signal, fetchImpl);
  } catch (error) {
    // The recipe record is still useful when a supporting definition cannot be
    // resolved. Keep its references visible and clearly non-clickable instead of
    // making the authoritative recipe itself unavailable.
    if (signal?.aborted) throw error;
  }
  const result = { record, entry: parsed.entry, linkedItems };
  if (!isDefinition(result)) throw new ViewReadError("incompatible-data", "The recipe-definition response is invalid.");
  return result;
}

export class RecipeRegistryClient {
  readonly #store: ResourceStore;
  readonly #page;
  readonly #definition;

  constructor({ serverOrigin, applicationId = APPLICATION_ID, fetchImpl = fetch }:
    { serverOrigin: string; applicationId?: string; fetchImpl?: typeof fetch }) {
    this.#store = new ResourceStore({ maximumEntries: 32, maximumRetainedBytes: 10 * 1024 * 1024,
      diagnosticName: "recipe-registry-resources" });
    this.#page = this.#store.define<RecipeRegistryRequest, RecipeRegistryPage>({
      name: "recipe-registry-page",
      cacheKey: (value) => { const request = normalizeRequest(value); return resourceCacheKey("recipe-registry-v1",
        applicationId, request.query, request.cursor, request.expectedResolutionFingerprint, request.relatedItemId); },
      read: (request, signal) => readRecipeRegistry({ serverOrigin, applicationId, request, signal, fetchImpl }),
      validate: isRegistryPage, maximumAgeMs: RESOURCE_FRESHNESS_MS.recipeRegistry,
      maximumEntryBytes: MAXIMUM_PAGE_BYTES,
    });
    this.#definition = this.#store.define<RecipeDefinitionRequest, RecipeDefinition>({
      name: "recipe-registry-definition",
      cacheKey: (request) => resourceCacheKey("recipe-definition-v1", applicationId, request.collection,
        request.id, request.expectedContentFingerprint, request.sourceLabel),
      read: (request, signal) => readRecipeDefinition({ serverOrigin, applicationId, request, signal, fetchImpl }),
      validate: isDefinition, maximumAgeMs: RESOURCE_FRESHNESS_MS.recipeDefinition,
      maximumEntryBytes: MAXIMUM_DETAIL_BYTES,
    });
  }

  async loadPage(request: RecipeRegistryRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#page.load(normalizeRequest(request), { signal, preferCached })).value;
  }
  async loadDefinition(request: RecipeDefinitionRequest, signal?: AbortSignal, preferCached = true) {
    return (await this.#definition.load(request, { signal, preferCached })).value;
  }
  invalidate(reason: ResourceInvalidationReason = "manual") { this.#store.invalidateAll(reason); }
  metrics(): ResourceStoreMetrics { return this.#store.metrics(); }
}
