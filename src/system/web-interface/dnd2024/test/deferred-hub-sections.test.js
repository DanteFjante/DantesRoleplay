import assert from "node:assert/strict";
import test from "node:test";
import { readDeferredHubSection } from "../src/server/game-server-context.js";

const origin = "http://localhost:6217";
const source = {
  applicationId: "dnd2024", stateSpaceId: "state.fixture",
  campaign: { id: "campaign.caldris.fixture", name: "Fixture" },
  actor: { id: "actor.fixture" }, party: [],
  audience: { seat: "dm", perspective: "dm" },
  contextSelection: { selectedWorldId: "world.caldris", selectedCampaignId: "campaign.caldris.fixture" },
  knowledge: { status: "unavailable", entries: [], locations: [] },
};
const response = (value, status = 200) => new Response(JSON.stringify(value), { status });
const component = (id, type, value) => response({
  entityId: id, qualifiedTypeId: type, valueJson: JSON.stringify(value),
});

for (const perspective of ["dm", "player"]) {
  test(`deferred history reads all 251 authorized entries with one request (${perspective})`, async () => {
    const entries = Array.from({ length: 251 }, (_, i) => ({
      id: `history.${i}`, occurredAtMinute: i, dateLabel: `Day ${i}`,
      precision: "exact", title: `Event ${i}`, summary: "An event.",
      ...(perspective === "dm" ? { subjects: [] } : {}),
    }));
    const calls = [];
    const result = await readDeferredHubSection({
      origin, source: { ...source, audience: { seat: "dm", perspective } }, section: "history",
      fetchImpl: async (input) => {
        const target = new URL(input); calls.push(target);
        assert.equal(target.searchParams.get("perspective"), perspective);
        return response({ status: "ready", perspective, entries });
      },
    });
    assert.deepEqual(result.chronology.entries, entries);
    assert.equal(calls.length, 1);
    assert.ok(calls[0].pathname.endsWith("/chronology"));
  });
}

test("deferred empty, unavailable and malformed history cannot be confused", async () => {
  const load = (value, status = 200) => readDeferredHubSection({
    origin, source, section: "history", fetchImpl: async () => response(value, status),
  });
  assert.equal((await load({ status: "empty", perspective: "dm", entries: [] })).chronology.status, "empty");
  await assert.rejects(load({}, 500), /unavailable/);
  await assert.rejects(load({ status: "ready", perspective: "player", entries: [] }), /unavailable/);
});

test("preview lore and people never request ambient DM knowledge or media", async () => {
  for (const section of ["lore", "people"]) {
    const calls = [];
    await assert.rejects(readDeferredHubSection({
      origin, section, source: { ...source, locationDirectory: [], audience: { seat: "dm", perspective: "player" } },
      fetchImpl: async (input) => { calls.push(input); throw new Error("Private read"); },
    }), /Actor binding/);
    assert.deepEqual(calls, []);
  }
});

test("Actor lore uses the authorized notebook and preserves all entries", async () => {
  const entries = [{ text: "A familiar rumour.", stance: "familiar", presentationKind: "lore" }];
  const result = await readDeferredHubSection({
    origin, section: "lore", source: { ...source, audience: { seat: "player", perspective: "player" } },
    fetchImpl: async (input) => {
      assert.ok(new URL(input).pathname.endsWith("/knowledge"));
      return response({ status: "ready", entries, locations: [] });
    },
  });
  assert.deepEqual(result.knowledge.entries, entries);
});

test("deferred locations traverse every page and suppress ambient media in preview", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "locations", source: { ...source, audience: { seat: "dm", perspective: "player" } },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/entities")) return response({
        items: [{ entityId: target.searchParams.has("cursor") ? "location.caldris.two" : "location.caldris.one",
          name: "Place" }], nextCursor: target.searchParams.has("cursor") ? null : "page.two",
      });
      if (target.pathname.endsWith("/components/game.core.world.location")) {
        const id = target.pathname.split("/").at(-3);
        return component(id, "game.core.world.location", { visibility: "public", kind: "village", summary: "A place." });
      }
      assert.ok(!target.pathname.endsWith("/media"));
      return response({}, 404);
    },
  });
  assert.deepEqual(result.locationDirectory.map((item) => item.id), ["location.caldris.one", "location.caldris.two"]);
  assert.equal(calls.filter((target) => target.pathname.endsWith("/entities")).length, 2);
});

test("failed directory continuations and first-page failures never produce empty success", async () => {
  for (const failFirst of [true, false]) {
    await assert.rejects(readDeferredHubSection({
      origin, source, section: "locations",
      fetchImpl: async (input) => response(failFirst || new URL(input).searchParams.has("cursor")
        ? {} : { items: [], nextCursor: "later" }, failFirst || new URL(input).searchParams.has("cursor") ? 500 : 200),
    }), /complete/);
  }
});

test("Current reuses loaded authorized locations and resolves the existing current-scene contract", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "current",
    source: { ...source, locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/components/game.core.campaign.current-scene"))
        return component(source.campaign.id, "game.core.campaign.current-scene",
          { location: { entityId: "location.caldris.one" } });
      return response({}, 404);
    },
  });
  assert.equal(result.currentSituation.status, "ready");
  assert.equal(result.currentSituation.locationId, "location.caldris.one");
  assert.ok(calls.every((target) => !target.pathname.endsWith("/entities")));
});

test("context discovery follows every continuation without hydrating characters or worlds", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "context", source,
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/entities")) {
        const second = target.searchParams.has("cursor");
        return response({ items: [{ entityId: second ? "campaign.other.fixture" : source.campaign.id,
          name: second ? "Other campaign" : "Fixture" }], nextCursor: second ? null : "next" });
      }
      assert.ok(target.pathname.endsWith("/components/game.core.campaign.root"));
      return component(target.pathname.split("/").at(-3), "game.core.campaign.root", { status: "active" });
    },
  });
  assert.equal(result.contextSelection.worlds.length, 2);
  assert.equal(result.contextSelection.selectedCampaignId, source.campaign.id);
  assert.equal(calls.length, 4);
});

test("people discovery reuses locations and does not read faction graphs", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "people", source: { ...source, locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/entities")) return response({
        items: [{ entityId: "actor.fixture", name: "Person" }], nextCursor: null,
      });
      if (target.pathname.endsWith("/containments")) return response({
        items: [{ containedEntityId: "actor.fixture", containerEntityId: "location.caldris.one" }], nextCursor: null,
      });
      return response({}, 404);
    },
  });
  assert.deepEqual(result.worldDirectory.people.map((person) => person.id), ["actor.fixture"]);
  assert.equal(calls.length, 4);
  assert.ok(calls.every((target) => !target.pathname.includes("relationships")));
});
