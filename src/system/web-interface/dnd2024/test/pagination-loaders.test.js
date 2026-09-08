import assert from "node:assert/strict";
import test from "node:test";

import { readDeferredCampaignDetails, readDeferredHubSection } from "../src/server/game-server-context.js";
import { contract as campaignDetailsContract } from "../src/server/campaign-details-contract.js";
import { contract as campaignLocationVisitsContract } from "../src/server/campaign-location-visits-contract.js";
import { contract as worldPeopleHoldingsContract } from "../src/server/world-people-holdings-contract.js";

const ORIGIN = "http://localhost:6217";
const CAMPAIGN_ID = "campaign.thalorien.brackenford";
const SOURCE = {
  applicationId: "dnd2024",
  stateSpaceId: "dnd2024-main",
  campaign: { id: CAMPAIGN_ID, name: "The Waystone at Brackenford" },
  actor: { id: "local-game-master", name: "Dungeon Master" },
  party: [],
  audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm", "player"] },
  contextSelection: { selectedWorldId: "world.thalorien", selectedCampaignId: CAMPAIGN_ID },
  knowledge: { status: "unavailable", entries: [], locations: [] },
};

function response(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function worldProjection(count) {
  const people = Array.from({ length: count }, (_, index) => ({
    id: `subject-${index + 1}`, name: `Person ${index + 1}`, locationId: "pagination-hall",
    kind: "NPC", motive: null,
  }));
  return {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    qualifiedQueryId: worldPeopleHoldingsContract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: worldPeopleHoldingsContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
    sourceRevisionFingerprint: "4".repeat(64),
    data: {
      version: 1, state: "ready", world: { id: "world.thalorien", name: "Thalorien" },
      locations: count < 200 ? [{ id: "pagination-hall", name: "Pagination Hall",
        parentId: "world.thalorien", kind: "region", status: "active", summary: "A hall." }] : [],
      people, holdings: [], limits: { contentsDepth: 4, recordCount: 200, complete: true },
    },
  };
}

test("deferred people loader retains the complete 200-record projection bound", async () => {
  const calls = [];
  const value = await readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people",
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/media-batch")) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [],
      });
      const projected = worldProjection(199);
      return response(200, projected);
    },
  });

  assert.equal(value.worldDirectory.people.length, 199);
  assert.equal(value.locationDirectory.length, 1);
  assert.equal(calls.length, 2);
  assert.ok(calls.every((target) => !/\/(entities|containments)$/u.test(target.pathname)));
});

test("deferred people rejects a projection beyond its complete record bound", async () => {
  await assert.rejects(readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people",
    fetchImpl: async () => response(200, worldProjection(201)),
  }), /incomplete/u);
});

test("campaign loader retains the bounded 100-record Campaign projection without entity fan-out", async () => {
  const chapters = Array.from({ length: 100 }, (_, index) => ({
    id: `${CAMPAIGN_ID}.chapter.${String(index + 1).padStart(3, "0")}`,
    name: `Chapter ${index + 1}`,
    status: "active",
    title: `Chapter ${index + 1}`,
    partyQuestion: "Does the next record remain visible?",
  }));
  const calls = [];
  const value = await readDeferredCampaignDetails({
    origin: ORIGIN,
    source: SOURCE,
    fetchImpl: async (input) => {
      const requested = new URL(input);
      calls.push(requested);
      const contract = requested.pathname.includes(campaignDetailsContract.id)
        ? campaignDetailsContract
        : campaignLocationVisitsContract;
      const data = contract === campaignDetailsContract
        ? { version: 1, campaignId: CAMPAIGN_ID, chapters, arcs: [], sessions: [] }
        : { campaignTitle: "The Waystone at Brackenford", visits: [], totalCount: 0,
          complete: true, nextCursor: null };
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data,
      });
    },
  });

  assert.equal(value.chapters.length, 100);
  assert.deepEqual(
    new Set(value.chapters.map((entry) => entry.id)),
    new Set(chapters.map((entry) => entry.id)),
  );
  assert.equal(calls.length, 2);
  assert.ok(calls.every((request) => request.pathname.includes("/read-models/") &&
    !request.pathname.includes("/components/")));
});

test("campaign visit pagination rejects a source revision change without returning partial data", async () => {
  const visits = Array.from({ length: 26 }, (_, index) => ({
    id: `visit.${index}`, name: `Visit ${index}`, firstVisitedMinute: index,
    lastVisitedMinute: index + 1, visitCount: 1, status: "departed",
    summary: "A visit.", memory: "The party remembers it.",
    locations: [{ id: `location.${index}`, name: `Location ${index}` }],
  }));
  await assert.rejects(readDeferredCampaignDetails({
    origin: ORIGIN,
    source: SOURCE,
    fetchImpl: async (input) => {
      const requested = new URL(input);
      const details = requested.pathname.includes(campaignDetailsContract.id);
      const second = requested.searchParams.has("cursor");
      const contract = details ? campaignDetailsContract : campaignLocationVisitsContract;
      const data = details
        ? { version: 1, campaignId: CAMPAIGN_ID, chapters: [], arcs: [], sessions: [] }
        : { campaignTitle: "The Waystone at Brackenford",
          visits: second ? visits.slice(25) : visits.slice(0, 25), totalCount: 26,
          complete: second, nextCursor: second ? null : "next" };
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: (second ? "B" : "A").repeat(64), data,
      });
    },
  }), /incomplete/u);
});
