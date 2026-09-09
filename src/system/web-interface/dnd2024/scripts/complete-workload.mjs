import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";

export const workloadViews = [
  "context",
  "campaign-overview", "campaign-log", "campaign-places", "campaign-outcomes", "campaign-quests",
  "campaign-threads", "campaign-clues",
  "party-overview", "character-sheet", "character-knowledge", "character-biography", "character-origin", "inventory",
  "inventory-item-details", "inventory-item-recipes", "inventory-item-uses",
  "registry-items", "registry-item-details", "registry-item-recipes",
  "registry-recipes", "registry-recipe-details",
  "world-overview", "map", "locations", "location-details", "location-people", "location-holdings",
  "people", "factions", "lore", "history",
  "current", "rules", "installed-content",
];
export const workloadChecks = [
  "startup", "context-switch", "inventory-direct-entry", "inventory-first-entry-style",
  "inventory-css-isolation",
  "inventory-item-return", "registry-item-return", "registry-recipe-return",
  "reload", "back-forward", "error-retry", "lazy-assets", "deferred-settled",
  "map-pixels-markers", "current-no-chatbox", "mutation-blocking",
];
export const websiteAudienceProfiles = ["shared-table"];
const groups = ["cold", "warm"];
const hash = value => typeof value === "string" && /^[a-f0-9]{64}$/iu.test(value);
const finite = value => Number.isFinite(value) && value >= 0;
const count = value => Number.isSafeInteger(value) && value >= 0;
const median = values => [...values].sort((a, b) => a - b)[Math.ceil(values.length / 2) - 1];
const text = value => typeof value === "string" && value.trim().length > 0;
const sameFields = (a, b, keys) => Boolean(a && b && keys.every(key => a[key] != null && a[key] === b[key]));
const machineKeys = ["platform", "release", "cpu", "logicalProcessors", "memoryGiB"];
const audienceKeys = ["role", "applicationId", "stateSpaceId", "campaignId"];
const readOnlyMediaBatchPath = /^\/api\/applications\/[^/]+\/state-spaces\/[^/]+\/media-batch$/u;
export function workloadHarnessFingerprint() {
  return createHash("sha256").update(Buffer.concat(
    ["sample-browser-baseline.mjs", "complete-workload.mjs", "collect-baseline.mjs",
      "../contracts/website-feature-contracts.json"]
      .map(file => readFileSync(new URL(file, import.meta.url))))).digest("hex");
}

/** Persist only counts and digests, never private record identities or notebook text. */
export function recordSetEvidence(ids) {
  if (!Array.isArray(ids) || ids.length > 10_000 || ids.some(id =>
    typeof id !== "string" || !id || id.length > 600) || new Set(ids).size !== ids.length)
    throw new Error("Invalid or duplicate workload record identity.");
  return { count: ids.length, recordsFingerprint: createHash("sha256").update(JSON.stringify([...ids].sort())).digest("hex") };
}

export const isWorkloadDataRead = request => request.path?.startsWith("/api/") &&
  request.path !== "/api/changes" && !request.path.startsWith("/api/blobs/");

export const isReadOnlyWorkloadRequest = request => ["GET", "HEAD"].includes(request?.method) ||
  request?.method === "POST" && readOnlyMediaBatchPath.test(request?.path ?? "");

export const isClassifiedWorkloadInteraction = value => typeof value === "string" &&
  (workloadViews.includes(value) || /^(?:navigation|(?:factions|locations|rules|installed-content)-page-[1-9][0-9]*|registry-(?:items|recipes)-page-[1-9][0-9]*)$/u.test(value));

/**
 * Observation collection is not release acceptance. Missing independent parity, actual
 * production-path SQL/allocation evidence or samples blocks acceptance; failing measured
 * gates fails it. Historical observation files remain untouched.
 */
export function completeWorkloadEvidence(source) {
  const missing = new Set(), failures = new Set();
  const reference = source?.workloadReference;
  const audience = source?.liveBefore?.audience;
  const expectedProfile = audience?.role === "game-master" && audience.actorId == null
    ? source?.perspective === "dm" ? "shared-table"
      : source?.perspective === "player" && text(source?.observerId) ? "observer-preview" : null
    : null;
  if (!expectedProfile || expectedProfile !== source?.audienceView ||
      !audienceKeys.every(key => text(audience?.[key])) ||
      !sameFields(audience, source?.liveAfter?.audience, audienceKeys) ||
      (audience?.actorId ?? null) !== (source?.liveAfter?.audience?.actorId ?? null))
    missing.add("The workload profile must match an unchanged authorized seat and perspective.");
  if (!text(source?.listener) || source.listener !== source?.liveBefore?.listener ||
      !text(source?.browser?.name) || !text(source?.browser?.version) ||
      !machineKeys.slice(0, 3).every(key => text(source?.machine?.[key])) ||
      !count(source?.machine?.logicalProcessors) || source.machine.logicalProcessors === 0 ||
      !finite(source?.machine?.memoryGiB) || source.machine.memoryGiB === 0 ||
      source?.workloadHarnessSha256 !== workloadHarnessFingerprint())
    missing.add("Exact listener, browser, machine and current complete-workload harness identities are required.");
  if (source?.readOnly !== true) missing.add("The read-only browser guard must be enabled.");
  const liveKeys = ["listener", "activeRevision", "activeEntityId", "pageContentHash", "bundleSha256", "runtimeFingerprint"];
  if (source?.liveBefore?.status !== "available" || source?.liveAfter?.status !== "available" ||
      liveKeys.some(key => source.liveBefore[key] == null || source.liveBefore[key] !== source.liveAfter[key]))
    missing.add("Verified unchanged listener, audience, runtime and served-page evidence is required.");
  if (reference?.kind !== "independent-api" || !hash(reference.fixtureFingerprint) ||
      reference.runtimeFingerprint !== source?.liveBefore?.runtimeFingerprint ||
      reference.audienceView !== source?.audienceView)
    missing.add("An independent, fixture/runtime/audience-bound workload reference is required.");
  const runs = Array.isArray(source?.runs) ? source.runs : [];
  const sampleIds = new Set();
  for (const [index, run] of runs.entries()) {
    if (!run || typeof run !== "object") { failures.add("Malformed sample."); continue; }
    if (run.id !== `${index % 2 === 0 ? "cold" : "warm"}-${Math.floor(index / 2) + 1}`)
      failures.add("Samples must be sequential paired cold/warm traversals.");
    if (!run.id || sampleIds.has(run.id)) failures.add("Duplicate or missing sample identity.");
    sampleIds.add(run.id);
    if (run.cacheState !== (index % 2 === 0 ? "cold" : "warm")) failures.add("Invalid sample cache group.");
    if (!count(run.scriptErrorCount)) missing.add("A browser script-error counter is required.");
    if (!["collected", "passed"].includes(run.status) || run.failure || run.scriptErrorCount > 0)
      failures.add("A failed traversal or browser error cannot satisfy acceptance.");
    for (const check of workloadChecks) {
      if (run.checks?.[check] !== "passed") failures.add(`Missing or failed served-browser check: ${check}.`);
    }
    if (!run.inventoryStyle || ["inline", "inline-block"].includes(run.inventoryStyle.display) ||
        run.inventoryStyle.cursor !== "pointer" || run.inventoryStyle.cue !== true)
      failures.add("Direct-entry Inventory did not retain its observed clickable presentation.");
    if (!finite(run.mapEvidence?.width) || run.mapEvidence.width === 0 ||
        !finite(run.mapEvidence?.height) || run.mapEvidence.height === 0 ||
        !count(run.mapEvidence?.markers) || run.mapEvidence.markers === 0)
      failures.add("The served map did not retain observed decoded pixels and markers.");
    if (!count(run.blockedWrites) || run.blockedWrites === 0 ||
        !Array.isArray(run.blockedOperations) || !run.blockedOperations.some(operation =>
          operation?.method === "POST" && operation.path === "/api/acceptance-mutation-probe"))
      failures.add("The browser mutation guard lacks an observed blocked-write probe.");
    for (const view of workloadViews) {
      const expected = reference?.views?.[view], actual = run.traversal?.[view];
      if (!expected || !actual) { missing.add(`Missing complete ${view} traversal/parity evidence.`); continue; }
      const explicitlyAbsent = expected.status === "unavailable" &&
        view === "current" && expected.reasonCode === "no-authorized-content";
      if ((!["ready", "empty"].includes(expected.status) && !explicitlyAbsent) ||
          expected.status !== actual.status || actual.complete !== true ||
          !count(expected.count) || !count(actual.count) || expected.count !== actual.count ||
          (expected.status === "ready" ? expected.count === 0 : expected.count !== 0) ||
          (expected.count === 0 && expected.recordsFingerprint !== recordSetEvidence([]).recordsFingerprint) ||
          (explicitlyAbsent && actual.reasonCode !== expected.reasonCode) ||
          !hash(expected.recordsFingerprint) || expected.recordsFingerprint !== actual.recordsFingerprint)
        failures.add(`${view} is incomplete, unavailable without independent evidence, or differs from the reference.`);
    }
    if (!Array.isArray(run.requests) || !count(run.requestCount) || run.requestCount !== run.requests.length)
      missing.add("A complete data-request ledger is required.");
    else if (run.requests.some(request => !request || typeof request !== "object" ||
        !text(request.path) || !request.path.startsWith("/") ||
        !isReadOnlyWorkloadRequest(request) ||
        isWorkloadDataRead(request) && !isClassifiedWorkloadInteraction(request.parentInteraction)))
      failures.add("Malformed, unclassified or mutating requests cannot enter a read-only acceptance sample.");
    else {
      const data = run.requests.filter(isWorkloadDataRead);
      if (data.length > 200) failures.add("Complete workload exceeds 200 data HTTP reads.");
      if (data.filter(request => request.parentInteraction === "navigation").length > 8)
        failures.add("Initial Campaign exceeds eight data HTTP reads.");
      const factionPages = new Map();
      for (const request of data.filter(request => request.parentInteraction?.startsWith("factions")))
        factionPages.set(request.parentInteraction, (factionPages.get(request.parentInteraction) ?? 0) + 1);
      if ([...factionPages.values()].some(reads => reads > 2)) failures.add("A Factions page exceeds two data HTTP reads.");
      if (data.some(request => request.outcome !== "response" || !Number.isInteger(request.status) ||
          request.status < 200 || request.status >= 400))
        failures.add("Failed or unfinished data reads cannot satisfy the complete workload.");
      if (run.requests.some(request => !isReadOnlyWorkloadRequest(request)))
        failures.add("Mutating requests cannot enter a read-only acceptance sample.");
      if (!data.some(request => request.method === "POST" && readOnlyMediaBatchPath.test(request.path)))
        missing.add("The complete data ledger must contain the read-only media-batch POST.");
      if (!data.some(request => request.path.includes("/content")))
        missing.add("The complete data ledger must contain Installed Content reads.");
      if (!data.some(request => request.path.endsWith("/rules") || request.path.includes("readable-rules")))
        missing.add("The complete data ledger must contain Rules reads.");
    }
    const assets = run.assets;
    if (!assets || !count(assets.cssCount) || assets.cssCount === 0 || !count(assets.jsCount) || assets.jsCount === 0 ||
        !count(assets.count) || !hash(assets.recordsFingerprint))
      missing.add("Mandatory traversed CSS and JavaScript asset evidence is required.");
    else if (reference?.assets?.recordsFingerprint !== assets.recordsFingerprint ||
        reference.assets.count !== assets.count || reference.assets.cssCount !== assets.cssCount ||
        reference.assets.jsCount !== assets.jsCount)
      failures.add("Traversed mandatory lazy assets differ from the independently pinned release assets.");
    const firstReadyAssets = run.firstReadyAssets;
    if (!firstReadyAssets || !count(firstReadyAssets.cssCount) || firstReadyAssets.cssCount === 0 ||
        !count(firstReadyAssets.jsCount) || firstReadyAssets.jsCount === 0 ||
        !count(firstReadyAssets.count) || !hash(firstReadyAssets.recordsFingerprint))
      missing.add("Direct-entry first-ready asset evidence is required before unrelated lazy CSS loads.");
    if (!finite(run.marks?.firstReady) || !finite(run.marks?.completeWorkload) || !finite(run.marks?.warmReturn))
      missing.add("Separate first-ready, complete-workload and warm-return timings are required.");
    const server = run.serverMeasurements;
    if (server?.owner !== "production-read-path" || server.sampleId !== run.id ||
        server.fixtureFingerprint !== reference?.fixtureFingerprint ||
        server.runtimeFingerprint !== source?.liveBefore?.runtimeFingerprint) {
      missing.add("Actual per-sample production SQL/source/allocation/scaling measurements are required.");
      continue;
    }
    for (const [view, maximum] of Object.entries({ campaign: 12, factions: 12, knowledge: 16, chronology: 12, completeWorkload: 80 })) {
      if (!count(server.sql?.[view])) missing.add(`Missing ${view} SQL measurement.`);
      else if (server.sql[view] > maximum) failures.add(`${view} exceeds its frozen SQL ceiling.`);
    }
    if (!count(server.warmSourceReads)) missing.add("Missing warm source-file read measurement.");
    else if (server.warmSourceReads !== 0) failures.add("Warm unchanged plans read source files.");
    for (const condition of ["mappingUpdate", "planEviction", "sourceDrift"]) {
      const first = server.firstRequestCosts?.[condition];
      if (first?.owner !== "production-read-path" || !count(first.sql) || !count(first.sourceReads) ||
          !finite(first.allocatedBytes) || !finite(first.elapsedMs))
        missing.add(`Missing measured first-request costs after ${condition}.`);
    }
    for (const view of ["campaign", "factions"]) {
      const scaling = server.scaling?.[view];
      if (!scaling || scaling.owner !== "production-read-path" || scaling.unrelatedPopulationMultiplier !== 2 ||
          !count(scaling.sampleCount) || scaling.sampleCount < 20 ||
          !["baseSql", "doubledSql", "baseAllocatedBytes", "doubledAllocatedBytes", "baseMedianMs", "doubledMedianMs"]
            .every(key => finite(scaling[key])))
        missing.add(`Missing actual ${view} doubled-population measurements.`);
      else if (scaling.doubledSql > scaling.baseSql + 2 ||
          scaling.doubledAllocatedBytes > scaling.baseAllocatedBytes * 1.1 ||
          scaling.doubledMedianMs > scaling.baseMedianMs * 1.1)
        failures.add(`${view} exceeds its frozen scaling allowance.`);
    }
    for (const size of ["small", "large", "doubledUnrelated"]) {
      const fixture = server.fixtureCases?.[size];
      if (fixture?.owner !== "production-read-path" || fixture.fixtureFingerprint !== reference?.fixtureFingerprint ||
          !count(fixture.sampleCount) || fixture.sampleCount < 20 || !finite(fixture.medianMs) ||
          !finite(fixture.allocatedBytes) || !count(fixture.sql) || !count(fixture.responseBytes))
        missing.add(`Missing actual ${size} production-shaped fixture measurements.`);
    }
    const caches = server.retainedCaches;
    if (!caches || !count(caches.retainedEntries) || !count(caches.retainedBytes) ||
        !count(caches.activeRequests) || !count(caches.hits) || !count(caches.misses))
      missing.add("Missing retained-cache measurements for the completed traversal.");
    else if (caches.activeRequests !== 0)
      failures.add("Completed traversal retained active resource requests.");
  }
  for (const group of groups) {
    const samples = runs.filter(run => run?.cacheState === group);
    if (samples.length < 20) missing.add(`${group} needs at least 20 complete sequential samples.`);
    const baseline = reference?.baseline?.[group];
    if (!baseline || !count(baseline.sampleCount) || baseline.sampleCount < 20 ||
        !finite(baseline.completeWorkloadP50Ms) || baseline.completeWorkloadP50Ms === 0 ||
        !sameFields(reference?.browser, source?.browser, ["name", "version"]) ||
        !sameFields(reference?.machine, source?.machine, machineKeys))
      missing.add(`Missing matched ${group} complete-workload baseline timing/browser/machine.`);
    else if (samples.length >= 20 && samples.every(run => finite(run.marks?.completeWorkload)) &&
        median(samples.map(run => run.marks.completeWorkload)) > baseline.completeWorkloadP50Ms * 0.5)
      failures.add(`${group} matched median improvement is below 50 percent.`);
  }
  return { status: failures.size ? "failed" : missing.size ? "blocked" : "passed",
    missing: [...missing], failures: [...failures],
    scope: "The shared-table website profile; an observer-preview profile is also required when that optional UI is delivered." };
}

export function completeReleaseEvidence(profiles) {
  const required = websiteAudienceProfiles;
  if (!Array.isArray(profiles) || profiles.some(profile => !profile || typeof profile !== "object"))
    return { status: "blocked", reason: "The shared-table website profile is required." };
  const actual = profiles.map(profile => profile.audienceView);
  if (!actual.length || actual.length > 2 || new Set(actual).size !== actual.length ||
      required.some(profile => !actual.includes(profile)) || actual.some(profile => ![...required, "observer-preview"].includes(profile)))
    return { status: "blocked", reason: "The shared-table website profile and only an optional observer preview are accepted." };
  const results = profiles.map(completeWorkloadEvidence);
  if (new Set(profiles.map(profile => profile.workloadReference?.fixtureFingerprint)).size !== 1)
    return { status: "blocked", reason: "The audience profiles do not share one pinned source fixture." };
  if (profiles.some(profile => !sameFields(profile.browser, profiles[0].browser, ["name", "version"]) ||
      !sameFields(profile.machine, profiles[0].machine, machineKeys)))
    return { status: "blocked", reason: "The audience profiles must share one browser and target machine." };
  return { status: results.some(result => result.status === "failed") ? "failed"
    : results.every(result => result.status === "passed") ? "passed" : "blocked", profiles: results };
}
