import validate, { contract } from "./item-details-validator.js";
import { ViewReadClient, ViewReadError } from "../data/view-read-client";
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
const fingerprint = (value: unknown) => typeof value === "string" && /^[a-f0-9]{64}$/i.test(value);
const id = (value: string) => /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(value);
const envelopeKeys = ["applicationId", "stateSpaceId", "qualifiedQueryId", "stateSpaceFingerprint", "resolutionFingerprint", "outputSchemaHash", "resultFingerprint", "sourceRevisionFingerprint", "data"];

async function boundedJson(response: Response): Promise<unknown> {
  if (!response.body) throw new ViewReadError("incompatible-data", "Missing item response.");
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = []; let size = 0;
  try {
    while (true) {
      const next = await reader.read(); if (next.done) break;
      size += next.value.byteLength;
      if (size > 70_000) { await reader.cancel(); throw new ViewReadError("incompatible-data", "Item response exceeds its limit."); }
      chunks.push(next.value);
    }
  } finally { reader.releaseLock(); }
  const bytes = new Uint8Array(size); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
  try { return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)); }
  catch { throw new ViewReadError("incompatible-data", "Invalid item response."); }
}

export async function readItemDetails(request: ItemDetailsRequest, signal: AbortSignal, fetchImpl: typeof fetch = fetch): Promise<ItemDetailsResult> {
  if (![request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.itemId].every(id) ||
      !["player", "dm"].includes(request.perspective)) return { status: "unavailable", data: null };
  const parameters = new URLSearchParams({ perspective: request.perspective, campaignId: request.campaignId, input: JSON.stringify({ itemId: request.itemId }) });
  const url = `/api/applications/${encodeURIComponent(request.applicationId)}/state-spaces/${encodeURIComponent(request.stateSpaceId)}/entities/${encodeURIComponent(request.observerId)}/read-models/${contract.id}?${parameters}`;
  const response = await fetchImpl(url, { signal, credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
  if (!response.ok) return { status: response.status === 403 ? "forbidden" : response.status === 409 ? "stale" : "unavailable", data: null };
  const envelope = await boundedJson(response) as Record<string, unknown> | null;
  const data = envelope?.data as ItemDetailsData | undefined;
  if (!envelope || Object.keys(envelope).length !== envelopeKeys.length || !envelopeKeys.every((key) => Object.hasOwn(envelope, key)) ||
      envelope.applicationId !== request.applicationId || envelope.stateSpaceId !== request.stateSpaceId || envelope.qualifiedQueryId !== contract.id ||
      envelope.outputSchemaHash !== contract.outputSchemaHash ||
      ![envelope.stateSpaceFingerprint, envelope.resolutionFingerprint, envelope.resultFingerprint, envelope.sourceRevisionFingerprint].every(fingerprint) ||
      !validate(data) || !data || data.observerId !== request.observerId || data.itemId !== request.itemId || data.perspective !== request.perspective ||
      new TextEncoder().encode(JSON.stringify(data)).length > 65_536 ||
      data.media.some((image) => !/^\/api\/read-model-media\/[a-f0-9]{64}\/content$/.test(image.contentUrl))) {
    throw new ViewReadError("incompatible-data", "The item response did not match its authorized selection.");
  }
  return { status: "ready", data, sourceRevision: envelope.sourceRevisionFingerprint as string, expiresAt: Date.now() + 30_000 };
}

let nextClient = 0;
// Each client belongs to one authorized hub-envelope lifetime. It is not shared
// between principals/bindings or persisted. Source revisions remain in results;
// notifications and binding refreshes retire every cached result in that lifetime.
export class ItemViewClient {
  readonly identity = ++nextClient;
  readonly maximumAgeMs: number;
  readonly reads: ViewReadClient<ItemDetailsRequest, ItemDetailsResult>;
  #revision = 0;
  #listeners = new Set<() => void>();
  constructor(fetchImpl: typeof fetch = fetch, maximumAgeMs = 30_000) {
    this.maximumAgeMs = maximumAgeMs;
    this.reads = new ViewReadClient({ read: async (request, signal) => {
      const result = await readItemDetails(request, signal, fetchImpl);
      return result.status === "ready" ? { ...result, expiresAt: Date.now() + maximumAgeMs } : result;
    },
      cacheKey: (request) => this.key(request), maximumCachedScopes: 8, maximumCacheAgeMs: maximumAgeMs,
      validate: (value): value is ItemDetailsResult => Boolean(value && typeof value === "object" && "status" in value &&
        ((value as ItemDetailsResult).status === "ready" ? validate((value as ItemDetailsResult).data) :
          ["forbidden", "unavailable", "stale"].includes((value as ItemDetailsResult).status) && (value as ItemDetailsResult).data === null)) });
  }
  key(request: ItemDetailsRequest) { return JSON.stringify([this.identity, contract.contentHash, request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.perspective, request.itemId, request.contextRevision]); }
  snapshot = () => this.#revision;
  subscribe = (listener: () => void) => { this.#listeners.add(listener); return () => { this.#listeners.delete(listener); }; };
  invalidate = () => { this.reads.invalidate(); this.#revision++; for (const listener of this.#listeners) listener(); };
}
