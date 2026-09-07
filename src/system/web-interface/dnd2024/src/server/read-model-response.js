const ENVELOPE_KEYS = [
  "applicationId",
  "stateSpaceId",
  "qualifiedQueryId",
  "stateSpaceFingerprint",
  "resolutionFingerprint",
  "outputSchemaHash",
  "resultFingerprint",
  "sourceRevisionFingerprint",
  "data",
];

const fingerprint = (value) =>
  typeof value === "string" && /^[a-f0-9]{64}$/iu.test(value);

/**
 * @typedef {{
 *   ready: readonly number[],
 *   forbidden?: readonly number[],
 *   stale?: readonly number[],
 *   unavailable?: readonly number[] | "remaining",
 * }} ReadModelStatusPolicy
 */

/**
 * @typedef {{
 *   qualifiedQueryId: string,
 *   stateSpaceFingerprint: string,
 *   resolutionFingerprint: string,
 *   outputSchemaHash: string,
 *   resultFingerprint: string,
 *   sourceRevisionFingerprint: string,
 * }} ReadModelEvidence
 */

/** @param {number} status @param {ReadModelStatusPolicy} policy */
function classifyStatus(status, policy) {
  if (policy.ready.includes(status)) return "ready";
  if (policy.forbidden?.includes(status)) return "forbidden";
  if (policy.stale?.includes(status)) return "stale";
  if (policy.unavailable === "remaining" || policy.unavailable?.includes(status))
    return "unavailable";
  return "incompatible";
}

/**
 * Reads one response body without trusting Content-Length or replacement-decoding invalid UTF-8.
 * Cancellation and other stream failures propagate to the feature adapter unchanged.
 *
 * @param {Response} response
 * @param {number} maximumBodyBytes
 * @returns {Promise<{status: "ready", value: unknown} | {status: "incompatible", reason: string}>}
 */
export async function readBoundedJson(response, maximumBodyBytes) {
  if (!response.body) return { status: "incompatible", reason: "missing-body" };
  const declaredLength = Number(response.headers.get("content-length"));
  if (Number.isFinite(declaredLength) && declaredLength > maximumBodyBytes) {
    await response.body.cancel();
    return { status: "incompatible", reason: "body-too-large" };
  }
  const reader = response.body.getReader();
  const chunks = [];
  let size = 0;
  try {
    while (true) {
      const next = await reader.read();
      if (next.done) break;
      size += next.value.byteLength;
      if (size > maximumBodyBytes) {
        await reader.cancel();
        return { status: "incompatible", reason: "body-too-large" };
      }
      chunks.push(next.value);
    }
  } finally {
    reader.releaseLock();
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.length;
  }
  try {
    return {
      status: "ready",
      value: JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes)),
    };
  } catch {
    return { status: "incompatible", reason: "invalid-json" };
  }
}

/**
 * @template T
 * @param {unknown} value
 * @param {{
 *   applicationId: string,
 *   stateSpaceId: string,
 *   query: {id: string, outputSchemaHash: string},
 *   maximumDataBytes?: number,
 *   validate: (value: unknown) => value is T,
 *   verify?: (value: T) => boolean,
 * }} contract
 * @returns {{data: T, evidence: ReadModelEvidence} | null}
 */
export function validateReadModelEnvelope(value, {
  applicationId,
  stateSpaceId,
  query,
  maximumDataBytes,
  validate,
  verify,
}) {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      Object.keys(value).length !== ENVELOPE_KEYS.length ||
      !ENVELOPE_KEYS.every((key) => Object.hasOwn(value, key))) return null;
  const record = /** @type {Record<string, unknown>} */ (value);
  if (record.applicationId !== applicationId || record.stateSpaceId !== stateSpaceId ||
      record.qualifiedQueryId !== query.id || record.outputSchemaHash !== query.outputSchemaHash ||
      ![record.stateSpaceFingerprint, record.resolutionFingerprint, record.resultFingerprint,
        record.sourceRevisionFingerprint].every(fingerprint) || !validate(record.data)) return null;
  if (maximumDataBytes !== undefined &&
      new TextEncoder().encode(JSON.stringify(record.data)).length > maximumDataBytes) return null;
  if (verify && !verify(record.data)) return null;
  return {
    data: record.data,
    evidence: {
      qualifiedQueryId: query.id,
      stateSpaceFingerprint: /** @type {string} */ (record.stateSpaceFingerprint),
      resolutionFingerprint: /** @type {string} */ (record.resolutionFingerprint),
      outputSchemaHash: query.outputSchemaHash,
      resultFingerprint: /** @type {string} */ (record.resultFingerprint),
      sourceRevisionFingerprint: /** @type {string} */ (record.sourceRevisionFingerprint),
    },
  };
}

/**
 * Applies the shared read-model transport and exact-envelope contract. Feature adapters remain
 * responsible for constructing authorized URLs and for all application-specific data semantics.
 *
 * @template T
 * @param {{
 *   fetchImpl?: typeof fetch,
 *   resource: RequestInfo | URL,
 *   init?: RequestInit,
 *   applicationId: string,
 *   stateSpaceId: string,
 *   query: {id: string, outputSchemaHash: string},
 *   maximumBodyBytes: number,
 *   maximumDataBytes?: number,
 *   statusPolicy: ReadModelStatusPolicy,
 *   validate: (value: unknown) => value is T,
 *   verify?: (value: T) => boolean,
 *   expectedSourceRevision?: string | null,
 * }} options
 * @returns {Promise<
 *   {status: "ready", data: T, evidence: ReadModelEvidence, response: Response} |
 *   {status: "forbidden" | "stale" | "unavailable", data: null, httpStatus: number, response: Response} |
 *   {status: "incompatible", data: null, httpStatus: number, reason: string, response: Response}
 * >}
 */
export async function readModelResponse({
  fetchImpl = fetch,
  resource,
  init,
  applicationId,
  stateSpaceId,
  query,
  maximumBodyBytes,
  maximumDataBytes,
  statusPolicy,
  validate,
  verify,
  expectedSourceRevision,
}) {
  const response = await fetchImpl(resource, init);
  const responseStatus = classifyStatus(response.status, statusPolicy);
  if (responseStatus !== "ready") {
    return responseStatus === "incompatible"
      ? { status: "incompatible", data: null, httpStatus: response.status, reason: "http-status", response }
      : { status: responseStatus, data: null, httpStatus: response.status, response };
  }
  const decoded = await readBoundedJson(response, maximumBodyBytes);
  if (decoded.status !== "ready")
    return { ...decoded, data: null, httpStatus: response.status, response };
  const envelope = validateReadModelEnvelope(decoded.value, {
    applicationId,
    stateSpaceId,
    query,
    maximumDataBytes,
    validate,
    verify,
  });
  if (!envelope)
    return { status: "incompatible", data: null, httpStatus: response.status, reason: "envelope-contract", response };
  if (expectedSourceRevision && envelope.evidence.sourceRevisionFingerprint !== expectedSourceRevision)
    return { status: "stale", data: null, httpStatus: response.status };
  return {
    status: "ready",
    data: envelope.data,
    evidence: envelope.evidence,
    response,
  };
}
