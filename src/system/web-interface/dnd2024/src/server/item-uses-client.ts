import validate, { contract } from "./item-uses-validator.js";
import { boundedJson } from "./item-read-response";
import { ViewReadError } from "../data/view-read-client";
import type { ItemDetailsRequest, ItemKnowledge, ItemSource, ItemDetailsData } from "./item-view-client";

export type UseEntry = {
  id: string; name: string; description: string | null; knowledgeState: Exclude<ItemKnowledge, "unknown">;
  sources: ItemSource[]; requirements: ItemDetailsData["properties"];
  availability: "not-evaluated" | "available" | "requirements-not-met" | "definition-incomplete";
  kind: "canonical-activity" | "recorded-application";
  costs: ItemDetailsData["properties"]; effects: string[];
  executionSupport: "supported" | "adjudication-required" | "unsupported";
  observerKnowledge: ItemKnowledge | null;
};
export type UseGroup = { state: "ready" | "empty" | "partial"; entries: UseEntry[]; nextOffset: number | null; reasons: ItemDetailsData["reasons"] };
export type ItemUsesData = { version: 1; observerId: string; itemId: string; perspective: "player" | "dm"; uses: UseGroup };
export type ItemUsesRequest = ItemDetailsRequest & { offset: number; expectedSourceRevision: string | null };
export type ItemUsesResult = { status: "ready"; data: ItemUsesData; sourceRevision: string; expiresAt: number }
  | { status: "forbidden" | "unavailable" | "stale"; data: null };
export const usesKey = (identity: number, r: ItemUsesRequest) => JSON.stringify([identity, contract.contentHash, r.applicationId, r.stateSpaceId,
  r.campaignId, r.observerId, r.perspective, r.itemId, r.contextRevision, r.offset, r.expectedSourceRevision]);
const fingerprint = (value: unknown) => typeof value === "string" && /^[A-F0-9]{64}$/.test(value);
export async function readItemUses(request: ItemUsesRequest, signal: AbortSignal, fetchImpl: typeof fetch = fetch): Promise<ItemUsesResult> {
  const input = { itemId: request.itemId, offset: request.offset, expectedSourceRevision: request.expectedSourceRevision };
  if (![request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.itemId].every(id => /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(id)) ||
      !["player", "dm"].includes(request.perspective) || ![input.offset].every(n => Number.isInteger(n) && n >= 0 && n <= 10000) ||
      input.expectedSourceRevision !== null && !fingerprint(input.expectedSourceRevision) || input.offset > 0 && !input.expectedSourceRevision)
    return { status: "unavailable", data: null };
  const params = new URLSearchParams({ campaignId: request.campaignId, perspective: request.perspective, input: JSON.stringify(input) });
  const url = `/api/applications/${encodeURIComponent(request.applicationId)}/state-spaces/${encodeURIComponent(request.stateSpaceId)}/entities/${encodeURIComponent(request.observerId)}/read-models/${contract.id}?${params}`;
  const response = await fetchImpl(url, { signal, cache: "no-store", credentials: "same-origin", headers: { Accept: "application/json" } });
  if (!response.ok) return { status: response.status === 409 ? "stale" : response.status === 403 ? "forbidden" : "unavailable", data: null };
  const e = await boundedJson(response) as Record<string, unknown> | null;
  const data = e?.data as ItemUsesData | undefined;
  const keys = ["applicationId", "stateSpaceId", "qualifiedQueryId", "stateSpaceFingerprint", "resolutionFingerprint", "outputSchemaHash", "resultFingerprint", "sourceRevisionFingerprint", "data"];
  if (!e || Object.keys(e).length !== keys.length || !keys.every(k => Object.hasOwn(e, k)) || e.applicationId !== request.applicationId || e.stateSpaceId !== request.stateSpaceId ||
      e.qualifiedQueryId !== contract.id || e.outputSchemaHash !== contract.outputSchemaHash || ![e.stateSpaceFingerprint, e.resolutionFingerprint, e.resultFingerprint, e.sourceRevisionFingerprint].every(fingerprint) ||
      !validate(data) || !data || data.itemId !== request.itemId || data.observerId !== request.observerId || data.perspective !== request.perspective ||
      new TextEncoder().encode(JSON.stringify(data)).length > 65536) throw new ViewReadError("incompatible-data", "Use response did not match the selected view.");
  if (input.expectedSourceRevision && e.sourceRevisionFingerprint !== input.expectedSourceRevision) return { status: "stale", data: null };
  for (const group of ["uses"] as const) {
    const g = data[group], offset = request.offset;
    if (new Set(g.entries.map(row => row.id)).size !== g.entries.length || g.nextOffset !== null && (g.nextOffset !== offset + g.entries.length || g.nextOffset <= offset))
      throw new ViewReadError("incompatible-data", "Use continuation is not an advancing page.");
  }
  return { status: "ready", data, sourceRevision: e.sourceRevisionFingerprint as string, expiresAt: Date.now() + 30000 };
}
