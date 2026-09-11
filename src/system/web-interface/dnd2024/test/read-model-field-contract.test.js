import assert from "node:assert/strict";
import test from "node:test";
import { readModelResponse, validateReadModelEnvelope } from "../src/server/read-model-response.js";

const hash = "1".repeat(64);
const query = { id: "test.query.fields", outputSchemaHash: hash };
const contract = {
  applicationId: "test-app",
  stateSpaceId: "test-state",
  query,
  validate: (value) => value !== null && typeof value === "object" && typeof value.quantity === "number",
  verify: (value) => value.actorId === "actor.test",
};
function envelope(overrides = {}) {
  return {
    applicationId: contract.applicationId,
    stateSpaceId: contract.stateSpaceId,
    qualifiedQueryId: query.id,
    stateSpaceFingerprint: hash,
    resolutionFingerprint: hash,
    outputSchemaHash: hash,
    resultFingerprint: hash,
    sourceRevisionFingerprint: hash,
    data: { actorId: "actor.test", quantity: 0 },
    ...overrides,
  };
}

test("shared field-read admission accepts newer schema evidence and inert additive metadata", () => {
  const response = envelope({ outputSchemaHash: "a".repeat(64), futureMetadata: { nested: true } });
  response.data.futureField = { unrelated: "not selected" };
  const read = validateReadModelEnvelope(response, contract);
  assert.ok(read);
  assert.equal(read.data.quantity, 0);
  assert.equal(read.evidence.outputSchemaHash, "a".repeat(64));
});

test("required envelope identity and evidence must be own properties, not inherited defaults", () => {
  for (const key of Object.keys(envelope())) {
    const response = envelope();
    const inherited = response[key];
    delete response[key];
    Object.setPrototypeOf(response, { [key]: inherited });
    assert.equal(validateReadModelEnvelope(response, contract), null, key);
  }
});

test("field tolerance retains scope, subject verification, producer checks, and fingerprint syntax", () => {
  for (const overrides of [
    { applicationId: "foreign" },
    { stateSpaceId: "foreign" },
    { qualifiedQueryId: "foreign" },
    { data: { actorId: "actor.foreign", quantity: 1 } },
    { data: { actorId: "actor.test", quantity: "1" } },
    ...["outputSchemaHash", "stateSpaceFingerprint", "resolutionFingerprint", "resultFingerprint", "sourceRevisionFingerprint"]
      .map((key) => ({ [key]: "malformed" })),
  ]) assert.equal(validateReadModelEnvelope(envelope(overrides), contract), null);
});

test("prototype-like unknown JSON keys stay inert during field consumption", () => {
  const response = envelope();
  response.data = JSON.parse('{"actorId":"actor.test","quantity":0,"__proto__":{"polluted":true},"constructor":{"prototype":{"polluted":true}}}');
  const read = validateReadModelEnvelope(response, {
    ...contract,
    consume: (data) => ({ actorId: data.actorId, quantity: data.quantity }),
  });
  assert.deepEqual(read.data, { actorId: "actor.test", quantity: 0 });
  assert.equal(Object.hasOwn(read.data, "__proto__"), false);
  assert.equal({}.polluted, undefined);
});

test("different schema identity cannot bypass an expected source revision", async () => {
  const response = new Response(JSON.stringify(envelope({ outputSchemaHash: "a".repeat(64) })));
  const read = await readModelResponse({
    ...contract,
    resource: "https://example.invalid/read",
    fetchImpl: async () => response,
    maximumBodyBytes: 4_096,
    maximumDataBytes: 2_048,
    statusPolicy: { ready: [200], forbidden: [403], stale: [409], unavailable: "remaining" },
    expectedSourceRevision: "2".repeat(64),
  });
  assert.equal(read.status, "stale");
  assert.equal(read.data, null);
});
