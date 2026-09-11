import assert from "node:assert/strict";
import test from "node:test";
import { readDeferredHubSection } from "../src/server/game-server-context.js";
import { contract as worldCampaignDirectoryContract } from "../src/server/world-campaign-directory-contract.js";
import { contract as worldLocationScopeContract } from "../src/server/world-location-scope-contract.js";
import { contract as worldLocationScopePageContract } from "../src/server/world-location-scope-page-contract.js";
import { contract as worldPeopleHoldingsPageContract } from "../src/server/world-people-holdings-page-contract.js";
import { contract as currentSceneContract } from "../src/server/current-scene-contract.js";
import { contract as campaignResumeContract } from "../src/server/campaign-resume-contract.js";

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
const historyEnvelope = (data, overrides = {}) => ({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
  qualifiedQueryId: "dnd2024.query.world-chronology", stateSpaceFingerprint: "1".repeat(64),
  resolutionFingerprint: "2".repeat(64), outputSchemaHash: "3".repeat(64),
  resultFingerprint: "4".repeat(64), sourceRevisionFingerprint: "5".repeat(64), data, ...overrides });
const historyResponse = (data, status = 200, overrides = {}) => response(historyEnvelope({
  coverage: "complete", fieldCoverage: data?.coverage ?? "complete", sourceRevision: "6".repeat(64),
  ...data,
}, overrides), status);
const partyResponse = (data) => response({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
  qualifiedQueryId: "dnd2024.query.party-knowledge", stateSpaceFingerprint: "1".repeat(64),
  resolutionFingerprint: "2".repeat(64), outputSchemaHash: "3".repeat(64),
  resultFingerprint: "4".repeat(64), sourceRevisionFingerprint: "5".repeat(64),
  data: { audience: "party", campaignId: source.campaign.id, worldId: source.contextSelection.selectedWorldId,
    sourceRevision: "6".repeat(64), coverage: "complete", fieldCoverage: "complete",
    ...data } });

test("T12 Current refuses a source binding replaced since bootstrap before loading optional scene data", async () => {
  let reads = 0;
  await assert.rejects(readDeferredHubSection({
    origin, section: "current", source: { ...source,
      campaign: { ...source.campaign, projection: { resolutionFingerprint: "1".repeat(64) } } },
    fetchImpl: async () => {
      reads += 1;
      return response({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
        qualifiedQueryId: campaignResumeContract.id, stateSpaceFingerprint: "1".repeat(64),
        resolutionFingerprint: "2".repeat(64), outputSchemaHash: campaignResumeContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: { campaign: { id: source.campaign.id }, scene: null } });
    },
  }), /source binding changed/u);
  assert.equal(reads, 1, "a replaced binding cannot read more optional entity or media data");
});

for (const perspective of ["dm", "player"]) {
  test(`deferred history follows source-fenced pages for all 251 authorized entries (${perspective})`, async () => {
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
        const supplied = target.searchParams.get("input");
        const pageInput = supplied === null ? {} : JSON.parse(supplied);
        const offset = pageInput.cursor === undefined ? 0 : Number(pageInput.cursor);
        if (offset === 0) assert.equal(supplied, null, "the first chronology page has no continuation input");
        if (offset > 0) assert.deepEqual(pageInput, {
          cursor: String(offset),
          expectedSourceRevision: "5".repeat(64),
          expectedGraphSourceRevision: "6".repeat(64),
        });
        const pageEntries = entries.slice(offset, offset + 40);
        const nextCursor = offset + pageEntries.length < entries.length ? String(offset + pageEntries.length) : undefined;
        return historyResponse({ status: "ready", perspective, entries: pageEntries,
          coverage: nextCursor ? "partial" : "complete", fieldCoverage: "complete", nextCursor });
      },
    });
    assert.deepEqual(result.chronology.entries, entries);
    assert.equal(result.chronology.coverage, "complete");
    assert.equal(calls.length, 7);
    assert.ok(calls[0].pathname.endsWith("/entities/campaign.caldris.fixture/read-models/dnd2024.query.world-chronology"));
    assert.equal(calls[0].searchParams.has("campaignId"), false);
    assert.equal(result.chronology.projection.qualifiedQueryId, "dnd2024.query.world-chronology");
  });
}

test("deferred empty, unavailable and malformed history cannot be confused", async () => {
  const load = (value, status = 200) => readDeferredHubSection({
    origin, source, section: "history", fetchImpl: async () => historyResponse(value, status),
  });
  assert.equal((await load({ status: "empty", perspective: "dm", entries: [] })).chronology.status, "empty");
  await assert.rejects(load({}, 500), /unavailable/);
  await assert.rejects(load({ status: "ready", perspective: "player", entries: [] }), /unavailable/);
});

test("deferred history rejects a server page above the 40-entry transport boundary", async () => {
  const entries = Array.from({ length: 41 }, (_, i) => ({
    id: `history.${i}`, occurredAtMinute: i, dateLabel: `Day ${i}`,
    precision: "exact", title: `Event ${i}`, summary: "An event.", subjects: [],
  }));
  const load = (items) => readDeferredHubSection({
    origin, source, section: "history",
    fetchImpl: async () => historyResponse({ status: "ready", perspective: "dm", entries: items }),
  });
  await assert.rejects(load(entries), /unavailable/u);
});

test("deferred history carries exact source evidence and rejects a changed continuation", async () => {
  const calls = [];
  await assert.rejects(readDeferredHubSection({ origin, source, section: "history",
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (calls.length === 1) return historyResponse({ status: "ready", perspective: "dm", coverage: "partial",
        fieldCoverage: "complete", nextCursor: "1", entries: [{ id: "history.0", title: "First" }] });
      const pageInput = JSON.parse(target.searchParams.get("input"));
      assert.deepEqual(pageInput, {
        cursor: "1",
        expectedSourceRevision: "5".repeat(64),
        expectedGraphSourceRevision: "6".repeat(64),
      });
      return historyResponse({}, 409);
    },
  }), /unavailable/u);
  assert.equal(calls.length, 2);
});

test("deferred history rejects duplicate rows and graph-source drift across pages", async () => {
  for (const drift of ["duplicate", "graph-source"]) {
    let calls = 0;
    await assert.rejects(readDeferredHubSection({ origin, source, section: "history",
      fetchImpl: async () => {
        calls += 1;
        if (calls === 1) return historyResponse({ status: "ready", perspective: "dm", coverage: "partial",
          fieldCoverage: "complete", nextCursor: "1", entries: [{ id: "history.same", title: "First" }] });
        return historyResponse({ status: "ready", perspective: "dm", entries: [
          { id: drift === "duplicate" ? "history.same" : "history.second", title: "Second" },
        ], sourceRevision: drift === "graph-source" ? "7".repeat(64) : "6".repeat(64) });
      },
    }), /unavailable/u);
    assert.equal(calls, 2);
  }
});

test("preview people fail closed when the registered party query is unavailable", async () => {
  for (const section of ["people"]) {
    const calls = [];
    await assert.rejects(readDeferredHubSection({
      origin, section, source: { ...source, locationDirectory: [], audience: { seat: "dm", perspective: "player" } },
      fetchImpl: async (input) => { calls.push(input); throw new Error("Private read"); },
    }), /Private read/);
    assert.equal(calls.length, 1);
    assert.ok(new URL(calls[0]).pathname.endsWith("/read-models/dnd2024.query.party-knowledge"));
  }
});

for (const seat of ["player", "dm"]) test(`${seat} Player lore uses the registered party union without a private notebook or media call`, async () => {
  const entries = [{ recognitionKey: "recognition.fixture", text: "A familiar rumour.", stance: "familiar",
    presentationKind: "recognition", admissions: [{ actorId: "actor.fixture", actorName: "Fixture", stance: "familiar", source: "explicit" }] }];
  const result = await readDeferredHubSection({
    origin, section: "lore", source: { ...source, audience: { seat, perspective: "player" } },
    fetchImpl: async (input) => {
      const target = new URL(input);
      assert.ok(target.pathname.endsWith("/read-models/dnd2024.query.party-knowledge"));
      assert.equal(target.searchParams.get("perspective"), "player");
      return partyResponse({ status: "ready", entries, locations: [] });
    },
  });
  assert.deepEqual(result.knowledge.entries, entries);
  assert.equal(result.knowledge.audience, "party");
});

test("deferred locations read the complete authorized hierarchy and suppress ambient media in preview", async () => {
  const calls = [];
  const locations = Array.from({ length: 2 }, (_, index) => ({
    id: `place-${index}`, name: `Place ${index}`, parentId: "world.caldris", slot: "region",
    kind: "region", status: index % 2 ? "draft" : "active", summary: "A place.",
    visibility: "public", mapAnchor: null,
  }));
  const result = await readDeferredHubSection({
    origin, section: "locations", source: { ...source, audience: { seat: "dm", perspective: "player" } },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      const scopeId = decodeURIComponent(target.pathname.split("/entities/")[1].split("/read-models/")[0]);
      const root = scopeId === "world.caldris";
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture",
        qualifiedQueryId: worldLocationScopePageContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldLocationScopePageContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, state: "ready",
          scope: root
            ? { id: "world.caldris", name: "Caldris", parentId: null, slot: "", kind: "world",
              status: "active", summary: "A gentle world.", visibility: "public", mapAnchor: null }
            : locations.find((location) => location.id === scopeId),
          locations: root ? locations : [], totalCount: root ? locations.length : 0,
          complete: true, nextCursor: null,
        },
      });
    },
  });
  assert.equal(result.locationDirectory.length, 3);
  assert.deepEqual(result.locationScopes.find(({ id }) => id === "world.caldris"), {
    id: "world.caldris", name: "Caldris", parentId: null,
    childIds: locations.map((location) => location.id), totalCount: 2,
    complete: true, nextCursor: null, sourceRevisionFingerprint: "4".repeat(64),
  });
  assert.ok(locations.every((location) =>
    result.locationScopes.find(({ id }) => id === location.id)?.childIds.length === 0));
  assert.equal(calls.length, 3);
  assert.match(calls[0].pathname, /entities\/world\.caldris\/read-models\/dnd2024\.query\.world-location-scope-page$/u);
  assert.ok(calls.every((target) => !target.pathname.endsWith("/entities") && !target.pathname.endsWith("/media")));
});

test("failed or incompatible root scopes never produce empty location success", async () => {
  for (const value of [response({}, 500), response({ unexpected: true })]) {
    await assert.rejects(readDeferredHubSection({
      origin, source, section: "locations", fetchImpl: async () => value,
    }), /scope/);
  }
});

for (const mediaCase of [
  { name: "DM illustration", seat: "dm", perspective: "dm", visible: true },
  { name: "DM seat fallback", seat: "dm", perspective: "dm", visible: true, omittedPerspective: true },
  { name: "Actor illustration", seat: "player", perspective: "player", visible: true },
  { name: "Player preview", seat: "dm", perspective: "player", preview: true },
  { name: "missing illustration", seat: "dm", perspective: "dm", empty: true },
  { name: "denied illustration", seat: "dm", perspective: "dm", denied: true },
  { name: "wrong media owner", seat: "dm", perspective: "dm", foreign: true },
]) test(`Current cross-checks its scene and resolves exact location media: ${mediaCase.name}`, async () => {
  const calls = [];
  const imageUrl = "/api/applications/dnd2024/state-spaces/state.fixture/entities/location.caldris.one/media/setting/content";
  const result = await readDeferredHubSection({
    origin, section: "current",
    source: { ...source, knowledge: { status: "empty", entries: [], locations: [] },
      audience: { seat: mediaCase.seat, ...(!mediaCase.omittedPerspective ? { perspective: mediaCase.perspective } : {}) },
      locationDirectory: [{ id: "location.caldris.one", name: "Place",
        ...(!mediaCase.visible ? { media: { setting: { imageUrl: "/stale-private-image.png" } } } : {}),
      }] },
    fetchImpl: async (input, init) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/media-batch")) {
        assert.deepEqual(JSON.parse(init.body), {
          entityIds: ["location.caldris.one"], perspective: mediaCase.perspective,
        });
        if (mediaCase.denied) return response({}, 403);
        return response({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
          items: [{ entityId: mediaCase.foreign ? "another-location" : "location.caldris.one",
            attachments: mediaCase.empty ? [] : [{ mediaId: "setting", role: "setting",
              mediaType: "image/png", width: 800, height: 600, alt: "The current place",
              caption: "", order: 0, contentUrl: imageUrl }],
          }],
        });
      }
      const resume = target.pathname.endsWith(campaignResumeContract.id);
      const contract = resume ? campaignResumeContract : currentSceneContract;
      const data = resume ? {
        version: 1,
        campaign: { id: source.campaign.id, name: "Fixture", title: "Fixture",
          premise: "Continue the fixture.", partyGoals: ["Continue."], toneAndBoundaries: ["Keep it safe."] },
        party: { activeMemberCount: 1 },
        scene: { locationId: "location.caldris.one", conversationId: null, encounterId: null },
        activeArc: null, activeChapter: null, activeSession: null, latestRecap: null,
        affordances: [{ key: "look-around", label: "Look around", summary: "Survey the area." }],
      } : {
        version: 1, kind: "exploration",
        location: { id: "location.caldris.one", kind: "site", summary: "A place.", visibility: "party" },
        conversationId: null, encounterId: null,
        affordances: [{ key: "look-around", label: "Look around", summary: "Survey the area." }],
      };
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture", qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data,
      });
    },
  });
  assert.equal(result.currentSituation.status, "ready");
  assert.equal(result.currentSituation.locationId, "location.caldris.one");
  assert.deepEqual(result.currentSituation.affordances, [
    { key: "look-around", label: "Look around", summary: "Survey the area." },
  ]);
  assert.equal(calls.length, 2 + (mediaCase.preview ? 0 : 1) + (mediaCase.perspective === "dm" ? 1 : 0));
  assert.equal(result.locationDirectory[0].media?.setting?.imageUrl, mediaCase.visible ? imageUrl : undefined);
  assert.equal(calls.filter((target) => target.pathname.endsWith("/media-batch")).length, mediaCase.preview ? 0 : 1);
  assert.equal(calls.filter((target) => target.pathname.includes("/read-models/")).length, 2);
  assert.ok(calls.filter((target) => target.pathname.includes("/read-models/"))
    .every((target) => target.searchParams.get("perspective") === mediaCase.perspective));
  const routeLookup = calls.find((target) => target.pathname.endsWith("/relationships"));
  if (mediaCase.perspective === "dm") {
    assert.equal(routeLookup.searchParams.get("toEntityId"), "location.caldris.one");
    assert.equal(routeLookup.searchParams.get("qualifiedKind"), "game.core.world.route.from");
  } else assert.equal(routeLookup, undefined);
  assert.ok(calls.every((target) =>
    !target.pathname.includes("/components/") && !target.pathname.endsWith("/containment")));
});

test("history keeps readable fields and peers while reporting incomplete rows and ignoring shape versions", async () => {
  const result = await readDeferredHubSection({ origin, source, section: "history",
    fetchImpl: async () => historyResponse({ status: "ready", perspective: "dm", version: 999, extra: "ignored", entries: [
      { id: "history.good", title: "Readable event", occurredAtMinute: "not a date", dateLabel: null,
        precision: null, summary: { changed: true }, subjects: [null, { id: "place.good", name: "A place", extra: true }] },
      { id: "history.ambiguous", title: "First" }, { id: "history.ambiguous", title: "Second" }, null,
    ] }),
  });
  assert.equal(result.chronology.status, "ready");
  assert.equal(result.chronology.coverage, "partial");
  assert.equal(result.chronology.entries.length, 1);
  const event = result.chronology.entries[0];
  assert.equal(event.title, "Readable event");
  assert.equal(event.occurredAtMinute, null, "unknown dates are not zero");
  assert.equal(event.summary, "Description unavailable.");
  assert.deepEqual(event.subjects, [{ id: "place.good", name: "A place" }]);
  assert.ok(event.unavailableFields.includes("subjects"));
});

test("Player history never forwards added DM subject or secret fields", async () => {
  const result = await readDeferredHubSection({ origin, source: { ...source, audience: { seat: "dm", perspective: "player" } },
    section: "history", fetchImpl: async () => historyResponse({ status: "ready", perspective: "player", entries: [
      { id: "history.party", occurredAtMinute: 0, dateLabel: "Today", precision: "exact", title: "Known event",
        summary: "Known summary", subjects: [{ id: "hidden-person", name: "Hidden person" }], dmTruth: "Hidden truth" },
    ] }),
  });
  assert.equal(result.chronology.entries[0].occurredAtMinute, 0, "a real zero remains zero");
  assert.equal(JSON.stringify(result).includes("Hidden"), false);
  assert.equal(JSON.stringify(result).includes("hidden-person"), false);
});

test("registered history rejects foreign scope, missing evidence and replaced source but accepts new shape hashes", async () => {
  const data = { status: "empty", perspective: "dm", entries: [], coverage: "partial",
    fieldCoverage: "partial", sourceRevision: "6".repeat(64) };
  const load = (value, currentSource = source) => readDeferredHubSection({ origin, source: currentSource,
    section: "history", fetchImpl: async () => response(value) });
  for (const value of [data, historyEnvelope(data, { stateSpaceId: "foreign" }),
    historyEnvelope(data, { applicationId: "foreign" }), historyEnvelope(data, { qualifiedQueryId: "foreign" }),
    historyEnvelope(data, { sourceRevisionFingerprint: null })]) {
    await assert.rejects(load(value), /unavailable/u);
  }
  await assert.rejects(load(historyEnvelope(data), { ...source, campaign: { ...source.campaign,
    projection: { resolutionFingerprint: "9".repeat(64) } } }), /unavailable/u);
  const result = await load(historyEnvelope(data, { outputSchemaHash: "F".repeat(64) }));
  assert.equal(result.chronology.status, "empty");
  assert.equal(result.chronology.coverage, "partial");
  assert.equal(result.chronology.projection.outputSchemaHash, "F".repeat(64));
});

test("Current keeps its bound scene when optional route relationships fail", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "current",
    source: { ...source, knowledge: { status: "empty", entries: [], locations: [] },
      audience: { seat: "dm", perspective: "dm" },
      locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/relationships")) return response({ error: "temporarily unavailable" }, 503);
      const resume = target.pathname.endsWith(campaignResumeContract.id);
      const contract = resume ? campaignResumeContract : currentSceneContract;
      const data = resume ? {
        version: 1,
        campaign: { id: source.campaign.id, name: "Fixture", title: "Fixture",
          premise: "Continue the fixture.", partyGoals: ["Continue."], toneAndBoundaries: ["Keep it safe."] },
        party: { activeMemberCount: 1 },
        scene: { locationId: "location.caldris.one", conversationId: null, encounterId: null },
        affordances: [],
      } : {
        version: 1, kind: "exploration",
        location: { id: "location.caldris.one", kind: "site", summary: "A place.", visibility: "party" },
        conversationId: null, encounterId: null, affordances: [],
      };
      return response({
        applicationId: source.applicationId, stateSpaceId: source.stateSpaceId, qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data,
      });
    },
  });
  assert.equal(result.currentSituation.status, "ready");
  assert.equal(result.currentSituation.locationId, "location.caldris.one");
  assert.equal(result.currentPlayProjection.resume.qualifiedQueryId, campaignResumeContract.id);
  assert.equal(result.currentPlayProjection.scene.qualifiedQueryId, currentSceneContract.id);
  assert.equal(result.currentPlayProjection.scene.sourceRevisionFingerprint, "4".repeat(64));
  assert.equal(result.currentSituation.routesCoverage, "unavailable");
  assert.deepEqual(result.knownRoutes, []);
  assert.equal(calls.filter((target) => target.pathname.endsWith("/relationships")).length, 1);
});

test("Current renders its bound scene before unavailable party knowledge and never requests the deferred union", async () => {
  const calls = [];
  const imageUrl = "/api/applications/dnd2024/state-spaces/state.fixture/entities/location.caldris.one/media/setting/content";
  const result = await readDeferredHubSection({
    origin, section: "current",
    source: { ...source, audience: { seat: "dm", perspective: "dm" },
      locationDirectory: [{ id: "location.caldris.one", name: "Place" }],
      knowledge: { status: "unavailable", entries: [], locations: [] } },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/read-models/dnd2024.query.party-knowledge"))
        throw new Error("party knowledge is intentionally unavailable");
      if (target.pathname.endsWith("/relationships"))
        throw new Error("route discovery must wait for the deferred knowledge owner");
      if (target.pathname.endsWith("/media-batch")) {
        return response({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
          items: [{ entityId: "location.caldris.one", attachments: [{ mediaId: "setting", role: "setting",
            mediaType: "image/png", width: 800, height: 600, alt: "The current place", caption: "", order: 0,
            contentUrl: imageUrl }] }] });
      }
      const resume = target.pathname.endsWith(campaignResumeContract.id);
      const contract = resume ? campaignResumeContract : currentSceneContract;
      const data = resume ? {
        version: 1,
        campaign: { id: source.campaign.id, name: "Fixture", title: "Fixture",
          premise: "Continue the fixture.", partyGoals: ["Continue."], toneAndBoundaries: ["Keep it safe."] },
        party: { activeMemberCount: 1 },
        scene: { locationId: "location.caldris.one", conversationId: null, encounterId: null },
        activeArc: null, activeChapter: null, activeSession: null, latestRecap: null,
        affordances: [{ key: "look-around", label: "Look around", summary: "Survey the area." }],
      } : {
        version: 1, kind: "exploration",
        location: { id: "location.caldris.one", kind: "site", summary: "A place.", visibility: "party" },
        conversationId: null, encounterId: null,
        affordances: [{ key: "look-around", label: "Look around", summary: "Survey the area." }],
      };
      return response({
        applicationId: source.applicationId, stateSpaceId: source.stateSpaceId, qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data,
      });
    },
  });
  assert.equal(result.currentSituation.status, "ready");
  assert.equal(result.currentSituation.kind, "exploration");
  assert.equal(result.currentSituation.locationId, "location.caldris.one");
  assert.equal(result.currentSituation.routesCoverage, "unavailable");
  assert.equal(result.locationDirectory[0].media.setting.imageUrl, imageUrl);
  assert.deepEqual(result.knownRoutes, []);
  assert.equal(calls.filter((target) => target.pathname.endsWith("/read-models/dnd2024.query.party-knowledge")).length, 0);
  assert.equal(calls.filter((target) => target.pathname.endsWith("/relationships")).length, 0);
  assert.equal(JSON.stringify(result).includes("party knowledge is intentionally unavailable"), false);
});

test("Current request budget remains hard when concurrent route failures follow it", async () => {
  const routeIds = Array.from({ length: 650 }, (_, index) => `route.fixture.${index}`);
  let fetchCalls = 0;
  await assert.rejects(readDeferredHubSection({
    origin, section: "current",
    source: { ...source, knowledge: { status: "empty", entries: [], locations: [] },
      locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input) => {
      fetchCalls += 1;
      const target = new URL(input);
      if (target.pathname.endsWith("/media-batch")) {
        return response({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
          items: [{ entityId: "location.caldris.one", attachments: [] }] });
      }
      if (target.pathname.endsWith("/relationships")) {
        const kind = target.searchParams.get("qualifiedKind");
        const fromEntityId = target.searchParams.get("fromEntityId");
        if (kind === "game.core.world.route.from") {
          const page = Number(target.searchParams.get("cursor") ?? "0");
          const start = page * 100;
          const items = routeIds.slice(start, start + 100).map((routeId) => ({
            fromEntityId: routeId, toEntityId: "location.caldris.one", qualifiedKind: kind,
          }));
          return response({ items, nextCursor: start + items.length < routeIds.length ? String(page + 1) : null });
        }
        const routeId = fromEntityId;
        const toEntityId = kind === "game.core.world.route.in-world"
          ? "world.caldris" : "location.caldris.one";
        return response({ items: [{ fromEntityId: routeId, toEntityId, qualifiedKind: kind }], nextCursor: null });
      }
      const componentMarker = "/components/";
      const componentIndex = target.pathname.indexOf(componentMarker);
      if (componentIndex >= 0) {
        const entityId = decodeURIComponent(target.pathname.slice(0, componentIndex)
          .split("/entities/")[1]);
        const componentTypeId = decodeURIComponent(target.pathname.slice(componentIndex + componentMarker.length));
        const value = componentTypeId === "game.core.world.route.availability"
          ? { status: "open" }
          : componentTypeId === "game.core.world.route"
            ? { status: "active", visibility: "public", mode: "on-foot", durationMinutes: 1 }
            : { kind: "site", status: "active", visibility: "public" };
        return response({ entityId, qualifiedTypeId: componentTypeId, valueJson: JSON.stringify(value) });
      }
      const resume = target.pathname.endsWith(campaignResumeContract.id);
      const contract = resume ? campaignResumeContract : currentSceneContract;
      const data = resume ? {
        version: 1,
        campaign: { id: source.campaign.id, name: "Fixture", title: "Fixture",
          premise: "Continue the fixture.", partyGoals: ["Continue."], toneAndBoundaries: ["Keep it safe."] },
        party: { activeMemberCount: 1 },
        scene: { locationId: "location.caldris.one", conversationId: null, encounterId: null },
        affordances: [],
      } : {
        version: 1, kind: "exploration",
        location: { id: "location.caldris.one", kind: "site", summary: "A place.", visibility: "party" },
        conversationId: null, encounterId: null, affordances: [],
      };
      return response({
        applicationId: source.applicationId, stateSpaceId: source.stateSpaceId, qualifiedQueryId: contract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: contract.outputSchemaHash, resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64), data,
      });
    },
  }), /bounded read budget/u);
  assert.ok(fetchCalls >= 2_000);
});

test("context discovery follows every registered continuation without hydrating entities", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "context", source: { ...source, world: { id: "world.caldris", name: "Caldris" } },
    fetchImpl: async (input) => {
      const target = new URL(input); calls.push(target);
      assert.match(target.pathname, /dnd2024\.query\.world-campaign-directory/u);
      assert.deepEqual(JSON.parse(target.searchParams.get("input")), { selectionId: source.campaign.id });
      assert.equal(target.searchParams.has("campaignId"), false,
        "the registered input binds the selected root without an application-aware query parameter");
      const second = target.searchParams.has("cursor");
      const campaign = {
        id: second ? "campaign.caldris.other" : source.campaign.id,
        name: second ? "Other campaign" : "Fixture",
        status: "active", title: second ? "Other campaign" : "Fixture",
        premise: "A bounded campaign.", partyGoals: ["Continue."],
        toneAndBoundaries: ["Keep it safe."], rulesetScope: "dnd2024",
        creationMethod: "manual", reviewFingerprint: "a".repeat(64),
      };
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture",
        qualifiedQueryId: worldCampaignDirectoryContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldCampaignDirectoryContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          worldSummary: "A fixture world.",
          campaigns: [campaign], totalCount: 2, complete: second, nextCursor: second ? null : "next",
        },
      });
    },
  });
  assert.equal(result.contextSelection.worlds.length, 1);
  assert.equal(result.contextSelection.worlds[0].campaigns.length, 2);
  assert.equal(result.contextSelection.selectedCampaignId, source.campaign.id);
  assert.equal(calls.length, 2);
  assert.ok(calls.every((target) => !target.pathname.endsWith("/entities") &&
    !target.pathname.includes("/components/")));
});

test("DM people and holdings use one source-bound page and one authorized media batch", async () => {
  const calls = [];
  const result = await readDeferredHubSection({
    origin, section: "people", source: { ...source, locationDirectory: [{ id: "location.caldris.one", name: "Place" }] },
    fetchImpl: async (input, init = {}) => {
      const target = new URL(input); calls.push(target);
      if (target.pathname.endsWith("/media-batch")) {
        assert.deepEqual(JSON.parse(init.body), { entityIds: ["subject-9"], perspective: "dm" });
        return response({ applicationId: "dnd2024", stateSpaceId: "state.fixture", items: [] });
      }
      return response({
        applicationId: "dnd2024", stateSpaceId: "state.fixture",
        qualifiedQueryId: worldPeopleHoldingsPageContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: worldPeopleHoldingsPageContract.outputSchemaHash,
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, state: "ready", world: { id: "world.caldris", name: "Caldris" },
          locations: [{ id: "place-azure", name: "Azure Reach", parentId: "world.caldris",
            kind: "region" }],
          people: [{ id: "subject-9", name: "Person", locationId: "place-azure", kind: "NPC",
            motive: null }],
          holdings: [{ id: "plain-identity", name: "Rope", locationId: "place-azure", kind: "Item" }],
          totalCount: 2, complete: true, nextCursor: null,
          limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
        },
      });
    },
  });
  assert.deepEqual(result.worldDirectory.people.map((person) => person.id), ["subject-9"]);
  assert.deepEqual(result.worldDirectory.holdings.map((holding) => holding.id), ["plain-identity"]);
  assert.equal(calls.length, 2);
  assert.match(calls[0].pathname,
    /entities\/world\.caldris\/read-models\/dnd2024\.query\.world-people-holdings-page$/u);
  assert.ok(calls.every((target) => !/\/(entities|containments)$/u.test(target.pathname) &&
    !target.pathname.includes("/components/") && !target.pathname.includes("relationships") &&
    !target.pathname.endsWith("/media")));
});

test("Actor People reuse only shared knowledge without probing actor-specific media", async () => {
  const calls = [];
  const actorSource = { ...source, audience: { seat: "player", perspective: "player" } };
  const result = await readDeferredHubSection({
    origin, section: "people", source: actorSource,
    fetchImpl: async (input, init = {}) => {
      const target = new URL(input); calls.push(target);
      assert.ok(target.pathname.endsWith("/read-models/dnd2024.query.party-knowledge"));
      return partyResponse({
        status: "ready", entries: [{ text: "Mara runs the harbour.", stance: "known",
          knowledgeId: "knowledge.mara", documentRevision: "1", admissions: [],
          presentationKind: "person" }], locations: [],
      });
    },
  });
  assert.deepEqual(result.knowledge.entries, [{
    text: "Mara runs the harbour.", stance: "known", presentationKind: "person",
    knowledgeId: "knowledge.mara", documentRevision: "1", admissions: [],
  }]);
  assert.equal(calls.length, 1);
  assert.ok(calls.every((target) => !target.pathname.includes("world-people-holdings") &&
    !target.pathname.endsWith("/media")));
});

test("empty DM People is distinct from a denied directory", async () => {
  const envelope = (data) => ({
    applicationId: "dnd2024", stateSpaceId: "state.fixture",
    qualifiedQueryId: worldPeopleHoldingsPageContract.id,
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: worldPeopleHoldingsPageContract.outputSchemaHash,
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data,
  });
  const empty = await readDeferredHubSection({
    origin, section: "people", source,
    fetchImpl: async () => response(envelope({
      version: 1, state: "ready", world: { id: "world.caldris", name: "Caldris" },
      locations: [], people: [], holdings: [],
      totalCount: 0, complete: true, nextCursor: null,
      limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
    })),
  });
  assert.deepEqual(empty.worldDirectory.people, []);
  assert.deepEqual(empty.worldDirectory.holdings, []);

  await assert.rejects(readDeferredHubSection({
    origin, section: "people", source,
    fetchImpl: async () => response(envelope({
      version: 1, state: "forbidden", world: null, locations: [], people: [], holdings: [],
      totalCount: 0, complete: true, nextCursor: null,
      limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
    })),
  }), /unavailable to this audience/u);
});

test("T03 T04 Lore retains readable authorized entries when a neighboring entry or optional grouping is malformed", async () => {
  const result = await readDeferredHubSection({
    origin, section: "lore", source: { ...source, audience: { seat: "player", perspective: "player" } },
    fetchImpl: async () => partyResponse({
      status: "ready", added: { ignored: true }, entries: [
        { knowledgeId: "knowledge.bell", text: "The quay bell rings at noon.", stance: "known", presentationKind: false },
        { text: false, stance: "known", presentationKind: "statement" },
        { text: "Unsafe missing stance", presentationKind: "statement", mediaOwnerId: "do-not-fetch" },
        { recognitionKey: "recognition.tale", text: "A familiar tale.", stance: "familiar", presentationKind: "statement",
          subject: { id: "hidden", name: "Hidden identity" }, mediaOwnerId: "hidden" },
      ], locations: [{ name: "Harbour", entries: [
        { knowledgeId: "knowledge.market", text: "The market opens early.", stance: "known", presentationKind: "statement" }, null,
      ] }, { name: false, entries: [] }],
    }),
  });
  assert.equal(result.knowledge.status, "ready");
  assert.equal(result.knowledge.coverage, "partial");
  assert.deepEqual(result.knowledge.entries, [
    { knowledgeId: "knowledge.bell", text: "The quay bell rings at noon.", stance: "known", presentationKind: "unavailable", admissions: [] },
    { recognitionKey: "recognition.tale", text: "A familiar tale.", stance: "familiar", presentationKind: "recognition", admissions: [] },
  ]);
  assert.deepEqual(result.knowledge.locations, [{ name: "Harbour", entries: [
    { knowledgeId: "knowledge.market", text: "The market opens early.", stance: "known", presentationKind: "statement", admissions: [] },
  ] }]);
});

test("T05 an unusable Lore response is partial, never a confirmed empty notebook", async () => {
  const result = await readDeferredHubSection({
    origin, section: "lore", source: { ...source, audience: { seat: "player", perspective: "player" } },
    fetchImpl: async () => partyResponse({ status: "ready", entries: [null], locations: false }),
  });
  assert.equal(result.knowledge.status, "ready");
  assert.equal(result.knowledge.coverage, "partial");
  assert.deepEqual(result.knowledge.entries, []);
});

test("T01 T04 T06 context names survive unrelated malformed narrative while unsafe choices stay excluded", async () => {
  const row = (id, extra = {}) => ({ id, name: id, status: "active", rulesetScope: "dnd2024",
    creationMethod: "manual", reviewFingerprint: "a".repeat(64), ...extra });
  const result = await readDeferredHubSection({
    origin, section: "context", source,
    fetchImpl: async () => response({
      applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
      qualifiedQueryId: worldCampaignDirectoryContract.id,
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: "F".repeat(64), resultFingerprint: "3".repeat(64),
      sourceRevisionFingerprint: "4".repeat(64),
      data: { selectedWorld: { id: "world.caldris", name: false, added: true }, worldSummary: false,
        campaigns: [row(source.campaign.id, { name: "Useful name", title: false, premise: [], partyGoals: null }),
          row("campaign.unnamed", { name: null }), row("campaign.unreviewed", { reviewFingerprint: null }),
          row("campaign.duplicate"), row("campaign.duplicate")],
        totalCount: 5, complete: true, nextCursor: null, added: true },
    }),
  });
  assert.equal(result.contextSelection.coverage, "partial");
  assert.equal(result.contextSelection.worlds[0].name, "Name unavailable");
  assert.deepEqual(result.contextSelection.worlds[0].campaigns, [
    { id: source.campaign.id, name: "Useful name" }, { id: "campaign.unnamed", name: "Name unavailable" },
  ]);
});

test("T01 T02 T04 T06 People retains field-local records and reports excluded identities honestly", async () => {
  const mediaOwners = [];
  const result = await readDeferredHubSection({
    origin, section: "people", source,
    fetchImpl: async (input, init = {}) => {
      if (new URL(input).pathname.endsWith("/media-batch")) {
        mediaOwners.push(...JSON.parse(init.body).entityIds);
        return response({ applicationId: source.applicationId, stateSpaceId: source.stateSpaceId, items: [] });
      }
      return response({
        applicationId: source.applicationId, stateSpaceId: source.stateSpaceId,
        qualifiedQueryId: worldPeopleHoldingsPageContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: "F".repeat(64), resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 99, extension: { ignored: true }, state: "ready",
          world: { id: "world.caldris", name: false },
          locations: [
            { id: "place", name: "Harbour", kind: 2, parentId: "world.caldris" },
            { id: "cycle-a", name: "A", kind: "region", parentId: "cycle-b" },
            { id: "cycle-b", name: "B", kind: "region", parentId: "cycle-a" },
          ],
          people: [
            { id: "keeper", name: "Keeper", kind: "Unfamiliar category", locationId: "place",
              motive: { status: false, summary: "Keeps the lamps lit.", visibility: "dm" }, extra: true },
            { id: "unnamed", name: false, kind: null, locationId: "place", motive: null },
            { id: "duplicate", name: "First", kind: "NPC", locationId: "place" },
            { id: "duplicate", name: "Second", kind: "NPC", locationId: "place" },
            { id: "unrooted", name: "Unrooted", kind: "NPC", locationId: "cycle-a" },
            { id: "__proto__", name: "Unsafe", kind: "NPC", locationId: "place" },
            { name: "Missing identity", kind: "NPC", locationId: "place" },
          ],
          holdings: [{ id: "rope", name: "Rope", kind: false, locationId: "place" }],
          totalCount: 8, complete: true, nextCursor: null,
          limits: { contentsDepth: 16, recordCount: 2000, pageSize: 50, hierarchyComplete: true },
        },
      });
    },
  });
  assert.equal(result.worldDirectory.peopleCoverage, "partial");
  assert.equal(result.worldDirectory.peopleHierarchyComplete, true);
  assert.equal(result.worldDirectory.directoryRecordCount, 8, "raw count is not fabricated from admitted rows");
  assert.deepEqual(result.worldDirectory.people.map((row) => row.id), ["keeper", "unnamed"]);
  assert.deepEqual(mediaOwners, ["keeper", "unnamed"], "excluded identities never initiate media reads");
  assert.equal(result.worldDirectory.people[0].kind, "Unfamiliar category");
  assert.equal(result.worldDirectory.people[0].motive.summary, "Keeps the lamps lit.");
  assert.equal(result.worldDirectory.people[0].motive.status, "unavailable");
  assert.equal(result.worldDirectory.people[0].extra, undefined);
  assert.deepEqual(result.worldDirectory.people[1].unavailableFields, ["name", "kind"]);
  assert.equal(result.worldDirectory.holdings[0].name, "Rope");
  assert.equal(result.worldDirectory.holdings[0].kind, "Unknown");
  assert.deepEqual(result.locationDirectory.map((row) => row.id), ["place"]);
  assert.deepEqual(result.locationDirectory[0].unavailableFields, ["kind"]);
});
