import assert from "node:assert/strict";
import test from "node:test";

import { readGameServerContext } from "../src/server/game-server-context.js";

const ORIGIN = "http://localhost:6217";
const CAMPAIGN_ID = "campaign.thalorien.brackenford";
const ENTITY_ROOT = "/api/applications/dnd2024/state-spaces/dnd2024-main/entities";
const RELATIONSHIP_ROOT = "/api/applications/dnd2024/state-spaces/dnd2024-main/relationships";
const CONTAINMENT_ROOT = "/api/applications/dnd2024/state-spaces/dnd2024-main/containments";

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

function dmFixture({ entities, relationships = () => [], containments = () => [], failPage = null }) {
  return async (input) => {
    const requested = new URL(input);
    const { pathname } = requested;
    if (pathname === "/api/audience-context") {
      return response(200, {
        status: "bound",
        applicationId: "dnd2024",
        stateSpaceId: "dnd2024-main",
        campaignId: CAMPAIGN_ID,
        role: "game-master",
      });
    }
    if (pathname === `${ENTITY_ROOT}/${CAMPAIGN_ID}`) {
      return response(200, { entityId: CAMPAIGN_ID, name: "The Waystone at Brackenford" });
    }
    if (pathname === `${ENTITY_ROOT}/${CAMPAIGN_ID}/components/game.core.campaign.root`) {
      return response(200, {
        entityId: CAMPAIGN_ID,
        qualifiedTypeId: "game.core.campaign.root",
        valueJson: JSON.stringify({ status: "active", premise: "A bounded fixture.", partyGoals: [], toneAndBoundaries: [] }),
      });
    }
    if (pathname === ENTITY_ROOT) return page(entities, requested);
    if (pathname === RELATIONSHIP_ROOT) {
      if (failPage?.owner === "relationship" && requested.searchParams.has("cursor")) return response(500, {});
      return page(relationships(requested), requested);
    }
    if (pathname === CONTAINMENT_ROOT) {
      if (failPage?.owner === "containment" && requested.searchParams.has("cursor")) return response(500, {});
      return page(containments(requested), requested);
    }
    if (pathname.endsWith("/components/game.core.world.faction")) {
      const entityId = decodeURIComponent(pathname.split("/entities/")[1].split("/components/")[0]);
      return response(200, {
        entityId,
        qualifiedTypeId: "game.core.world.faction",
        valueJson: JSON.stringify({
          status: "active",
          visibility: "gm",
          summary: "A faction used to exercise pagination.",
          goals: ["Prove complete reads."],
          methods: ["Use advancing cursors."],
          assets: [],
          agenda: { state: "ready", summary: "Retain every relationship." },
        }),
      });
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
  const factionId = "faction.thalorien.pagination";
  const actorIds = Array.from({ length: 101 }, (_, index) => `actor.pagination.${index + 1}`);
  const memberIds = Array.from({ length: 101 }, (_, index) => `member.pagination.${index + 1}`);
  const entities = [
    { entityId: locationId, name: "Pagination Hall" },
    ...actorIds.map((entityId, index) => ({ entityId, name: `Actor ${index + 1}` })),
    { entityId: factionId, name: "The Cursor Keepers" },
  ];
  const fetchImpl = dmFixture({
    entities,
    failPage,
    containments: (requested) => requested.searchParams.get("containerEntityId") === locationId
      ? actorIds.map((containedEntityId) => ({ containedEntityId, containerEntityId: locationId }))
      : [],
    relationships: (requested) =>
      requested.searchParams.get("fromEntityId") === factionId &&
      requested.searchParams.get("qualifiedKind") === "game.core.world.faction.member"
        ? memberIds.map((toEntityId) => ({
            fromEntityId: factionId,
            toEntityId,
            qualifiedKind: "game.core.world.faction.member",
          }))
        : [],
  });
  return { fetchImpl, actorIds, memberIds };
}

test("world loaders retain all 101 containment and faction relationship records", async () => {
  const fixture = worldFixture();
  const value = await readGameServerContext({ serverOrigin: ORIGIN, fetchImpl: fixture.fetchImpl });

  assert.equal(value.status, "connected");
  assert.equal(value.worldDirectory.people.length, 101);
  assert.deepEqual(new Set(value.worldDirectory.people.map((entry) => entry.id)), new Set(fixture.actorIds));
  assert.deepEqual(value.worldDirectory.factions[0].memberIds, fixture.memberIds);
});

for (const owner of ["containment", "relationship"]) {
  test(`${owner} failure after a valid first page is explicit incompleteness`, async () => {
    const fixture = worldFixture({ failPage: { owner } });
    const value = await readGameServerContext({ serverOrigin: ORIGIN, fetchImpl: fixture.fetchImpl });

    assert.equal(value.status, "unavailable");
    assert.match(value.message, /world directory/i);
    assert.equal(value.worldDirectory, undefined);
  });
}

test("campaign loader retains all 101 chapter records", async () => {
  const entities = Array.from({ length: 101 }, (_, index) => ({
    entityId: `${CAMPAIGN_ID}.chapter.${String(index + 1).padStart(3, "0")}`,
    name: `Chapter ${index + 1}`,
    createdAtUtc: `2026-01-01T00:${String(index % 60).padStart(2, "0")}:00Z`,
  }));
  const value = await readGameServerContext({
    serverOrigin: ORIGIN,
    fetchImpl: dmFixture({ entities }),
  });

  assert.equal(value.status, "connected");
  assert.equal(value.campaign.chapters.length, 101);
  assert.deepEqual(
    new Set(value.campaign.chapters.map((entry) => entry.id)),
    new Set(entities.map((entry) => entry.entityId)),
  );
});
