import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { audienceViewFor, livePageEvidence, machineProfile, normalizeListener, sameLivePage, sha256, webRoot } from './collect-baseline.mjs';

// Private bodies, query values, cookies, console messages and DOM text never enter the report.
export function requestMetadata(request, parentInteraction, started) {
  return { path: new URL(request.url()).pathname, method: request.method(), parentInteraction,
    started, durationMs: null, status: null, payloadBytes: null, cacheResult: 'not-reported', outcome: 'pending' };
}

export const reportedPayloadBytes = value => Number.isSafeInteger(value) && value >= 0 ? value : null;

export function initializeBrowserProbe({ perspective }) {
  localStorage.setItem('dnd2024-table-mode', perspective);
  window.__DND_BASELINE_BLOCKED_WRITES__ = 0;
  window.__DND_BASELINE_BLOCKED_OPERATIONS__ = [];
  window.__DND_BASELINE_SCRIPT_ERRORS__ = 0;
  window.addEventListener('error', () => { window.__DND_BASELINE_SCRIPT_ERRORS__++; });
  window.addEventListener('unhandledrejection', () => { window.__DND_BASELINE_SCRIPT_ERRORS__++; });
  const allowed = method => ['GET', 'HEAD'].includes(String(method ?? 'GET').toUpperCase());
  const rejectWrite = () => {
    window.__DND_BASELINE_BLOCKED_WRITES__++;
    throw new Error('Baseline blocks writes');
  };
  const originalFetch = window.fetch.bind(window);
  window.fetch = (input, init) => {
    if (!allowed(init?.method ?? input?.method)) {
      window.__DND_BASELINE_BLOCKED_WRITES__++;
      window.__DND_BASELINE_BLOCKED_OPERATIONS__.push({
        method: String(init?.method ?? input?.method),
        path: new URL(typeof input === 'string' || input instanceof URL ? String(input) : input.url, location.href).pathname,
      });
      return Promise.reject(new Error('Baseline blocks writes'));
    }
    return originalFetch(input, init);
  };
  const open = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function (method, ...args) {
    if (!allowed(method)) return rejectWrite();
    return open.call(this, method, ...args);
  };
  navigator.sendBeacon = url => {
    window.__DND_BASELINE_BLOCKED_WRITES__++;
    window.__DND_BASELINE_BLOCKED_OPERATIONS__.push({ method: 'BEACON', path: new URL(url, location.href).pathname });
    return false;
  };
  HTMLFormElement.prototype.submit = rejectWrite;
  HTMLFormElement.prototype.requestSubmit = rejectWrite;
  document.addEventListener('submit', event => { event.preventDefault(); rejectWrite(); }, true);
  // Use DOM commit observations for the unchanged release, whose shell mark predates React commit.
  window.__DND_BASELINE_DOM_MARKS__ = {};
  const observe = () => {
    const marks = window.__DND_BASELINE_DOM_MARKS__;
    if (!marks.shell && document.querySelector('.information-hub')) marks.shell = performance.now();
    if (!marks.activeView && document.querySelector('.information-hub[data-perspective="' + perspective + '"]:not(.bootstrap-shell):not(.rules-only-hub) #main-view-heading'))
      marks.activeView = performance.now();
  };
  new MutationObserver(observe).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ['data-perspective'] });
  observe();
}

async function sample(page, client, cacheState, index) {
  const requests = [];
  const pending = new Set();
  const byRequest = new Map();
  const cacheHits = new Set();
  const networkUrls = new Map();
  const cachedUrls = new Set();
  const entryUrls = new Map();
  let phase = 'navigation';
  let scriptErrors = 0;
  const now = () => performance.now();
  const onRequest = request => {
    const entry = requestMetadata(request, phase, now());
    requests.push(entry); byRequest.set(request, entry);
    entryUrls.set(entry, request.url());
  };
  const finish = async request => {
    const entry = byRequest.get(request);
    if (!entry) return;
    entry.durationMs = now() - entry.started;
    delete entry.started;
    const response = await request.response();
    entry.status = response?.status() ?? null;
    const headers = response ? await response.allHeaders() : {};
    entry.cacheResult = headers['cache-status'] ?? headers['x-cache'] ?? 'not-reported';
    try { entry.payloadBytes = reportedPayloadBytes((await request.sizes()).responseBodySize); } catch { /* unavailable, not zero */ }
    entry.outcome = request.failure() ? 'network-error' : 'response';
  };
  const onFinished = request => {
    const task = finish(request).catch(() => {}).finally(() => pending.delete(task));
    pending.add(task);
  };
  const onError = () => { scriptErrors++; };
  const onNetworkRequest = event => networkUrls.set(event.requestId, event.request.url);
  const onCacheHit = event => {
    cacheHits.add(event.requestId);
    if (networkUrls.has(event.requestId)) cachedUrls.add(networkUrls.get(event.requestId));
  };
  page.on('request', onRequest);
  page.on('requestfinished', onFinished);
  page.on('requestfailed', onFinished);
  page.on('pageerror', onError);
  client.on('Network.requestServedFromCache', onCacheHit);
  client.on('Network.requestWillBeSent', onNetworkRequest);
  const run = { id: cacheState + '-' + index, cacheState, status: 'failed', marks: {}, outcomes: {}, requests };
  const time = () => page.evaluate(() => performance.now());
  const paint = () => page.evaluate(() => new Promise(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(resolve))));
  try {
    await page.goto(page.baselineUrl, { waitUntil: 'domcontentloaded', timeout: 15_000 });
    await page.waitForFunction(() => window.__DND_BASELINE_SCRIPT_ERRORS__ > 0 ||
      window.__DND_BASELINE_DOM_MARKS__?.activeView &&
      performance.getEntriesByName('dnd2024.bootstrap.response').length, null, { timeout: 15_000 });
    const observed = await page.evaluate(() => ({
      ...window.__DND_BASELINE_DOM_MARKS__,
      bootstrap: performance.getEntriesByName('dnd2024.bootstrap.response')[0]?.startTime,
    }));
    Object.assign(run.marks, observed);
    await page.waitForLoadState('networkidle', { timeout: 15_000 });
    if (!await page.locator('.information-hub:not(.bootstrap-shell):not(.rules-only-hub) #main-view-heading').count()) {
      for (const name of ['shell', 'bootstrap', 'activeView', 'character', 'map']) {
        if (run.marks[name] === undefined) run.outcomes[name] = {
          status: 'error', reason: 'The live application failed before the requested view could render.',
        };
      }
      run.outcomes.current = { status: 'error', reason: 'The live application failed before navigation.' };
      run.combatBoard = { status: 'unavailable', reason: 'The application did not render.' };
      run.status = 'collected';
      return run;
    }
    phase = 'character';
    const characterStart = await time();
    const navigation = page.getByRole('navigation', { name: 'Main table views', exact: true });
    await navigation.getByRole('button', { name: 'Party', exact: true }).click();
    await page.locator('.character-page, .view-render-error').waitFor({ state: 'visible' });
    run.step = 'open-character-sheet';
    if (await page.getByRole('button', { name: 'Character sheet', exact: true }).count()) {
      await page.getByRole('button', { name: 'Character sheet', exact: true }).click();
      await page.locator('.character-section-heading').waitFor({ state: 'visible' });
    }
    run.step = 'wait-canonical-sheet';
    await paint();
    const characterStatus = await page.evaluate(() => {
      if (document.querySelector('.view-render-error')) return 'error';
      for (const state of ['stale', 'error', 'forbidden', 'empty']) {
        if (document.querySelector('.character-state--' + state)) return state;
      }
      return document.querySelector('.character-sheet-v2') ? 'ready' : 'unavailable';
    });
    run.outcomes.character = { status: characterStatus, reason: characterStatus === 'ready'
      ? null : 'The unchanged live view did not render a ready canonical character sheet.' };
    if (characterStatus === 'ready') run.marks.character = await time() - characterStart;
    await page.waitForLoadState('networkidle', { timeout: 30_000 });
    phase = 'map';
    const mapStart = await time();
    await navigation.getByRole('button', { name: 'World', exact: true }).click();
    await page.getByRole('navigation', { name: 'World sections', exact: true })
      .getByRole('button', { name: 'Map', exact: true }).click();
    await page.waitForLoadState('networkidle', { timeout: 30_000 });
    if (await page.locator('.world-map-canvas').count()) {
      await page.locator('.world-map-canvas').waitFor({ state: 'visible' });
      await page.waitForFunction(() => [...document.querySelectorAll('.world-map-canvas img')]
        .every(image => image.complete && image.naturalWidth > 0));
      await paint();
      run.marks.map = await time() - mapStart;
    } else {
      run.outcomes.map = { status: 'unavailable', reason: 'No authorized map canvas rendered.' };
    }
    await page.waitForLoadState('networkidle', { timeout: 30_000 });
    phase = 'current';
    const currentStart = await time();
    await navigation.getByRole('button', { name: 'Current View', exact: true }).click();
    await page.locator('.current-scene-view, .view-render-error').waitFor({ state: 'visible' });
    if (await page.locator('.view-render-error').count()) {
      run.outcomes.current = { status: 'error', reason: 'The Current view error boundary rendered.' };
      run.combatBoard = { status: 'unavailable', reason: 'Current view could not render.' };
    } else if (await page.locator('.tactical-board-viewport').count()) {
      await paint();
      run.marks.combatBoard = await time() - currentStart;
    } else {
      run.combatBoard = { status: 'not-applicable', reason: 'No authorized tactical board in the current live situation.' };
    }
    await page.waitForLoadState('networkidle', { timeout: 30_000 });
    run.blockedWrites = await page.evaluate(() => window.__DND_BASELINE_BLOCKED_WRITES__);
    run.blockedOperations = await page.evaluate(() => window.__DND_BASELINE_BLOCKED_OPERATIONS__);
    assert.ok(requests.every(request => ['GET', 'HEAD'].includes(request.method)));
    run.status = 'collected';
  } catch (error) {
    run.failure = { phase, category: error.name }; // Error messages can contain private locator text.
    // Retain early shell/bootstrap observations even when a later await timed out.
    const earlyMarks = await page.evaluate(() => ({
      ...window.__DND_BASELINE_DOM_MARKS__,
      bootstrap: performance.getEntriesByName('dnd2024.bootstrap.response')[0]?.startTime,
    })).catch(() => ({}));
    for (const [name, value] of Object.entries(earlyMarks)) if (Number.isFinite(value)) run.marks[name] = value;
    run.failure.controls = await page.evaluate(() => ({
      sheetButtons: [...document.querySelectorAll('button')].filter(button => /character sheet/i.test(button.textContent)).length,
      sheets: document.querySelectorAll('.character-sheet-v2').length,
      errors: document.querySelectorAll('.character-state--error, [role="alert"]').length,
      loading: document.querySelectorAll('[aria-busy="true"]').length,
      perspective: document.querySelector('.information-hub')?.getAttribute('data-perspective'),
      rosterSize: document.querySelectorAll('.character-roster__member').length,
    })).catch(() => null);
    if (error.name === 'TimeoutError') {
      // A timed-out live view is an observation, not a readiness measurement.
      for (const name of ['shell', 'bootstrap', 'activeView', 'character', 'map']) {
        if (run.marks[name] === undefined && !run.outcomes[name]) run.outcomes[name] = {
          status: 'unavailable', reason: 'The live view did not become observable within the bounded browser timeout.',
        };
      }
      run.combatBoard ??= { status: 'unavailable', reason: 'Navigation did not reach a ready combat board.' };
      run.status = 'collected';
    }
  } finally {
    run.blockedWrites = await page.evaluate(() => window.__DND_BASELINE_BLOCKED_WRITES__).catch(() => null);
    run.blockedOperations = await page.evaluate(() => window.__DND_BASELINE_BLOCKED_OPERATIONS__).catch(() => []);
    await Promise.allSettled([...pending]);
    page.off('request', onRequest); page.off('requestfinished', onFinished);
    page.off('requestfailed', onFinished); page.off('pageerror', onError);
    client.off('Network.requestServedFromCache', onCacheHit);
    client.off('Network.requestWillBeSent', onNetworkRequest);
    run.browserCacheHits = cacheHits.size;
    run.scriptErrorCount = scriptErrors;
    run.requestCount = requests.length;
    for (const request of requests) {
      delete request.started;
      if (request.outcome === 'pending') request.outcome = 'incomplete';
      if (cachedUrls.has(entryUrls.get(request))) {
        request.cacheResult = 'browser-cache';
        // Chromium reported a cache hit: no network response body was transferred.
        request.payloadBytes = 0;
      }
    }
    run.payloadBytes = requests.every(request => request.payloadBytes !== null)
      ? requests.reduce((sum, request) => sum + request.payloadBytes, 0) : null;
  }
  return run;
}

async function main() {
  const options = { listener: 'http://localhost:6217', output: resolve(webRoot, '.tmp/website-slice-0/browser.json'), pairs: 20,
    perspective: 'player' };
  for (let i = 2; i < process.argv.length; i++) {
    const name = process.argv[i]; const value = process.argv[++i];
    assert.ok(value);
    if (name === '--listener') options.listener = normalizeListener(value);
    else if (name === '--output') options.output = resolve(value);
    else if (name === '--playwright-module') options.module = pathToFileURL(resolve(value)).href;
    else if (name === '--browser-executable') options.executable = resolve(value);
    else if (name === '--pairs') options.pairs = Number(value);
    else if (name === '--perspective') options.perspective = value;
    else throw new Error('Unknown option ' + name);
  }
  assert.ok(Number.isInteger(options.pairs) && options.pairs >= 1 && options.pairs <= 100);
  assert.ok(['player', 'dm'].includes(options.perspective), 'Perspective must be player or dm');
  const { chromium } = await import(options.module ?? 'playwright');
  const browser = await chromium.launch({ headless: true, ...(options.executable ? { executablePath: options.executable } : {}) });
  const report = {
    schema: 'dnd2024.browser-baseline.v2', listener: options.listener,
    samplerSha256: sha256(await readFile(fileURLToPath(import.meta.url))),
    browser: { name: 'Chromium (Playwright)', version: browser.version(), headless: true },
    machine: machineProfile(), generatedAtUtc: new Date().toISOString(), readOnly: true,
    protocol: {
      viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, throttling: 'none',
      navigationTimeoutMs: 15000, readinessTimeoutMs: 15000, viewTimeoutMs: 15000, idleTimeoutMs: 30000,
      cold: 'Fresh isolated browser context with HTTP cache explicitly cleared; server remains warm.',
      warm: 'Second full navigation in the same context after World, Character sheet, Map and Current view.',
      timingOrigins: { shell: 'navigation', bootstrap: 'navigation', activeView: 'navigation',
        character: 'Party navigation start through canonical sheet paint', map: 'World navigation start through map image load and paint',
        combatBoard: 'Current view navigation start through board paint, only when present' },
    },
    limitations: [
      'Cold means browser cache, not a server or OS restart. Warm private API reads still obey no-store.',
      'The unchanged live bundle is measured, not the newly built source bundle.',
      'DOM-observed shell and active-view marks measure commit; component marks do not promise image decode.',
      'No combat timing is invented when the live campaign has no tactical board.',
      'Automatic conversation creation and all other writes are blocked, not performed as a side effect of navigation.',
      'Only paths and transport metadata are retained; cache status is unknown unless reported by the browser or server.',
    ],
    runs: [],
  };
  const save = async () => {
    await mkdir(dirname(options.output), { recursive: true });
    await writeFile(options.output, JSON.stringify(report, null, 2) + '\n');
  };
  try {
    report.liveBefore = await livePageEvidence(options.listener);
    report.perspective = options.perspective;
    report.audienceView = audienceViewFor(report.liveBefore.audience, report.perspective);
    for (let index = 1; index <= options.pairs; index++) {
      const context = await browser.newContext({ viewport: report.protocol.viewport, deviceScaleFactor: 1, serviceWorkers: 'block' });
      try {
        await context.addInitScript(initializeBrowserProbe, { perspective: report.perspective });
        const page = await context.newPage();
        page.baselineUrl = options.listener + '/ui/dnd2024-play';
        page.setDefaultTimeout(15_000);
        const client = await context.newCDPSession(page);
        await client.send('Network.enable');
        await client.send('Network.clearBrowserCache');
        for (const cacheState of ['cold', 'warm']) {
          const run = await sample(page, client, cacheState, index);
          report.runs.push(run);
          console.log(JSON.stringify({ sample: run.id, status: run.status, requestCount: run.requestCount, failure: run.failure }));
          await save();
          if (run.status !== 'collected') throw new Error('Browser sample failed; partial evidence retained');
        }
      } finally { await context.close(); }
    }
    report.liveAfter = await livePageEvidence(options.listener);
    assert.ok(sameLivePage(report.liveBefore, report.liveAfter), 'Live revision or runtime changed during sampling');
  } finally {
    await save();
    await browser.close();
  }
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) await main();
