import assert from "node:assert/strict";
import test from "node:test";

import { readDeferredCampaignDetails, readDeferredHubSection } from "../src/server/game-server-context.js";
import { contract as campaignDetailsContract } from "../src/server/campaign-details-contract.js";
import { contract as campaignLocationVisitsContract } from "../src/server/campaign-location-visits-contract.js";

const ORIGIN = "http://localhost:6217";
const CAMPAIGN_ID = "campaign.thalorien.brackenford";
const ENTITY_ROOT = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
const CONTAINMENT_ROOT = "/api/applications/dnd2024/state-spaces/dnd2024-main/containments";
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

function page(items, requested) {
  const offset = Number(requested.searchParams.get("cursor") ?? 0);
  const limit = Number(requested.searchParams.get("limit") ?? 100);
  return response(200, {
    items: items.slice(offset, offset + limit),
    nextCursor: offset + limit < items.length ? String(offset + limit) : null,
  });
}

function dmFixture({ entities, containments = () => [], failPage = null }) {
  return async (input) => {
    const requested = new URL(input);
    const { pathname } = requested;
    if (pathname === ENTITY_ROOT) return page(entities, requested);
    if (pathname === CONTAINMENT_ROOT) {
      if (failPage?.owner === "containment" && requested.searchParams.has("cursor")) return response(500, {});
      return page(containments(requested), requested);
    }
    const chapterMatch = pathname.match(/\/entities\/(.+)\/components\/game\.core\.campaign\.chapter$/u);
    if (chapterMatch) {
      const entityId = decodeURIComponent(chapterMatch[1]);
      return response(200, {
        entityId,
        qualifiedTypeId: "game.core.campaign.chapter",
        valueJson: JSON.stringify({
          status: "active",
          title: entityId.split(".").at(-1),
          partyQuestion: "Does the next page remain visible?",
        }),
      });
    }
    return response(404, {});
  };
}

function worldFixture({ failPage = null } = {}) {
  const locationId = "location.thalorien.pagination";
  const actorIds = Array.from({ length: 101 }, (_, index) => `actor.pagination.${index + 1}`);
  const entities = [
    { entityId: locationId, name: "Pagination Hall" },
    ...actorIds.map((entityId, index) => ({ entityId, name: `Actor ${index + 1}` })),
  ];
  const fetchImpl = dmFixture({
    entities,
    failPage,
    containments: (requested) => requested.searchParams.get("containerEntityId") === locationId
      ? actorIds.map((containedEntityId) => ({ containedEntityId, containerEntityId: locationId }))
      : [],
  });
  return { fetchImpl, actorIds };
}

test("deferred people loader retains all 101 containment records", async () => {
  const fixture = worldFixture();
  const value = await readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people", fetchImpl: fixture.fetchImpl,
  });

  assert.equal(value.worldDirectory.people.length, 101);
  assert.deepEqual(new Set(value.worldDirectory.people.map((entry) => entry.id)), new Set(fixture.actorIds));
});

test("deferred people rejects containment failure after a valid first page", async () => {
  const fixture = worldFixture({ failPage: { owner: "containment" } });
  await assert.rejects(readDeferredHubSection({
    origin: ORIGIN, source: SOURCE, section: "people", fetchImpl: fixture.fetchImpl,
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
