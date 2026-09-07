import assert from "node:assert/strict";
import test from "node:test";

import { readDeferredCampaignDetails, readDeferredHubSection } from "../src/server/game-server-context.js";

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

test("campaign loader retains all 101 chapter records", async () => {
  const entities = Array.from({ length: 101 }, (_, index) => ({
    entityId: `${CAMPAIGN_ID}.chapter.${String(index + 1).padStart(3, "0")}`,
    name: `Chapter ${index + 1}`,
    createdAtUtc: `2026-01-01T00:${String(index % 60).padStart(2, "0")}:00Z`,
  }));
  const value = await readDeferredCampaignDetails({
    origin: ORIGIN,
    source: SOURCE,
    fetchImpl: dmFixture({ entities }),
  });

  assert.equal(value.chapters.length, 101);
  assert.deepEqual(
    new Set(value.chapters.map((entry) => entry.id)),
    new Set(entities.map((entry) => entry.entityId)),
  );
});
