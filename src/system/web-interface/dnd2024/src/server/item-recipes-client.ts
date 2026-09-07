import validate, { contract } from "./item-recipes-validator.js";
import { itemReadId, readItemResponse } from "./item-read-response";
import type { ItemDetailsRequest, ItemKnowledge, ItemSource, ItemDetailsData } from "./item-view-client";

export type RecipeEntry = {
  id: string; name: string; description: string | null; knowledgeState: Exclude<ItemKnowledge, "unknown">;
  sources: ItemSource[]; requirements: ItemDetailsData["properties"];
  availability: "not-evaluated" | "available" | "requirements-not-met" | "definition-incomplete";
  outputs: { name: string; definitionId: string | null; quantity: number }[];
  materials: RecipeEntry["outputs"]; tools: string[]; duration: string | null; observerKnowledge: ItemKnowledge | null;
};
export type RecipeGroup = { state: "ready" | "empty" | "partial"; entries: RecipeEntry[]; nextOffset: number | null; reasons: ItemDetailsData["reasons"] };
export type ItemRecipesData = { version: 1; observerId: string; itemId: string; perspective: "player" | "dm"; makes: RecipeGroup; uses: RecipeGroup };
export type ItemRecipesRequest = ItemDetailsRequest & { makesOffset: number; usesOffset: number; expectedSourceRevision: string | null };
export type ItemRecipesResult = { status: "ready"; data: ItemRecipesData; sourceRevision: string; expiresAt: number }
  | { status: "forbidden" | "unavailable" | "stale"; data: null };
export const recipesKey = (identity: number, r: ItemRecipesRequest) => JSON.stringify([identity, contract.contentHash, r.applicationId, r.stateSpaceId,
  r.campaignId, r.observerId, r.perspective, r.itemId, r.contextRevision, r.makesOffset, r.usesOffset, r.expectedSourceRevision]);
const fingerprint = (value: unknown) => typeof value === "string" && /^[A-F0-9]{64}$/.test(value);
export async function readItemRecipes(request: ItemRecipesRequest, signal: AbortSignal, fetchImpl: typeof fetch = fetch): Promise<ItemRecipesResult> {
  const input = { itemId: request.itemId, makesOffset: request.makesOffset, usesOffset: request.usesOffset, expectedSourceRevision: request.expectedSourceRevision };
  if (![request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.itemId].every(itemReadId) ||
      !["player", "dm"].includes(request.perspective) || ![input.makesOffset, input.usesOffset].every(n => Number.isInteger(n) && n >= 0 && n <= 10000) ||
      input.expectedSourceRevision !== null && !fingerprint(input.expectedSourceRevision) || (input.makesOffset > 0 || input.usesOffset > 0) && !input.expectedSourceRevision)
    return { status: "unavailable", data: null };
  return readItemResponse<ItemRecipesData>({ request, input, contract, validate, expectedSourceRevision: input.expectedSourceRevision,
    errorMessage: "Recipe response did not match the selected view.", verify: (data) => {
      if (data.itemId !== request.itemId || data.observerId !== request.observerId || data.perspective !== request.perspective) return false;
      return (["makes", "uses"] as const).every((group) => {
        const page = data[group], offset = request[group === "makes" ? "makesOffset" : "usesOffset"];
        return new Set(page.entries.map(row => row.id)).size === page.entries.length &&
          (page.nextOffset === null || page.nextOffset === offset + page.entries.length && page.nextOffset > offset);
      });
    },
  }, signal, fetchImpl);
}
