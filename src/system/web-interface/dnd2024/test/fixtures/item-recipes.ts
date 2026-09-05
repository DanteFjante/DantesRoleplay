import { contract } from "../../src/server/item-recipes-validator.js";
import { itemEnvelope, itemRequest } from "./item-details";
import type { ItemRecipesData, ItemRecipesRequest, RecipeEntry } from "../../src/server/item-recipes-client";
export const recipeRequest: ItemRecipesRequest = { ...itemRequest, makesOffset: 0, usesOffset: 0, expectedSourceRevision: null };
export function recipeData(request = recipeRequest): ItemRecipesData {
  const row = (n: number): RecipeEntry => ({ id: `recipe.${n}`, name: `Restoring the travel staff ${n + 1}`, description: "A recorded woodworking recipe using a prepared shaft and resin.",
    knowledgeState: "suspected", sources: [{ label: "Woodworker’s notebook", knowledgeState: "suspected" }],
    requirements: [{ label: "Training", value: "Woodcarver’s tools proficiency", unit: null, sources: [{ label: "Recipe record", knowledgeState: "known" }], observerKnowledge: null }],
    availability: "not-evaluated", outputs: [{ name: "Travel staff", quantity: 1, definitionId: "definition.staff" }],
    materials: [{ name: "Prepared wooden shaft", quantity: 1, definitionId: "definition.shaft" }, { name: "Resin", quantity: 2, definitionId: "definition.resin" }],
    tools: ["Woodcarver’s tools"], duration: "1 Day", observerKnowledge: request.perspective === "dm" ? "suspected" : null });
  return { version: 1, observerId: request.observerId, itemId: request.itemId, perspective: request.perspective,
    makes: { state: request.makesOffset ? "ready" : "partial", entries: [row(request.makesOffset)], nextOffset: request.makesOffset ? null : 1, reasons: request.makesOffset ? [] : ["page-limit"] },
    uses: { state: "ready", entries: [row(0)], nextOffset: null, reasons: [] } };
}
export function recipeEnvelope(request = recipeRequest, data = recipeData(request)) {
  return { ...itemEnvelope(request), qualifiedQueryId: contract.id, outputSchemaHash: contract.outputSchemaHash, data };
}
