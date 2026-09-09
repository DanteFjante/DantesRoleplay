import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { audienceViewFor, livePageEvidence, machineProfile, normalizeListener, requiredMarks,
  sameLivePage, sha256, webRoot } from './collect-baseline.mjs';
import { completeWorkloadEvidence, isReadOnlyWorkloadRequest, recordSetEvidence, workloadHarnessFingerprint } from './complete-workload.mjs';
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
  const allowed = (method, target) => {
    method = String(method ?? 'GET').toUpperCase();
    if (['GET', 'HEAD'].includes(method)) return true;
    const path = new URL(typeof target === 'string' || target instanceof URL ? String(target) : target?.url, location.href).pathname;
    return method === 'POST' && /^\/api\/applications\/[^/]+\/state-spaces\/[^/]+\/media-batch$/u.test(path);
  };
  const rejectWrite = () => {
    window.__DND_BASELINE_BLOCKED_WRITES__++;
    throw new Error('Baseline blocks writes');
  };
  const originalFetch = window.fetch.bind(window);
  window.fetch = (input, init) => {
    if (!allowed(init?.method ?? input?.method, input)) {
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
    if (!allowed(method, args[0])) return rejectWrite();
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
  const run = { id: cacheState + '-' + index, cacheState, status: 'failed', marks: {}, outcomes: {},
    checks: {}, traversal: {}, requests };
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
  const settle = async () => { await waitForFiniteRequests(); await paint(); await waitForFiniteRequests(); await paint(); };
  const passed = name => { run.checks[name] = 'passed'; };
  const capture = async (name, selector, fallbackIds = []) => {
    const snapshot = await page.evaluate(({ selector, fallbackIds }) => {
      const root = document.querySelector('#information-content');
      if (!root?.querySelector('#main-view-heading') && !root?.querySelector('.item-page, .recipe-page'))
        return { status: 'unloaded', ids: [] };
      if (root.querySelector('.view-loading, [aria-busy="true"]')) return { status: 'unloaded', ids: [] };
      if (root.querySelector('.view-render-error, [role="alert"]')) return { status: 'error', ids: [] };
      const ids = [...root.querySelectorAll(selector)].map(element =>
        element.getAttribute('data-record-id') ?? element.getAttribute('data-item-open') ??
        element.getAttribute('data-registry-entry') ?? element.getAttribute('data-recipe-entry') ??
        (element.id || null) ??
        element.getAttribute('aria-labelledby')?.replace(/-heading$/u, '') ??
        element.querySelector('h2, h3')?.textContent?.trim()).filter(Boolean);
      return { status: ids.length || fallbackIds.length ? 'ready' : 'empty', ids: ids.length ? ids : fallbackIds };
    }, { selector, fallbackIds });
    run.traversal[name] = { status: snapshot.status,
      complete: ['ready', 'empty'].includes(snapshot.status), ...recordSetEvidence(snapshot.ids) };
    return snapshot;
  };
  const clickMain = async label => {
    phase = ({ Campaign: 'campaign-overview', Party: 'party-overview', World: 'world-overview',
      'Current View': 'current', Rules: 'rules', 'Installed Content': 'installed-content' })[label];
    await page.getByRole('navigation', { name: 'Main table views', exact: true })
      .getByRole('button', { name: label, exact: true }).click();
    await settle();
  };
  const clickSection = async (navigationLabel, label, interaction) => {
    phase = interaction;
    await page.getByRole('navigation', { name: navigationLabel, exact: true })
      .getByRole('button', { name: label, exact: true }).click();
    await settle();
  };
  const loadAll = async (labelPattern, interactionPrefix) => {
    let pageNumber = 1;
    while (await page.getByRole('button', { name: labelPattern }).count()) {
      assert.ok(++pageNumber <= 100, `${interactionPrefix} continuation did not terminate`);
      phase = `${interactionPrefix}-page-${pageNumber}`;
      await page.getByRole('button', { name: labelPattern }).click();
      await settle();
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
      for (const name of requiredMarks) {
        if (run.marks[name] === undefined) run.outcomes[name] = {
          status: 'error', reason: 'The live application failed before the requested view could render.',
        };
      }
      run.outcomes.current = { status: 'error', reason: 'The live application failed before navigation.' };
      run.combatBoard = { status: 'unavailable', reason: 'The application did not render.' };
      run.status = 'collected';
      return run;
    }
    passed('startup');
    await page.locator('.character-page, .view-render-error').first().waitFor({ state: 'visible' });
    await settle();
    passed('inventory-direct-entry');
    const firstInventoryButton = page.locator('[data-item-open]').first();
    assert.ok(await firstInventoryButton.count(), 'Production traversal requires at least one carried item');
    const firstInventoryItemId = await firstInventoryButton.getAttribute('data-item-open');
    const inventoryStyle = await firstInventoryButton.evaluate(element => {
      const style = getComputedStyle(element);
      return { display: style.display, cursor: style.cursor,
        cue: Boolean(element.querySelector('.character-inventory__view-cue')) };
    });
    run.inventoryStyle = inventoryStyle;
    assert.ok(inventoryStyle.display !== 'inline' && inventoryStyle.display !== 'inline-block' &&
      inventoryStyle.cursor === 'pointer' && inventoryStyle.cue, 'Inventory first-entry interaction styling is missing');
    passed('inventory-first-entry-style');
    const firstReadyAssetPaths = await page.evaluate(() => [...new Set(performance.getEntriesByType('resource')
      .map(entry => new URL(entry.name).pathname).filter(path => /\/assets\/.*\.(?:css|js)$/u.test(path)))]);
    assert.ok(!firstReadyAssetPaths.some(path =>
      /(?:ItemRecipes|ItemRegistryWorkspaceFeature|ScopedMapWorkspace|RulesView|InstalledContentView|PreviewViewsFeature)/u.test(path)),
    'An unrelated lazy feature asset loaded before direct-entry Inventory became ready');
    run.firstReadyAssets = { ...recordSetEvidence(firstReadyAssetPaths),
      cssCount: firstReadyAssetPaths.filter(path => path.endsWith('.css')).length,
      jsCount: firstReadyAssetPaths.filter(path => path.endsWith('.js')).length };
    passed('inventory-css-isolation');
    run.marks.firstReady = await time();
    await capture('inventory', '[data-item-open]');

    phase = 'inventory-item-details';
    await firstInventoryButton.click(); await page.locator('.item-page').waitFor({ state: 'visible' }); await settle();
    await capture('inventory-item-details', '.item-page', [firstInventoryItemId]);
    phase = 'inventory-item-recipes';
    await page.getByRole('tab', { name: 'Known recipes', exact: true }).click(); await settle();
    await capture('inventory-item-recipes', '.item-page', [firstInventoryItemId]);
    phase = 'inventory-item-uses';
    await page.getByRole('tab', { name: 'Known uses', exact: true }).click(); await settle();
    await capture('inventory-item-uses', '.item-page', [firstInventoryItemId]);
    await page.locator('.item-page__breadcrumbs').getByRole('button', { name: 'Inventory', exact: true }).click();
    await page.locator('[data-item-open]').first().waitFor({ state: 'visible' }); await settle();
    assert.equal(await page.evaluate(id => document.activeElement?.getAttribute('data-item-open') === id, firstInventoryItemId), true);
    passed('inventory-item-return');
    await page.goForward(); await page.locator('.item-page').waitFor({ state: 'visible' });
    await page.goBack(); await page.locator('[data-item-open]').first().waitFor({ state: 'visible' }); await settle();
    passed('back-forward');

    await clickSection('Character dossier sections', 'Overview', 'party-overview');
    await page.locator('.character-overview, .character-overview-skeleton').first().waitFor({ state: 'visible' }); await settle();
    await capture('party-overview', '[data-character-member]');
    const characterStart = await time();
    await clickSection('Character dossier sections', 'Character', 'character-sheet');
    await page.locator('.character-sheet-v2, .character-state').first().waitFor({ state: 'visible' });
    await capture('character-sheet', '.character-page[data-record-id]');
    run.marks.character = await time() - characterStart;
    for (const [label, name] of [['Knowledge', 'character-knowledge'], ['Biography', 'character-biography'],
      ['Origin', 'character-origin']]) {
      await clickSection('Character dossier sections', label, name);
      await capture(name, '.character-page[data-record-id]');
    }

    await clickSection('Character dossier sections', 'Registry', 'registry-items');
    await page.locator('.party-registry').waitFor({ state: 'visible' }); await settle();
    await loadAll(/^Load more \(/u, 'registry-items');
    const firstRegistryItem = page.locator('[data-registry-entry]').first();
    assert.ok(await firstRegistryItem.count(), 'Production traversal requires at least one registry item');
    const registryItemId = await firstRegistryItem.getAttribute('data-registry-entry');
    await capture('registry-items', '[data-registry-entry]');
    phase = 'registry-item-details'; await firstRegistryItem.click();
    await page.locator('.item-page').waitFor({ state: 'visible' }); await settle();
    await capture('registry-item-details', '.item-page', [registryItemId]);
    phase = 'registry-item-recipes';
    await page.getByRole('tab', { name: 'Recipes', exact: true }).click(); await settle();
    await capture('registry-item-recipes', '.item-page', [registryItemId]);
    await page.locator('.item-page__breadcrumbs').getByRole('button', { name: 'Items', exact: true }).click();
    await page.locator('[data-registry-entry]').first().waitFor({ state: 'visible' }); await settle();
    assert.equal(await page.evaluate(id => document.activeElement?.getAttribute('data-registry-entry') === id, registryItemId), true);
    passed('registry-item-return');
    await clickSection('Registry sections', 'Recipes', 'registry-recipes');
    await page.locator('.recipe-registry').waitFor({ state: 'visible' }); await settle();
    await loadAll(/^Load more \(/u, 'registry-recipes');
    const firstRecipe = page.locator('[data-recipe-entry]').first();
    assert.ok(await firstRecipe.count(), 'Production traversal requires at least one registry recipe');
    const recipeId = await firstRecipe.getAttribute('data-recipe-entry');
    await capture('registry-recipes', '[data-recipe-entry]');
    phase = 'registry-recipe-details'; await firstRecipe.click();
    await page.locator('.recipe-page').waitFor({ state: 'visible' }); await settle();
    await capture('registry-recipe-details', '.recipe-page', [recipeId]);
    await page.locator('.item-page__breadcrumbs').getByRole('button', { name: 'Recipes', exact: true }).click();
    await page.locator('[data-recipe-entry]').first().waitFor({ state: 'visible' }); await settle();
    assert.equal(await page.evaluate(id => document.activeElement?.getAttribute('data-recipe-entry') === id, recipeId), true);
    passed('registry-recipe-return');

    phase = 'context'; await page.locator('.world-context__trigger').click(); await settle();
    const contextIds = [];
    const selectedCampaignId = await page.locator('.context-picker__campaign[aria-current="true"]')
      .getAttribute('data-record-id');
    for (const world of await page.locator('.context-picker__world').all()) {
      contextIds.push(await world.getAttribute('data-record-id'));
      await world.click(); await paint();
      contextIds.push(...await page.locator('.context-picker__campaign').evaluateAll(
        elements => elements.map(element => element.dataset.recordId)));
    }
    const contextFailed = !await page.locator('.context-picker').count() ||
      await page.locator('.context-picker [role="alert"]').count();
    run.traversal.context = { status: contextFailed ? 'error' : contextIds.length ? 'ready' : 'empty',
      complete: !contextFailed, ...recordSetEvidence(contextIds) };
    assert.ok(contextIds.length >= 2 && selectedCampaignId, 'At least one world/campaign context must be selectable');
    passed('context-switch');
    await page.getByRole('button', { name: 'Close world and campaign selection', exact: true }).click();

    await clickMain('Campaign');
    await page.locator('.campaign-view').waitFor({ state: 'visible' }); await settle();
    const campaignViews = [
      ['Overview', 'campaign-overview', '.campaign-overview', [selectedCampaignId]],
      ['Adventure Log', 'campaign-log', '.campaign-log-entry[data-record-id]', []],
      ['Places Visited', 'campaign-places', '.campaign-place-card[data-record-id]', []],
      ['Outcomes', 'campaign-outcomes', '.campaign-outcome-card[data-record-id]', []],
      ['Quests', 'campaign-quests', '.campaign-quest-card[data-record-id]', []],
      ['Open Threads', 'campaign-threads', '.campaign-thread-card[data-record-id]', []],
      ['Clues', 'campaign-clues', '.campaign-clue-card[data-record-id]', []],
    ];
    for (const [label, name, selector, fallback] of campaignViews) {
      await clickSection('Campaign sections', label, name); await capture(name, selector, fallback);
    }

    await clickMain('World');
    await clickSection('World sections', 'Overview', 'world-overview');
    await capture('world-overview', '.world-overview[data-record-id]');
    const mapStart = await time();
    await clickSection('World sections', 'Map', 'map');
    await page.locator('.world-map-canvas[data-base="present"]').waitFor({ state: 'visible' });
    await page.waitForFunction(() => [...document.querySelectorAll('.world-map-canvas img')]
      .every(image => image.complete && image.naturalWidth > 0));
    const mapPixels = await page.locator('.world-map-canvas[data-base="present"]').evaluate(element => ({
      width: element.querySelector('img')?.naturalWidth ?? 0,
      height: element.querySelector('img')?.naturalHeight ?? 0,
      markers: element.querySelectorAll('.world-map-marker').length,
    }));
    assert.ok(mapPixels.width > 0 && mapPixels.height > 0 && mapPixels.markers > 0);
    run.mapEvidence = mapPixels;
    passed('map-pixels-markers');
    await capture('map', '.world-map-canvas[data-base="present"]');
    run.marks.map = await time() - mapStart;

    await clickSection('World sections', 'Locations', 'locations');
    const locationIds = new Set();
    for (let depth = 0; depth < 20; depth++) {
      await loadAll(/^Load more locations/u, 'locations');
      for (const id of await page.locator('.location-row[data-record-id]').evaluateAll(elements => elements.map(element => element.dataset.recordId))) locationIds.add(id);
      const row = page.locator('.location-row[data-record-id]').first();
      if (!await row.count()) break;
      const previousHeading = await page.locator('#location-browser-heading').textContent();
      await row.click(); await settle();
      if (await page.locator('#location-browser-heading').textContent() === previousHeading) break;
    }
    while (await page.getByRole('button', { name: 'Parent location', exact: true }).count()) {
      await page.getByRole('button', { name: 'Parent location', exact: true }).click(); await settle();
    }
    run.traversal.locations = { status: locationIds.size ? 'ready' : 'empty', complete: true,
      ...recordSetEvidence([...locationIds]) };
    const selectedLocationId = await page.locator('.location-row[aria-pressed="true"]').getAttribute('data-record-id');
    assert.ok(selectedLocationId, 'Production traversal requires a selected location workspace');
    for (const [label, name, selector, fallback] of [
      ['Details', 'location-details', '.location-detail[data-record-id]', [selectedLocationId]],
      ['People & Creatures', 'location-people', '.location-person-card[data-record-id]', []],
      [/^Holdings/u, 'location-holdings', '.holding-card[data-record-id]', []],
    ]) {
      phase = name;
      await page.locator('.location-section-tabs').getByRole('button', { name: label, exact: typeof label === 'string' }).click();
      await settle(); await capture(name, selector, fallback);
    }
    for (const [label, name, selector] of [
      ['People', 'people', '.world-person-list__item[data-record-id]'],
      ['Factions', 'factions', '.faction-card[data-record-id]'],
      ['Lore', 'lore', '.lore-card[data-record-id]'], ['History', 'history', '.history-event[data-record-id]'],
    ]) {
      await clickSection('World sections', label, name);
      if (name === 'factions') await loadAll('Load more factions', 'factions');
      await capture(name, selector);
    }

    const currentStart = await time(); await clickMain('Current View');
    await page.locator('.current-scene-view, .view-render-error').first().waitFor({ state: 'visible' }); await settle();
    const current = await page.locator('.current-play-workspace').evaluateAll(elements => ({
      status: elements[0]?.dataset.viewStatus ?? 'unloaded',
      ids: elements[0]?.dataset.recordId ? [elements[0].dataset.recordId] : [],
    }));
    run.traversal.current = { status: current.status, complete: ['ready', 'unavailable'].includes(current.status),
      ...(current.status === 'unavailable' ? { reasonCode: 'no-authorized-content' } : {}), ...recordSetEvidence(current.ids) };
    assert.equal(await page.locator('dnd-play-conversation, .play-conversation-panel').count(), 0);
    passed('current-no-chatbox');
    if (await page.locator('.tactical-board-viewport').count()) run.marks.combatBoard = await time() - currentStart;
    else run.combatBoard = { status: 'not-applicable', reason: 'No tactical board in the current recorded situation.' };

    await clickMain('Rules');
    await page.locator('.rules-view, .rules-empty-state').first().waitFor({ state: 'visible' }); await settle();
    await loadAll(/^Show more \(/u, 'rules');
    await capture('rules', '.rule-index-card[data-record-id]');
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('#information-content #main-view-heading').waitFor({ state: 'visible' }); await settle();
    passed('reload');
    await clickMain('Installed Content');
    await page.locator('.installed-content-view').waitFor({ state: 'visible' }); await settle();
    await loadAll(/^Load more \(/u, 'installed-content');
    await capture('installed-content', '.installed-content-record[data-record-id]');

    const warmReturnStart = await time();
    await clickMain('Party');
    await clickSection('Character dossier sections', 'Inventory', 'inventory');
    await page.locator('[data-item-open]').first().waitFor({ state: 'visible' }); await settle();
    run.marks.warmReturn = await time() - warmReturnStart;

    const recoveryPage = await page.context().newPage();
    try {
      let failed = false;
      await recoveryPage.route('**/api/audience-context', async route => {
        if (!failed) { failed = true; await route.abort('failed'); } else await route.continue();
      });
      await recoveryPage.goto(page.baselineUrl, { waitUntil: 'domcontentloaded', timeout: 60_000 });
      await recoveryPage.getByRole('button', { name: 'Retry application', exact: true }).waitFor({ state: 'visible' });
      await recoveryPage.unroute('**/api/audience-context');
      await recoveryPage.getByRole('button', { name: 'Retry application', exact: true }).click();
      await recoveryPage.locator('.information-hub:not(.bootstrap-shell) #main-view-heading').waitFor({ state: 'visible' });
      passed('error-retry');
    } finally { await recoveryPage.close(); }

    await page.evaluate(async () => {
      try { await fetch('/api/acceptance-mutation-probe', { method: 'POST', body: 'not-sent' }); } catch { /* guard proof */ }
    });
    run.blockedWrites = await page.evaluate(() => window.__DND_BASELINE_BLOCKED_WRITES__);
    run.blockedOperations = await page.evaluate(() => window.__DND_BASELINE_BLOCKED_OPERATIONS__);
    assert.ok(run.blockedWrites > 0 && run.blockedOperations.some(operation => operation.path === '/api/acceptance-mutation-probe'));
    passed('mutation-blocking');
    await settle(); passed('deferred-settled');
    const assetPaths = [...new Set(requests.map(request => request.path).filter(path => /\/assets\/.*\.(?:css|js)$/u.test(path)))];
    run.assets = { ...recordSetEvidence(assetPaths), cssCount: assetPaths.filter(path => path.endsWith('.css')).length,
      jsCount: assetPaths.filter(path => path.endsWith('.js')).length };
    if (run.assets.cssCount > 0 && run.assets.jsCount > 0) passed('lazy-assets');
    run.marks.completeWorkload = await time();
    assert.ok(requests.every(isReadOnlyWorkloadRequest));
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
      for (const name of requiredMarks) {
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
    perspective: 'dm', pairSpacingMs: 61_000 };
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
  assert.equal(options.perspective, 'dm', 'The shared-site harness uses DM display; Player filtering requires an explicit observer harness');
  let browser;
  const report = {
    schema: 'dnd2024.browser-baseline.v3', listener: options.listener,
    samplerSha256: sha256(await readFile(fileURLToPath(import.meta.url))),
    workloadHarnessSha256: workloadHarnessFingerprint(),
    browser: null,
    machine: machineProfile(), generatedAtUtc: new Date().toISOString(), readOnly: true,
    protocol: {
      viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 1, throttling: 'none',
      navigationTimeoutMs: 60000, readinessTimeoutMs: 60000, viewTimeoutMs: 60000, idleTimeoutMs: 60000,
      cold: 'Fresh isolated browser context with HTTP cache explicitly cleared; server remains warm.',
      warm: 'Second complete direct-entry navigation in the same context after Campaign, Party, Inventory/Item, Registry, World, Current, Rules and Installed Content.',
      pairSpacingMs: options.pairSpacingMs,
      timingOrigins: { shell: 'navigation', bootstrap: 'navigation', activeView: 'navigation',
        character: 'Party navigation start through canonical sheet paint', map: 'World navigation start through map image load and paint',
        combatBoard: 'Current view navigation start through board paint, only when present',
        firstReady: 'Navigation through the first styled direct-entry Inventory paint',
        warmReturn: 'Return to the cached Inventory after the full feature traversal',
        completeWorkload: 'Navigation through the complete shared-site traversal, separate from first ready view' },
    },
    limitations: [
      'Cold means browser cache, not a server or OS restart. Warm private API reads still obey no-store.',
      'The unchanged live bundle is measured, not the newly built source bundle.',
      'DOM-observed shell and active-view marks measure commit; component marks do not promise image decode.',
      'No combat timing is invented when the live campaign has no tactical board.',
      'The read-only media-batch POST is permitted; automatic conversation creation and every mutation are blocked.',
      'Startup retry is exercised on an isolated browser page with one deliberately failed audience read; it is included in complete-workload timing but excluded from the primary request ledger.',
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
    const bootstrap = await readGameServerContext({
      serverOrigin: options.listener,
      requestedPerspective: options.perspective,
    });
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
        page.baselineUrl = options.listener + '/ui/dnd2024-play#view?tab=party&section=inventory';
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
