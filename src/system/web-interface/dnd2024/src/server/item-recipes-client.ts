import validate, { contract } from "./item-recipes-validator.js";
import { boundedJson } from "./item-read-response";
import { ViewReadError } from "../data/view-read-client";
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
  if (![request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.itemId].every(id => /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(id)) ||
      !["player", "dm"].includes(request.perspective) || ![input.makesOffset, input.usesOffset].every(n => Number.isInteger(n) && n >= 0 && n <= 10000) ||
      input.expectedSourceRevision !== null && !fingerprint(input.expectedSourceRevision) || (input.makesOffset > 0 || input.usesOffset > 0) && !input.expectedSourceRevision)
    return { status: "unavailable", data: null };
  const params = new URLSearchParams({ campaignId: request.campaignId, perspective: request.perspective, input: JSON.stringify(input) });
  const url = `/api/applications/${encodeURIComponent(request.applicationId)}/state-spaces/${encodeURIComponent(request.stateSpaceId)}/entities/${encodeURIComponent(request.observerId)}/read-models/${contract.id}?${params}`;
  const response = await fetchImpl(url, { signal, cache: "no-store", credentials: "same-origin", headers: { Accept: "application/json" } });
  if (!response.ok) return { status: response.status === 409 ? "stale" : response.status === 403 ? "forbidden" : "unavailable", data: null };
  const e = await boundedJson(response) as Record<string, unknown> | null;
  const data = e?.data as ItemRecipesData | undefined;
  const keys = ["applicationId", "stateSpaceId", "qualifiedQueryId", "stateSpaceFingerprint", "resolutionFingerprint", "outputSchemaHash", "resultFingerprint", "sourceRevisionFingerprint", "data"];
  if (!e || Object.keys(e).length !== keys.length || !keys.every(k => Object.hasOwn(e, k)) || e.applicationId !== request.applicationId || e.stateSpaceId !== request.stateSpaceId ||
      e.qualifiedQueryId !== contract.id || e.outputSchemaHash !== contract.outputSchemaHash || ![e.stateSpaceFingerprint, e.resolutionFingerprint, e.resultFingerprint, e.sourceRevisionFingerprint].every(fingerprint) ||
      !validate(data) || !data || data.itemId !== request.itemId || data.observerId !== request.observerId || data.perspective !== request.perspective ||
      new TextEncoder().encode(JSON.stringify(data)).length > 65536) throw new ViewReadError("incompatible-data", "Recipe response did not match the selected view.");
  if (input.expectedSourceRevision && e.sourceRevisionFingerprint !== input.expectedSourceRevision) return { status: "stale", data: null };
  for (const group of ["makes", "uses"] as const) {
    const g = data[group], offset = request[group === "makes" ? "makesOffset" : "usesOffset"];
    if (new Set(g.entries.map(row => row.id)).size !== g.entries.length || g.nextOffset !== null && (g.nextOffset !== offset + g.entries.length || g.nextOffset <= offset))
      throw new ViewReadError("incompatible-data", "Recipe continuation is not an advancing page.");
  }
  return { status: "ready", data, sourceRevision: e.sourceRevisionFingerprint as string, expiresAt: Date.now() + 30000 };
}
