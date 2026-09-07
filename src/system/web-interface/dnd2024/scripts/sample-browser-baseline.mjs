import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { audienceViewFor, livePageEvidence, machineProfile, normalizeListener, sameLivePage, sha256, webRoot } from './collect-baseline.mjs';
import { completeWorkloadEvidence, recordSetEvidence } from './complete-workload.mjs';
import { readGameServerContext } from '../src/server/game-server-context.js';

// Private bodies, query values, cookies, console messages and DOM text never enter the report.
export function requestMetadata(request, parentInteraction, started) {
  return { path: new URL(request.url()).pathname, method: request.method(), parentInteraction,
    started, durationMs: null, status: null, payloadBytes: null, cacheResult: 'not-reported', outcome: 'pending' };
}

export const reportedPayloadBytes = value => Number.isSafeInteger(value) && value >= 0 ? value : null;

export const isPersistentReadPath = path => path === '/api/changes';

export const browserStorageState = (listener, perspective) => ({
  cookies: [],
  origins: [{ origin: listener, localStorage: [{ name: 'dnd2024-table-mode', value: perspective }] }],
});

export const remainingPairDelay = (previousStart, now, spacingMs) => previousStart === null
  ? 0 : Math.max(0, previousStart + spacingMs - now);

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
  const run = { id: cacheState + '-' + index, cacheState, status: 'failed', marks: {}, outcomes: {}, traversal: {}, requests };
  const time = () => page.evaluate(() => performance.now());
  const paint = () => page.evaluate(() => new Promise(resolve =>
    requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const waitForFiniteRequests = async () => {
    const deadline = Date.now() + 60_000;
    while ([...byRequest].some(([request, entry]) => entry.outcome === 'pending'
      && !isPersistentReadPath(new URL(request.url()).pathname))) {
      if (Date.now() >= deadline) throw new Error('Finite browser requests did not settle within 60 seconds');
      await page.waitForTimeout(25);
    }
  };
  try {
    await page.goto(page.baselineUrl, { waitUntil: 'domcontentloaded', timeout: 60_000 });
    await page.waitForFunction(() => window.__DND_BASELINE_SCRIPT_ERRORS__ > 0 ||
      window.__DND_BASELINE_DOM_MARKS__?.activeView &&
      performance.getEntriesByName('dnd2024.bootstrap.response').length, null, { timeout: 60_000 });
    const observed = await page.evaluate(() => ({
      ...window.__DND_BASELINE_DOM_MARKS__,
      bootstrap: performance.getEntriesByName('dnd2024.bootstrap.response')[0]?.startTime,
    }));
    Object.assign(run.marks, observed);
    await waitForFiniteRequests();
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
    const characterButton = page.getByRole('button', { name: /^Character( sheet)?$/ });
    if (await characterButton.count()) {
      await characterButton.click();
      await page.locator([
        '.character-sheet-v2',
        '.character-state--stale',
        '.character-state--error',
        '.character-state--forbidden',
        '.character-state--empty',
      ].join(', ')).first().waitFor({ state: 'visible' });
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
    run.traversal.character = { status: characterStatus, complete: true, ...recordSetEvidence(
      characterStatus === 'ready' ? await page.locator('.character-page[data-record-id]').evaluateAll(
        elements => elements.map(element => element.dataset.recordId)) : []) };
    await waitForFiniteRequests();
    phase = 'map';
    const mapStart = await time();
    await navigation.getByRole('button', { name: 'World', exact: true }).click();
    await page.getByRole('navigation', { name: 'World sections', exact: true })
      .getByRole('button', { name: 'Map', exact: true }).click();
    await paint(); await waitForFiniteRequests(); await paint();
    if (await page.locator('.world-map-canvas[data-base="present"]').count()) {
      await page.locator('.world-map-canvas').waitFor({ state: 'visible' });
      await page.waitForFunction(() => [...document.querySelectorAll('.world-map-canvas img')]
        .every(image => image.complete && image.naturalWidth > 0));
      await paint();
      run.marks.map = await time() - mapStart;
      run.traversal.map = { status: 'ready', complete: true, ...recordSetEvidence(
        await page.locator('.world-map-canvas[data-base="present"]').evaluateAll(elements =>
          elements.map(element => element.dataset.recordId))) };
    } else {
      const notReady = await page.locator('#information-content .view-loading, #information-content [role="alert"], #information-content .view-render-error').count();
      const status = notReady ? 'unloaded' : 'unavailable';
      run.outcomes.map = { status, reason: 'No authorized map canvas rendered.' };
      run.traversal.map = { status, complete: !notReady, ...recordSetEvidence([]) };
    }
    await waitForFiniteRequests();
    for (const view of ['history', 'lore', 'locations', 'people', 'factions']) {
      phase = view;
      await page.getByRole('navigation', { name: 'World sections', exact: true })
        .getByRole('button', { name: view[0].toUpperCase() + view.slice(1), exact: true }).click();
      await waitForFiniteRequests();
      await paint();
      await waitForFiniteRequests();
      // Factions is the currently paged World view. A cursor which never advances fails
      // the bounded traversal; never count the first page as the complete directory.
      let pages = 0;
      while (view === 'factions' && await page.getByRole('button', { name: 'Load more factions', exact: true }).count()) {
        assert.ok(++pages <= 10, 'Faction continuation did not terminate');
        phase = 'factions-page-' + (pages + 1);
        await page.getByRole('button', { name: 'Load more factions', exact: true }).click();
        await waitForFiniteRequests(); await paint();
      }
      const snapshot = await page.evaluate(view => {
        const root = document.querySelector('#information-content');
        if (!root?.querySelector('#main-view-heading')) return { status: 'unloaded', ids: [] };
        if (document.querySelector('.information-hub > [role="alert"]') ||
            root?.querySelector('[role="alert"], .view-render-error')) return { status: 'error', ids: [] };
        if (root?.querySelector('.view-loading, [aria-busy="true"]')) return { status: 'unloaded', ids: [] };
        const selectors = { history: '.history-event', lore: '.lore-card',
          locations: '.location-row', people: '.world-person-card', factions: '.faction-card' };
        const elements = [...(root?.querySelectorAll(selectors[view]) ?? [])];
        const ids = elements.map(element => element.dataset.recordId ??
          (view === 'people' ? element.id.replace(/^world-person-/, '') :
            view === 'factions' ? element.id.replace(/^world-faction-/, '') :
              element.getAttribute('aria-labelledby')?.replace(/-heading$/, '')));
        return { status: ids.length ? 'ready' : 'empty', ids };
      }, view);
      run.traversal[view] = { status: snapshot.status,
        complete: ['ready', 'empty'].includes(snapshot.status), ...recordSetEvidence(snapshot.ids) };
    }
    phase = 'context';
    await page.locator('.world-context__trigger').click();
    await paint(); await waitForFiniteRequests(); await paint();
    const contextIds = [];
    for (const world of await page.locator('.context-picker__world').all()) {
      contextIds.push(await world.getAttribute('data-record-id'));
      await world.click(); await paint();
      contextIds.push(...await page.locator('.context-picker__campaign').evaluateAll(
        elements => elements.map(element => element.dataset.recordId)));
    }
    const contextFailed = !await page.locator('.context-picker').count() ||
      await page.locator('.context-picker [role="alert"], .context-picker [role="status"]').count();
    run.traversal.context = { status: contextFailed ? 'error' : contextIds.length ? 'ready' : 'empty',
      complete: !contextFailed, ...recordSetEvidence(contextIds) };
    await page.getByRole('button', { name: 'Close world and campaign selection', exact: true }).click();
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
    await paint(); await waitForFiniteRequests(); await paint();
    const current = await page.locator('.current-play-workspace').evaluateAll(elements => ({
      status: elements[0]?.dataset.viewStatus ?? 'unloaded',
      ids: elements[0]?.dataset.recordId ? [elements[0].dataset.recordId] : [],
    }));
    run.traversal.current = { status: current.status, complete: ['ready', 'unavailable'].includes(current.status),
      ...recordSetEvidence(current.ids) };
    run.marks.completeWorkload = await time();
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
      sheetButtons: [...document.querySelectorAll('button')].filter(button => /^Character( sheet)?$/i.test(button.textContent.trim())).length,
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
  const options = { listener: 'http://localhost:6217', output: resolve(webRoot, '.tmp/complete-workload/browser.json'), pairs: 20,
    perspective: 'player', pairSpacingMs: 61_000 };
  for (let i = 2; i < process.argv.length; i++) {
    const name = process.argv[i]; const value = process.argv[++i];
    assert.ok(value);
    if (name === '--listener') options.listener = normalizeListener(value);
    else if (name === '--output') options.output = resolve(value);
    else if (name === '--playwright-module') options.module = pathToFileURL(resolve(value)).href;
    else if (name === '--browser-executable') options.executable = resolve(value);
    else if (name === '--pairs') options.pairs = Number(value);
    else if (name === '--perspective') options.perspective = value;
    else if (name === '--pair-spacing-ms') options.pairSpacingMs = Number(value);
    else if (name === '--workload-reference') options.reference = resolve(value);
    else if (name === '--server-measurements') options.serverMeasurements = resolve(value);
    else throw new Error('Unknown option ' + name);
  }
  assert.ok(Number.isInteger(options.pairs) && options.pairs >= 1 && options.pairs <= 100);
  assert.ok(Number.isInteger(options.pairSpacingMs) && options.pairSpacingMs >= 0 && options.pairSpacingMs <= 300_000);
  assert.ok(['player', 'dm'].includes(options.perspective), 'Perspective must be player or dm');
  let browser;
  const report = {
    schema: 'dnd2024.browser-baseline.v3', listener: options.listener,
    samplerSha256: sha256(await readFile(fileURLToPath(import.meta.url))),
    browser: null,
    machine: machineProfile(), generatedAtUtc: new Date().toISOString(), readOnly: true,
    protocol: {
      viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, throttling: 'none',
      navigationTimeoutMs: 60000, readinessTimeoutMs: 60000, viewTimeoutMs: 60000, idleTimeoutMs: 60000,
      cold: 'Fresh isolated browser context with HTTP cache explicitly cleared; server remains warm.',
      warm: 'Second navigation in the same context after Character, all World directories and continuations, context discovery, Map and Current.',
      pairSpacingMs: options.pairSpacingMs,
      timingOrigins: { shell: 'navigation', bootstrap: 'navigation', activeView: 'navigation',
        character: 'Party navigation start through canonical sheet paint', map: 'World navigation start through map image load and paint',
        combatBoard: 'Current view navigation start through board paint, only when present',
        completeWorkload: 'Navigation through the complete authorized traversal, separate from first ready view' },
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
    const bootstrap = await readGameServerContext({ serverOrigin: options.listener,
      requestedPerspective: options.perspective, deferCharacterDetails: true,
      deferCampaignDetails: true, deferWorldDirectory: true, useRegisteredCampaignSummary: true });
    if (bootstrap.status !== 'connected') {
      report.blocker = { phase: 'preflight', code: 'LIVE_BOOTSTRAP_UNAVAILABLE' };
      throw new Error('The live Campaign bootstrap is unavailable; no acceptance samples were manufactured.');
    }
    if (options.reference) report.workloadReference = JSON.parse(await readFile(options.reference, 'utf8'));
    const measurements = options.serverMeasurements ? JSON.parse(await readFile(options.serverMeasurements, 'utf8')) : {};
    const { chromium } = await import(options.module ?? 'playwright');
    browser = await chromium.launch({ headless: true, ...(options.executable ? { executablePath: options.executable } : {}) });
    report.browser = { name: 'Chromium (Playwright)', version: browser.version(), headless: true };
    let previousPairStarted = null;
    for (let index = 1; index <= options.pairs; index++) {
      const delay = remainingPairDelay(previousPairStarted, Date.now(), options.pairSpacingMs);
      if (delay) await new Promise(resolve => setTimeout(resolve, delay));
      previousPairStarted = Date.now();
      const context = await browser.newContext({ viewport: report.protocol.viewport, deviceScaleFactor: 1,
        serviceWorkers: 'block', storageState: browserStorageState(options.listener, report.perspective) });
      try {
        await context.addInitScript(initializeBrowserProbe, { perspective: report.perspective });
        const page = await context.newPage();
        page.baselineUrl = options.listener + '/ui/dnd2024-play';
        page.setDefaultTimeout(60_000);
        const client = await context.newCDPSession(page);
        await client.send('Network.enable');
        await client.send('Network.clearBrowserCache');
        for (const cacheState of ['cold', 'warm']) {
          const run = await sample(page, client, cacheState, index);
          if (measurements[run.id]) run.serverMeasurements = measurements[run.id];
          report.runs.push(run);
          console.log(JSON.stringify({ sample: run.id, status: run.status, requestCount: run.requestCount, failure: run.failure }));
          await save();
          if (run.status !== 'collected') throw new Error('Browser sample failed; partial evidence retained');
        }
      } finally { await context.close(); }
    }
    report.liveAfter = await livePageEvidence(options.listener);
    assert.ok(sameLivePage(report.liveBefore, report.liveAfter), 'Live revision or runtime changed during sampling');
  } catch (error) {
    report.blocker ??= { phase: report.runs.length ? 'sampling-or-final-verification' : 'preflight',
      code: 'VERIFIED_LIVE_EVIDENCE_UNAVAILABLE', category: error.name };
    throw error;
  } finally {
    report.acceptance = completeWorkloadEvidence(report);
    if (report.acceptance.status !== 'passed') process.exitCode = 1;
    await save();
    await browser?.close();
  }
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) await main();
