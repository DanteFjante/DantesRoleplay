import { itemReadId, readItemResponse } from "./item-read-response";
import { readItemUses, type ItemUsesRequest } from "./item-uses-client";
import { readItemRecipes, type ItemRecipesRequest } from "./item-recipes-client";
import { contract } from "./item-details-validator.js";
import { projectItemDetails } from "./item-display-projection";
import type { Perspective } from "../data/hub-types";
import type { ItemMediaEntry } from "../components/EntityMediaGallery";

export type ItemKnowledge = "known" | "suspected" | "believed" | "doubted" | "disbelieved" | "familiar" | "unknown";
export type ItemSource = { label: string; knowledgeState: Exclude<ItemKnowledge, "unknown"> };
export type ItemDetailsData = {
  version: 1; observerId: string; itemId: string; perspective: Perspective; state: "ready" | "partial";
  name: string; description: string | null; definitionId: string | null; quantity: number | null;
  container: { itemId: string; name: string; observerKnowledge: ItemKnowledge | null } | null;
  equipmentSlots: string[];
  properties: { label: string; value: string | number | boolean; unit: string | null; sources: ItemSource[]; observerKnowledge: ItemKnowledge | null }[];
  sources: ItemSource[]; media: ItemMediaEntry[];
  reasons: ("inventory-bound" | "source-incomplete" | "page-limit" | "byte-limit" | "dependency-unavailable")[];
  observerKnowledge: ItemKnowledge | null;
};
export type ItemDetailsRequest = {
  applicationId: string; stateSpaceId: string; campaignId: string; observerId: string;
  itemId: string; perspective: Perspective; contextRevision: string;
};
export type ItemDetailsResult = { status: "ready"; data: ItemDetailsData; sourceRevision: string; expiresAt: number }
  | { status: "forbidden" | "unavailable" | "stale"; data: null };
export async function readItemDetails(request: ItemDetailsRequest, signal: AbortSignal, fetchImpl: typeof fetch = fetch): Promise<ItemDetailsResult> {
  if (![request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.itemId].every(itemReadId) ||
      !["player", "dm"].includes(request.perspective)) return { status: "unavailable", data: null };
  return readItemResponse<ItemDetailsData>({ request, input: { itemId: request.itemId, selectionId: request.campaignId }, contract,
    errorMessage: "The item response did not match its authorized selection.",
    consume: (value) => projectItemDetails(value, request),
    verify: (data) => data.observerId === request.observerId && data.itemId === request.itemId && data.perspective === request.perspective &&
      data.media.every((image) => /^\/api\/read-model-media\/[a-f0-9]{64}\/content$/.test(image.contentUrl)),
  }, signal, fetchImpl);
}

/**
 * Compatibility read adapter for fixture-only callers. It owns no completed responses; production
 * routes use ItemResourceOwner and the shared Redux store.
 */
export class ItemViewClient {
  constructor(readonly fetchImpl: typeof fetch = fetch) {}
  loadDetails(request: ItemDetailsRequest, signal: AbortSignal) { return readItemDetails(request, signal, this.fetchImpl); }
  loadUses(request: ItemUsesRequest, signal: AbortSignal) { return readItemUses(request, signal, this.fetchImpl); }
  loadRecipes(request: ItemRecipesRequest, signal: AbortSignal) { return readItemRecipes(request, signal, this.fetchImpl); }
}
