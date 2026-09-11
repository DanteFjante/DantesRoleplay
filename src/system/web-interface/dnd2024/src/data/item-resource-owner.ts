import type { ReadyHubEnvelope } from "./hub-types";
import { RESOURCE_FRESHNESS_MS } from "./resource-policy";
import { RequestCoordinator, type CoordinatedRequest } from "./request-coordinator";
import { allocateItemRequestToken, commitItemFacet, itemActions, itemFacetKey, itemScope,
  peekItemFacet, type HubStore, type ItemFacet, type ItemFacetRequest, type ItemFacetResult } from "./hub-store";
import type { ItemDetailsRequest, ItemDetailsResult } from "../server/item-view-client";
import type { ItemUsesRequest, ItemUsesResult } from "../server/item-uses-client";
import type { ItemRecipesRequest, ItemRecipesResult } from "../server/item-recipes-client";

export type ItemResourceOwnerOptions = {
  store: HubStore;
  readDetails: (request: ItemDetailsRequest, signal: AbortSignal) => Promise<ItemDetailsResult>;
  readUses: (request: ItemUsesRequest, signal: AbortSignal) => Promise<ItemUsesResult>;
  readRecipes: (request: ItemRecipesRequest, signal: AbortSignal) => Promise<ItemRecipesResult>;
  maximumAgeMs?: number;
};

const itemResult = (value: unknown): value is ItemFacetResult => Boolean(value && typeof value === "object" &&
  "status" in value && ["ready", "forbidden", "unavailable", "stale"].includes((value as { status?: unknown }).status as string));
const itemDetailsResult = (value: unknown): value is ItemDetailsResult => itemResult(value);
const itemUsesResult = (value: unknown): value is ItemUsesResult => itemResult(value);
const itemRecipesResult = (value: unknown): value is ItemRecipesResult => itemResult(value);
const itemBytes = (value: unknown) => new TextEncoder().encode(JSON.stringify(value)).byteLength;

/**
 * Item read lifecycle only: Redux is the sole completed-response owner. Exact query keys keep
 * details, uses, recipes, offsets, observer and source revision isolated from one another.
 */
export class ItemResourceOwner {
  readonly #store: HubStore;
  readonly #coordinator = new RequestCoordinator();
  readonly #maximumAgeMs: number;
  readonly #details: CoordinatedRequest<ItemDetailsRequest, ItemDetailsResult>;
  readonly #uses: CoordinatedRequest<ItemUsesRequest, ItemUsesResult>;
  readonly #recipes: CoordinatedRequest<ItemRecipesRequest, ItemRecipesResult>;
  #scope: string | null = null;

  constructor(options: ItemResourceOwnerOptions) {
    this.#store = options.store;
    this.#maximumAgeMs = options.maximumAgeMs ?? RESOURCE_FRESHNESS_MS.itemDetails;
    this.#details = { key: (request, generation) => `${itemFacetKey("details", request)}\u0000${generation}`,
      read: options.readDetails, validate: itemDetailsResult, maximumBytes: 524_288 };
    this.#uses = { key: (request, generation) => `${itemFacetKey("uses", request)}\u0000${generation}`,
      read: options.readUses, validate: itemUsesResult, maximumBytes: 524_288 };
    this.#recipes = { key: (request, generation) => `${itemFacetKey("recipes", request)}\u0000${generation}`,
      read: options.readRecipes, validate: itemRecipesResult, maximumBytes: 524_288 };
  }

  replaceScope(envelope: ReadyHubEnvelope, force = false) {
    const scope = itemScope(envelope);
    if (force || this.#scope !== scope) {
      this.#coordinator.replaceScope(scope, true);
      this.#store.dispatch(itemActions.scopeReplaced({ scope, force }));
    }
    this.#scope = scope;
    return scope;
  }

  async loadDetails(envelope: ReadyHubEnvelope, request: ItemDetailsRequest, signal?: AbortSignal,
    preferCached = true) {
    return this.#load("details", envelope, request, this.#details, signal, preferCached);
  }

  async loadUses(envelope: ReadyHubEnvelope, request: ItemUsesRequest, signal?: AbortSignal,
    preferCached = true) {
    return this.#load("uses", envelope, request, this.#uses, signal, preferCached);
  }

  async loadRecipes(envelope: ReadyHubEnvelope, request: ItemRecipesRequest, signal?: AbortSignal,
    preferCached = true) {
    return this.#load("recipes", envelope, request, this.#recipes, signal, preferCached);
  }

  invalidateAll() {
    this.#coordinator.invalidate();
    this.#store.dispatch(itemActions.invalidated(undefined));
  }

  invalidateObject(qualifiedId: string) {
    if (!qualifiedId.startsWith("dnd2024.object.inventory-item-")) return false;
    this.invalidateAll();
    return true;
  }

  async #load<TRequest extends ItemFacetRequest, TValue extends ItemFacetResult>(facet: ItemFacet,
    envelope: ReadyHubEnvelope, request: TRequest, definition: CoordinatedRequest<TRequest, TValue>, signal?: AbortSignal,
    preferCached = true): Promise<TValue> {
    const scope = this.replaceScope(envelope);
    const key = itemFacetKey(facet, request);
    const cached = preferCached ? peekItemFacet<TValue>(this.#store.getState(), scope, key, this.#maximumAgeMs) : null;
    if (cached) return cached;
    const requestToken = allocateItemRequestToken();
    const generation = this.#store.getState().items.generation;
    this.#store.dispatch(itemActions.requestStarted({ scope, key, requestToken }));
    try {
      const value = await this.#coordinator.load(definition, request, signal);
      const ready = value.status === "ready" ? { ...value, expiresAt: Date.now() + this.#maximumAgeMs } : value;
      this.#store.dispatch(commitItemFacet({ scope, key, value: ready }, {
        generation, requestToken, bytes: itemBytes(ready), confirmedAt: Date.now(),
      }));
      return ready as TValue;
    } catch (error) {
      this.#store.dispatch(itemActions.requestFinished({ scope, key, requestToken }));
      throw error;
    }
  }
}
