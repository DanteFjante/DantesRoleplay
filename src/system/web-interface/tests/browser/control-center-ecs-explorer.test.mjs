import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import {createRequire} from 'node:module';

const require = createRequire(new URL('../../dnd2024/package.json', import.meta.url));
const {JSDOM} = require('jsdom');
const html = await readFile(new URL('../../examples/control-center/index.html', import.meta.url), 'utf8');
const inlineSource = html.split('<script>')[1].split('</script>')[0];

const page = (items, nextCursor = null) => ({items, nextCursor});
const type = index => ({qualifiedId: `fixture.component.${index}`, version: 1});

async function mount({secondPage = page([type(26)], null), delaySecondPage = 0, continuationPages = null} = {}) {
  const requests = [];
  let continuationCall = 0;
  const firstPage = page(Array.from({length: 25}, (_, index) => type(index + 1)), 'cursor-1');
  const dom = new JSDOM(`<!doctype html><body>
    <div id="workspace-title"></div><div id="overview-detail"></div><div id="overview-state"></div><div id="overview-heading"></div>
    <ecs-explorer id="ecs-explorer" data-workspace></ecs-explorer>
  </body>`, {url: 'http://control.test/#/applications/alpha', runScripts: 'outside-only'});
  const response = value => ({ok: true, status: 200, json: async () => value});
  dom.window.fetch = async path => {
    const text = String(path);
    requests.push(text);
    if (text === '/api/control/status') {
      return response({status: 'ready', access: {mode: 'local'}, panels: []});
    }
    if (text === '/api/control/structure/applications') {
      return response({items: [{id: 'alpha', displayName: 'Alpha', description: 'Fixture'},
        {id: 'beta', displayName: 'Beta', description: 'Fixture'}]});
    }
    if (text === '/api/control/structure/applications/alpha') return response({id: 'alpha', displayName: 'Alpha'});
    if (text === '/api/control/structure/applications/alpha/state-spaces') return response({items: []});
    if (text === '/api/control/structure/applications/alpha/catalog') return response({status: 'empty', collections: []});
    if (text === '/api/control/structure/applications/alpha/component-types?limit=25') return response(firstPage);
    if (text.startsWith('/api/control/structure/applications/alpha/component-types?limit=25&cursor=')) {
      if (delaySecondPage) await new Promise(resolve => setTimeout(resolve, delaySecondPage));
      const continuation = continuationPages
        ? continuationPages[Math.min(continuationCall++, continuationPages.length - 1)]
        : secondPage;
      return response(continuation);
    }
    if (text === '/api/control/structure/applications/beta') return response({id: 'beta', displayName: 'Beta'});
    if (text === '/api/control/structure/applications/beta/state-spaces') return response({items: []});
    if (text === '/api/control/structure/applications/beta/catalog') return response({status: 'empty', collections: []});
    if (text === '/api/control/structure/applications/beta/component-types?limit=25') return response(page([type(100)], null));
    throw new Error(`unexpected fixture request: ${text}`);
  };
  dom.window.eval(inlineSource);
  const waitFor = async predicate => {
    for (let attempt = 0; attempt < 100; attempt += 1) {
      if (predicate()) return;
      await new Promise(resolve => setTimeout(resolve, 5));
    }
    assert.fail('control-center fixture did not settle');
  };
  await waitFor(() => dom.window.document.querySelector('[data-component-type-status]'));
  const section = [...dom.window.document.querySelectorAll('.explorer-section')]
    .find(value => value.querySelector('h3')?.textContent === 'Component contracts');
  assert.ok(section);
  return {dom, requests, list: section.querySelector('.explorer-list'), waitFor};
}

test('control-center explorer loads component contract continuations with honest loaded state', async () => {
  const fixture = await mount({secondPage: page(Array.from({length: 25}, (_, index) => type(index + 26)), null)});
  assert.match(fixture.list.querySelector('[data-component-type-status]').textContent, /Loaded 25 component contracts; more contracts are available/);
  assert.ok([...fixture.list.querySelectorAll('button')].some(button => button.textContent === 'Load more component contracts'));
  assert.equal(fixture.requests.filter(path => path.includes('/component-types')).length, 1);

  fixture.list.querySelector('button:last-child').click();
  fixture.list.querySelector('button:last-child').click();
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('Loaded all 50'));
  assert.equal(fixture.list.querySelectorAll('.explorer-choice').length, 50);
  assert.equal(fixture.list.querySelectorAll('button').length, 50);
  assert.equal(fixture.requests.filter(path => path.includes('/component-types')).length, 2);
});

test('control-center explorer fences a delayed page when the application scope changes', async () => {
  const fixture = await mount({delaySecondPage: 30});
  fixture.list.querySelector('button:last-child').click();
  fixture.dom.window.location.hash = '#/applications/beta';
  const currentList = () => [...fixture.dom.window.document.querySelectorAll('.explorer-section')]
    .find(value => value.querySelector('h3')?.textContent === 'Component contracts')?.querySelector('.explorer-list');
  await fixture.waitFor(() => currentList()?.querySelector('[data-component-type-status]')?.textContent.includes('Loaded all 1'));
  assert.equal(currentList().querySelectorAll('.explorer-choice').length, 1);
  assert.match(currentList().querySelector('.explorer-choice').textContent, /^fixture\.component\.100/);
});

test('control-center explorer stops repeated cursors without issuing an unscoped retry', async () => {
  const fixture = await mount({secondPage: page([type(26)], 'cursor-1')});
  fixture.list.querySelector('button:last-child').click();
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('repeated a continuation cursor'));
  assert.equal(fixture.list.querySelectorAll('.explorer-choice').length, 26);
  assert.equal(fixture.list.querySelectorAll('button').length, 26);
  assert.equal(fixture.requests.filter(path => path.includes('/component-types')).length, 2);
});

test('control-center explorer keeps loaded contracts while a failed continuation is retried', async () => {
  const fixture = await mount();
  let fail = true;
  const originalFetch = fixture.dom.window.fetch;
  fixture.dom.window.fetch = async path => {
    if (String(path).includes('cursor=cursor-1') && fail) {
      fail = false;
      return {ok: false, status: 503, json: async () => ({})};
    }
    return originalFetch(path);
  };
  fixture.list.querySelector('button:last-child').click();
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('next page is unavailable'));
  assert.equal(fixture.list.querySelectorAll('.explorer-choice').length, 25);
  const retry = fixture.list.querySelector('button:last-child');
  assert.equal(retry.textContent, 'Retry component contracts');
  retry.click();
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('Loaded all 26'));
  assert.equal(fixture.list.querySelectorAll('.explorer-choice').length, 26);
});

test('control-center explorer detects a continuation cursor cycle before issuing a repeated request', async () => {
  const fixture = await mount({continuationPages: [
    page([type(26)], 'cursor-2'),
    page([type(27)], 'cursor-1')
  ]});
  fixture.list.querySelector('button:last-child').click();
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('Loaded 26'));
  fixture.list.querySelector('button:last-child').click();
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('Loaded 27 component contracts; more contracts are available'));
  fixture.list.querySelector('button:last-child').click();
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('repeated a continuation cursor'));
  assert.equal(fixture.list.querySelectorAll('.explorer-choice').length, 27);
  assert.equal(fixture.requests.filter(path => path.includes('/component-types')).length, 3);
});

test('control-center explorer stops and clears continuation at the retained contract bound on an overflowing final page', async () => {
  const continuationPages = [];
  for (let pageIndex = 0; pageIndex < 19; pageIndex += 1) {
    continuationPages.push(page(
      Array.from({length: 25}, (_, index) => type(26 + pageIndex * 25 + index)),
      `cursor-${pageIndex + 2}`));
  }
  continuationPages.push(page(Array.from({length: 25}, (_, index) => type(501 + index)), null));
  const fixture = await mount({continuationPages});
  for (let pageIndex = 0; pageIndex < continuationPages.length; pageIndex += 1) {
    fixture.list.querySelector('button:last-child').click();
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  await fixture.waitFor(() => fixture.list.querySelector('[data-component-type-status]')?.textContent.includes('512-contract display bound'));
  assert.equal(fixture.list.querySelectorAll('.explorer-choice').length, 512);
  assert.equal(fixture.list.querySelectorAll('button').length, 512);
  assert.equal(fixture.requests.filter(path => path.includes('/component-types')).length, 21);
});
