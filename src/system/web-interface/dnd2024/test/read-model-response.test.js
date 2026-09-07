import assert from "node:assert/strict";
import test from "node:test";
import { readModelResponse } from "../src/server/read-model-response.js";

const query = { id: "dnd2024.query.fixture", outputSchemaHash: "E".repeat(64) };
const scope = { applicationId: "dnd2024", stateSpaceId: "dnd2024-main" };
const envelope = () => ({
  ...scope,
  qualifiedQueryId: query.id,
  stateSpaceFingerprint: "A".repeat(64),
  resolutionFingerprint: "B".repeat(64),
  outputSchemaHash: query.outputSchemaHash,
  resultFingerprint: "C".repeat(64),
  sourceRevisionFingerprint: "D".repeat(64),
  data: { version: 1, value: "visible" },
});
const response = (value, status = 200) => new Response(JSON.stringify(value), { status });
const read = (fetchImpl, options = {}) => readModelResponse({
  fetchImpl,
  resource: "https://table.test/read-model",
  init: { signal: new AbortController().signal, cache: "no-store" },
  ...scope,
  query,
  maximumBodyBytes: 1_024,
  maximumDataBytes: 512,
  statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: [404, 503] },
  validate: (value) => value?.version === 1 && typeof value.value === "string",
  verify: (value) => value.value !== "foreign",
  ...options,
});

test("shared read-model transport returns only exact, scoped, schema-valid envelopes", async () => {
  const result = await read(async (_resource, init) => {
    assert.equal(init.cache, "no-store");
    return response(envelope());
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.data, { version: 1, value: "visible" });
  assert.equal(result.evidence.qualifiedQueryId, query.id);
  assert.equal(result.evidence.sourceRevisionFingerprint, "D".repeat(64));
});

for (const [name, mutate] of [
  ["extra envelope field", (value) => { value.private = true; }],
  ["wrong application scope", (value) => { value.applicationId = "other"; }],
  ["wrong state-space scope", (value) => { value.stateSpaceId = "other"; }],
  ["wrong query", (value) => { value.qualifiedQueryId = "dnd2024.query.other"; }],
  ["wrong schema", (value) => { value.outputSchemaHash = "0".repeat(64); }],
  ["missing provenance", (value) => { delete value.sourceRevisionFingerprint; }],
  ["invalid schema data", (value) => { value.data.version = 2; }],
  ["wrong feature binding", (value) => { value.data.value = "foreign"; }],
]) test(`shared read-model transport rejects ${name}`, async () => {
  const value = envelope();
  mutate(value);
  assert.equal((await read(async () => response(value))).status, "incompatible");
});

test("shared read-model transport enforces per-query body and data limits", async () => {
  const oversizedBody = await read(async () => new Response("x".repeat(1_025)));
  assert.equal(oversizedBody.status, "incompatible");

  const value = envelope();
  value.data.value = "x".repeat(513);
  const oversizedData = await read(async () => response(value), { maximumBodyBytes: 2_048 });
  assert.equal(oversizedData.status, "incompatible");
});

test("shared read-model transport classifies only statuses allowed by each query", async () => {
  for (const [httpStatus, expected] of [[403, "forbidden"], [409, "stale"], [404, "unavailable"], [503, "unavailable"]]) {
    const result = await read(async () => new Response("PRIVATE", { status: httpStatus }));
    assert.equal(result.status, expected);
  }
  assert.equal((await read(async () => new Response(null, { status: 422 }))).status, "incompatible");
});

test("shared read-model transport detects stale source revisions after validation", async () => {
  const result = await read(async () => response(envelope()), {
    expectedSourceRevision: "F".repeat(64),
  });
  assert.equal(result.status, "stale");
  assert.equal(result.httpStatus, 200);
});

test("shared read-model transport propagates cancellation", async () => {
  await assert.rejects(
    read(async () => { throw new DOMException("Replaced", "AbortError"); }),
    { name: "AbortError" },
  );
});
