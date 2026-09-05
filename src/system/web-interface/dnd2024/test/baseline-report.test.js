import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from 'node:fs';

import { browserEvidence, gateEvidence, machineProfile, normalizeListener, requiredMarks,
  sameLivePage, sha256, summarizeRequests, summarizeSamples } from "../scripts/collect-baseline.mjs";
import { initializeBrowserProbe, reportedPayloadBytes, requestMetadata } from "../scripts/sample-browser-baseline.mjs";
import { JSDOM } from 'jsdom';

test("baseline percentiles always retain sample count, p50, and p95", () => {
  assert.deepEqual(summarizeSamples([30, 10, 20, 40, 50]), {
    sampleCount: 5,
    p50Ms: 30,
    p95Ms: 50,
  });
  assert.equal(summarizeSamples([]), null);
  assert.equal(summarizeSamples([10, Number.NaN]), null);
  assert.equal(summarizeSamples([-1, 2]), null);
});

const live = { status: 'available', listener: 'http://localhost:6217', activeRevision: 45,
  activeEntityId: 'web-page:dnd2024', pageContentHash: 'page', bundleSha256: 'bundle', runtimeFingerprint: 'runtime' };
function samples() {
  return { listener: live.listener, browser: { name: 'Chrome', version: '152' }, machine: machineProfile(),
    samplerSha256: sha256(readFileSync(new URL('../scripts/sample-browser-baseline.mjs', import.meta.url))),
    readOnly: true, liveBefore: { ...live }, liveAfter: { ...live },
    runs: ['cold', 'warm'].flatMap(cacheState => Array.from({ length: 20 }, (_, index) => ({
      id: cacheState + index, cacheState, status: 'passed',
      requests: [], requestCount: 0, payloadBytes: 0, browserCacheHits: 0,
      marks: Object.fromEntries(requiredMarks.map(name => [name, index + 1])),
    }))) };
}

test('baseline requires 20 complete samples per metric, not merely 20 rows', () => {
  const source = samples();
  assert.equal(browserEvidence(source, live).status, 'complete');
  assert.deepEqual(browserEvidence(source, live).metrics.cold.character, {
    sampleCount: 20, p50Ms: 10, p95Ms: 19, listener: live.listener, browser: source.browser,
    unavailableCount: 0, status: 'measured',
  });
  delete source.runs[0].marks.character;
  assert.equal(browserEvidence(source, live).status, 'invalid');
  assert.deepEqual(browserEvidence(source, live).metrics, {});
});

test('a reproducible unavailable view is baseline failure evidence, never a ready latency', () => {
  const source = samples();
  for (const run of source.runs) {
    delete run.marks.character;
    run.outcomes = { character: { status: 'empty', reason: 'No canonical sheet rendered.' } };
  }
  const report = browserEvidence(source, live);
  assert.equal(report.status, 'complete-with-view-failures');
  assert.equal(report.metrics.cold.character.sampleCount, 0);
  assert.equal(report.metrics.cold.character.p50Ms, null);
  assert.equal(report.metrics.cold.character.unavailableCount, 20);
});

test('request summaries keep the interaction, sample denominator, statuses, cache result and units', () => {
  const request = { path: '/api/example', method: 'GET', parentInteraction: 'character',
    durationMs: 10, payloadBytes: 12, status: 404, cacheResult: 'not-reported', outcome: 'response' };
  const result = summarizeRequests([
    { requests: [request], requestCount: 1, payloadBytes: 12, browserCacheHits: 0 },
    { requests: [{ ...request, durationMs: 20 }], requestCount: 1, payloadBytes: 12, browserCacheHits: 0 },
  ], live.listener, { name: 'Chrome', version: '152' });
  assert.deepEqual(result.counts, { sampleCount: 2, p50: 1, p95: 1, unit: 'requests' });
  assert.deepEqual(result.paths[0].duration, { sampleCount: 2, p50Ms: 10, p95Ms: 20 });
  assert.equal(result.paths[0].statuses[404], 2);
  assert.equal(result.paths[0].parentInteraction, 'character');
  assert.equal(result.paths[0].cacheResults['not-reported'], 2);
});

test('baseline rejects another listener, page, runtime, audience, browser or machine', () => {
  for (const mutate of [
    value => { value.listener = 'https://localhost:5144'; },
    value => { value.liveBefore.activeRevision++; },
    value => { value.liveAfter.bundleSha256 = 'changed'; },
    value => { value.liveAfter.runtimeFingerprint = 'another-audience'; },
    value => { value.browser.version = ''; },
    value => { value.machine.cpu = 'another-machine'; },
    value => { value.readOnly = false; },
    value => { value.samplerSha256 = 'changed'; },
  ]) {
    const source = samples(); mutate(source);
    assert.equal(browserEvidence(source, live).status, 'invalid');
  }
  assert.equal(sameLivePage(live, { status: 'blocked' }), false);
});

test('duplicate, failed, undersampled or nonfinite runs cannot pass', () => {
  for (const mutate of [
    value => { value.runs[0].id = value.runs[1].id; },
    value => { value.runs[0].status = 'failed'; },
    value => { value.runs.pop(); },
    value => { value.runs[0].marks.map = Infinity; },
    value => { value.runs[0].requestCount = 4; },
  ]) {
    const source = samples(); mutate(source);
    assert.equal(browserEvidence(source, live).status, 'invalid');
  }
  assert.match(browserEvidence(null, live).reason, /available, but no browser samples/);
  assert.match(browserEvidence(null, { status: 'blocked' }).reason, /No verified live listener/);
});

test('separate gates retain TAP summaries and failing TypeScript diagnostics', () => {
  assert.deepEqual(gateEvidence({ status: 0, stdout: '# tests 22\n# pass 22\n# fail 0' }, 'tests').summary,
    { tests: 22, pass: 22, fail: 0 });
  const failing = gateEvidence({ status: 1, stdout: 'src/example.ts(1,1): error TS2322: incompatible' }, 'typecheck');
  assert.equal(failing.status, 'failed');
  assert.equal(failing.diagnostics.length, 1);
  assert.match(failing.rawOutput, /TS2322/);
});

test('baseline stays on credential-free loopback listeners with ordinary TLS verification', () => {
  assert.equal(normalizeListener('https://localhost:5144/'), 'https://localhost:5144');
  for (const url of ['https://external.example', 'http://user:secret@localhost:6217', 'http://localhost:6217/?secret=1',
    'http://localhost:6217/ui/dnd2024-play', 'file:///private']) assert.throws(() => normalizeListener(url));
});

test('browser ledger records transport metadata, not query values or private bodies', () => {
  assert.equal(reportedPayloadBytes(-803), null);
  assert.equal(reportedPayloadBytes(0), 0);
  assert.equal(reportedPayloadBytes(123), 123);
  const entry = requestMetadata({
    url: () => 'http://localhost:6217/api/read-models/character?secret=hidden',
    method: () => 'GET', postData: () => 'private-payload',
  }, 'character', 1);
  assert.equal(entry.path, '/api/read-models/character');
  assert.equal(entry.parentInteraction, 'character');
  assert.equal(entry.payloadBytes, null);
  assert.doesNotMatch(JSON.stringify(entry), /secret|hidden|private-payload/);
  const source = samples();
  source.runs[0].requestCount = 1;
  source.runs[0].requests = [{ ...entry, responseBody: 'private-payload' }];
  const report = browserEvidence(source, live);
  assert.equal(report.status, 'invalid');
  assert.doesNotMatch(JSON.stringify(report), /private-payload/);
});

test('browser guard rejects mutating URL and Request fetches without recording their bodies', async () => {
  const dom = new JSDOM('<!doctype html><html><body></body></html>', {
    url: live.listener, runScripts: 'outside-only',
  });
  let calls = 0;
  dom.window.fetch = async () => { calls++; return new Response(null, { status: 204 }); };
  dom.window.eval('(' + initializeBrowserProbe.toString() + ')({perspective:"player"})');
  try {
    await dom.window.fetch('/api/audience-context');
    await assert.rejects(dom.window.fetch(new dom.window.URL('/api/conversations?secret=hidden', live.listener),
      { method: 'POST', body: 'private-body' }), /blocks writes/);
    await assert.rejects(dom.window.fetch(new Request(live.listener + '/api/actions',
      { method: 'POST', body: 'private-body' })), /blocks writes/);
    assert.equal(calls, 1);
    assert.equal(dom.window.__DND_BASELINE_BLOCKED_WRITES__, 2);
    assert.equal(dom.window.__DND_BASELINE_BLOCKED_OPERATIONS__[0].path, '/api/conversations');
    assert.doesNotMatch(JSON.stringify(dom.window.__DND_BASELINE_BLOCKED_OPERATIONS__), /private-body|hidden/);
  } finally { dom.window.close(); }
});
