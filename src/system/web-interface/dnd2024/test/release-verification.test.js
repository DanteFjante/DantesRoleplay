import assert from 'node:assert/strict';
import test from 'node:test';
import { generateKeyPairSync } from 'node:crypto';
import { signManifest, verifyManifest, canonicalJson } from '../scripts/release-signature.mjs';
import { verifyRuntimeTarget, verifyBrowserEvidence, probeRuntime } from '../scripts/release-runtime-verification.mjs';
import { sha256 } from '../scripts/create-release-manifest.mjs';
import { resolveReleaseOrigin, verifyLiveRelease } from '../scripts/verify-live-release.mjs';
import { recordSetEvidence, workloadChecks, workloadHarnessFingerprint, workloadViews } from '../scripts/complete-workload.mjs';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

test('public HTTP verification requires the exact signed origin and never substitutes localhost', () => {
  const publicOrigin = 'http://98.128.172.181';
  assert.equal(resolveReleaseOrigin(publicOrigin, publicOrigin), publicOrigin);
  assert.equal(resolveReleaseOrigin('http://localhost:6217/'), 'http://localhost:6217');
  assert.equal(resolveReleaseOrigin('https://example.test'), 'https://example.test');
  assert.throws(() => resolveReleaseOrigin(publicOrigin), /exact origin/);
  for (const other of ['http://localhost:6217', 'http://98.128.172.181:6217', 'http://98.128.172.182'])
    assert.throws(() => resolveReleaseOrigin(other, publicOrigin), /origin drift/);
  for (const suffix of ['/ui/dnd2024-play', '/?x=1', '/#view'])
    assert.throws(() => resolveReleaseOrigin(publicOrigin + suffix, publicOrigin));
  assert.throws(() => resolveReleaseOrigin('http://user:password@98.128.172.181', publicOrigin));
  assert.throws(() => resolveReleaseOrigin('file:///C:/release'));
});

test('public verification retains signed runtime, audience, exact assets and no-redirect transport checks', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'roleplay-public-release-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const { expected, readiness, audience } = fixture();
  expected.origin = 'http://98.128.172.181';
  const html = '<script src="/ui/dnd2024-play/assets/index-abcdefgh.js"></script>';
  const script = 'console.log("fixture");';
  const keys = generateKeyPairSync('ed25519');
  const manifest = signManifest({ schemaVersion: 2, pageId: 'dnd2024-play', expectedRuntime: expected,
    files: [{path: 'index.html', length: Buffer.byteLength(html), sha256: sha256(Buffer.from(html))},
      {path: 'assets/index-abcdefgh.js', length: Buffer.byteLength(script), sha256: sha256(Buffer.from(script))}],
    assetReferences: ['assets/index-abcdefgh.js'] }, keys.privateKey);
  const manifestPath = join(directory, 'manifest.json');
  await writeFile(manifestPath, JSON.stringify(manifest));
  const browserEvidence = completeBrowserEvidence(manifest, expected.origin);
  let drift = null;
  const requests = [];
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    requests.push(url);
    assert.equal(new URL(url).origin, expected.origin);
    assert.equal(options.redirect, 'error');
    assert.ok(options.signal instanceof AbortSignal);
    const path = new URL(url).pathname;
    let body = path.includes('/readiness/') ? readiness : path === '/api/audience-context' ? audience
      : path.includes('/read-models/') ? {data: {}, resultFingerprint: hash, sourceRevisionFingerprint: hash}
        : path.includes('/mechanics/') ? {version: 1, contentFingerprint: hash}
          : path.includes('/assets/') ? script : html;
    if (drift === 'asset' && path.includes('/assets/')) body += 'tampered';
    if (drift === 'audience' && path === '/api/audience-context') body = {...audience, policyRevision: 'B'.repeat(64)};
    if (drift === 'runtime' && path.includes('/readiness/')) body = {...readiness, status: 'failed'};
    return new Response(typeof body === 'string' ? body : JSON.stringify(body), {
      headers: {'cache-control': path.includes('/assets/') ? 'private, immutable' : 'private, no-store'} });
  });
  const options = {manifestPath, baseUrl: expected.origin, trustedPublicKey: keys.publicKey.export({type: 'spki', format: 'pem'}), browserEvidence};
  const report = await verifyLiveRelease(options);
  assert.equal(report.baseUrl, expected.origin);
  assert.equal(report.status, 'passed');
  assert.ok(requests.some(url => url.includes('/read-models/')));
  for (drift of ['asset', 'audience', 'runtime']) await assert.rejects(verifyLiveRelease(options));
  drift = null;
  await assert.rejects(verifyLiveRelease({...options, browserEvidence: {...browserEvidence, url: 'http://localhost:6217/ui/dnd2024-play'}}));
  const tampered = {...manifest, expectedRuntime: {...expected, origin: 'http://98.128.172.182'}};
  await writeFile(manifestPath, JSON.stringify(tampered));
  await assert.rejects(verifyLiveRelease(options), /signature is invalid/);
});

const hash = 'A'.repeat(64);
function fixture() {
  const checks = ['database', 'application-registration', 'active-catalog-snapshot', 'catalog-materialization',
    'extension-resolution', 'query-callability', 'web-page-release', 'audience-binding']
    .map(name => ({ name, status: 'ready', code: name.toUpperCase(), evidence: { revision: '2', fingerprint: hash } }));
  const audience = { status: 'bound', applicationId: 'sample', stateSpaceId: 'sample-main', campaignId: 'campaign.fixture', role: 'game-master', actorId: null, policyRevision: hash, bindingRevision: hash, participationRevision: null };
  return { audience, readiness: { status: 'ready', applicationId: 'sample', checks }, expected: {
    ...audience, checks: Object.fromEntries(checks.map(({ name, code, evidence }) => [name, { code, ...evidence }])),
    readModels: [{ entityId: 'actor.fixture', queryId: 'sample.query.sheet', resultFingerprint: hash, sourceRevisionFingerprint: hash }],
    actions: [{ mechanicId: 'sample.mechanic.inspect', version: 1, contentFingerprint: hash }]
  } };
}

const browserChecks = ['no-wheel-zoom', 'ganji-dossier', 'player-dm-boundary',
  'complete-feature-traversal', 'campaign-records', 'map-pixels-markers',
  'inventory-first-entry-style', 'registry-items', 'registry-recipes', 'direct-entry',
  'reload-back-forward-retry', 'read-only-data-ledger', 'cache-reuse', 'current-no-chatbox'];

function completeBrowserEvidence(manifest, baseUrl) {
  const runtimeFingerprint = 'a'.repeat(64), fixtureFingerprint = 'b'.repeat(64);
  const browser = { name: 'fixture-browser', version: '1' };
  const machine = { platform: 'fixture', release: 'fixture', cpu: 'fixture', logicalProcessors: 4, memoryGiB: 16 };
  const assets = { ...recordSetEvidence(['/assets/app.css', '/assets/app.js']), cssCount: 1, jsCount: 1 };
  const views = Object.fromEntries(workloadViews.map(view => [view,
    { status: 'ready', complete: true, ...recordSetEvidence([view]) }]));
  const scaling = { owner: 'production-read-path', unrelatedPopulationMultiplier: 2, sampleCount: 20,
    baseSql: 8, doubledSql: 9, baseAllocatedBytes: 1000, doubledAllocatedBytes: 1050,
    baseMedianMs: 10, doubledMedianMs: 10.5 };
  const fixtureCase = { owner: 'production-read-path', fixtureFingerprint, sampleCount: 20,
    medianMs: 10, allocatedBytes: 1000, sql: 8, responseBytes: 2048 };
  const requests = [
    { path: '/api/audience-context', parentInteraction: 'navigation', method: 'GET', status: 200, outcome: 'response' },
    { path: '/api/applications/sample/state-spaces/sample-main/media-batch', parentInteraction: 'map', method: 'POST', status: 200, outcome: 'response' },
    { path: '/api/applications/sample/content', parentInteraction: 'installed-content', method: 'GET', status: 200, outcome: 'response' },
    { path: '/api/applications/sample/read-models/readable-rules', parentInteraction: 'rules', method: 'GET', status: 200, outcome: 'response' },
  ];
  const pageContentHash = manifest.expectedRuntime.checks['web-page-release'].fingerprint.toLowerCase();
  const live = { status: 'available', listener: baseUrl, activeRevision: 2, activeEntityId: 'page',
    pageContentHash, bundleSha256: 'd'.repeat(64), runtimeFingerprint,
    audience: { role: 'game-master', actorId: null, applicationId: manifest.expectedRuntime.applicationId,
      stateSpaceId: manifest.expectedRuntime.stateSpaceId, campaignId: manifest.expectedRuntime.campaignId } };
  const workloadProfiles = ['shared-table'].map(audienceView => ({
    audienceView, perspective: 'dm', readOnly: true,
    listener: baseUrl, browser, machine, workloadHarnessSha256: workloadHarnessFingerprint(),
    liveBefore: structuredClone(live), liveAfter: structuredClone(live),
    workloadReference: { kind: 'independent-api', runtimeFingerprint, fixtureFingerprint, audienceView,
      views, assets, browser, machine, baseline: { cold: { sampleCount: 20, completeWorkloadP50Ms: 1000 },
        warm: { sampleCount: 20, completeWorkloadP50Ms: 900 } } },
    runs: Array.from({ length: 20 }, (_, index) => ['cold', 'warm'].map(cacheState => ({
      id: `${cacheState}-${index + 1}`, cacheState, status: 'collected', scriptErrorCount: 0,
      requestCount: requests.length, requests: structuredClone(requests), assets, firstReadyAssets: assets,
      inventoryStyle: { display: 'flex', cursor: 'pointer', cue: true },
      mapEvidence: { width: 2000, height: 1500, markers: 3 },
      blockedWrites: 1, blockedOperations: [{ method: 'POST', path: '/api/acceptance-mutation-probe' }],
      traversal: structuredClone(views),
      marks: { firstReady: 100, completeWorkload: 400, warmReturn: 5 },
      checks: Object.fromEntries(workloadChecks.map(check => [check, 'passed'])),
      serverMeasurements: { owner: 'production-read-path', runtimeFingerprint, fixtureFingerprint,
        sampleId: `${cacheState}-${index + 1}`, warmSourceReads: 0,
        sql: { campaign: 12, factions: 12, knowledge: 16, chronology: 12, completeWorkload: 80 },
        firstRequestCosts: Object.fromEntries(['mappingUpdate', 'planEviction', 'sourceDrift'].map(condition =>
          [condition, { owner: 'production-read-path', sql: 12, sourceReads: 1, allocatedBytes: 1000, elapsedMs: 10 }])),
        scaling: { campaign: scaling, factions: scaling }, fixtureCases: { small: fixtureCase, large: fixtureCase,
          doubledUnrelated: fixtureCase }, retainedCaches: { retainedEntries: 10, retainedBytes: 4096,
          activeRequests: 0, hits: 20, misses: 10 } },
    }))).flat(),
  }));
  const origins = [{ origin: baseUrl, status: 'passed', pageId: manifest.pageId,
    pageContentHash, bundleSha256: 'd'.repeat(64), runtimeFingerprint }];
  if (!['localhost', '127.0.0.1', '[::1]'].includes(new URL(baseUrl).hostname)) origins.push({
    ...origins[0], origin: 'http://localhost:6217',
  });
  const release = { pageContentHash, bundleSha256: 'd'.repeat(64), runtimeFingerprint };
  return { manifestFingerprint: sha256(Buffer.from(canonicalJson(manifest))),
    url: baseUrl + '/ui/' + manifest.pageId, role: manifest.expectedRuntime.role,
    checks: Object.fromEntries(browserChecks.map(name => [name, 'passed'])),
    observations: ['Synthetic unit-test evidence'], workloadProfiles, origins,
    artifacts: { 'test:fixture': { kind: 'test', sha256: 'e'.repeat(64) } },
    dispositions: Object.fromEntries([
      ...Array.from({ length: 12 }, (_, index) => `W${String(index).padStart(2, '0')}`),
      ...Array.from({ length: 14 }, (_, index) => `R${String(index).padStart(2, '0')}`),
    ].map(id => [id, { status: 'passed', evidence: ['test:fixture'] }])),
    restart: { before: release, after: structuredClone(release) },
    recovery: { previousReleaseUsable: true, dataAndBlobsRecoverable: true } };
}

test('release signing requires an independent trusted key and detects payload tampering', () => {
  const keys = generateKeyPairSync('ed25519');
  const publicKey = keys.publicKey.export({ type: 'spki', format: 'pem' });
  const manifest = signManifest({ schemaVersion: 2, expectedRuntime: fixture().expected, files: [] }, keys.privateKey);
  assert.equal(verifyManifest(manifest, publicKey).schemaVersion, 2);
  assert.throws(() => verifyManifest({ ...manifest, files: [{ path: 'other.js' }] }, publicKey));
  assert.throws(() => verifyManifest(manifest));
  assert.throws(() => verifyManifest({ schemaVersion: 2 }, publicKey));
  const stranger = generateKeyPairSync('ed25519').publicKey.export({ type: 'spki', format: 'pem' });
  assert.throws(() => verifyManifest(manifest, stranger));
});

test('release target rejects every owner fingerprint or revision drift and audience mismatch', () => {
  const { expected, readiness, audience } = fixture();
  verifyRuntimeTarget(expected, readiness, audience);
  const gm = { ...audience, role: 'game-master', actorId: null, participationRevision: undefined };
  verifyRuntimeTarget({ ...expected, ...gm }, readiness, gm);
  for (const original of readiness.checks) for (const field of ['revision', 'fingerprint']) {
    const altered = structuredClone(readiness);
    altered.checks.find(check => check.name === original.name).evidence[field] = 'changed';
    assert.throws(() => verifyRuntimeTarget(expected, altered, audience));
  }
  assert.throws(() => verifyRuntimeTarget(expected, { ...readiness, status: 'failed' }, audience));
  assert.throws(() => verifyRuntimeTarget(expected, readiness, { ...audience, role: 'actor', actorId: 'actor.fixture' }));
  assert.throws(() => verifyRuntimeTarget(expected, readiness, { ...audience, stateSpaceId: 'wrong' }));
  for (const field of ['policyRevision', 'bindingRevision']) {
    assert.throws(() => verifyRuntimeTarget(expected, readiness, { ...audience, [field]: 'B'.repeat(64) }));
    assert.throws(() => verifyRuntimeTarget({ ...expected, [field]: undefined }, readiness, audience));
  }
  const actor = { ...audience, role: 'actor', actorId: 'actor.fixture', participationRevision: hash };
  verifyRuntimeTarget({ ...expected, ...actor }, readiness, actor);
  assert.throws(() => verifyRuntimeTarget({ ...expected, ...actor, participationRevision: undefined }, readiness, actor));
  assert.throws(() => verifyRuntimeTarget(expected, { ...readiness, checks: [] }, audience));
});

test('runtime probes compare actual callable query results and action contract fingerprints', async () => {
  const { expected, readiness, audience } = fixture();
  const response = path => path.includes('/readiness/') ? readiness : path === '/api/audience-context' ? audience
    : path.includes('/read-models/') ? { data: {}, resultFingerprint: hash, sourceRevisionFingerprint: hash }
      : { version: 1, contentFingerprint: hash };
  await probeRuntime(expected, async path => response(path));
  await assert.rejects(probeRuntime(expected, async path => path.includes('/read-models/')
    ? { ...response(path), resultFingerprint: 'B'.repeat(64) } : response(path)));
  await assert.rejects(probeRuntime(expected, async path => path.includes('/mechanics/')
    ? { ...response(path), version: 2 } : response(path)));
});

test('browser proof must bind the exact signed release, role, listener and completed checks', () => {
  const manifest = { pageId: 'dnd2024-play', expectedRuntime: fixture().expected };
  const baseUrl = 'http://localhost:6217';
  const evidence = completeBrowserEvidence(manifest, baseUrl);
  verifyBrowserEvidence(manifest, baseUrl, evidence);
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl));
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, { ...evidence, checks: {} }));
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, { ...evidence, role: 'actor' }));
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, { ...evidence, manifestFingerprint: 'B'.repeat(64) }));
  for (const mutate of [
    changed => { changed.workloadProfiles[0].listener = 'http://localhost:9999'; },
    changed => { changed.origins[0].bundleSha256 = 'f'.repeat(64); },
    changed => { changed.dispositions.R13.status = 'blocked'; },
    changed => { changed.dispositions.R13.evidence = ['missing:artifact']; },
    changed => { changed.restart.after.runtimeFingerprint = 'f'.repeat(64); },
    changed => { changed.recovery.previousReleaseUsable = false; },
  ]) {
    const changed = structuredClone(evidence); mutate(changed);
    assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, changed));
  }
});

test('signed parameterized probes retain item, perspective and campaign without accepting scope drift', async () => {
  const { expected, readiness, audience } = fixture();
  Object.assign(expected.readModels[0], { input: { itemId: 'item.fixture', offset: 0 }, perspective: 'player', campaignId: expected.campaignId });
  let query;
  const response = async path => {
    if (path.includes('/readiness/')) return readiness;
    if (path === '/api/audience-context') return audience;
    if (path.includes('/read-models/')) { query = new URL(path, 'http://localhost'); return { data: {}, resultFingerprint: hash, sourceRevisionFingerprint: hash }; }
    return { version: 1, contentFingerprint: hash };
  };
  await probeRuntime(expected, response);
  assert.deepEqual(JSON.parse(query.searchParams.get('input')), expected.readModels[0].input);
  assert.equal(query.searchParams.get('perspective'), 'player');
  assert.equal(query.searchParams.get('campaignId'), audience.campaignId);
  expected.readModels[0].campaignId = 'other';
  await assert.rejects(probeRuntime(expected, response));
});

test('item release evidence rejects missing tabs, observer drift, unprobed selections and overflow', () => {
  const expected = fixture().expected;
  expected.itemView = { observerId: 'actor.fixture', itemIds: ['item.fixture'], perspectives: ['player'], widths: [320] };
  expected.readModels = ['details', 'recipes', 'uses'].map(tab => ({ entityId: 'actor.fixture', queryId: `dnd2024.query.inventory-item-${tab}`, input: { itemId: 'item.fixture' }, perspective: 'player', campaignId: expected.campaignId }));
  const manifest = { pageId: 'dnd2024-play', expectedRuntime: expected }, baseUrl = 'http://localhost:6217';
  const evidence = { ...completeBrowserEvidence(manifest, baseUrl),
    checks: Object.fromEntries([...browserChecks, 'item-return', 'item-request-budget', 'item-knowledge-boundary'].map(name => [name, 'passed'])),
    itemViews: [{ itemId: 'item.fixture', observerId: 'actor.fixture', perspective: 'player', tabs: { details: 'ready', recipes: 'empty', uses: 'partial' } }],
    itemLayouts: [{ width: 320, clientWidth: 305, scrollWidth: 305 }] };
  verifyBrowserEvidence(manifest, baseUrl, evidence);
  for (const mutate of [e => { delete e.itemViews[0].tabs.uses; }, e => { e.itemViews[0].observerId = 'other'; },
    e => { e.itemLayouts[0].scrollWidth = 321; }, e => { e.itemViews = []; }, e => { e.itemLayouts = []; }]) {
    const changed = structuredClone(evidence); mutate(changed); assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, changed));
  }
  expected.readModels.pop(); evidence.manifestFingerprint = sha256(Buffer.from(canonicalJson(manifest)));
  assert.throws(() => verifyBrowserEvidence(manifest, baseUrl, evidence));
});
