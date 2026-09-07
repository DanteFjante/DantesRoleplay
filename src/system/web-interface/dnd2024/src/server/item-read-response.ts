import { ViewReadError } from "../data/view-read-client";
import type { Perspective } from "../data/hub-types";

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

const envelopeKeys = ["applicationId", "stateSpaceId", "qualifiedQueryId", "stateSpaceFingerprint", "resolutionFingerprint", "outputSchemaHash", "resultFingerprint", "sourceRevisionFingerprint", "data"];
const fingerprint = (value: unknown): value is string => typeof value === "string" && /^[A-F0-9]{64}$/i.test(value);
export const itemReadId = (value: string) => /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(value);

export async function readItemResponse<T>({
  request, input, contract, validate, verify, errorMessage, expectedSourceRevision,
}: {
  request: ItemReadBinding;
  input: Record<string, unknown>;
  contract: ItemReadContract;
  validate: (value: unknown) => boolean;
  verify?: (value: T) => boolean;
  errorMessage: string;
  expectedSourceRevision?: string | null;
}, signal: AbortSignal, fetchImpl: typeof fetch = fetch): Promise<ItemReadSuccess<T> | ItemReadFailure> {
  const parameters = new URLSearchParams({
    perspective: request.perspective,
    campaignId: request.campaignId,
    input: JSON.stringify(input),
  });
  const url = `/api/applications/${encodeURIComponent(request.applicationId)}/state-spaces/${encodeURIComponent(request.stateSpaceId)}/entities/${encodeURIComponent(request.observerId)}/read-models/${contract.id}?${parameters}`;
  const response = await fetchImpl(url, { signal, credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
  if (!response.ok) return { status: response.status === 403 ? "forbidden" : response.status === 409 ? "stale" : "unavailable", data: null };
  const envelope = await boundedJson(response) as Record<string, unknown> | null;
  const data = envelope?.data as T | undefined;
  if (!envelope || Object.keys(envelope).length !== envelopeKeys.length || !envelopeKeys.every((key) => Object.hasOwn(envelope, key)) ||
      envelope.applicationId !== request.applicationId || envelope.stateSpaceId !== request.stateSpaceId || envelope.qualifiedQueryId !== contract.id ||
      envelope.outputSchemaHash !== contract.outputSchemaHash ||
      ![envelope.stateSpaceFingerprint, envelope.resolutionFingerprint, envelope.resultFingerprint, envelope.sourceRevisionFingerprint].every(fingerprint) ||
      !validate(data) || !data || new TextEncoder().encode(JSON.stringify(data)).length > 65_536 || verify && !verify(data)) {
    throw new ViewReadError("incompatible-data", errorMessage);
  }
  if (expectedSourceRevision && envelope.sourceRevisionFingerprint !== expectedSourceRevision)
    return { status: "stale", data: null };
  return { status: "ready", data, sourceRevision: envelope.sourceRevisionFingerprint as string, expiresAt: Date.now() + 30_000 };
}

export async function boundedJson(response: Response): Promise<unknown> {
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
