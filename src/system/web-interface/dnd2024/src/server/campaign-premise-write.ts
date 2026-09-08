import type { ReadyHubEnvelope } from "../data/hub-types";
import { validateRegisteredCampaignSummary } from "./campaign-summary.js";
import { contract as campaignSummaryContract } from "./campaign-summary-contract.js";
import { readBoundedJson } from "./read-model-response.js";

export type CampaignPremiseWriteRequest = {
  envelope: ReadyHubEnvelope;
  premise: string;
  idempotencyKey?: string;
};

export type CampaignPremiseWriteResult = {
  premise: string;
  applied: boolean;
  replayed: boolean;
  noOp: boolean;
  operationId: string;
  sourceRevisionFingerprint: string;
};

export type CampaignPremiseWriter = (
  request: CampaignPremiseWriteRequest,
  signal?: AbortSignal,
) => Promise<CampaignPremiseWriteResult>;

export type CampaignPremiseWriteFailure =
  | "authorization"
  | "stale"
  | "idempotency"
  | "rejected"
  | "incompatible"
  | "transport"
  | "cancelled";

export class CampaignPremiseWriteError extends Error {
  public readonly category: CampaignPremiseWriteFailure;
  public readonly code: string;

  constructor(
    category: CampaignPremiseWriteFailure,
    code: string,
    message: string,
    options?: ErrorOptions,
  ) {
    super(message, options);
    this.name = "CampaignPremiseWriteError";
    this.category = category;
    this.code = code;
  }
}

const exactKeys = (value: unknown, keys: string[]) => Boolean(value && typeof value === "object" &&
  !Array.isArray(value) && Object.keys(value).length === keys.length &&
  keys.every((key) => Object.hasOwn(value, key)));
const token = (value: unknown, maximum = 200) => typeof value === "string" && value.length > 0 &&
  value.length <= maximum && value === value.trim() && !/\s/u.test(value);
const fingerprint = (value: unknown) => typeof value === "string" && /^[0-9A-F]{64}$/iu.test(value);

function failure(status: number, value: unknown) {
  const code = exactKeys(value, ["code", "message"]) && typeof (value as { code?: unknown }).code === "string"
    ? (value as { code: string }).code
    : "OBJECT_WRITE_UNAVAILABLE";
  if (status === 403 && code === "OBJECT_WRITE_FORBIDDEN")
    return new CampaignPremiseWriteError("authorization", code, "Only the authorized DM can edit the campaign premise.");
  if (status === 409 && code === "OBJECT_WRITE_SOURCE_STALE")
    return new CampaignPremiseWriteError("stale", code, "The campaign changed. It has been refreshed; review your draft and retry.");
  if (status === 409 && code === "OBJECT_WRITE_IDEMPOTENCY_CONFLICT")
    return new CampaignPremiseWriteError("idempotency", code, "This save key was already used for a different edit. Retry the draft.");
  if (status === 400 || status === 404 || status === 422)
    return new CampaignPremiseWriteError("rejected", code, "The campaign premise edit was rejected without changing the campaign.");
  return new CampaignPremiseWriteError("transport", code, "The campaign premise could not be saved.");
}

function validResult(value: unknown, request: CampaignPremiseWriteRequest, premise: string) {
  const keys = ["applicationId", "stateSpaceId", "qualifiedQueryId", "applied", "replayed", "noOp",
    "operationId", "sourceRevisionFingerprint", "data"];
  if (!exactKeys(value, keys)) return null;
  const result = value as Record<string, unknown>;
  if (result.applicationId !== request.envelope.applicationId ||
      result.stateSpaceId !== request.envelope.stateSpaceId ||
      result.qualifiedQueryId !== campaignSummaryContract.id ||
      typeof result.applied !== "boolean" || typeof result.replayed !== "boolean" ||
      typeof result.noOp !== "boolean" || !token(result.operationId) ||
      !fingerprint(result.sourceRevisionFingerprint) ||
      (result.applied && (result.replayed || result.noOp)) ||
      (!result.applied && !result.noOp) || (result.replayed && !result.noOp)) return null;
  const projected = validateRegisteredCampaignSummary(result.data, null, "dm");
  if (!projected || projected.premise !== premise) return null;
  return {
    premise: projected.premise,
    applied: result.applied as boolean,
    replayed: result.replayed as boolean,
    noOp: result.noOp as boolean,
    operationId: result.operationId as string,
    sourceRevisionFingerprint: result.sourceRevisionFingerprint as string,
  };
}

/**
 * Sends one exact mapped Campaign Summary edit. A transport-uncertain response is retried once
 * with byte-identical input and the same idempotency key; HTTP rejections are never retried.
 */
export function createCampaignPremiseWriter(
  fetchImpl: typeof fetch = fetch,
  retryDelayMs = 25,
): CampaignPremiseWriter {
  return async (request, signal) => {
    const { envelope } = request;
    const premise = request.premise.trim();
    const evidence = envelope.objectQueries?.campaignSummary;
    const campaignId = envelope.contextSelection?.selectedCampaignId ?? envelope.revision;
    const idempotencyKey = request.idempotencyKey ?? globalThis.crypto.randomUUID();
    if (envelope.audience.seat !== "dm" || envelope.audience.perspective !== "dm")
      throw new CampaignPremiseWriteError("authorization", "OBJECT_WRITE_FORBIDDEN",
        "Only the authorized DM can edit the campaign premise.");
    if (!token(envelope.applicationId) || !token(envelope.stateSpaceId) || !token(campaignId) ||
        !token(idempotencyKey, 128) || premise.length < 1 || premise.length > 1_000 ||
        evidence?.qualifiedQueryId !== campaignSummaryContract.id ||
        !fingerprint(evidence.sourceRevisionFingerprint))
      throw new CampaignPremiseWriteError("incompatible", "OBJECT_WRITE_REQUEST_INVALID",
        "Refresh the campaign before editing its premise.");

    const body = JSON.stringify({
      idempotencyKey,
      expectedSourceRevisionFingerprint: evidence.sourceRevisionFingerprint,
      changes: { premise },
      relationshipEdits: [],
    });
    const resource = `/api/applications/${encodeURIComponent(envelope.applicationId)}` +
      `/state-spaces/${encodeURIComponent(envelope.stateSpaceId)}/entities/${encodeURIComponent(campaignId)}` +
      `/read-models/${encodeURIComponent(campaignSummaryContract.id)}`;

    for (let attempt = 0; attempt < 2; attempt += 1) {
      try {
        const response = await fetchImpl(resource, {
          method: "PATCH",
          credentials: "same-origin",
          headers: { Accept: "application/json", "Content-Type": "application/json" },
          cache: "no-store",
          body,
          signal,
        });
        const bounded = await readBoundedJson(response, 70_000);
        if (bounded.status !== "ready")
          throw new CampaignPremiseWriteError("incompatible", "OBJECT_WRITE_RESPONSE_INVALID",
            "The server returned an incompatible campaign response. Refresh before continuing.");
        const decoded = bounded.value;
        if (!response.ok) throw failure(response.status, decoded);
        const result = validResult(decoded, request, premise);
        if (!result) throw new CampaignPremiseWriteError("incompatible", "OBJECT_WRITE_RESPONSE_INVALID",
          "The server saved an incompatible campaign response. Refresh before continuing.");
        return result;
      } catch (error) {
        if (signal?.aborted || error instanceof DOMException && error.name === "AbortError")
          throw new CampaignPremiseWriteError("cancelled", "OBJECT_WRITE_CANCELLED", "The campaign edit was cancelled.", { cause: error });
        if (error instanceof CampaignPremiseWriteError) throw error;
        if (attempt === 0 && error instanceof TypeError) {
          await new Promise<void>((resolve, reject) => {
            const timer = globalThis.setTimeout(resolve, retryDelayMs);
            signal?.addEventListener("abort", () => {
              globalThis.clearTimeout(timer);
              reject(new CampaignPremiseWriteError("cancelled", "OBJECT_WRITE_CANCELLED",
                "The campaign edit was cancelled."));
            }, { once: true });
          });
          continue;
        }
        throw new CampaignPremiseWriteError("transport", "OBJECT_WRITE_UNAVAILABLE",
          "The campaign premise could not be saved. The server may not have answered; retry safely.", { cause: error });
      }
    }
    throw new CampaignPremiseWriteError("transport", "OBJECT_WRITE_UNAVAILABLE",
      "The campaign premise could not be saved. The server may not have answered; retry safely.");
  };
}

export const writeCampaignPremise = createCampaignPremiseWriter();
