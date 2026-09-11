import type { ItemMediaEntry } from "../components/EntityMediaGallery";
import type { Perspective } from "../data/hub-types";
import type { ItemDetailsData, ItemKnowledge, ItemSource } from "./item-view-client";
import type { RecipeEntry, RecipeGroup } from "./item-recipes-client";
import type { UseEntry, UseGroup } from "./item-uses-client";

type Dict = Record<string, unknown>;
const ID = /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/u;
const KNOWLEDGE = new Set<ItemKnowledge>(["known", "suspected", "believed", "doubted", "disbelieved", "familiar", "unknown"]);
const SOURCE_KNOWLEDGE = new Set<Exclude<ItemKnowledge, "unknown">>(["known", "suspected", "believed", "doubted", "disbelieved", "familiar"]);
const DETAIL_REASONS = new Set<ItemDetailsData["reasons"][number]>(["inventory-bound", "source-incomplete", "page-limit", "byte-limit", "dependency-unavailable"]);

function object(value: unknown): Dict | null {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as Dict : null;
}

function own(value: unknown, key: string): unknown {
  const item = object(value);
  return item && Object.hasOwn(item, key) ? item[key] : undefined;
}

function text(value: unknown, maximum: number, allowEmpty = false): string | null {
  return typeof value === "string" && value.length <= maximum && value.trim() === value &&
    (allowEmpty || value.length > 0) && !/[\u0000-\u001F\u007F]/u.test(value) ? value : null;
}

function id(value: unknown): string | null {
  const result = text(value, 200);
  return result && ID.test(result) ? result : null;
}

function knowledge(value: unknown): ItemKnowledge | null {
  return typeof value === "string" && KNOWLEDGE.has(value as ItemKnowledge) ? value as ItemKnowledge : null;
}

function source(value: unknown): ItemSource | null {
  const item = object(value);
  const label = text(own(item, "label"), 160);
  const knowledgeState = own(item, "knowledgeState");
  return label && typeof knowledgeState === "string" && SOURCE_KNOWLEDGE.has(knowledgeState as Exclude<ItemKnowledge, "unknown">)
    ? { label, knowledgeState: knowledgeState as Exclude<ItemKnowledge, "unknown"> } : null;
}

function sources(value: unknown, maximum: number): { value: ItemSource[]; incomplete: boolean } {
  if (value === undefined) return { value: [], incomplete: false };
  if (!Array.isArray(value) || value.length > maximum) return { value: [], incomplete: true };
  const result = value.map(source).filter((entry): entry is ItemSource => entry !== null);
  return { value: result, incomplete: result.length !== value.length };
}

function property(value: unknown, perspective?: Perspective): ItemDetailsData["properties"][number] | null {
  const item = object(value);
  const label = text(own(item, "label"), 100);
  const raw = own(item, "value");
  const propertyValue = typeof raw === "string" ? text(raw, 512, true)
    : typeof raw === "boolean" ? raw : typeof raw === "number" && Number.isFinite(raw) ? raw : null;
  if (!label || propertyValue === null) return null;
  const unitValue = own(item, "unit");
  const unit = unitValue === null || unitValue === undefined ? null : text(unitValue, 80);
  if (unitValue !== null && unitValue !== undefined && unit === null) return null;
  const nested = sources(own(item, "sources"), 4);
  const observerValue = own(item, "observerKnowledge");
  const observerKnowledge = observerValue === null || observerValue === undefined ? null : knowledge(observerValue);
  if (observerValue !== null && observerValue !== undefined && observerKnowledge === null) return null;
  if (perspective === "player" && observerValue !== null && observerValue !== undefined) return null;
  return { label, value: propertyValue, unit, sources: nested.value, observerKnowledge };
}

export function projectProperties(value: unknown, maximum = 32, perspective?: Perspective): { value: ItemDetailsData["properties"]; incomplete: boolean } {
  if (value === undefined) return { value: [], incomplete: false };
  if (!Array.isArray(value) || value.length > maximum) return { value: [], incomplete: true };
  const result = value.map((entry) => property(entry, perspective)).filter((entry): entry is ItemDetailsData["properties"][number] => entry !== null);
  return { value: result, incomplete: result.length !== value.length };
}

function media(value: unknown): { value: ItemMediaEntry[]; incomplete: boolean } {
  if (value === undefined) return { value: [], incomplete: false };
  if (!Array.isArray(value) || value.length > 8) return { value: [], incomplete: true };
  const result = value.map((candidate) => {
    const item = object(candidate);
    const contentUrl = text(own(item, "contentUrl"), 512);
    const alt = text(own(item, "alt"), 240);
    const captionValue = own(item, "caption");
    const caption = captionValue === null || captionValue === undefined ? null : text(captionValue, 240);
    return contentUrl && /^\/api\/read-model-media\/[a-f0-9]{64}\/content$/u.test(contentUrl) && alt &&
      (captionValue === null || captionValue === undefined || caption !== null)
      ? { contentUrl, alt, caption } : null;
  }).filter((entry): entry is ItemMediaEntry => entry !== null);
  return { value: result, incomplete: result.length !== value.length };
}

function detailReasons(value: unknown): { value: ItemDetailsData["reasons"]; incomplete: boolean } {
  if (value === undefined) return { value: [], incomplete: false };
  if (!Array.isArray(value) || value.length > 8) return { value: [], incomplete: true };
  const result = [...new Set(value.filter((entry): entry is ItemDetailsData["reasons"][number] =>
    typeof entry === "string" && DETAIL_REASONS.has(entry as ItemDetailsData["reasons"][number])))];
  return { value: result, incomplete: result.length !== value.length };
}

export function projectItemDetails(value: unknown, binding: {
  observerId: string; itemId: string; perspective: Perspective;
}): ItemDetailsData | null {
  const input = object(value);
  if (!input || own(input, "observerId") !== binding.observerId || own(input, "itemId") !== binding.itemId ||
      own(input, "perspective") !== binding.perspective) return null;
  const rawName = text(own(input, "name"), 160);
  const descriptionValue = own(input, "description");
  const description = descriptionValue === null || descriptionValue === undefined ? null : text(descriptionValue, 2_048);
  const definitionValue = own(input, "definitionId");
  const definitionId = definitionValue === null || definitionValue === undefined ? null : id(definitionValue);
  const quantityValue = own(input, "quantity");
  const quantity = quantityValue === null || quantityValue === undefined ? null
    : Number.isSafeInteger(quantityValue) && (quantityValue as number) >= 0 ? quantityValue as number : null;
  let incomplete = !rawName || (descriptionValue !== null && descriptionValue !== undefined && description === null) ||
    (definitionValue !== null && definitionValue !== undefined && definitionId === null) ||
    quantityValue === undefined || (quantityValue !== null && quantityValue !== undefined && quantity === null);
  const containerValue = own(input, "container");
  let container: ItemDetailsData["container"] = null;
  if (containerValue !== null && containerValue !== undefined) {
    const containerInput = object(containerValue);
    const containerId = id(own(containerInput, "itemId"));
    const containerName = text(own(containerInput, "name"), 160);
    const containerKnowledgeValue = own(containerInput, "observerKnowledge");
    const containerKnowledge = containerKnowledgeValue === null || containerKnowledgeValue === undefined ? null : knowledge(containerKnowledgeValue);
    if (containerId && (containerName || containerId) &&
        (containerKnowledgeValue === null || containerKnowledgeValue === undefined || containerKnowledge !== null))
      container = { itemId: containerId, name: containerName ?? containerId,
        observerKnowledge: binding.perspective === "player" ? null : containerKnowledge };
    else incomplete = true;
  }
  const slotsValue = own(input, "equipmentSlots");
  const equipmentSlots = Array.isArray(slotsValue) ? slotsValue.map((entry) => text(entry, 80)).filter((entry): entry is string => entry !== null).slice(0, 16) : [];
  if (slotsValue !== undefined && (!Array.isArray(slotsValue) || equipmentSlots.length !== slotsValue.length)) incomplete = true;
  const projectedProperties = projectProperties(own(input, "properties"), 32, binding.perspective);
  const projectedSources = sources(own(input, "sources"), 8);
  const projectedMedia = media(own(input, "media"));
  const projectedReasons = detailReasons(own(input, "reasons"));
  const observerValue = own(input, "observerKnowledge");
  let observerKnowledge = observerValue === null || observerValue === undefined ? null : knowledge(observerValue);
  if (observerValue !== null && observerValue !== undefined && observerKnowledge === null) incomplete = true;
  if (binding.perspective === "player" && observerValue !== null && observerValue !== undefined) {
    // DM-only observer knowledge must never cross the player projection, even
    // when the rest of the item is display-admitted.
    observerKnowledge = null;
    incomplete = true;
  }
  incomplete ||= projectedProperties.incomplete || projectedSources.incomplete || projectedMedia.incomplete || projectedReasons.incomplete;
  const sourceState = own(input, "state");
  const state = sourceState === "ready" || sourceState === "partial" ? sourceState : null;
  if (sourceState === undefined || state === null) incomplete = true;
  const reasons = [...new Set([...projectedReasons.value, ...(incomplete ? ["source-incomplete" as const] : [])])];
  return {
    version: 1, observerId: binding.observerId, itemId: binding.itemId, perspective: binding.perspective,
    state: incomplete || state === "partial" ? "partial" : "ready",
    name: rawName ?? binding.itemId, description, definitionId, quantity, container, equipmentSlots,
    properties: projectedProperties.value, sources: projectedSources.value, media: projectedMedia.value, reasons,
    observerKnowledge,
  };
}

function entrySources(value: unknown): { value: ItemSource[]; incomplete: boolean } { return sources(value, 8); }

function commonEntry(value: unknown, kind: "use" | "recipe", perspective: "player" | "dm") {
  const item = object(value);
  const entryId = id(own(item, "id"));
  if (!entryId) return null;
  const name = text(own(item, "name"), 160) ?? entryId;
  let incomplete = !text(own(item, "name"), 160);
  const descriptionValue = own(item, "description");
  const description = descriptionValue === null || descriptionValue === undefined ? null : text(descriptionValue, 2_048);
  if (descriptionValue !== null && descriptionValue !== undefined && description === null) incomplete = true;
  const knowledgeValue = own(item, "knowledgeState");
  const parsedKnowledge = knowledge(knowledgeValue);
  const knowledgeState = parsedKnowledge ?? "unknown";
  if (!parsedKnowledge) incomplete = true;
  if (knowledgeState === "unknown") incomplete = true;
  const projectedSources = entrySources(own(item, "sources"));
  const observerValue = own(item, "observerKnowledge");
  let observerKnowledge = observerValue === null || observerValue === undefined ? null : knowledge(observerValue);
  if (observerValue !== null && observerValue !== undefined && observerKnowledge === null) incomplete = true;
  if (perspective === "player" && observerValue !== null && observerValue !== undefined) {
    observerKnowledge = null;
    incomplete = true;
  }
  const requirements = projectProperties(own(item, "requirements"), 32, perspective);
  incomplete ||= projectedSources.incomplete || requirements.incomplete;
  const availabilityValue = own(item, "availability");
  let availability = ["not-evaluated", "available", "requirements-not-met", "definition-incomplete"].includes(String(availabilityValue))
    ? availabilityValue as UseEntry["availability"] : "definition-incomplete";
  if (availabilityValue !== undefined && availability === "definition-incomplete" && availabilityValue !== "definition-incomplete") incomplete = true;
  if (kind === "use") {
    const useKindValue = own(item, "kind");
    const useKind = useKindValue === "canonical-activity" || useKindValue === "recorded-application" || useKindValue === "unknown"
      ? useKindValue : "unknown";
    if (useKindValue !== undefined && useKindValue !== "canonical-activity" && useKindValue !== "recorded-application") incomplete = true;
    const costs = projectProperties(own(item, "costs"), 32, perspective);
    const effectsValue = own(item, "effects");
    const effects = Array.isArray(effectsValue) ? effectsValue.map((entry) => text(entry, 2_048)).filter((entry): entry is string => entry !== null).slice(0, 32) : [];
    const effectsIncomplete = effectsValue === undefined || !Array.isArray(effectsValue) || effects.length !== effectsValue.length;
    const missingPrerequisites = own(item, "requirements") === undefined || own(item, "costs") === undefined || effectsValue === undefined;
    incomplete ||= missingPrerequisites || costs.incomplete || effectsIncomplete;
    const supportValue = own(item, "executionSupport");
    let executionSupport = ["supported", "adjudication-required", "unsupported"].includes(String(supportValue))
      ? supportValue as UseEntry["executionSupport"] : "unsupported";
    if (supportValue !== undefined && executionSupport === "unsupported" && supportValue !== "unsupported") incomplete = true;
    if (missingPrerequisites || requirements.incomplete || costs.incomplete || effectsIncomplete || useKind === "unknown" || knowledgeState === "unknown") {
      availability = "definition-incomplete";
      executionSupport = "unsupported";
    }
    const result: UseEntry = { id: entryId, name, description, knowledgeState, sources: projectedSources.value,
      requirements: requirements.value, availability, kind: useKind, costs: costs.value, effects,
      executionSupport, observerKnowledge };
    return { value: result, incomplete };
  }
  const outputs = quantityReferences(own(item, "outputs"));
  const materials = quantityReferences(own(item, "materials"));
  const toolsValue = own(item, "tools");
  const tools = Array.isArray(toolsValue) ? toolsValue.map((entry) => text(entry, 160)).filter((entry): entry is string => entry !== null).slice(0, 8) : [];
  const durationValue = own(item, "duration");
  const duration = durationValue === null || durationValue === undefined ? null : text(durationValue, 160);
  const missingRecipeFields = own(item, "requirements") === undefined || own(item, "outputs") === undefined || own(item, "materials") === undefined ||
    own(item, "tools") === undefined || own(item, "duration") === undefined || outputs.incomplete || materials.incomplete || (toolsValue !== undefined && (!Array.isArray(toolsValue) || tools.length !== toolsValue.length)) ||
    (durationValue !== null && durationValue !== undefined && duration === null);
  incomplete ||= missingRecipeFields;
  const result: RecipeEntry = { id: entryId, name, description, knowledgeState, sources: projectedSources.value,
    requirements: requirements.value, availability, outputs: outputs.value, materials: materials.value, tools, duration, observerKnowledge };
  if (missingRecipeFields || outputs.incomplete || materials.incomplete || requirements.incomplete) result.availability = "definition-incomplete";
  return { value: result, incomplete };
}

function quantityReferences(value: unknown): { value: RecipeEntry["outputs"]; incomplete: boolean } {
  if (value === undefined) return { value: [], incomplete: false };
  if (!Array.isArray(value) || value.length > 16) return { value: [], incomplete: true };
  const result = value.map((candidate) => {
    const item = object(candidate);
    const rawName = text(own(item, "name"), 160);
    const definitionValue = own(item, "definitionId");
    const definitionId = definitionValue === null || definitionValue === undefined ? null : id(definitionValue);
    const quantity = own(item, "quantity");
    return (rawName || definitionId) && Number.isSafeInteger(quantity) && (quantity as number) >= 1
      ? { name: rawName ?? definitionId!, definitionId, quantity: quantity as number } : null;
  }).filter((entry): entry is RecipeEntry["outputs"][number] => entry !== null);
  return { value: result, incomplete: result.length !== value.length };
}

function group(value: unknown, offset: number, kind: "use" | "recipe", perspective: "player" | "dm"): { value: UseGroup | RecipeGroup; incomplete: boolean } {
  const input = object(value);
  const entriesValue = own(input, "entries");
  let incomplete = entriesValue === undefined || !Array.isArray(entriesValue) || entriesValue.length > 64;
  const sourceEntries = Array.isArray(entriesValue) ? entriesValue.slice(0, 64) : [];
  const projectedEntries = sourceEntries.map((entry) => commonEntry(entry, kind, perspective)).filter(Boolean) as Array<{
    value: UseEntry | RecipeEntry; incomplete: boolean;
  }>;
  const identityCounts = new Map(projectedEntries.map((entry) => [entry.value.id, 0]));
  for (const entry of projectedEntries) identityCounts.set(entry.value.id, (identityCounts.get(entry.value.id) ?? 0) + 1);
  const uniqueEntries = projectedEntries.filter((entry) => (identityCounts.get(entry.value.id) ?? 0) === 1);
  incomplete ||= uniqueEntries.length !== projectedEntries.length;
  incomplete ||= uniqueEntries.some((entry) => entry.incomplete) || uniqueEntries.length !== sourceEntries.length;
  const reasonsValue = own(input, "reasons");
  const reasons = Array.isArray(reasonsValue) ? [...new Set(reasonsValue.filter((entry): entry is ItemDetailsData["reasons"][number] => DETAIL_REASONS.has(entry as ItemDetailsData["reasons"][number])))] : [];
  incomplete ||= reasonsValue !== undefined && (!Array.isArray(reasonsValue) || reasons.length !== reasonsValue.length);
  const nextValue = own(input, "nextOffset");
  incomplete ||= nextValue === undefined;
  // nextOffset is a server cursor over source rows, not over the rows that
  // survived local display projection. Preserve it when valid even if this
  // page is partial, so an omitted row cannot strand later pages.
  const nextCandidate = nextValue === null || nextValue === undefined ? null
    : Number.isSafeInteger(nextValue) && (nextValue as number) > offset && (nextValue as number) <= 10_000 ? nextValue as number : null;
  incomplete ||= nextValue !== null && nextValue !== undefined && nextCandidate === null;
  const nextOffset = nextCandidate;
  const stateValue = own(input, "state");
  const state = stateValue === "empty" || stateValue === "ready" || stateValue === "partial" ? stateValue : null;
  incomplete ||= stateValue === undefined || state === null;
  const finalState = incomplete || state === "partial" ? "partial" : state === "empty" && uniqueEntries.length === 0 ? "empty" : "ready";
  if (finalState === "partial" && !reasons.includes("source-incomplete")) reasons.push("source-incomplete");
  if (kind === "use") return { value: { state: finalState, entries: uniqueEntries.map((entry) => entry.value) as UseEntry[], nextOffset, reasons }, incomplete };
  return { value: { state: finalState, entries: uniqueEntries.map((entry) => entry.value) as RecipeEntry[], nextOffset, reasons }, incomplete };
}

export function isProjectedItemDetails(value: unknown): value is ItemDetailsData {
  const item = object(value);
  return Boolean(item && own(item, "version") === 1 && typeof own(item, "observerId") === "string"
    && typeof own(item, "itemId") === "string" && (own(item, "perspective") === "player" || own(item, "perspective") === "dm")
    && (own(item, "state") === "ready" || own(item, "state") === "partial") && typeof own(item, "name") === "string"
    && (own(item, "description") === null || typeof own(item, "description") === "string")
    && (own(item, "quantity") === null || Number.isSafeInteger(own(item, "quantity")))
    && Array.isArray(own(item, "properties")) && Array.isArray(own(item, "sources"))
    && Array.isArray(own(item, "media")) && Array.isArray(own(item, "reasons")));
}

export function projectItemUses(value: unknown, binding: { observerId: string; itemId: string; perspective: "player" | "dm" }, offset: number): UseGroup | null {
  const input = object(value);
  if (!input || own(input, "observerId") !== binding.observerId || own(input, "itemId") !== binding.itemId || own(input, "perspective") !== binding.perspective) return null;
  const projected = group(own(input, "uses"), offset, "use", binding.perspective);
  return projected.value as UseGroup;
}

export function projectItemRecipes(value: unknown, binding: { observerId: string; itemId: string; perspective: "player" | "dm" }, offsets: { makes: number; uses: number }): { makes: RecipeGroup; uses: RecipeGroup } | null {
  const input = object(value);
  if (!input || own(input, "observerId") !== binding.observerId || own(input, "itemId") !== binding.itemId || own(input, "perspective") !== binding.perspective) return null;
  return { makes: group(own(input, "makes"), offsets.makes, "recipe", binding.perspective).value as RecipeGroup,
    uses: group(own(input, "uses"), offsets.uses, "recipe", binding.perspective).value as RecipeGroup };
}
