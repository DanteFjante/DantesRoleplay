import validate, { contract } from "./item-uses-validator.js";
import { itemReadId, readItemResponse } from "./item-read-response";
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
  if (![request.applicationId, request.stateSpaceId, request.campaignId, request.observerId, request.itemId].every(itemReadId) ||
      !["player", "dm"].includes(request.perspective) || ![input.offset].every(n => Number.isInteger(n) && n >= 0 && n <= 10000) ||
      input.expectedSourceRevision !== null && !fingerprint(input.expectedSourceRevision) || input.offset > 0 && !input.expectedSourceRevision)
    return { status: "unavailable", data: null };
  return readItemResponse<ItemUsesData>({ request, input, contract, validate, expectedSourceRevision: input.expectedSourceRevision,
    errorMessage: "Use response did not match the selected view.", verify: (data) => {
      const group = data.uses;
      return data.itemId === request.itemId && data.observerId === request.observerId && data.perspective === request.perspective &&
        new Set(group.entries.map(row => row.id)).size === group.entries.length &&
        (group.nextOffset === null || group.nextOffset === request.offset + group.entries.length && group.nextOffset > request.offset);
    },
  }, signal, fetchImpl);
}
