import { readItemUses, usesKey, type ItemUsesRequest, type ItemUsesResult } from "./item-uses-client";
import { itemReadId, readItemResponse } from "./item-read-response";
import { readItemRecipes, recipesKey, type ItemRecipesRequest, type ItemRecipesResult } from "./item-recipes-client";
import validate, { contract } from "./item-details-validator.js";
import { ResourceStore, type KeyedResource, type ResourceInvalidationReason } from "../data/resource-store";
import { RESOURCE_FRESHNESS_MS } from "../data/resource-policy";
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
  return readItemResponse<ItemDetailsData>({ request, input: { itemId: request.itemId }, contract, validate,
    errorMessage: "The item response did not match its authorized selection.",
    verify: (data) => data.observerId === request.observerId && data.itemId === request.itemId && data.perspective === request.perspective &&
      data.media.every((image) => /^\/api\/read-model-media\/[a-f0-9]{64}\/content$/.test(image.contentUrl)),
  }, signal, fetchImpl);
}

let nextClient = 0;
// Each client belongs to one authorized hub-envelope lifetime. It is not shared
// between principals/bindings or persisted. Source revisions remain in results;
// notifications and binding refreshes retire every cached result in that lifetime.
export class ItemViewClient {
  readonly identity = ++nextClient;
  readonly maximumAgeMs: number;
  readonly uses: KeyedResource<ItemUsesRequest, ItemUsesResult>;
  readonly recipes: KeyedResource<ItemRecipesRequest, ItemRecipesResult>;
  readonly reads: KeyedResource<ItemDetailsRequest, ItemDetailsResult>;
  readonly #store: ResourceStore;
  #revision = 0;
  #listeners = new Set<() => void>();
  constructor(fetchImpl: typeof fetch = fetch, maximumAgeMs = RESOURCE_FRESHNESS_MS.itemDetails) {
    this.maximumAgeMs = maximumAgeMs;
    this.#store = new ResourceStore({ maximumEntries: 24, maximumRetainedBytes: 6 * 1024 * 1024,
      diagnosticName: "item-resources" });
    this.uses = this.#store.define({ name: "item-uses", read: async (request, signal) => {
      const value = await readItemUses(request, signal, fetchImpl);
      return value.status === "ready" ? { ...value, expiresAt: Date.now() + maximumAgeMs } : value;
    }, cacheKey: (request) => usesKey(this.identity, request), maximumAgeMs,
      maximumEntryBytes: 524_288,
      validate: (value): value is ItemUsesResult => Boolean(value && typeof value === "object" && "status" in value) });
    this.recipes = this.#store.define({ name: "item-recipes", read: async (request, signal) => {
      const value = await readItemRecipes(request, signal, fetchImpl);
      return value.status === "ready" ? { ...value, expiresAt: Date.now() + maximumAgeMs } : value;
    }, cacheKey: (request) => recipesKey(this.identity, request), maximumAgeMs,
      maximumEntryBytes: 524_288,
      validate: (value): value is ItemRecipesResult => Boolean(value && typeof value === "object" && "status" in value) });
    this.reads = this.#store.define({ name: "item-details", read: async (request, signal) => {
      const result = await readItemDetails(request, signal, fetchImpl);
      return result.status === "ready" ? { ...result, expiresAt: Date.now() + maximumAgeMs } : result;
    },
      cacheKey: (request) => this.key(request), maximumAgeMs,
      maximumEntryBytes: 524_288,
      validate: (value): value is ItemDetailsResult => Boolean(value && typeof value === "object" && "status" in value &&
        ((value as ItemDetailsResult).status === "ready" ? validate((value as ItemDetailsResult).data) :
          ["forbidden", "unavailable", "stale"].includes((value as ItemDetailsResult).status) && (value as ItemDetailsResult).data === null)) });
  }
  key(request: ItemDetailsRequest) { return JSON.stringify([this.identity, contract.contentHash, request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.perspective, request.itemId, request.contextRevision]); }
  snapshot = () => this.#revision;
  subscribe = (listener: () => void) => { this.#listeners.add(listener); return () => { this.#listeners.delete(listener); }; };
  invalidate = (reason: ResourceInvalidationReason = "manual") => {
    this.#store.invalidateAll(reason);
    this.#revision++;
    for (const listener of this.#listeners) listener();
  };
  cacheMetrics = () => this.#store.metrics();
}
