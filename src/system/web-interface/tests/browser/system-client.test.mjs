import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';

const sourceUrl = new URL('../../../../../DantesRoleplay.Web/BrowserComponents/system-client.js', import.meta.url);
const source = await readFile(sourceUrl, 'utf8');
const moduleUrl = `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`;
const {
  SystemClientError,
  SystemRequestScope,
  SystemWebClient,
  normalizePublishedApplication
} = await import(moduleUrl);

const origin = 'http://system.test';

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

test('normalizes and orders fixture pages without publication internals', () => {
  const value = normalizePublishedApplication(application({pages: [
    page({entityId: 'web-page:z', slug: 'z', navigationLabel: 'Zulu', order: 4, url: '/ui/z'}),
    page({entityId: 'web-page:a', slug: 'a', navigationLabel: 'Alpha', order: 1, url: '/ui/a'})
  ]}), origin);
  assert.deepEqual(value.pages.map(item => item.slug), ['a', 'z']);
  assert.equal('publicationStateSpaceId' in value, false);
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
