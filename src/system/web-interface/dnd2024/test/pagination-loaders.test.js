import assert from "node:assert/strict";
import test from "node:test";

import {
  readAllWorldPeopleHoldingsPages,
  readDeferredCampaignDetails,
  readDeferredHubSection,
} from "../src/server/game-server-context.js";
import { contract as campaignDetailsContract } from "../src/server/campaign-details-contract.js";
import { contract as campaignLocationVisitsContract } from "../src/server/campaign-location-visits-contract.js";
import { contract as worldPeopleHoldingsPageContract } from "../src/server/world-people-holdings-page-contract.js";
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

function worldProjection(count, offset = 0) {
  const allPeople = Array.from({ length: count }, (_, index) => ({
    id: `subject-${index + 1}`, name: `Person ${index + 1}`, locationId: "pagination-hall",
    kind: "NPC", motive: null,
  }));
  const people = allPeople.slice(offset, offset + 50);
  const nextOffset = offset + people.length;
  return {
    applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
    qualifiedQueryId: worldPeopleHoldingsPageContract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: worldPeopleHoldingsPageContract.outputSchemaHash, resultFingerprint: "3".repeat(64),
    sourceRevisionFingerprint: "4".repeat(64),
    data: {
      version: 1, state: "ready", world: { id: "world.thalorien", name: "Thalorien" },
      locations: people.length ? [{ id: "pagination-hall", name: "Pagination Hall",
        parentId: "world.thalorien", kind: "region" }] : [],
      people, holdings: [], totalCount: count, complete: nextOffset === count,
      nextCursor: nextOffset === count ? null : String(nextOffset),
      limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
    },
  };
}

test("deferred people loader follows every source-bound directory page", async () => {
  const calls = [];
  const value = await readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people",
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/media-batch")) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [],
      });
      const inputValue = JSON.parse(target.searchParams.get("input"));
      const projected = worldProjection(199, inputValue.offset);
      return response(200, projected);
    },
  });

  assert.equal(value.worldDirectory.people.length, 199);
  assert.equal(value.locationDirectory.length, 1);
  assert.equal(calls.length, 8);
  assert.ok(calls.every((target) => !/\/(entities|containments)$/u.test(target.pathname)));
});

test("deferred people rejects a projection beyond its complete 2,000-record bound", async () => {
  await assert.rejects(readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people",
    fetchImpl: async () => response(200, worldProjection(2_001)),
  }), /incomplete/u);
});

test("people and holdings retain all 1,000 plus 1,000 records across forty pages", async () => {
  const records = [
    ...Array.from({ length: 1_000 }, (_, index) => ({ category: "people", id: `person-${index}` })),
    ...Array.from({ length: 1_000 }, (_, index) => ({ category: "holdings", id: `holding-${index}` })),
  ];
  let calls = 0;
  const result = await readAllWorldPeopleHoldingsPages(async (cursor, revision) => {
    const offset = cursor === null ? 0 : Number(cursor);
    assert.equal(revision, offset === 0 ? null : "A".repeat(64));
    calls += 1;
    const page = records.slice(offset, offset + 50);
    return {
      status: "ready", world: { id: "world.thalorien", name: "Thalorien" },
      locations: [{ id: "pagination-hall", name: "Pagination Hall", kind: "region",
        summary: "Pagination Hall is part of Thalorien.", containerId: "world.thalorien" }],
      people: page.filter((item) => item.category === "people").map((item) => ({
        id: item.id, name: item.id, locationId: "pagination-hall", kind: "NPC",
      })),
      holdings: page.filter((item) => item.category === "holdings").map((item) => ({
        id: item.id, name: item.id, locationId: "pagination-hall", kind: "Item",
      })),
      totalCount: 2_000, complete: offset === 1_950,
      nextCursor: offset === 1_950 ? null : String(offset + 50), hierarchyComplete: true,
      sourceRevisionFingerprint: "A".repeat(64), projection: {},
    };
  });
  assert.equal(result.status, "ready");
  assert.equal(result.people.length, 1_000);
  assert.equal(result.holdings.length, 1_000);
  assert.equal(calls, 40);
});

test("people pagination rejects repeated cursors and mixed source revisions", async () => {
  const page = (nextCursor, sourceRevisionFingerprint) => ({
    status: "ready", world: { id: "world.thalorien", name: "Thalorien" }, locations: [],
    people: [{ id: `person-${nextCursor}`, name: "Person", locationId: "place", kind: "NPC" }],
    holdings: [], totalCount: 2, complete: false, nextCursor, hierarchyComplete: true,
    sourceRevisionFingerprint, projection: {},
  });
  const repeated = await readAllWorldPeopleHoldingsPages(async () => page("50", "A".repeat(64)));
  assert.equal(repeated.status, "error");
  let call = 0;
  const mixed = await readAllWorldPeopleHoldingsPages(async () =>
    page(call++ === 0 ? "50" : null, (call === 1 ? "A" : "B").repeat(64)));
  assert.equal(mixed.status, "stale");
});

test("people pagination never falls back to the legacy snapshot after continuation starts", async () => {
  let calls = 0;
  await assert.rejects(readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people",
    fetchImpl: async (input) => {
      const target = new URL(input);
      calls += 1;
      if (target.pathname.endsWith("/media-batch")) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [],
      });
      const offset = JSON.parse(target.searchParams.get("input")).offset;
      return offset === 0 ? response(200, worldProjection(51, 0)) : response(500, {});
    },
  }), /incomplete/u);
  assert.equal(calls, 3, "first page, its media batch, and failed continuation only");
});

test("people uses the fixed snapshot only when the paged contract is not registered yet", async () => {
  const calls = [];
  const value = await readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people",
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target.pathname);
      if (target.pathname.endsWith(`/${worldPeopleHoldingsPageContract.id}`)) return response(404, {});
      if (target.pathname.endsWith("/media-batch")) return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main", items: [],
      });
      return response(200, {
        applicationId: "dnd2024", stateSpaceId: "dnd2024-main",
        qualifiedQueryId: worldPeopleHoldingsContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldPeopleHoldingsContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, state: "ready", world: { id: "world.thalorien", name: "Thalorien" },
          locations: [{ id: "pagination-hall", name: "Pagination Hall", parentId: "world.thalorien",
            kind: "region", status: "active", summary: "A hall." }],
          people: [{ id: "legacy-person", name: "Legacy Person", locationId: "pagination-hall",
            kind: "NPC", motive: null }],
          holdings: [], limits: { contentsDepth: 4, recordCount: 200, complete: true },
        },
      });
    },
  });
  assert.deepEqual(value.worldDirectory.people.map((person) => person.id), ["legacy-person"]);
  assert.equal(calls.length, 3);
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
