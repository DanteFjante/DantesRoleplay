import assert from "node:assert/strict";
import test from "node:test";
import { completeReleaseEvidence, completeWorkloadEvidence, isWorkloadDataRead, recordSetEvidence, workloadHarnessFingerprint, workloadViews } from "../scripts/complete-workload.mjs";

function evidence(audienceView = "game-master") {
  const runtimeFingerprint = "a".repeat(64), fixtureFingerprint = "b".repeat(64);
  const views = Object.fromEntries(workloadViews.map(view => [view, {
    status: "ready", complete: true, ...recordSetEvidence([view + ".record"]),
  }]));
  const scaling = { owner: "production-read-path", unrelatedPopulationMultiplier: 2, sampleCount: 20,
    baseSql: 8, doubledSql: 9, baseAllocatedBytes: 1000, doubledAllocatedBytes: 1050, baseMedianMs: 10, doubledMedianMs: 10.5 };
  const live = { status: "available", listener: "http://localhost:6217", activeRevision: 1,
    activeEntityId: "page", pageContentHash: "c".repeat(64), bundleSha256: "d".repeat(64), runtimeFingerprint,
    audience: { role: audienceView === "actor" ? "actor" : "game-master", actorId: audienceView === "actor" ? "actor.fixture" : null,
      applicationId: "fixture", stateSpaceId: "state.fixture", campaignId: "campaign.fixture" } };
  const browser = { name: "test-browser", version: "test-version" };
  const machine = { platform: "test", release: "test", cpu: "test", logicalProcessors: 4, memoryGiB: 16 };
  // Synthetic unit-test measurements exercise the validator, never stand in for a live run.
  return { audienceView, perspective: audienceView === "game-master" ? "dm" : "player", readOnly: true,
    listener: live.listener, browser, machine, workloadHarnessSha256: workloadHarnessFingerprint(),
    liveBefore: live, liveAfter: structuredClone(live),
    workloadReference: { kind: "independent-api", runtimeFingerprint, fixtureFingerprint, audienceView, views,
      browser: { ...browser }, machine: { ...machine },
      baseline: { cold: { sampleCount: 20, completeWorkloadP50Ms: 1000 }, warm: { sampleCount: 20, completeWorkloadP50Ms: 900 } } },
    runs: Array.from({ length: 20 }, (_, index) => ["cold", "warm"].map(cacheState => ({
      id: `${cacheState}-${index + 1}`, cacheState, status: "collected", scriptErrorCount: 0,
      requestCount: 1, requests: [{ path: "/api/audience-context", parentInteraction: "navigation", method: "GET", status: 200, outcome: "response" }],
      marks: { completeWorkload: 400 }, traversal: structuredClone(views),
      serverMeasurements: { owner: "production-read-path", runtimeFingerprint, fixtureFingerprint,
        sampleId: `${cacheState}-${index + 1}`, warmSourceReads: 0,
        sql: { campaign: 12, factions: 12, knowledge: 16, chronology: 12, completeWorkload: 80 },
        firstRequestCosts: Object.fromEntries(["mappingUpdate", "planEviction", "sourceDrift"].map(condition =>
          [condition, { owner: "production-read-path", sql: 12, sourceReads: 1, allocatedBytes: 1000, elapsedMs: 10 }])),
        scaling: { campaign: structuredClone(scaling), factions: structuredClone(scaling) } },
    }))).flat() };
}

test("only equivalent complete traversals with real measurement fields can pass the frozen gates", () => {
  assert.equal(completeWorkloadEvidence(evidence()).status, "passed");
  assert.equal(completeReleaseEvidence(["game-master", "gm-player-preview", "actor"].map(evidence)).status, "passed");
  assert.equal(completeReleaseEvidence([evidence()]).status, "blocked");
});

test("collected shell-only observations, missing SQL and undersampling remain blocked", () => {
  for (const mutate of [
    value => { delete value.workloadReference; },
    value => { delete value.runs[0].traversal.history; },
    value => { delete value.runs[0].serverMeasurements; },
    value => { delete value.runs[0].serverMeasurements.scaling.campaign; },
    value => { value.runs = value.runs.slice(0, 38); },
    value => { value.workloadReference.runtimeFingerprint = "other"; },
    value => { delete value.liveAfter; },
    value => { value.liveAfter.activeRevision++; },
    value => { delete value.runs[0].scriptErrorCount; },
    value => { delete value.runs[0].serverMeasurements.firstRequestCosts; },
    value => { value.audienceView = "not-an-authorized-profile"; },
    value => { value.liveAfter.audience.actorId = "another-actor"; },
    value => { value.perspective = "player"; },
    value => { delete value.browser; },
    value => { delete value.machine; },
    value => { value.workloadHarnessSha256 = "0".repeat(64); },
    value => { value.workloadReference.browser.version = "different"; },
    value => { value.workloadReference.machine.cpu = "different"; },
    value => { value.workloadReference.baseline.cold = { sampleCount: 20, loaderP50Ms: 1000 }; },
  ]) {
    const value = evidence(); mutate(value);
    assert.equal(completeWorkloadEvidence(value).status, "blocked");
  }
});

test("unloaded, failed, partial, wrong-record and empty-substitute views cannot be accepted", () => {
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
  ]) {
    const value = evidence(); mutate(value);
    assert.equal(completeWorkloadEvidence(value).status, "failed");
  }
});

test("an independently confirmed absence differs from an unloaded or failed feature", () => {
  const value = evidence();
  const absent = { status: "unavailable", complete: true, reasonCode: "no-authorized-content", ...recordSetEvidence([]) };
  value.workloadReference.views.map = structuredClone(absent);
  for (const run of value.runs) run.traversal.map = structuredClone(absent);
  assert.equal(completeWorkloadEvidence(value).status, "passed");
  delete value.runs[0].traversal.map.reasonCode;
  assert.equal(completeWorkloadEvidence(value).status, "failed", "an unavailable view needs its own explicit reason");
  value.runs[0].traversal.map = { ...absent, status: "unloaded" };
  assert.equal(completeWorkloadEvidence(value).status, "failed");
});

test("the release aggregator cannot bypass audience, browser, machine or harness checks", () => {
  for (const mutate of [
    profiles => { profiles[1].liveBefore.audience.role = "actor"; },
    profiles => { delete profiles[1].browser; },
    profiles => { delete profiles[1].workloadHarnessSha256; },
    profiles => { profiles[1].machine.cpu = profiles[1].workloadReference.machine.cpu = "other target"; },
  ]) {
    const profiles = ["game-master", "gm-player-preview", "actor"].map(evidence);
    mutate(profiles);
    assert.equal(completeReleaseEvidence(profiles).status, "blocked");
  }
  assert.equal(completeReleaseEvidence(null).status, "blocked");
  assert.equal(completeWorkloadEvidence(null).status, "blocked");
});

test("the initial, complete-workload, SQL, source, scaling and latency gates are independent", () => {
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
    value => { for (const run of value.runs) run.marks.completeWorkload = 800; },
  ]) {
    const value = evidence(); mutate(value);
    assert.equal(completeWorkloadEvidence(value).status, "failed");
  }
});

test("record evidence is order independent, rejects duplicate identities and retains no private IDs", () => {
  assert.deepEqual(recordSetEvidence(["private.b", "private.a"]), recordSetEvidence(["private.a", "private.b"]));
  assert.doesNotMatch(JSON.stringify(recordSetEvidence(["private.a"])), /private/);
  assert.throws(() => recordSetEvidence(["same", "same"]));
  assert.equal(isWorkloadDataRead({ path: "/api/changes" }), false);
  assert.equal(isWorkloadDataRead({ path: "/api/read-model-media/hash/content" }), false);
  assert.equal(isWorkloadDataRead({ path: "/api/entities/one/media" }), true);
});
