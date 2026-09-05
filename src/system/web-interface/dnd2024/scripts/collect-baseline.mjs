import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { cpus, platform, release, totalmem } from 'node:os';
import { dirname, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const scriptPath = fileURLToPath(import.meta.url);
export const webRoot = resolve(dirname(scriptPath), '..');
const repositoryRoot = resolve(webRoot, '../../../..');
export const requiredMarks = ['shell', 'bootstrap', 'activeView', 'character', 'map'];
export const sha256 = value => createHash('sha256').update(value).digest('hex');
export const machineProfile = () => ({
  platform: platform(), release: release(), cpu: cpus()[0]?.model ?? 'unknown',
  logicalProcessors: cpus().length, memoryGiB: Number((totalmem() / 1024 ** 3).toFixed(1)),
});

export function normalizeListener(value) {
  const url = new URL(value);
  assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(url.hostname), 'Baseline is local-only');
  assert.ok(['http:', 'https:'].includes(url.protocol));
  assert.ok(!url.username && !url.password && !url.search && !url.hash && url.pathname === '/');
  return url.origin;
}

export function worktreeEvidence(excludedPaths = []) {
  const git = (...args) => execFileSync(process.env.GIT_EXECUTABLE || 'git', args, {
    cwd: repositoryRoot, maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'],
  });
  const exclusions = excludedPaths.map(path => relative(repositoryRoot, resolve(path)).replaceAll('\\', '/'))
    .filter(path => path && !path.startsWith('../') && !/^[A-Za-z]:/.test(path));
  const commit = git('rev-parse', 'HEAD').toString().trim();
  const diff = git('-c', 'core.autocrlf=false', 'diff', '--binary', '--no-ext-diff', 'HEAD', '--', '.',
    ...exclusions.map(path => ':(exclude)' + path));
  const untracked = git('ls-files', '--others', '--exclude-standard', '-z').toString().split('\0')
    .filter(path => path && !path.startsWith('_to_delete/') && !exclusions.includes(path)).sort();
  const trackedDiffSha256 = sha256(diff);
  const untrackedManifestSha256 = sha256(untracked.map(path =>
    sha256(readFileSync(resolve(repositoryRoot, path))) + '  ' + path).join('\n'));
  return {
    commit, branch: git('branch', '--show-current').toString().trim(),
    dirty: diff.length > 0 || untracked.length > 0, trackedDiffSha256, untrackedManifestSha256,
    untrackedFileCount: untracked.length, excludedEvidencePaths: exclusions,
    fingerprint: sha256(JSON.stringify({ commit, trackedDiffSha256, untrackedManifestSha256 })),
  };
}

export function gateEvidence(result, kind) {
  const rawOutput = ((result.stdout ?? '') + (result.stderr ?? '')).trim();
  const lines = rawOutput.split(/\r?\n/);
  const summary = Object.fromEntries(lines.flatMap(line => {
    const match = line.match(/^(?:#|ℹ) (tests|pass|fail|cancelled|skipped|todo) (\d+)$/u);
    return match ? [[match[1], Number(match[2])]] : [];
  }));
  return {
    exitCode: result.status ?? 1, status: result.status === 0 ? 'passed' : 'failed', rawOutput,
    ...(kind === 'tests' ? { summary } : {}),
    ...(kind === 'typecheck' ? { diagnostics: lines.filter(line => /error TS\d+:/.test(line)) } : {}),
  };
}

function runGate(args, kind) {
  return { command: 'node ' + args.join(' '), ...gateEvidence(spawnSync(process.execPath, args, {
    cwd: webRoot, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024, timeout: 300_000,
  }), kind) };
}

function bundleEvidence() {
  const root = resolve(webRoot, 'server-dist');
  const walk = path => readdirSync(path, { withFileTypes: true }).flatMap(entry =>
    entry.isDirectory() ? walk(resolve(path, entry.name)) : [resolve(path, entry.name)]);
  const artifacts = walk(root).sort().map(path => {
    const bytes = readFileSync(path);
    return { path: relative(root, path).replaceAll('\\', '/'), bytes: bytes.length,
      gzipBytes: gzipSync(bytes).length, sha256: sha256(bytes) };
  });
  return { bytes: artifacts.reduce((sum, file) => sum + file.bytes, 0),
    gzipBytes: artifacts.reduce((sum, file) => sum + file.gzipBytes, 0),
    sha256: sha256(JSON.stringify(artifacts)), artifacts };
}

async function fetchBytes(origin, path) {
  const response = await fetch(new URL(path, origin), { redirect: 'error', signal: AbortSignal.timeout(15_000) });
  assert.equal(response.status, 200, path + ' HTTP ' + response.status);
  return Buffer.from(await response.arrayBuffer());
}

// Inspect every immutable asset, including lazy chunks. This has no publication/write route.
export async function livePageEvidence(listener) {
  listener = normalizeListener(listener);
  const json = async path => JSON.parse((await fetchBytes(listener, path)).toString('utf8'));
  const root = '/api/control/web/applications/dnd2024/pages/';
  const diagnostic = await json(root + 'dnd2024-play');
  const activeEntityId = diagnostic.page.entityId;
  const detail = await json(root + encodeURIComponent(activeEntityId));
  const activeRevision = detail.content.activeRevision;
  assert.ok(Number.isInteger(activeRevision) && activeRevision > 0);
  const revision = await json(root + encodeURIComponent(activeEntityId) + '/revisions/' + activeRevision);
  assert.equal(revision.summary.revision, activeRevision);
  const html = await fetchBytes(listener, '/ui/dnd2024-play');
  assert.equal(html.toString('utf8'), revision.html, 'Published HTML differs from active revision');
  const files = [{ path: 'index.html', bytes: html.length, sha256: sha256(html) }];
  for (const asset of revision.assets) {
    assert.ok(/^assets\/[A-Za-z0-9_./-]+$/.test(asset.path) && !asset.path.includes('..'));
    const bytes = await fetchBytes(listener, '/ui/dnd2024-play/' + asset.path);
    assert.equal(sha256(bytes).toUpperCase(), asset.contentHash.toUpperCase(), 'Live asset drift: ' + asset.path);
    files.push({ path: asset.path, bytes: bytes.length, sha256: sha256(bytes) });
  }
  files.sort((a, b) => a.path.localeCompare(b.path));
  assert.equal(files.length, revision.summary.assetCount + 1);
  const readiness = await json('/api/readiness/applications/dnd2024');
  const page = readiness.checks.find(check => check.name === 'web-page-release');
  assert.equal(page.evidence.revision, String(activeRevision));
  assert.equal(page.evidence.fingerprint, revision.summary.contentHash);
  const audience = await json('/api/audience-context');
  const runtimePins = readiness.checks.map(check => ({ name: check.name, status: check.status,
    revision: check.evidence?.revision, fingerprint: check.evidence?.fingerprint }));
  return {
    status: 'available', listener, activeRevision, activeEntityId,
    pageContentHash: revision.summary.contentHash, bundleSha256: sha256(JSON.stringify(files)),
    bundleBytes: files.reduce((sum, file) => sum + file.bytes, 0), assetCount: files.length - 1,
    runtimeFingerprint: sha256(JSON.stringify({ runtimePins, audience })),
    audience: { role: audience.role, applicationId: audience.applicationId, stateSpaceId: audience.stateSpaceId,
      campaignId: audience.campaignId, actorId: audience.actorId }, files,
  };
}

export function sameLivePage(a, b) {
  return a?.status === 'available' && b?.status === 'available' &&
    ['listener', 'activeRevision', 'activeEntityId', 'pageContentHash', 'bundleSha256', 'runtimeFingerprint']
      .every(key => a[key] === b[key]);
}

export function summarizeSamples(values) {
  if (!Array.isArray(values) || !values.length || values.some(value => !Number.isFinite(value) || value < 0)) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const percentile = value => sorted[Math.min(sorted.length - 1, Math.ceil(value * sorted.length) - 1)];
  return { sampleCount: sorted.length, p50Ms: percentile(0.5), p95Ms: percentile(0.95) };
}

function summarizeCounts(values, unit) {
  const summary = summarizeSamples(values);
  return summary ? { sampleCount: summary.sampleCount, p50: summary.p50Ms, p95: summary.p95Ms, unit } : null;
}

export function summarizeRequests(runs, listener, browser) {
  const byPath = new Map();
  for (const run of runs) for (const request of run.requests ?? []) {
    const key = JSON.stringify([request.parentInteraction, request.method, request.path]);
    if (!byPath.has(key)) byPath.set(key, []);
    byPath.get(key).push(request);
  }
  return {
    listener, browser,
    counts: summarizeCounts(runs.map(run => run.requestCount), 'requests'),
    transferredPayload: summarizeCounts(runs.map(run => run.payloadBytes), 'bytes'),
    browserCacheHits: summarizeCounts(runs.map(run => run.browserCacheHits), 'requests'),
    paths: [...byPath].sort(([a], [b]) => a.localeCompare(b)).map(([key, entries]) => {
      const [parentInteraction, method, path] = JSON.parse(key);
      const statuses = {};
      const cacheResults = {};
      for (const entry of entries) {
        const status = entry.status ?? entry.outcome;
        statuses[status] = (statuses[status] ?? 0) + 1;
        cacheResults[entry.cacheResult] = (cacheResults[entry.cacheResult] ?? 0) + 1;
      }
      return { parentInteraction, method, path, requestCount: entries.length, statuses, cacheResults,
        incompleteCount: entries.filter(entry => entry.outcome === 'incomplete').length,
        duration: summarizeSamples(entries.map(entry => entry.durationMs).filter(Number.isFinite)), listener, browser };
    }),
  };
}

export function browserEvidence(source, live) {
  const requiredSamples = { cold: 20, warm: 20 };
  if (!source) return { status: 'blocked', requiredSamples, reason: live?.status === 'available'
    ? 'Live listener is available, but no browser samples were supplied.' : 'No verified live listener is available.' };
  const problems = [];
  if (source.samplerSha256 !== sha256(readFileSync(resolve(webRoot, 'scripts/sample-browser-baseline.mjs'))))
    problems.push('Browser sampler source differs from the measured version.');
  if (!sameLivePage(source.liveBefore, live) || !sameLivePage(source.liveBefore, source.liveAfter))
    problems.push('Listener, runtime, audience, or published revision does not match the browser run.');
  if (!source.browser?.name || !source.browser?.version || source.listener !== live?.listener)
    problems.push('An exact listener and browser identity are required.');
  if (JSON.stringify(source.machine) !== JSON.stringify(machineProfile())) problems.push('Target machine differs.');
  if (source.readOnly !== true) problems.push('Read-only browser guard was not enabled.');
  const metrics = {};
  const requests = {};
  const viewFailures = [];
  const observedSamples = {};
  const ids = new Set();
  for (const run of source.runs ?? []) {
    if (!run.id || ids.has(run.id)) problems.push('Duplicate or missing sample ID.');
    ids.add(run.id);
    if (!['cold', 'warm'].includes(run.cacheState)) problems.push('Invalid cache group.');
    if (!Number.isSafeInteger(run.requestCount) || !Array.isArray(run.requests) || run.requestCount !== run.requests.length)
      problems.push('Missing or inconsistent request ledger.');
    else if (run.requests.some(request =>
      Object.keys(request).some(key => !['path', 'method', 'parentInteraction', 'durationMs', 'status',
        'payloadBytes', 'cacheResult', 'outcome'].includes(key)) ||
      typeof request.path !== 'string' || !request.path.startsWith('/') ||
      request.path.includes('?') || request.path.includes('#') ||
      !['GET', 'HEAD'].includes(request.method) ||
      !(Number.isFinite(request.durationMs) && request.durationMs >= 0 || request.durationMs === null && request.outcome === 'incomplete') ||
      request.payloadBytes !== null && (!Number.isSafeInteger(request.payloadBytes) || request.payloadBytes < 0)))
      problems.push('Invalid or unsafe request metadata.');
  }
  for (const cacheState of ['cold', 'warm']) {
    const runs = (source.runs ?? []).filter(run => run.cacheState === cacheState);
    observedSamples[cacheState] = runs.length;
    requests[cacheState] = summarizeRequests(runs, source.listener, source.browser);
    if (runs.length < 20) problems.push(cacheState + ': fewer than 20 runs.');
    metrics[cacheState] = {};
    for (const name of requiredMarks) {
      const unavailable = runs.filter(run => run.marks?.[name] === undefined &&
        ['stale', 'empty', 'error', 'forbidden', 'unavailable'].includes(run.outcomes?.[name]?.status) &&
        typeof run.outcomes[name].reason === 'string' && run.outcomes[name].reason.length > 0);
      // A baseline may observe a broken view. Never turn its elapsed time into a ready latency.
      const ready = runs.filter(run => !unavailable.includes(run));
      const summary = summarizeSamples(ready.map(run => run.marks?.[name]));
      if (!summary && ready.length) problems.push(cacheState + ': missing or invalid ' + name + ' timing.');
      if (unavailable.length) viewFailures.push({ cacheState, view: name, count: unavailable.length });
      metrics[cacheState][name] = { ...(summary ?? { sampleCount: 0, p50Ms: null, p95Ms: null }),
        unavailableCount: unavailable.length, status: unavailable.length ? 'view-unavailable' : 'measured',
        listener: source.listener, browser: source.browser };
    }
    for (const view of ['current', 'browser-script']) {
      const count = runs.filter(run => view === 'current' ? run.outcomes?.current?.status === 'error' : run.scriptErrorCount > 0).length;
      if (count) viewFailures.push({ cacheState, view, count });
    }
    if (runs.some(run => !['collected', 'passed'].includes(run.status))) problems.push(cacheState + ': failed runs.');
  }
  // Invalid evidence never gets plausible-looking percentile tables.
  return { status: problems.length ? 'invalid' : viewFailures.length ? 'complete-with-view-failures' : 'complete',
    requiredSamples, observedSamples, problems, viewFailures, perspective: source.perspective, audienceView: source.audienceView,
    listener: source.listener, browser: source.browser, protocol: source.protocol,
    samplerSha256: source.samplerSha256,
    metrics: problems.length ? {} : metrics, requests: problems.length ? {} : requests,
    runs: problems.length ? [] : source.runs, limitations: source.limitations ?? [] };
}

function optionsFrom(argv) {
  const result = { listeners: [], output: resolve(webRoot, '.tmp/website-slice-0/baseline.json') };
  for (let i = 0; i < argv.length; i++) {
    const name = argv[i]; const value = argv[++i];
    assert.ok(value, 'Missing value for ' + name);
    if (name === '--listener') result.listeners.push(normalizeListener(value));
    else if (name === '--output') result.output = resolve(value);
    else if (name === '--browser-results') result.browserResults = resolve(value);
    else throw new Error('Unknown option ' + name);
  }
  if (!result.listeners.length) result.listeners = ['http://localhost:6217', 'https://localhost:5144'];
  return result;
}

async function main() {
  const options = optionsFrom(process.argv.slice(2));
  const exclusions = [options.output, ...(options.browserResults ? [options.browserResults] : [])];
  const source = worktreeEvidence(exclusions);
  const gates = {
    nodeTests: runGate(['--test', '--test-reporter=tap', 'test/*.test.js'], 'tests'),
    mountedTests: runGate(['--import', 'tsx', '--test', '--test-reporter=tap', 'test/mounted/*.test.tsx'], 'tests'),
    typecheck: runGate(['node_modules/typescript/bin/tsc', '--noEmit'], 'typecheck'),
    serverBuild: runGate(['node_modules/vite/bin/vite.js', 'build', '--config', 'vite.server.config.ts'], 'build'),
  };
  const probes = [];
  let live;
  for (const listener of options.listeners) {
    try { live = await livePageEvidence(listener); break; }
    catch (error) { probes.push({ listener, status: 'unavailable', reason: error.message }); }
  }
  live ??= { status: 'blocked', probes, reason: 'No configured listener could be verified. Build time is not a live measurement.' };
  const browserMeasurements = browserEvidence(options.browserResults
    ? JSON.parse(readFileSync(options.browserResults, 'utf8')) : null, live);
  const report = {
    schema: 'dnd2024.website-baseline.v2', generatedAtUtc: new Date().toISOString(), source,
    sourceUnchangedDuringGates: source.fingerprint === worktreeEvidence(exclusions).fingerprint,
    target: { machine: machineProfile(), browser: browserMeasurements.browser ?? null }, gates,
    sourceBundle: gates.serverBuild.status === 'passed' ? bundleEvidence() : null, live, browserMeasurements,
    invariants: { livePagePublishedOrActivated: false, privatePayloadBodiesRecorded: false },
  };
  mkdirSync(dirname(options.output), { recursive: true });
  writeFileSync(options.output, JSON.stringify(report, null, 2) + '\n');
  console.log(JSON.stringify({ output: options.output, gates: Object.fromEntries(Object.entries(gates).map(([key, gate]) =>
    [key, { status: gate.status, summary: gate.summary, diagnostics: gate.diagnostics }])),
  live: { status: live.status, activeRevision: live.activeRevision }, browser: browserMeasurements.status }));
  if (Object.values(gates).some(gate => gate.status !== 'passed') || !report.sourceUnchangedDuringGates ||
      !['complete', 'complete-with-view-failures'].includes(browserMeasurements.status)) process.exitCode = 1;
}

if (resolve(process.argv[1] ?? '') === scriptPath) await main();
