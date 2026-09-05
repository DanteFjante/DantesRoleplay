import { contract } from "../../src/server/item-uses-validator.js";
import { itemEnvelope, itemRequest } from "./item-details";
import type { ItemUsesData, ItemUsesRequest, UseEntry } from "../../src/server/item-uses-client";
export const usesRequest: ItemUsesRequest = { ...itemRequest, offset: 0, expectedSourceRevision: null };
export function usesData(request = usesRequest): ItemUsesData {
  const row = (n: number): UseEntry => ({ id: `use.${n}`, name: ["Staff attack", "Carve a replacement peg", "Consume a restorative", "Signal across the ravine"][n % 4],
    description: n % 4 === 3 ? "The traveller believes the hollow staff can carry a whistle over the ravine." : "A recorded activity for the selected item.",
    kind: n % 4 === 3 ? "recorded-application" : "canonical-activity", knowledgeState: n % 4 === 3 ? "believed" : "known",
    sources: [{ label: n % 4 === 3 ? "Traveller’s notebook" : "Canonical activity record", knowledgeState: n % 4 === 3 ? "believed" : "known" }],
    requirements: [{ label: "Training", value: "Recorded proficiency requirement; current eligibility has not been evaluated", unit: null, sources: [], observerKnowledge: null }],
    costs: n % 4 === 3 ? [] : [{ label: "Activation", value: "Action", unit: null, sources: [{ label: "Canonical activity record", knowledgeState: "known" }], observerKnowledge: null }],
    effects: n % 4 === 3 ? [] : ["The recorded effect is described here; opening this view performs no action."],
    availability: n % 4 === 2 ? "requirements-not-met" : "not-evaluated", executionSupport: n % 4 === 3 ? "adjudication-required" : "unsupported",
    observerKnowledge: request.perspective === "dm" ? n % 4 === 3 ? "believed" : "known" : null });
  return { version: 1, observerId: request.observerId, itemId: request.itemId, perspective: request.perspective,
    uses: { state: request.offset ? "ready" : "partial", entries: request.offset ? [row(request.offset)] : [0, 1, 2, 3].map(row), nextOffset: request.offset ? null : 4, reasons: request.offset ? [] : ["page-limit"] } };
}
export function usesEnvelope(request = usesRequest, data = usesData(request)) {
  return { ...itemEnvelope(request), qualifiedQueryId: contract.id, outputSchemaHash: contract.outputSchemaHash, data };
}
