import assert from "node:assert/strict";
import test from "node:test";
import { readEncounterBoard } from "../src/server/encounter-board.js";
import { boardEnvelope } from "./fixtures/encounter-board.js";

const request = { origin: "http://localhost:6217", entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
  encounterId: "encounter.brackenford.ambush", perspective: "player", campaignId: "campaign.brackenford" };
const read = (envelope, extra = {}) => readEncounterBoard({ ...request,
  fetchImpl: async () => new Response(JSON.stringify(envelope), { status: 200 }), ...extra });

test("board reads one closed catalog projection, never raw components or media", async () => {
  const calls = [];
  const result = await read(boardEnvelope(), { fetchImpl: async (url, options) => {
    calls.push(String(url));
    assert.equal(options.cache, "no-store");
    assert.equal(options.method, undefined);
    return new Response(JSON.stringify(boardEnvelope()));
  } });
  assert.equal(calls.length, 1);
  const url = new URL(calls[0]);
  assert.equal(url.searchParams.get("campaignId"), null);
  assert.equal(url.searchParams.get("perspective"), "player");
  assert.deepEqual(JSON.parse(url.searchParams.get("input")), { selectionId: request.campaignId });
  assert.equal(result.participants[0].id, "participation.brackenford.hero");
  assert.equal(result.participants[0].position.width, 2);
  assert.equal(result.turn, undefined);
  assert.equal(JSON.stringify(result).includes("visibility"), false);
});

test("board binds active turn to the same visible participant, not an invented actor id", async () => {
  const envelope = boardEnvelope();
  envelope.data.turn = { id: "turn.test", participationId: envelope.data.participants[0].participationId, ordinal: 0 };
  envelope.data.participants[0].activeTurn = true;
  const result = await read(envelope);
  assert.equal(result.turn.actorName, "Hero");
  assert.equal(result.turn.actorId, undefined);
  assert.equal(result.participants[0].active, true);
});

test("board keeps useful projected geometry with additive envelope fields and newer schema evidence", async () => {
  const envelope = boardEnvelope();
  envelope.outputSchemaHash = "e".repeat(64);
  envelope.privateMetadata = { producerVersion: 2 };
  const result = await read(envelope);
  assert.equal(result.participants[0].id, "participation.brackenford.hero");
  assert.equal(result.participants[0].position.width, 2);
});

for (const [name, mutate] of [
  ["wrong encounter", value => { value.data.encounter.id = "encounter.other"; }],
  ["wrong application", value => { value.applicationId = "other"; }],
  ["wrong state space", value => { value.stateSpaceId = "other"; }],
  ["DM response in Player preview", value => { value.data.perspective = "dm"; }],
  ["malformed schema", value => { value.outputSchemaHash = "not-a-fingerprint"; }],
  ["missing provenance", value => { delete value.sourceRevisionFingerprint; }],
]) test(`board fails closed on ${name}`, async () => {
  const envelope = boardEnvelope(); mutate(envelope);
  assert.equal(await read(envelope), null);
});

for (const [name, mutate, check] of [
  ["hidden geometry", value => { value.data.obstacles[0].visibility = "dm"; }, result => assert.equal(result.obstacles.length, 0)],
  ["private prompt", value => { value.data.prompt = "SECRET_CANARY"; }, result => assert.equal(result.participants.length, 1)],
  ["private media", value => { value.data.participants[0].media = "SECRET_CANARY"; }, result => assert.equal(result.participants.length, 1)],
  ["out of bounds footprint", value => { value.data.participants[0].position.x = 11; }, result => assert.equal(result.participants.length, 0)],
  ["out of bounds obstacle", value => { value.data.obstacles[0].area.height = 64; }, result => assert.equal(result.obstacles.length, 0)],
  ["duplicate participant", value => { value.data.participants.push(value.data.participants[0]); }, result => assert.equal(result.participants.length, 0)],
  ["duplicate geometry", value => { value.data.obstacles[0].id = value.data.terrain[0].id; }, result => { assert.equal(result.obstacles.length, 0); assert.equal(result.terrain.length, 0); }],
  ["hidden active turn", value => { value.data.turn = { id: "turn.secret", participationId: "secret", ordinal: 0 }; }, result => assert.equal(result.turn, undefined)],
  ["inconsistent active turn", value => { value.data.participants[0].activeTurn = true; }, result => assert.equal(result.participants[0].active, false)],
  ["malformed participant annotation", value => { value.data.participants[0].status = { private: true }; value.data.unknown = "ignored"; }, result => assert.equal(result.participants[0].id, "participation.brackenford.hero")],
  ["missing participant name", value => { delete value.data.participants[0].name; }, result => assert.equal(result.participants[0].name, "participation.brackenford.hero")],
]) test(`board preserves safe fields with local display omission on ${name}`, async () => {
  const envelope = boardEnvelope(); mutate(envelope);
  const result = await read(envelope);
  assert.ok(result);
  check(result);
  assert.equal(result.columns, 12);
});

for (const field of ["terrain", "obstacles", "participants"]) test(`board marks absent ${field} collection partial`, async () => {
  const envelope = boardEnvelope(); delete envelope.data[field];
  const result = await read(envelope);
  assert.ok(result);
  assert.equal(result.coverage, "partial");
  assert.match(result.notices.join(" "), /data is incomplete/u);
  assert.equal(result[field].length, 0);
});

for (const status of [403, 404, 409, 422, 500]) test(`board HTTP ${status} stays unavailable without raw fallback`, async () => {
  let calls = 0;
  assert.equal(await read(null, { fetchImpl: async () => { calls++; return new Response("{}", { status }); } }), null);
  assert.equal(calls, 1);
});

test("board propagates cancellation", async () => {
  await assert.rejects(read(null, { fetchImpl: async () => { throw new DOMException("Replaced", "AbortError"); } }), { name: "AbortError" });
});

test("board rejects a response above its explicit transport limit", async () => {
  assert.equal(await read(null, {
    fetchImpl: async () => new Response("x".repeat(262_145)),
  }), null);
});

test("board rejects an oversized bounded collection instead of presenting it as empty", async () => {
  const envelope = boardEnvelope();
  envelope.data.participants = Array.from({ length: 101 }, (_, index) => ({
    ...boardEnvelope().data.participants[0], participationId: `participation.${index}`,
  }));
  assert.equal(await read(envelope), null);
});
