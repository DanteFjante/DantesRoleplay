import assert from "node:assert/strict";
import test from "node:test";

import {
  CampaignPremiseWriteError,
  createCampaignPremiseWriter,
} from "../src/server/campaign-premise-write.ts";

const sourceRevision = "A".repeat(64);
const nextRevision = "B".repeat(64);

function envelope(perspective = "dm", seat = "dm") {
  return {
    applicationId: "dnd2024", stateSpaceId: "state.fixture", revision: "campaign.fixture",
    audience: { seat, perspective, allowedPerspectives: [perspective] },
    contextSelection: {
      selectedCampaignId: "campaign.fixture", selectedWorldId: "world.fixture", worlds: [],
    },
    objectQueries: { campaignSummary: {
      qualifiedQueryId: "dnd2024.query.campaign-summary",
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: "3".repeat(64), resultFingerprint: "4".repeat(64),
      sourceRevisionFingerprint: sourceRevision,
    } },
  };
}

function summary(premise) {
  return {
    status: "active", title: "Fixture Campaign", premise,
    partyGoals: ["Continue the campaign."], toneAndBoundaries: ["Respect the table."],
    party: [], totalCount: 0, complete: true, nextCursor: null,
  };
}

function success(premise, overrides = {}) {
  return new Response(JSON.stringify({
    applicationId: "dnd2024", stateSpaceId: "state.fixture",
    qualifiedQueryId: "dnd2024.query.campaign-summary",
    applied: true, replayed: false, noOp: false,
    operationId: "operation.fixture", sourceRevisionFingerprint: nextRevision,
    data: summary(premise), ...overrides,
  }), { status: 200, headers: { "content-type": "application/json" } });
}

test("premise writer sends one exact mapped PATCH and accepts committed or no-op results", async () => {
  const calls = [];
  const writer = createCampaignPremiseWriter(async (input, init) => {
    calls.push({ input, init });
    return success("A reviewed premise.");
  }, 0);
  const result = await writer({
    envelope: envelope(), premise: "  A reviewed premise.  ", idempotencyKey: "edit-fixture-1",
  });

  assert.equal(calls.length, 1);
  assert.equal(calls[0].input,
    "/api/applications/dnd2024/state-spaces/state.fixture/entities/campaign.fixture/read-models/dnd2024.query.campaign-summary?campaign=campaign.fixture");
  assert.equal(calls[0].init.method, "PATCH");
  assert.equal(calls[0].init.credentials, "same-origin");
  assert.deepEqual(JSON.parse(calls[0].init.body), {
    idempotencyKey: "edit-fixture-1",
    expectedSourceRevisionFingerprint: sourceRevision,
    changes: { premise: "A reviewed premise." },
    relationshipEdits: [],
  });
  assert.deepEqual(result, {
    premise: "A reviewed premise.", applied: true, replayed: false, noOp: false,
    operationId: "operation.fixture", sourceRevisionFingerprint: nextRevision,
  });

  const noOp = await createCampaignPremiseWriter(async () => success("Existing premise.", {
    applied: false, replayed: false, noOp: true,
  }), 0)({ envelope: envelope(), premise: "Existing premise.", idempotencyKey: "edit-fixture-noop" });
  assert.equal(noOp.noOp, true);
  assert.equal(noOp.applied, false);
});

test("an uncertain response retries once with byte-identical input and the same idempotency key", async () => {
  const calls = [];
  const writer = createCampaignPremiseWriter(async (input, init) => {
    calls.push({ input, body: init.body });
    if (calls.length === 1) throw new TypeError("connection closed after send");
    return success("Safely replayed premise.", { applied: false, replayed: true, noOp: true });
  }, 0);

  const result = await writer({
    envelope: envelope(), premise: "Safely replayed premise.", idempotencyKey: "stable-retry-key",
  });
  assert.equal(calls.length, 2);
  assert.deepEqual(calls[0], calls[1]);
  assert.equal(result.replayed, true);
});

test("stale, rejected, malformed, and unauthorized writes remain distinct and do not retry", async () => {
  let calls = 0;
  const staleWriter = createCampaignPremiseWriter(async () => {
    calls += 1;
    return new Response(JSON.stringify({
      code: "OBJECT_WRITE_SOURCE_STALE", message: "The object changed. Refresh before saving.",
    }), { status: 409 });
  }, 0);
  await assert.rejects(
    staleWriter({ envelope: envelope(), premise: "Stale premise.", idempotencyKey: "stale-key" }),
    (error) => error instanceof CampaignPremiseWriteError && error.category === "stale",
  );
  assert.equal(calls, 1);

  const rejectedWriter = createCampaignPremiseWriter(async () => new Response(JSON.stringify({
    code: "OBJECT_WRITE_REJECTED", message: "Rejected.",
  }), { status: 422 }), 0);
  await assert.rejects(
    rejectedWriter({ envelope: envelope(), premise: "Rejected premise.", idempotencyKey: "rejected-key" }),
    (error) => error instanceof CampaignPremiseWriteError && error.category === "rejected",
  );

  const malformedWriter = createCampaignPremiseWriter(async () => success("Different premise."), 0);
  await assert.rejects(
    malformedWriter({ envelope: envelope(), premise: "Requested premise.", idempotencyKey: "malformed-key" }),
    (error) => error instanceof CampaignPremiseWriteError && error.category === "incompatible",
  );

  let unauthorizedCalls = 0;
  const unauthorizedWriter = createCampaignPremiseWriter(async () => {
    unauthorizedCalls += 1;
    return success("Forbidden premise.");
  }, 0);
  await assert.rejects(
    unauthorizedWriter({
      envelope: envelope("player", "player"), premise: "Forbidden premise.", idempotencyKey: "forbidden-key",
    }),
    (error) => error instanceof CampaignPremiseWriteError && error.category === "authorization",
  );
  assert.equal(unauthorizedCalls, 0);
});
