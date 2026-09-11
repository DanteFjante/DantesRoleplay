import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import test from 'node:test';

const require = createRequire(new URL('../../dnd2024/package.json', import.meta.url));
const { JSDOM } = require('jsdom');
const source = await readFile(new URL('../../../../../DantesRoleplay.Web/BrowserComponents/application-conversation.js', import.meta.url), 'utf8');
const importSource = "import('/components/system-client.js')\n  .then(module => module.systemWebClient)";
const normalizedSource = source.replaceAll('\r\n', '\n');
assert.ok(normalizedSource.includes(importSource), 'Only the external client import is substituted in the actual component source.');
const deferred = () => {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
};
const tick = async () => { for (let index = 0; index < 4; index++) await new Promise(done => setTimeout(done, 0)); };
const message = (ordinal, text = `message ${ordinal}`) => ({ ordinal, role: 'player', text });
const view = overrides => ({ id: 'conversation.fixture', status: 'ready', messages: [message(5)],
  currentSituation: null, hasEarlierMessages: false, activeAgenda: null, ...overrides });

async function mount(requestJson, clientReady) {
  const dom = new JSDOM('<!doctype html><body></body>', { url: 'https://table.example.test/', runScripts: 'outside-only' });
  dom.window.__conversationClient = clientReady ?? Promise.resolve({ requestJson });
  dom.window.eval(normalizedSource.replace(importSource, 'window.__conversationClient'));
  const widget = dom.window.document.createElement('application-conversation');
  widget.setAttribute('application-id', 'fixture');
  widget.setAttribute('state-space-id', 'state.fixture');
  widget.setAttribute('session-context-id', 'session.fixture');
  const errors = [];
  widget.addEventListener('error', event => errors.push(event.detail));
  dom.window.document.body.append(widget);
  await tick();
  return { dom, widget, errors, close: () => dom.window.close(),
    button: text => [...widget.querySelectorAll('button')].find(button => button.textContent === text) };
}

test('conversation keeps valid fields and rows despite malformed optional collections', async () => {
  const fixture = await mount(async () => view({
    messages: [null, message(2, 'retained dialogue'), message(3), message(3, 'ambiguous'), { ordinal: 4, role: {}, text: {} }],
    currentSituation: { kind: 'exploration', summary: 'retained summary', participants: { wrong: [] }, location: { name: 'A known place' } },
    activeAgenda: { status: 'running', tasks: [null, { ordinal: 1, status: 'running', batches: { wrong: [] } }] },
  }));
  try {
    assert.match(fixture.widget.textContent, /retained dialogue/);
    assert.match(fixture.widget.textContent, /retained summary/);
    assert.match(fixture.widget.textContent, /A known place/);
    assert.match(fixture.widget.textContent, /Message text unavailable/);
    assert.match(fixture.widget.textContent, /Step progress unavailable/);
    assert.match(fixture.widget.textContent, /Some participant details are unavailable/);
    assert.match(fixture.widget.textContent, /Some interactions are unavailable/);
    assert.doesNotMatch(fixture.widget.textContent, /ambiguous|\[object Object\]/);
    assert.deepEqual(fixture.errors, []);
  } finally { fixture.close(); }
});

test('only declared unfinished agenda states expose the replacement affordance', async () => {
  for (const status of ['planning', 'awaiting-confirmation', 'needs-attention', 'completed', 'cancelled', 'future-state']) {
    const fixture = await mount(async () => view({ activeAgenda: { status, tasks: [] } }));
    try {
      const label = [...fixture.widget.querySelectorAll('label')].find(value => value.textContent.includes('Replace unfinished'));
      assert.equal(label.hidden, !['planning', 'awaiting-confirmation', 'needs-attention'].includes(status), status);
    } finally { fixture.close(); }
  }
});

test('conversation bounds message collections and does not promote unknown command state', async () => {
  const fixture = await mount(async () => view({ status: { value: 'awaiting-confirmation' },
    messages: Array.from({ length: 65 }, (_, index) => message(index + 1)), currentSituation: [] }));
  try {
    assert.match(fixture.widget.textContent, /Some interactions are unavailable/);
    assert.match(fixture.widget.textContent, /Current situation unavailable/);
    assert.equal(fixture.button('Confirm actions').hidden, true);
    assert.equal(fixture.widget.querySelector('[role=log]').querySelectorAll('p').length, 1);
    assert.deepEqual(fixture.errors, []);
  } finally { fixture.close(); }
});

test('conversation accepts the server maximum of 64 recent messages and bounds retained text', async () => {
  const full = await mount(async () => view({ messages: Array.from({ length: 64 }, (_, index) => message(index + 1)) }));
  try { assert.equal(full.widget.querySelector('[role=log]').querySelectorAll('p').length, 64); }
  finally { full.close(); }
  const oversized = await mount(async () => view({ messages: Array.from({ length: 64 }, (_, index) => message(index + 1, 'x'.repeat(8000))) }));
  try {
    assert.match(oversized.widget.textContent, /Some interactions are unavailable/);
    assert.equal(oversized.widget.querySelector('[role=log]').querySelectorAll('p').length, 1);
  } finally { oversized.close(); }
});

test('optional captions and an invalid neighboring attachment do not hide a valid same-owner image', async () => {
  const owner = '/api/applications/fixture/state-spaces/state.fixture/entities/location.fixture/media';
  const fixture = await mount(async path => path.endsWith('/media') ? {
    entityId: 'location.fixture', attachments: [
      { role: 'setting', mediaId: 'bad' },
      { role: 'setting', mediaId: 'escape', mediaType: 'image/png', width: 20, height: 20,
        alt: 'Wrong owner', contentUrl: `${owner}/../private/media/image/content` },
      { role: 'setting', mediaId: 'image', mediaType: 'image/png', width: 20, height: 20,
        alt: 'Visible scene', caption: {}, contentUrl: `${owner}/image/content` },
    ],
  } : view({ currentSituation: { kind: 'exploration', summary: 'Scene', location: { id: 'location.fixture' } } }));
  try {
    assert.equal(fixture.widget.querySelectorAll('img').length, 1);
    assert.equal(fixture.widget.querySelector('img').getAttribute('src'), `${owner}/image/content`);
    assert.equal(fixture.widget.querySelector('figcaption').textContent, 'Visible scene');
    assert.deepEqual(fixture.errors, []);
  } finally { fixture.close(); }
});

test('disconnect during client import prevents old generation from issuing a request', async () => {
  const ready = deferred();
  const calls = [];
  const fixture = await mount(null, ready.promise);
  try {
    fixture.widget.remove();
    fixture.dom.window.document.body.append(fixture.widget);
    ready.resolve({ requestJson: async (path, options) => { calls.push({ path, options }); return view(); } });
    await tick();
    assert.equal(calls.length, 1);
    assert.equal(calls[0].options.signal.aborted, false);
    assert.deepEqual(fixture.errors, []);
  } finally { fixture.close(); }
});

test('ignored abort cannot render an old generation into a reconnected conversation', async () => {
  const old = deferred();
  let calls = 0;
  const fixture = await mount(async () => ++calls === 1 ? old.promise : view({ messages: [message(1, 'new generation')] }));
  try {
    fixture.widget.remove();
    fixture.dom.window.document.body.append(fixture.widget);
    await tick();
    old.resolve(view({ messages: [message(1, 'old generation')] }));
    await tick();
    assert.match(fixture.widget.textContent, /new generation/);
    assert.doesNotMatch(fixture.widget.textContent, /old generation/);
    assert.deepEqual(fixture.errors, []);
  } finally { fixture.close(); }
});

test('send joins creation and cannot issue duplicate pending turns', async () => {
  const create = deferred();
  const turn = deferred();
  const calls = [];
  const fixture = await mount(async path => {
    calls.push(path);
    return path.endsWith('/turns') ? turn.promise : create.promise;
  });
  try {
    fixture.widget.querySelector('textarea').value = 'one turn';
    fixture.button('Send').click();
    fixture.button('Send').click();
    create.resolve(view());
    await tick();
    fixture.button('Send').click();
    assert.equal(calls.length, 2, 'one create and one turn, even during delayed creation');
    turn.resolve(view({ messages: [message(6, 'turn confirmed')], currentSituation: { participants: 42 } }));
    await tick();
    assert.match(fixture.widget.textContent, /turn confirmed/);
    assert.equal(fixture.button('Send').disabled, false);
    assert.deepEqual(fixture.errors, []);
  } finally { fixture.close(); }
});

test('an accepted execute response with malformed optional fields still emits its receipt once', async () => {
  let executions = 0;
  const fixture = await mount(async path => {
    if (!path.endsWith('/execute')) return view({ status: 'awaiting-confirmation' });
    executions++;
    return view({ messages: {}, activeAgenda: { tasks: 5 }, currentSituation: { participants: 8 },
      lastExecution: { receipt: { id: 'receipt.accepted' } } });
  });
  const receipts = [];
  fixture.widget.addEventListener('receipt', event => receipts.push(event.detail));
  try {
    fixture.button('Confirm actions').click();
    await tick();
    assert.equal(executions, 1);
    assert.equal(receipts.length, 1);
    assert.equal(receipts[0].id, 'receipt.accepted');
    assert.deepEqual(fixture.errors, []);
  } finally { fixture.close(); }
});

test('history is scoped to an older ordinal, rejects ambiguity and stops malformed cursors', async () => {
  const fixture = await mount(async path => path.includes('/history?')
    ? { messages: [message(2, 'older valid'), message(3), message(3), message(7, 'not earlier')], nextBeforeOrdinal: 5 }
    : view({ hasEarlierMessages: true }));
  try {
    fixture.button('Load earlier interactions').click();
    await tick();
    assert.match(fixture.widget.textContent, /older valid/);
    assert.doesNotMatch(fixture.widget.textContent, /not earlier|player: message 3/);
    assert.match(fixture.widget.textContent, /Some earlier interactions are unavailable/);
    assert.equal(fixture.button('Load earlier interactions').hidden, true);
  } finally { fixture.close(); }
});

test('a history read started before a new turn cannot overwrite the confirmed turn', async () => {
  const history = deferred();
  const fixture = await mount(async path => path.includes('/history?') ? history.promise :
    path.endsWith('/turns') ? view({ messages: [message(6, 'latest confirmed')] }) : view({ hasEarlierMessages: true }));
  try {
    fixture.button('Load earlier interactions').click();
    fixture.widget.querySelector('textarea').value = 'advance';
    fixture.button('Send').click();
    await tick();
    history.resolve({ messages: [message(1, 'retired history')], nextBeforeOrdinal: null });
    await tick();
    assert.match(fixture.widget.textContent, /latest confirmed/);
    assert.doesNotMatch(fixture.widget.textContent, /retired history/);
  } finally { fixture.close(); }
});
