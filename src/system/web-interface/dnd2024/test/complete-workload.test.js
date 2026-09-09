import assert from "node:assert/strict";
import test from "node:test";

import {
  completeReleaseEvidence,
  completeWorkloadEvidence,
  isClassifiedWorkloadInteraction,
  isReadOnlyWorkloadRequest,
  isWorkloadDataRead,
  recordSetEvidence,
  websiteAudienceProfiles,
  workloadChecks,
  workloadHarnessFingerprint,
  workloadViews,
} from "../scripts/complete-workload.mjs";

function evidence(audienceView = "shared-table") {
  const runtimeFingerprint = "a".repeat(64);
  const fixtureFingerprint = "b".repeat(64);
  const views = Object.fromEntries(workloadViews.map(view => [view, {
    status: "ready", complete: true, ...recordSetEvidence([view + ".record"]),
  }]));
  const scaling = { owner: "production-read-path", unrelatedPopulationMultiplier: 2, sampleCount: 20,
    baseSql: 8, doubledSql: 9, baseAllocatedBytes: 1000, doubledAllocatedBytes: 1050,
    baseMedianMs: 10, doubledMedianMs: 10.5 };
  const fixtureCase = { owner: "production-read-path", fixtureFingerprint, sampleCount: 20,
    medianMs: 10, allocatedBytes: 1000, sql: 8, responseBytes: 2048 };
  const live = { status: "available", listener: "http://localhost:6217", activeRevision: 1,
    activeEntityId: "page", pageContentHash: "c".repeat(64), bundleSha256: "d".repeat(64), runtimeFingerprint,
    audience: { role: "game-master", actorId: null, applicationId: "fixture",
      stateSpaceId: "state.fixture", campaignId: "campaign.fixture" } };
  const browser = { name: "test-browser", version: "test-version" };
  const machine = { platform: "test", release: "test", cpu: "test", logicalProcessors: 4, memoryGiB: 16 };
  const assets = { ...recordSetEvidence([
    "/ui/dnd2024-play/assets/app.css", "/ui/dnd2024-play/assets/app.js",
  ]), cssCount: 1, jsCount: 1 };
  const requests = [
    { path: "/api/audience-context", parentInteraction: "navigation", method: "GET", status: 200, outcome: "response" },
    { path: "/api/applications/fixture/state-spaces/state.fixture/media-batch", parentInteraction: "map", method: "POST", status: 200, outcome: "response" },
    { path: "/api/applications/fixture/content", parentInteraction: "installed-content", method: "GET", status: 200, outcome: "response" },
    { path: "/api/applications/fixture/read-models/readable-rules", parentInteraction: "rules", method: "GET", status: 200, outcome: "response" },
  ];

  return {
    audienceView, perspective: audienceView === "shared-table" ? "dm" : "player", readOnly: true,
    ...(audienceView === "observer-preview" ? { observerId: "actor.fixture" } : {}),
    listener: live.listener, browser, machine, workloadHarnessSha256: workloadHarnessFingerprint(),
    liveBefore: live, liveAfter: structuredClone(live),
    workloadReference: { kind: "independent-api", runtimeFingerprint, fixtureFingerprint, audienceView,
      views, assets, browser: { ...browser }, machine: { ...machine },
      baseline: { cold: { sampleCount: 20, completeWorkloadP50Ms: 1000 },
        warm: { sampleCount: 20, completeWorkloadP50Ms: 900 } } },
    runs: Array.from({ length: 20 }, (_, index) => ["cold", "warm"].map(cacheState => ({
      id: `${cacheState}-${index + 1}`, cacheState, status: "collected", scriptErrorCount: 0,
      requestCount: requests.length, requests: structuredClone(requests),
      inventoryStyle: { display: "flex", cursor: "pointer", cue: true },
      mapEvidence: { width: 2000, height: 1500, markers: 3 },
      blockedWrites: 1, blockedOperations: [{ method: "POST", path: "/api/acceptance-mutation-probe" }],
      marks: { firstReady: 100, completeWorkload: 400, warmReturn: 5 },
      checks: Object.fromEntries(workloadChecks.map(check => [check, "passed"])),
      assets: structuredClone(assets), firstReadyAssets: structuredClone(assets), traversal: structuredClone(views),
      serverMeasurements: { owner: "production-read-path", runtimeFingerprint, fixtureFingerprint,
        sampleId: `${cacheState}-${index + 1}`, warmSourceReads: 0,
        sql: { campaign: 12, factions: 12, knowledge: 16, chronology: 12, completeWorkload: 80 },
        firstRequestCosts: Object.fromEntries(["mappingUpdate", "planEviction", "sourceDrift"].map(condition =>
          [condition, { owner: "production-read-path", sql: 12, sourceReads: 1,
            allocatedBytes: 1000, elapsedMs: 10 }])),
        scaling: { campaign: structuredClone(scaling), factions: structuredClone(scaling) },
        fixtureCases: { small: structuredClone(fixtureCase), large: structuredClone(fixtureCase),
          doubledUnrelated: structuredClone(fixtureCase) },
        retainedCaches: { retainedEntries: 10, retainedBytes: 4096, activeRequests: 0, hits: 20, misses: 10 } },
    }))).flat(),
  };
}

test("only equivalent complete traversals with real measurement fields can pass the frozen gates", () => {
  assert.equal(completeWorkloadEvidence(evidence()).status, "passed");
  assert.deepEqual(websiteAudienceProfiles, ["shared-table"]);
  assert.equal(completeReleaseEvidence(websiteAudienceProfiles.map(evidence)).status, "passed");
  assert.equal(completeReleaseEvidence([evidence(), evidence("observer-preview")]).status, "passed");
  assert.equal(completeReleaseEvidence([evidence("observer-preview")]).status, "blocked");
  const failed = evidence(); failed.runs[0].checks.startup = "failed";
  assert.equal(completeReleaseEvidence([failed]).status, "failed");
});

test("missing references, measurements, assets and shared-site identity remain blocked", () => {
  for (const mutate of [
    value => { delete value.workloadReference; },
    value => { delete value.runs[0].traversal.history; },
    value => { delete value.runs[0].serverMeasurements; },
    value => { delete value.runs[0].serverMeasurements.scaling.campaign; },
    value => { delete value.runs[0].serverMeasurements.fixtureCases.large; },
    value => { delete value.runs[0].serverMeasurements.retainedCaches; },
    value => { delete value.runs[0].assets; },
    value => { delete value.runs[0].firstReadyAssets; },
    value => { value.runs = value.runs.slice(0, 38); },
    value => { value.workloadReference.runtimeFingerprint = "other"; },
    value => { delete value.liveAfter; },
    value => { value.liveAfter.activeRevision++; },
    value => { delete value.runs[0].scriptErrorCount; },
    value => { delete value.runs[0].serverMeasurements.firstRequestCosts; },
    value => { value.audienceView = "actor-seat"; },
    value => { value.liveAfter.audience.actorId = "actor.fixture"; },
    value => { value.perspective = "player"; },
    value => { delete value.browser; },
    value => { delete value.machine; },
    value => { value.workloadHarnessSha256 = "0".repeat(64); },
    value => { value.workloadReference.browser.version = "different"; },
    value => { value.workloadReference.machine.cpu = "different"; },
    value => { value.workloadReference.baseline.cold = { sampleCount: 20, loaderP50Ms: 1000 }; },
  ]) {
    const value = evidence(); mutate(value);
    assert.notEqual(completeWorkloadEvidence(value).status, "passed");
  }
});

test("failed checks, partial traversals, wrong records and mutations cannot be accepted", () => {
  for (const mutate of [
    value => { value.runs[0].traversal.history.status = "unloaded"; },
    value => { value.runs[0].traversal.locations.status = "unavailable"; },
    value => { value.runs[0].traversal.factions.complete = false; },
    value => { value.runs[0].traversal.people.recordsFingerprint = "c".repeat(64); },
    value => { Object.assign(value.runs[0].traversal.lore, { status: "empty", ...recordSetEvidence([]) }); },
    value => { value.runs[0].failure = { category: "TimeoutError" }; },
    value => { value.runs[0].scriptErrorCount = 1; },
    value => { value.runs.reverse(); },
    value => { value.runs[0].cacheState = "warm"; },
    value => { value.runs[0].requests[0].path = undefined; },
    value => { value.runs[0].requests[0].parentInteraction = undefined; },
    value => { value.runs[0].requests[0] = null; },
    value => { value.runs[0] = null; },
    value => { value.runs[0].checks.reload = "failed"; },
    value => { value.runs[0].inventoryStyle.cursor = "default"; },
    value => { value.runs[0].mapEvidence.markers = 0; },
    value => { value.runs[0].blockedOperations = []; },
    value => { value.runs[0].requests[1].method = "PUT"; },
  ]) {
    const value = evidence(); mutate(value);
    assert.equal(completeWorkloadEvidence(value).status, "failed");
  }
});

test("an independently confirmed Current absence differs from an unloaded feature while the release still requires a map", () => {
  const value = evidence();
  const absent = { status: "unavailable", complete: true, reasonCode: "no-authorized-content",
    ...recordSetEvidence([]) };
  value.workloadReference.views.current = structuredClone(absent);
  for (const run of value.runs) run.traversal.current = structuredClone(absent);
  assert.equal(completeWorkloadEvidence(value).status, "passed");
  delete value.runs[0].traversal.current.reasonCode;
  assert.equal(completeWorkloadEvidence(value).status, "failed");
  value.runs[0].traversal.current = { ...absent, status: "unloaded" };
  assert.equal(completeWorkloadEvidence(value).status, "failed");
  const noMap = evidence();
  noMap.workloadReference.views.map = structuredClone(absent);
  for (const run of noMap.runs) run.traversal.map = structuredClone(absent);
  assert.equal(completeWorkloadEvidence(noMap).status, "failed");
});

test("the release aggregator cannot bypass audience, browser, machine or harness checks", () => {
  for (const mutate of [
    profiles => { profiles[0].liveBefore.audience.role = "actor"; },
    profiles => { delete profiles[0].browser; },
    profiles => { delete profiles[0].workloadHarnessSha256; },
    profiles => { profiles[0].machine.cpu = "other target"; },
  ]) {
    const profiles = websiteAudienceProfiles.map(evidence); mutate(profiles);
    assert.equal(completeReleaseEvidence(profiles).status, "blocked");
  }
  assert.equal(completeReleaseEvidence(null).status, "blocked");
  assert.equal(completeWorkloadEvidence(null).status, "blocked");
});

test("HTTP, SQL, source, scaling, cache and latency gates stay independent", () => {
  for (const mutate of [
    value => { value.runs[0].requests = Array(201).fill({ path: "/api/entities", method: "GET", status: 200, outcome: "response", parentInteraction: "locations" }); value.runs[0].requestCount = 201; },
    value => { value.runs[0].requests = Array(9).fill(value.runs[0].requests[0]); value.runs[0].requestCount = 9; },
    value => { value.runs[0].requests = Array(3).fill({ ...value.runs[0].requests[0], parentInteraction: "factions-page-1" }); value.runs[0].requestCount = 3; },
    value => { value.runs[0].requests[0].status = 404; },
    value => { value.runs[0].serverMeasurements.sql.completeWorkload = 81; },
    value => { value.runs[0].serverMeasurements.warmSourceReads = 1; },
    value => { value.runs[0].serverMeasurements.scaling.campaign.doubledSql = 11; },
    value => { value.runs[0].serverMeasurements.scaling.factions.doubledAllocatedBytes = 1101; },
    value => { value.runs[0].serverMeasurements.scaling.factions.doubledMedianMs = 11.1; },
    value => { value.runs[0].serverMeasurements.retainedCaches.activeRequests = 1; },
    value => { for (const run of value.runs) run.marks.completeWorkload = 800; },
  ]) {
    const value = evidence(); mutate(value);
    assert.equal(completeWorkloadEvidence(value).status, "failed");
  }
});

test("the complete ledger includes rules, content and only the read-only media POST", () => {
  assert.equal(isWorkloadDataRead({ path: "/api/changes" }), false);
  assert.equal(isWorkloadDataRead({ path: "/api/applications/dnd2024/content" }), true);
  assert.equal(isWorkloadDataRead({ path: "/api/read-model-media/hash/content" }), true);
  assert.equal(isReadOnlyWorkloadRequest({ method: "POST", path: "/api/applications/a/state-spaces/b/media-batch" }), true);
  assert.equal(isReadOnlyWorkloadRequest({ method: "POST", path: "/api/applications/a/actions" }), false);
  for (const interaction of ["registry-recipes-page-2", "locations-page-3", "rules-page-4",
    "installed-content-page-5"])
    assert.equal(isClassifiedWorkloadInteraction(interaction), true);
  assert.equal(isClassifiedWorkloadInteraction("unknown"), false);
});

test("record evidence is order independent, rejects duplicate identities and retains no private IDs", () => {
  assert.deepEqual(recordSetEvidence(["private.b", "private.a"]), recordSetEvidence(["private.a", "private.b"]));
  assert.doesNotMatch(JSON.stringify(recordSetEvidence(["private.a"])), /private/);
  assert.throws(() => recordSetEvidence(["same", "same"]));
});
