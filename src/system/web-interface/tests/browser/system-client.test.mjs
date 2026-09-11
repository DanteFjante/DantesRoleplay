import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {readFile} from 'node:fs/promises';
import test from 'node:test';

const require = createRequire(new URL('../../dnd2024/package.json', import.meta.url));
const {JSDOM} = require('jsdom');
const sourceUrl = new URL('../../../../../DantesRoleplay.Web/BrowserComponents/system-client.js', import.meta.url);
const source = await readFile(sourceUrl, 'utf8');
const moduleUrl = `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`;
const {
  SystemClientError,
  SystemRequestScope,
  SystemWebClient,
  InterruptedRequestStore,
  normalizePublishedPage,
  normalizePublishedApplication
} = await import(moduleUrl);

const origin = 'http://system.test';
const publicationSource = await readFile(new URL('../../../../../DantesRoleplay.Web/BrowserComponents/system-publication.js', import.meta.url), 'utf8');
const publicationImport = /import \{[\s\S]*?\} from '\/components\/system-client\.js';/u;

function page(overrides = {}) {
  return {
    entityId: 'web-page:rules',
    slug: 'rules',
    title: 'Rules',
    navigationLabel: 'Rules',
    order: 2,
    visibility: 'public',
    url: '/ui/rules',
    contentPageId: 'rules',
    isIndexPage: false,
    enabled: true,
    ...overrides
  };
}

function application(overrides = {}) {
  return {
    applicationId: 'sample',
    displayName: 'Sample application',
    publicationStatus: 'ready',
    isPublishable: true,
    isClickable: true,
    hasAdditionalPages: true,
    resolutionFingerprint: 'A'.repeat(64),
    indexPage: page({entityId: 'web-page:home', slug: 'sample', title: 'Sample',
      navigationLabel: 'Open', order: 0, url: '/ui/sample', isIndexPage: true}),
    pages: [page()],
    ...overrides
  };
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: {'content-type': 'application/json'}
  });
}

async function mountApplicationNavigation(value) {
  const dom = new JSDOM('<!doctype html><body></body>', {url: origin, runScripts: 'outside-only'});
  dom.window.__systemClient = {SystemClientError, SystemRequestScope, normalizePublishedApplication,
    systemWebClient: {}, validSystemIdentifier: (...args) => args[0]};
  const script = publicationSource.replace(publicationImport,
    'const {SystemClientError, SystemRequestScope, normalizePublishedApplication, systemWebClient, validSystemIdentifier} = window.__systemClient;')
    .replaceAll('export class ', 'class ');
  dom.window.eval(script);
  const navigation = dom.window.document.createElement('application-navigation');
  dom.window.document.body.append(navigation);
  navigation.application = value;
  return {dom, navigation};
}

async function mountApplicationPageHost(client, slug = 'rules') {
  const dom = new JSDOM('<!doctype html><body></body>', {url: origin, runScripts: 'outside-only'});
  dom.window.__systemClient = {SystemClientError, SystemRequestScope, normalizePublishedApplication,
    systemWebClient: {}, validSystemIdentifier: (...args) => args[0]};
  const script = publicationSource.replace(publicationImport,
    'const {SystemClientError, SystemRequestScope, normalizePublishedApplication, systemWebClient, validSystemIdentifier} = window.__systemClient;')
    .replaceAll('export class ', 'class ');
  dom.window.eval(script);
  const host = dom.window.document.createElement('application-page-host');
  host.setAttribute('application-id', 'sample');
  host.setAttribute('page-slug', slug);
  host.setAttribute('manual', '');
  host.client = client;
  dom.window.document.body.append(host);
  await new Promise(resolve => setTimeout(resolve, 0));
  return {dom, host};
}

test('interrupted request store keeps only closed session metadata and fails closed when storage is unavailable', () => {
  const prior = Object.getOwnPropertyDescriptor(globalThis, 'sessionStorage');
  const values = new Map();
  Object.defineProperty(globalThis, 'sessionStorage', {configurable: true, value: {
    getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value)
  }});
  try {
    const store = new InterruptedRequestStore('test.interrupted');
    const safeAi = {kind: 'ai', key: 'request.1', provider: 'local', surface: 'inner', applicationId: null,
      stateSpaceId: null, resolutionFingerprint: null, contextFingerprint: 'A'.repeat(64), sourceReferences: ['surface:inner']};
    assert.equal(store.write('ai-workspace-inner', safeAi), true);
    assert.deepEqual(store.read('ai-workspace-inner'), safeAi);
    const httpAi = {...safeAi, contextFingerprint: null};
    assert.equal(store.write('ai-workspace-inner', httpAi), true);
    assert.deepEqual(store.read('ai-workspace-inner'), httpAi);
    assert.equal(store.write('ai-workspace-inner', {...httpAi, sourceReferences: []}), false);
    assert.equal(store.write('ai-workspace-inner', {...safeAi, key: 'request.2', body: 'private'}), false);
    values.set('test.interrupted', JSON.stringify({'ai-workspace-inner': {...safeAi, prompt: 'private'}}));
    assert.deepEqual(store.read('ai-workspace-inner'), {malformed: true});
    values.set('test.interrupted', 'x'.repeat(8_193));
    assert.deepEqual(store.read('ai-workspace-inner'), {malformed: true});
    Object.defineProperty(globalThis, 'sessionStorage', {configurable: true, value: undefined});
    assert.deepEqual(store.read('ai-workspace-inner'), {malformed: true});
    assert.equal(store.write('ai-workspace-inner', safeAi), false);
  } finally {
    if (prior) Object.defineProperty(globalThis, 'sessionStorage', prior);
    else delete globalThis.sessionStorage;
  }
});

test('normalizes and orders fixture pages without publication internals', () => {
  const value = normalizePublishedApplication(application({pages: [
    page({entityId: 'web-page:z', slug: 'z', navigationLabel: 'Zulu', order: 4, url: '/ui/z'}),
    page({entityId: 'web-page:a', slug: 'a', navigationLabel: 'Alpha', order: 1, url: '/ui/a'})
  ]}), origin);
  assert.deepEqual(value.pages.map(item => item.slug), ['a', 'z']);
  assert.equal('publicationStateSpaceId' in value, false);
});

test('keeps useful application and neighboring page fields when optional display fields are malformed', () => {
  const value = normalizePublishedApplication(application({displayName: undefined, pages: [
    page({entityId: 'web-page:kept', slug: 'kept', title: undefined, navigationLabel: undefined, order: 'later'}),
    page({entityId: 'web-page:bad-url', slug: 'bad-url', url: 'javascript:alert(1)'}),
    page({entityId: 'web-page:other', slug: 'other', navigationLabel: 'Other', order: 3}),
  ]}), origin);
  assert.equal(value.displayName, 'sample');
  assert.deepEqual(value.pages.map(item => item.slug), ['other', 'kept']);
  assert.equal(value.pages.find(item => item.slug === 'kept').title, 'kept');
  assert.equal(value.pages.find(item => item.slug === 'kept').navigationLabel, 'kept');
  assert.equal(value.pages.find(item => item.slug === 'kept').order, null);
  assert.equal(value.coverage, 'partial');
  assert.ok(value.unavailableFields.includes('displayName'));
  assert.ok(value.unavailableFields.includes('pages'));
});

test('orders multiple pages with unavailable order deterministically by navigation label and ID', () => {
  const value = normalizePublishedApplication(application({pages: [
    page({entityId: 'web-page:z', slug: 'z', navigationLabel: 'Zulu', order: undefined}),
    page({entityId: 'web-page:a', slug: 'a', navigationLabel: 'Alpha', order: undefined}),
  ]}), origin);
  assert.deepEqual(value.pages.map(item => item.slug), ['a', 'z']);
});

test('unknown or non-public page flags never become navigable or clickable', () => {
  const value = normalizePublishedApplication(application({
    indexPage: page({isIndexPage: true, visibility: 'mystery', enabled: 'yes'}),
    pages: [
      page({entityId: 'web-page:unknown', slug: 'unknown', visibility: 'mystery', enabled: true}),
      page({entityId: 'web-page:disabled', slug: 'disabled', visibility: 'public', enabled: false}),
      page({entityId: 'web-page:visible', slug: 'visible', visibility: 'public', enabled: true}),
    ]
  }), origin);
  assert.equal(value.indexPage, null);
  assert.equal(value.isClickable, false);
  assert.deepEqual(value.pages.map(item => item.slug), ['visible']);
  const direct = normalizePublishedPage(page({visibility: 'mystery', enabled: 'yes'}), origin);
  assert.equal(direct.visibility, null);
  assert.equal(direct.enabled, null);
  assert.equal(value.coverage, 'partial');
});

test('additional-page admission requires an explicit non-index marker', () => {
  const value = normalizePublishedApplication(application({pages: [
    page({entityId: 'web-page:known', slug: 'known', isIndexPage: false}),
    page({entityId: 'web-page:misplaced-index', slug: 'misplaced-index', isIndexPage: true}),
    page({entityId: 'web-page:unknown-kind', slug: 'unknown-kind', isIndexPage: undefined}),
  ]}), origin);
  assert.deepEqual(value.pages.map(item => item.slug), ['known']);
  assert.equal(value.pageCoverage, 'partial');
  assert.ok(value.unavailableFields.includes('pages'));
});

test('clickability never outruns an explicitly non-publishable application', () => {
  const value = normalizePublishedApplication(application({isPublishable: false, isClickable: true}), origin);
  assert.equal(value.isPublishable, false);
  assert.equal(value.isClickable, false);
});

test('ambiguous duplicate page identities are omitted without hiding valid neighbors', () => {
  const value = normalizePublishedApplication(application({pages: [
    page({entityId: 'web-page:duplicate', slug: 'duplicate', order: 1}),
    page({entityId: 'web-page:duplicate', slug: 'duplicate', order: 2}),
    page({entityId: 'web-page:neighbor', slug: 'neighbor', order: 3}),
  ]}), origin);
  assert.deepEqual(value.pages.map(item => item.slug), ['neighbor']);
  assert.equal(value.coverage, 'partial');
  assert.ok(value.unavailableFields.includes('pages'));
});

test('all page identity collisions stay withheld, including a malformed twin and index overlap', () => {
  const value = normalizePublishedApplication(application({pages: [
    page({entityId: 'web-page:same-id', slug: 'first', order: 1}),
    page({entityId: 'web-page:same-id', slug: 'second', order: 2}),
    page({entityId: 'web-page:first', slug: 'same-slug', order: 3}),
    page({entityId: 'web-page:second', slug: 'same-slug', order: 4}),
    page({entityId: 'web-page:home', slug: 'sample', order: 5, url: 'javascript:bad'}),
    page({entityId: 'web-page:neighbor', slug: 'neighbor', order: 6}),
  ]}), origin);
  assert.deepEqual(value.pages.map(item => item.slug), ['neighbor']);
  assert.equal(value.indexPage, null);
  assert.equal(value.isClickable, false);
  assert.equal(value.coverage, 'partial');
});

test('a malformed sibling or index cannot promote its ambiguous identity to a usable link', () => {
  const value = normalizePublishedApplication(application({
    indexPage: page({entityId: 'web-page:broken-index', slug: 'broken-index', isIndexPage: true, url: 'javascript:bad'}),
    pages: [
      page({entityId: 'web-page:broken-index', slug: 'index-twin'}),
      page({entityId: 'web-page:broken-row', slug: 'good-slug'}),
      page({entityId: 'web-page:broken-row', slug: null}),
      page({entityId: 'web-page:good-id', slug: 'broken-slug'}),
      page({entityId: null, slug: 'broken-slug'}),
      page({entityId: 'web-page:neighbor', slug: 'neighbor'}),
    ],
  }), origin);
  assert.deepEqual(value.pages.map(item => item.slug), ['neighbor']);
  assert.equal(value.indexPage, null);
  assert.equal(value.isClickable, false);
  assert.equal(value.coverage, 'partial');
});

test('known hidden or disabled pages are suppressed without being reported as malformed', () => {
  const value = normalizePublishedApplication(application({
    indexPage: page({entityId: 'web-page:home', slug: 'sample', isIndexPage: true, visibility: 'hidden'}),
    pages: [page({visibility: 'hidden'}), page({entityId: 'web-page:disabled', slug: 'disabled', enabled: false})]
  }), origin);
  assert.deepEqual(value.pages, []);
  assert.equal(value.coverage, undefined);
  assert.equal(value.isClickable, false);
});

test('publication navigation visibly reports partial page coverage while retaining usable links', async () => {
  const value = normalizePublishedApplication(application({pages: [
    page({entityId: 'web-page:bad', slug: 'bad', url: 'javascript:bad'}),
    page({entityId: 'web-page:good', slug: 'good', order: 2}),
  ]}), origin);
  const mounted = await mountApplicationNavigation(value);
  try {
    assert.equal(mounted.navigation.shadowRoot.querySelectorAll('[part="page-link"]').length, 1);
    assert.equal(mounted.navigation.shadowRoot.querySelector('[part="page-link"]')?.textContent, 'Rules');
    assert.equal(mounted.navigation.shadowRoot.querySelector('[part="partial-status"]')?.textContent,
      'Some application pages are unavailable.');
  } finally { mounted.dom.window.close(); }
});

test('application navigation reports partial application details without claiming pages are unavailable', async () => {
  const value = normalizePublishedApplication(application({displayName: undefined}), origin);
  const mounted = await mountApplicationNavigation(value);
  try {
    assert.equal(mounted.navigation.shadowRoot.querySelector('[part="partial-status"]')?.textContent,
      'Some application publication details are unavailable.');
  } finally { mounted.dom.window.close(); }
});

test('prototype-only publication fields are not admitted as trusted display data', () => {
  const inheritedPage = Object.create(page());
  const inheritedApplication = Object.create(application());
  assert.equal(normalizePublishedPage(inheritedPage, origin), null);
  assert.equal(normalizePublishedApplication(inheritedApplication, origin), null);
});

test('malformed application resolution evidence remains a hard rejection', () => {
  assert.equal(normalizePublishedApplication(application({resolutionFingerprint: 'not-a-fingerprint'}), origin), null);
});

test('discovers fixture applications across cursors and retains fingerprints', async () => {
  const calls = [];
  const client = new SystemWebClient({origin, retryDelayMilliseconds: 0, fetch: async url => {
    calls.push(url.href);
    if (url.searchParams.has('cursor')) return json({
      applications: [application({applicationId: 'second', displayName: 'Beta'})],
      systemPages: [], nextCursor: null
    });
    return json({
      applications: [application({applicationId: 'first', displayName: 'Zulu'})],
      systemPages: [{pageId: 'home', title: 'Home', url: '/'}], nextCursor: 'next'
    });
  }});
  const result = await client.discoverAllApplications();
  assert.equal(result.pageCount, 2);
  assert.deepEqual(result.applications.map(item => item.applicationId), ['second', 'first']);
  assert.equal(result.resolutionFingerprints.first, 'A'.repeat(64));
  assert.equal(calls.length, 2);
});

test('discovery retains valid application rows when one publication row is unusable', async () => {
  const client = new SystemWebClient({origin, maximumRetries: 0, fetch: async () => json({
    applications: [application({applicationId: 'valid', displayName: 'Valid'}), {applicationId: '../invalid', pages: []}],
    systemPages: [{pageId: 'home', title: 'Home', url: '/'}], nextCursor: null
  })});
  const result = await client.discoverApplications();
  assert.deepEqual(result.applications.map(item => item.applicationId), ['valid']);
  assert.equal(result.coverage, 'partial');
  assert.deepEqual(result.unavailableFields, ['applications']);
});

test('discovery keeps application and system-page partial coverage independent', async () => {
  const client = new SystemWebClient({origin, maximumRetries: 0, fetch: async () => json({
    applications: [application({applicationId: 'valid', displayName: 'Valid'})],
    systemPages: [{pageId: '../invalid', title: 'Bad', url: '/'}], nextCursor: null
  })});
  const result = await client.discoverApplications();
  assert.deepEqual(result.unavailableFields, ['systemPages']);
  assert.equal(result.applications.length, 1);
  assert.equal(result.systemPages.length, 0);
});

test('retries bounded transient reads and preserves structured errors', async () => {
  let calls = 0;
  const retrying = new SystemWebClient({origin, maximumRetries: 1, retryDelayMilliseconds: 0,
    fetch: async () => ++calls === 1 ? json({error: 'TEMPORARY', message: 'Try later.'}, 503) : json({ok: true})});
  assert.deepEqual(await retrying.requestJson('/api/example'), {ok: true});
  assert.equal(calls, 2);

  const failing = new SystemWebClient({origin, maximumRetries: 0,
    fetch: async () => json({error: 'EXACT_FAILURE', message: 'Readable failure.'}, 400)});
  await assert.rejects(failing.requestJson('/api/example'), error =>
    error instanceof SystemClientError && error.code === 'EXACT_FAILURE' &&
    error.message === 'Readable failure.' && error.status === 400);
});

test('request scopes cancel stale navigation responses', () => {
  const scope = new SystemRequestScope();
  const first = scope.begin();
  const second = scope.begin();
  assert.equal(first.signal.aborted, true);
  assert.equal(first.isCurrent(), false);
  assert.equal(second.signal.aborted, false);
  assert.equal(second.isCurrent(), true);
});

test('rejects a changed application resolution fingerprint', async () => {
  let discovery = true;
  const client = new SystemWebClient({origin, maximumRetries: 0, fetch: async url => {
    if (url.pathname === '/api/web/applications' && discovery) {
      discovery = false;
      return json({applications: [application()], systemPages: [], nextCursor: null});
    }
    return json(application({resolutionFingerprint: 'B'.repeat(64)}));
  }});
  await client.discoverAllApplications();
  await assert.rejects(client.getApplication('sample'), error =>
    error.code === 'WEB_RESOLUTION_FINGERPRINT_STALE' && error.retryable === true);
  assert.equal((await client.getApplication('sample')).resolutionFingerprint, 'B'.repeat(64));
});

test('loadPublishedPage returns the verified canonical page instead of promoting a changed raw page', async () => {
  const client = new SystemWebClient({origin, maximumRetries: 0, fetch: async url => {
    if (url.pathname.endsWith('/pages/rules')) return json(page({
      title: 'Untrusted replacement', navigationLabel: 'Untrusted replacement',
      url: '/ui/changed', enabled: false
    }));
    return json(application());
  }});
  const result = await client.loadPublishedPage('sample', 'rules');
  assert.equal(result.page.entityId, 'web-page:rules');
  assert.equal(result.page.slug, 'rules');
  assert.equal(result.page.url, '/ui/rules');
  assert.equal(result.page.enabled, true);
  assert.equal(result.page.title, 'Rules');
});

test('the real application page host renders the verified canonical page URL', async () => {
  const client = new SystemWebClient({origin, maximumRetries: 0, fetch: async url => {
    if (url.pathname.endsWith('/pages/rules')) return json(page({url: '/ui/changed', enabled: false}));
    return json(application());
  }});
  const mounted = await mountApplicationPageHost(client);
  try {
    assert.equal(mounted.host.dataset.state, 'ready');
    assert.equal(mounted.host.shadowRoot.querySelector('[part="page-link"]')?.getAttribute('href'),
      '/ui/rules');
  } finally { mounted.dom.window.close(); }
});
