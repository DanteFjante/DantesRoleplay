import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  PERFORMANCE_MARKS,
  PERFORMANCE_MEASURES,
  markActiveViewReady,
  markBootstrapResponse,
  markCharacterReady,
  markCombatBoardReady,
  markMapReady,
  markShellReady,
  resetPerformanceMarksForTests,
} from "../src/observability/performance.js";
import {
  DEVELOPMENT_OBSERVABILITY_KEY,
  installDevelopmentRequestLedger,
  recordDevelopmentDiagnostic,
  withinDevelopmentInteraction,
} from "../src/observability/request-ledger.js";

test("readiness marks are stable and recorded only once", () => {
  resetPerformanceMarksForTests();
  const entries = [];
  const target = { performance: { mark: (name, options) => entries.push({ name, options }) } };

  assert.equal(markShellReady(target), true);
  assert.equal(markShellReady(target), false);
  markBootstrapResponse("ready", target);
  markActiveViewReady("world", target);
  markCharacterReady("actor.1", target);
  markMapReady("map.1", target);
  markCombatBoardReady("encounter.1", target);

  assert.deepEqual(entries.map((entry) => entry.name), Object.values(PERFORMANCE_MARKS));
});

test("the first ready view records a navigation-to-view latency measure", () => {
  resetPerformanceMarksForTests();
  const measures = [];
  const target = { performance: {
    mark: () => {},
    measure: (name, options) => measures.push({ name, options }),
  } };

  markActiveViewReady("world", target);
  markActiveViewReady("campaign", target);

  assert.equal(measures.length, 1);
  assert.equal(measures[0].name, PERFORMANCE_MEASURES.firstReadyView);
  assert.deepEqual(measures[0].options,
    { start: 0, end: PERFORMANCE_MARKS.activeViewReady, detail: { view: "world" } });
});

test("development request ledger records metadata without URLs, query values, or bodies", async () => {
  let now = 10;
  const target = {
    location: { origin: "https://table.example" },
    performance: { now: () => (now += 5) },
    fetch: async () => new Response("secret response body", {
      status: 200,
      headers: { "cache-status": "local; hit" },
    }),
  };
  const observability = installDevelopmentRequestLedger({ target });

  await withinDevelopmentInteraction("bootstrap", () => target.fetch(
    "/api/campaigns/campaign.1?private=do-not-record",
    { method: "POST", body: "do-not-record" },
  ), target);
  await new Promise((resolve) => setImmediate(resolve));

  const snapshot = observability.snapshot();
  assert.equal(snapshot.requests.length, 1);
  assert.deepEqual(snapshot.requests[0], {
    id: 1,
    parentInteraction: "bootstrap:1",
    path: "/api/campaigns/campaign.1",
    method: "POST",
    durationMs: 5,
    status: 200,
    payloadBytes: 20,
    cacheResult: "local; hit",
    outcome: "response",
  });
  assert.doesNotMatch(JSON.stringify(snapshot), /private=|do-not-record|secret response body/);

  observability.restore();
  assert.equal(target[DEVELOPMENT_OBSERVABILITY_KEY], undefined);
});

test("development ledger records bounded party-read status without component values", async () => {
  const target = {
    location: { origin: "https://table.example" },
    performance: { now: () => 1 },
    fetch: async () => new Response(null, { status: 204 }),
  };
  const observability = installDevelopmentRequestLedger({ target, maximumEntries: 2 });
  await withinDevelopmentInteraction("hub-load", async () => {
    assert.equal(recordDevelopmentDiagnostic("party-read", {
      campaignId: "campaign.one",
      partyDiscovery: "ready",
      sourceRevision: "live:one",
      members: [{
        actorId: "actor.one",
        readModelStatus: "error",
        sections: { sheet: "error", inventory: "error" },
        diagnosticId: "request-422",
      }],
    }, target), true);
  }, target);

  const snapshot = observability.snapshot();
  assert.deepEqual(snapshot.diagnostics, [{
    id: "party-read:1",
    parentInteraction: "hub-load:1",
    kind: "party-read",
    detail: {
      campaignId: "campaign.one",
      partyDiscovery: "ready",
      sourceRevision: "live:one",
      members: [{
        actorId: "actor.one",
        readModelStatus: "error",
        sections: { sheet: "error", inventory: "error" },
        diagnosticId: "request-422",
      }],
    },
  }]);
  assert.doesNotMatch(JSON.stringify(snapshot), /componentValue|valueJson|private biography/u);
  observability.restore();
});

test("production component paths emit every readiness mark", () => {
  const source = [
    "../src/server-host/main.tsx",
    "../src/components/BootstrapShell.tsx",
    "../src/components/DndInformationHub.tsx",
    "../src/components/RulesOnlyHub.tsx",
    "../src/components/PartyView.tsx",
    "../src/components/MapCanvas.tsx",
    "../src/components/PreviewViews.tsx",
  ].map((path) => readFileSync(new URL(path, import.meta.url), "utf8")).join("\n");

  for (const call of [
    "markShellReady(",
    "markBootstrapResponse(",
    "markActiveViewReady(",
    "markCharacterReady(",
    "markMapReady(",
    "markCombatBoardReady(",
  ]) assert.match(source, new RegExp(call.replace("(", "\\(")));
  assert.match(source, /process\.env\.NODE_ENV !== "production"\) installDevelopmentRequestLedger\(\)/);
});

test("ledger bounds, overlapping interaction attribution and fetch restoration are honest", async () => {
  const fetch = async () => new Response(null, { status: 204 });
  const target = { fetch };
  assert.throws(() => installDevelopmentRequestLedger({ target, maximumEntries: 0 }), RangeError);
  const ledger = installDevelopmentRequestLedger({ target, maximumEntries: 2 });
  const first = ledger.beginInteraction("first");
  const second = ledger.beginInteraction("second");
  await target.fetch("/overlapping");
  ledger.endInteraction(first);
  await target.fetch("/second");
  ledger.endInteraction(second);
  await target.fetch("/outside");
  const requests = ledger.snapshot().requests;
  assert.equal(ledger.snapshot().totalRequests, 3);
  assert.equal(ledger.snapshot().droppedRequests, 1);
  assert.equal(requests.length, 2);
  assert.equal(requests[0].parentInteraction, second.id);
  assert.equal(requests[1].parentInteraction, null);
  ledger.restore();
  assert.equal(target.fetch, fetch);
});
