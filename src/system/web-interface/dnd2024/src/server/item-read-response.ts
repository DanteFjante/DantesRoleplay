import { ViewReadError } from "../data/view-read-client";
import type { Perspective } from "../data/hub-types";
import { readModelResponse } from "./read-model-response.js";

export type ItemReadContract = { id: string; outputSchemaHash: string };
export type ItemReadBinding = {
  applicationId: string;
  stateSpaceId: string;
  campaignId: string;
  observerId: string;
  itemId: string;
  perspective: Perspective;
};
export type ItemReadFailure = { status: "forbidden" | "unavailable" | "stale"; data: null };
export type ItemReadSuccess<T> = { status: "ready"; data: T; sourceRevision: string; expiresAt: number };

export const itemReadId = (value: string) => /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(value);

export async function readItemResponse<T>({
  request, input, contract, validate, consume, verify, errorMessage, expectedSourceRevision,
}: {
  request: ItemReadBinding;
  input: Record<string, unknown>;
  contract: ItemReadContract;
  validate?: (value: unknown) => boolean;
  consume?: (value: unknown) => T | null;
  verify?: (value: T) => boolean;
  errorMessage: string;
  expectedSourceRevision?: string | null;
}, signal: AbortSignal, fetchImpl: typeof fetch = fetch): Promise<ItemReadSuccess<T> | ItemReadFailure> {
  const parameters = new URLSearchParams({
    perspective: request.perspective,
    input: JSON.stringify(input),
  });
  const url = `/api/applications/${encodeURIComponent(request.applicationId)}/state-spaces/${encodeURIComponent(request.stateSpaceId)}/entities/${encodeURIComponent(request.observerId)}/read-models/${contract.id}?${parameters}`;
  const result = await readModelResponse({
    fetchImpl,
    resource: url,
    init: { signal, credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } },
    applicationId: request.applicationId,
    stateSpaceId: request.stateSpaceId,
    query: { id: contract.id, outputSchemaHash: contract.outputSchemaHash },
    maximumBodyBytes: 70_000,
    maximumDataBytes: 65_536,
    statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
    ...(consume ? { consume } : { validate: (value: unknown): value is T => Boolean(validate?.(value)) }),
    verify,
    expectedSourceRevision,
  });
  if (result.status === "incompatible") {
    throw new ViewReadError("incompatible-data", errorMessage);
  }
  if (result.status !== "ready") return { status: result.status, data: null };
  return {
    status: "ready",
    data: result.data,
    sourceRevision: result.evidence.sourceRevisionFingerprint,
    expiresAt: Date.now() + 30_000,
  };
}
