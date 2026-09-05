import assert from "node:assert/strict";
import test from "node:test";
import { loadInitialHub, requestedHubPreferences } from "../../src/data/hub-preferences";
import type { HubEnvelope } from "../../src/data/hub-types";

const storage = (mode: string | null, campaign: string | null = null) => ({
  getItem: (key: string) => key === "dnd2024-table-mode" ? mode : campaign,
});

test("startup requests the saved view once instead of loading Player followed by DM", async () => {
  const requests: unknown[] = [];
  const expected = { version: 1, status: "ready" } as HubEnvelope;
  const result = await loadInitialHub(async (...args) => { requests.push(args); return expected; },
    storage("dm", "campaign.test"));
  assert.deepEqual(requests, [["dm", "campaign.test"]]);
  assert.equal(result, expected);
});

test("blocked and invalid preferences default safely, and removed campaigns fall back to the bound campaign", async () => {
  assert.deepEqual(requestedHubPreferences({ getItem: () => { throw new Error("blocked"); } }),
    { perspective: "player", campaignId: undefined });
  assert.deepEqual(requestedHubPreferences(storage("invalid", "bad campaign")),
    { perspective: "player", campaignId: undefined });
  const requests: unknown[] = [];
  await loadInitialHub(async (...args) => {
    requests.push(args);
    return { version: 1, status: "denied", message: "Unavailable campaign" };
  }, storage("dm", "campaign.removed"));
  assert.deepEqual(requests, [["dm", "campaign.removed"], ["dm"]]);
});

test("startup does not retry a busy server", async () => {
  const expected = { version: 1, status: "unavailable", message: "Busy" } as const;
  let calls = 0;
  assert.equal(await loadInitialHub(async () => { calls += 1; return expected; }, storage("dm", "campaign.test")), expected);
  assert.equal(calls, 1);
});
