import assert from "node:assert/strict";
import test from "node:test";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { readEncounterBoard } from "../src/server/encounter-board.js";
import { boardEnvelope } from "./fixtures/encounter-board.js";

const request = { origin: "http://localhost:6217", entityRoot: "/api/applications/dnd2024/state-spaces/dnd2024-main/entities",
  encounterId: "encounter.brackenford.ambush", perspective: "player" };
const read = (envelope, extra = {}) => readEncounterBoard({ ...request,
  fetchImpl: async () => new Response(JSON.stringify(envelope), { status: 200 }), ...extra });

test("precompiled board validator matches the exact catalog contract", () => {
  execFileSync(process.execPath, [fileURLToPath(new URL('../scripts/generate-board-validator.mjs', import.meta.url)), '--check']);
});

test("board reads one closed catalog projection, never raw components or media", async () => {
  const calls = [];
  const result = await read(boardEnvelope(), { fetchImpl: async (url, options) => {
    calls.push(String(url));
    assert.equal(options.cache, "no-store");
    assert.equal(options.method, undefined);
    return new Response(JSON.stringify(boardEnvelope()));
  } });
  assert.equal(calls.length, 1);
  assert.match(calls[0], /encounter.brackenford.ambush\/read-models\/dnd2024.query.encounter-board\?perspective=player$/u);
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

for (const [name, mutate] of [
  ["wrong encounter", value => { value.data.encounter.id = "encounter.other"; }],
  ["wrong application", value => { value.applicationId = "other"; }],
  ["wrong state space", value => { value.stateSpaceId = "other"; }],
  ["DM response in Player preview", value => { value.data.perspective = "dm"; }],
  ["hidden geometry", value => { value.data.obstacles[0].visibility = "dm"; }],
  ["private prompt", value => { value.data.prompt = "SECRET_CANARY"; }],
  ["private media", value => { value.data.participants[0].media = "SECRET_CANARY"; }],
  ["schema mismatch", value => { value.outputSchemaHash = "e".repeat(64); }],
  ["missing provenance", value => { delete value.sourceRevisionFingerprint; }],
  ["out of bounds footprint", value => { value.data.participants[0].position.x = 11; }],
  ["out of bounds obstacle", value => { value.data.obstacles[0].area.height = 64; }],
  ["duplicate participant", value => { value.data.participants.push(value.data.participants[0]); }],
  ["duplicate geometry", value => { value.data.obstacles[0].id = value.data.terrain[0].id; }],
  ["hidden active turn", value => { value.data.turn = { id: "turn.secret", participationId: "secret", ordinal: 0 }; }],
  ["inconsistent active turn", value => { value.data.participants[0].activeTurn = true; }],
]) test(`board fails closed on ${name}`, async () => {
  const envelope = boardEnvelope(); mutate(envelope);
  assert.equal(await read(envelope), null);
});

for (const status of [403, 404, 409, 422, 500]) test(`board HTTP ${status} stays unavailable without raw fallback`, async () => {
  let calls = 0;
  assert.equal(await read(null, { fetchImpl: async () => { calls++; return new Response("{}", { status }); } }), null);
  assert.equal(calls, 1);
});

test("board propagates cancellation", async () => {
  await assert.rejects(read(null, { fetchImpl: async () => { throw new DOMException("Replaced", "AbortError"); } }), { name: "AbortError" });
});
