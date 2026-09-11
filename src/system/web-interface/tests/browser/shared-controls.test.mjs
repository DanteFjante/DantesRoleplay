import assert from 'node:assert/strict';
import {webcrypto} from 'node:crypto';
import {TextEncoder} from 'node:util';
import {createRequire} from 'node:module';
import {readFile} from 'node:fs/promises';
import test from 'node:test';

const require = createRequire(new URL('../../dnd2024/package.json', import.meta.url));
const {JSDOM} = require('jsdom');
const root = new URL('../../../../../DantesRoleplay.Web/BrowserComponents/', import.meta.url);
const tick = async () => { for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0)); };

async function dom() {
  return new JSDOM('<!doctype html><body></body>', {url: 'https://system.example.test/', runScripts: 'outside-only'});
}

test('application entity display keeps valid rows when a neighboring row is malformed', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  fixture.window.fetch = async () => ({ok: true, json: async () => ({items: [
    {entityId: 'entity.good', name: 'Good'}, {entityId: 'entity.bad', name: {unexpected: true}}
  ]})});
  fixture.window.eval(source);
  const picker = fixture.window.document.createElement('application-entity-picker');
  picker.setAttribute('application-id', 'app'); picker.setAttribute('state-space-id', 'state');
  fixture.window.document.body.append(picker);
  await tick();
  try {
    assert.equal(picker.shadowRoot.querySelectorAll('option').length, 3);
    assert.match(picker.shadowRoot.textContent, /Some current entities are unavailable/);
  } finally { picker.remove(); await tick(); fixture.window.close(); }
});

test('governance discovery renders valid capabilities and reports partial coverage', async () => {
  const source = await readFile(new URL('governance-control-center.js', root), 'utf8');
  const fixture = await dom();
  fixture.window.fetch = async () => ({ok: true, json: async () => [
    {id: 'system.good', version: 1, fingerprint: 'A'.repeat(64), owner: 'owner', description: 'Read', mode: 'read',
      inputSchema: {}, outputSchema: {}, contract: {}},
    {id: '../bad'}
  ]});
  fixture.window.eval(source);
  const center = fixture.window.document.createElement('governance-control-center');
  fixture.window.document.body.append(center);
  await tick();
  try {
    assert.equal(center.shadowRoot.querySelectorAll('article').length, 1);
    assert.match(center.shadowRoot.querySelector('[part="status"]').textContent, /Some capability entries are unavailable/);
  } finally { fixture.window.close(); }
});

test('application action retries an uncertain prepare with the original idempotency key', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  const bodies = [];
  let attempts = 0;
  fixture.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => ({
      qualifiedMechanicId: 'mechanic.test', name: 'Test action', description: 'A bounded action.', input: {},
      capability: {id: 'mechanic.test', input: {schemaJson: '{"type":"object","properties":{}}'}}, roles: []
    })};
    if (String(url).endsWith('/prepare')) {
      bodies.push(JSON.parse(options.body)); attempts++;
      if (attempts === 1) throw new Error('connection lost');
      return {ok: true, json: async () => ({ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {}, receipt: {id: 'receipt.test'}})};
    }
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test');
  fixture.window.document.body.append(control);
  await tick();
  await tick();
  control.shadowRoot.querySelector('button').click();
  await tick();
  control.shadowRoot.querySelector('button').click();
  await tick();
  try {
    assert.equal(bodies.length, 2);
    assert.equal(bodies[0].idempotencyKey, bodies[1].idempotencyKey);
    assert.deepEqual(bodies[0].input, bodies[1].input);
  } finally { fixture.window.close(); }
});

test('reloaded malformed successful action recovery only performs a read and stays fenced', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  fixture.window.sessionStorage.setItem('dantes.application-action-recovery.v1', JSON.stringify({
    'app\nstate\nmechanic.test': {phase: 'execute', key: 'application-execute.saved', resolutionReceiptId: 'receipt.saved'}
  }));
  const calls = [];
  fixture.window.fetch = async (url, options = {}) => {
    calls.push({url: String(url), method: options.method || 'GET'});
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => ({
      qualifiedMechanicId: 'mechanic.test', name: 'Test action', description: 'A bounded action.', input: {},
      capability: {id: 'mechanic.test', input: {schemaJson: '{"type":"object","properties":{}}'}}, roles: []
    })};
    if (String(url).includes('/recoveries/')) return {ok: true, json: async () => ({})};
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state'); control.setAttribute('mechanic-id', 'mechanic.test');
  fixture.window.document.body.append(control); await tick(); await tick();
  try {
    assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
    assert.equal(control.shadowRoot.querySelector('button').disabled, true);
    assert.match(control.shadowRoot.textContent, /remains uncertain/i);
    assert.ok(Array.from(control.shadowRoot.querySelectorAll('button')).some(button => /check earlier action again/i.test(button.textContent)));
    assert.ok(fixture.window.sessionStorage.getItem('dantes.application-action-recovery.v1'));
  } finally { fixture.window.close(); }
});

test('application action records its recovery fence before a held POST, so a reload does not post again', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const first = await dom(); let resolvePost; const held = new Promise(resolve => { resolvePost = resolve; });
  first.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) return held;
    throw new Error(`unexpected ${url}`);
  };
  first.window.eval(source);
  const initial = first.window.document.createElement('application-action-button');
  initial.setAttribute('application-id', 'app'); initial.setAttribute('state-space-id', 'state'); initial.setAttribute('mechanic-id', 'mechanic.test');
  first.window.document.body.append(initial); await tick(); initial.shadowRoot.querySelector('button').click(); await tick();
  const fence = first.window.sessionStorage.getItem('dantes.application-action-recovery.v1');
  assert.ok(fence && fence.includes('application-prepare'));
  const stored = JSON.parse(fence);
  assert.deepEqual(Object.keys(stored), ['app\nstate\nmechanic.test']);
  assert.deepEqual(Object.keys(stored['app\nstate\nmechanic.test']).sort(), ['key', 'phase'],
    'Recovery may retain request identity, never an input body, role data, or confirmed game state');
  const reloaded = await dom(); reloaded.window.sessionStorage.setItem('dantes.application-action-recovery.v1', fence);
  const calls = [];
  reloaded.window.fetch = async (url, options = {}) => {
    calls.push({url: String(url), method: options.method || 'GET'});
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).includes('/recoveries/')) return {ok: false, status: 404, json: async () => ({})};
    throw new Error(`unexpected ${url}`);
  };
  reloaded.window.eval(source);
  const control = reloaded.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state'); control.setAttribute('mechanic-id', 'mechanic.test');
  reloaded.window.document.body.append(control); await tick();
  try { assert.equal(calls.filter(call => call.method === 'POST').length, 0); assert.equal(control.shadowRoot.querySelector('button').disabled, true); }
  finally { resolvePost?.({ok: false, status: 503, json: async () => ({})}); first.window.close(); reloaded.window.close(); }
});

test('application action fails closed when recovery metadata cannot be persisted', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom(); let posts = 0;
  Object.defineProperty(fixture.window, 'sessionStorage', {configurable: true, value: {
    getItem: () => null, setItem: () => { throw new Error('storage unavailable'); }
  }});
  fixture.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if ((options.method || 'GET') === 'POST') posts++;
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state'); control.setAttribute('mechanic-id', 'mechanic.test');
  fixture.window.document.body.append(control); await tick(); control.shadowRoot.querySelector('button').click(); await tick();
  try { assert.equal(posts, 0); assert.match(control.shadowRoot.textContent, /cannot be safely sent/i); }
  finally { fixture.window.close(); }
});

test('reloaded terminal action recovery clears metadata without replaying its execution', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  fixture.window.sessionStorage.setItem('dantes.application-action-recovery.v1', JSON.stringify({
    'app\nstate\nmechanic.test': {phase: 'execute', key: 'application-execute.done', resolutionReceiptId: 'receipt.done'}
  }));
  const calls = [];
  fixture.window.fetch = async (url, options = {}) => {
    calls.push({url: String(url), method: options.method || 'GET'});
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => ({
      qualifiedMechanicId: 'mechanic.test', name: 'Test action', description: 'A bounded action.', input: {},
      capability: {id: 'mechanic.test', input: {schemaJson: '{"type":"object","properties":{}}'}}, roles: []
    })};
    if (String(url).includes('/recoveries/')) return {ok: true, json: async () => ({id: 'receipt.execution', kind: 'execution', status: 'succeeded', code: 'DONE', safeSummary: 'Recovered safely.', resolutionReceiptId: 'receipt.done', idempotencyKey: 'application-execute.done', applicationId: 'app', stateSpaceId: 'state'})};
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state'); control.setAttribute('mechanic-id', 'mechanic.test');
  fixture.window.document.body.append(control); await tick(); await tick();
  try {
    assert.equal(calls.filter(call => call.method !== 'GET').length, 0);
    assert.equal(control.shadowRoot.querySelector('button').disabled, false);
    assert.equal(fixture.window.sessionStorage.getItem('dantes.application-action-recovery.v1'), '{}');
    assert.match(control.shadowRoot.textContent, /Recovered safely/);
  } finally { fixture.window.close(); }
});

function actionDescriptor() {
  return {qualifiedMechanicId: 'mechanic.test', name: 'Test action', description: 'A bounded action.', input: {},
    capability: {id: 'mechanic.test', input: {schemaJson: '{"type":"object","properties":{}}'}}, roles: []};
}

function actionOutcome() {
  return {successful: true, code: 'INTERACTION_EXECUTION_SUCCEEDED', safeSummary: 'Done.', actionResults: [],
    receipt: {receipt: {id: 'execution.test', status: 'succeeded'}}};
}

test('application action clears a known prepare rejection and permits a fresh request', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  const bodies = [];
  let attempts = 0;
  fixture.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) {
      bodies.push(JSON.parse(options.body)); attempts++;
      if (attempts === 1) return {ok: false, status: 400, json: async () => ({error: 'APPLICATION_ACTION_INPUT_INVALID', message: 'Input rejected.'})};
      return {ok: true, json: async () => ({ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {}, receipt: {id: 'receipt.test'}})};
    }
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await tick();
  control.shadowRoot.querySelector('button').click(); await tick();
  try {
    assert.equal(control._pendingPrepare, null);
    assert.equal(control.shadowRoot.querySelector('button').disabled, false);
    assert.match(control.shadowRoot.querySelector('.status').textContent, /Input rejected/);
    control.shadowRoot.querySelector('button').click(); await tick();
    assert.equal(bodies.length, 2);
    assert.notEqual(bodies[0].idempotencyKey, bodies[1].idempotencyKey);
  } finally { fixture.window.close(); }
});

test('application action keeps malformed execution uncertain and retries the exact payload', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  const bodies = [];
  let executes = 0;
  fixture.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) return {ok: true, json: async () => ({ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {step: 'exact'}, receipt: {id: 'receipt.test'}})};
    if (String(url).endsWith('/execute')) {
      bodies.push(JSON.parse(options.body)); executes++;
      return {ok: true, json: async () => executes === 1 ? {} : actionOutcome()};
    }
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await tick(); control.shadowRoot.querySelector('button').click(); await tick();
  control.shadowRoot.querySelector('.review button').click(); await tick();
  try {
    assert.equal(bodies.length, 1);
    assert.ok(control._pendingExecute);
    assert.match(control.shadowRoot.querySelector('.status').textContent, /may have been accepted/);
    control.shadowRoot.querySelector('.review button').click(); await tick();
    assert.equal(bodies.length, 2);
    assert.equal(bodies[0].idempotencyKey, bodies[1].idempotencyKey);
    assert.deepEqual(bodies[0], bodies[1]);
    assert.equal(control._pendingExecute, null);
  } finally { fixture.window.close(); }
});

test('application action rejects semantically inconsistent execution receipts without clearing recovery', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  const bodies = [];
  let executes = 0;
  fixture.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) return {ok: true, json: async () => ({
      ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {step: 'exact'}, receipt: {id: 'receipt.test'}
    })};
    if (String(url).endsWith('/execute')) {
      bodies.push(JSON.parse(options.body)); executes++;
      const malformed = executes === 1
        ? {successful: true, code: 'INTERACTION_EXECUTION_FAILED', safeSummary: 'Contradictory.',
          receipt: {receipt: {id: 'execution.test', status: 'failed'}}}
        : executes === 2
          ? {successful: true, code: 'INTERACTION_EXECUTION_SUCCEEDED', safeSummary: 'Unknown status.',
            receipt: {receipt: {id: 'execution.test', status: 'future-status'}}}
          : actionOutcome();
      return {ok: true, json: async () => malformed};
    }
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await tick(); control.shadowRoot.querySelector('button').click(); await tick();
  control.shadowRoot.querySelector('.review button').click(); await tick();
  assert.ok(control._pendingExecute);
  assert.match(control.shadowRoot.querySelector('.status').textContent, /may have been accepted/);
  control.shadowRoot.querySelector('.review button').click(); await tick();
  try {
    assert.ok(control._pendingExecute);
    assert.equal(bodies.length, 2);
    assert.deepEqual(bodies[0], bodies[1]);
    control.shadowRoot.querySelector('.review button').click(); await tick();
    assert.equal(control._pendingExecute, null);
    assert.equal(bodies.length, 3);
    assert.deepEqual(bodies[0], bodies[2]);
  } finally { fixture.window.close(); }
});

test('application action keeps an invalid prepared command fenced before review', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom(); let prepareCalls = 0;
  fixture.window.fetch = async (url) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) {
      prepareCalls++;
      return {ok: true, json: async () => ({ready: true, proposalFingerprint: 'not-a-fingerprint', proposal: [], receipt: {id: ''}})};
    }
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await tick(); control.shadowRoot.querySelector('button').click(); await tick();
  try {
    assert.equal(prepareCalls, 1);
    assert.ok(control._pendingPrepare);
    assert.equal(control._prepared, null);
    assert.match(control.shadowRoot.querySelector('.status').textContent, /may have been accepted/);
  } finally { fixture.window.close(); }
});

test('application action ignores a late prepare outcome after disconnect', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  let resolvePrepare;
  fixture.window.fetch = async (url) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) return new Promise(resolve => { resolvePrepare = () => resolve({ok: true, json: async () => ({ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {}, receipt: {id: 'receipt.test'}})}); });
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await tick(); control.shadowRoot.querySelector('button').click(); await tick();
  const before = control.shadowRoot.textContent;
  control.remove(); resolvePrepare(); await tick();
  try { assert.equal(control.shadowRoot.textContent, before); assert.equal(control._prepared, null); }
  finally { fixture.window.close(); }
});

test('uncertain execution blocks new preparation and survives reconnect and scope changes', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  const executions = [];
  let preparations = 0;
  fixture.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) {
      preparations++;
      return {ok: true, json: async () => ({ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {original: true}, receipt: {id: 'receipt.test'}})};
    }
    if (String(url).endsWith('/execute')) {
      executions.push({url: String(url), body: JSON.parse(options.body)});
      if (executions.length === 1) throw new Error('lost response');
      return {ok: true, json: async () => actionOutcome()};
    }
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await control._prepare(); await control._execute();
  try {
    await control._prepare();
    assert.equal(preparations, 1);
    assert.throws(() => { control.input = {changed: true}; }, /Recover the exact reviewed execution/);
    assert.throws(() => { control.roleEntityIds = {other: 'entity.other'}; }, /Recover the exact reviewed execution/);
    control.remove(); fixture.window.document.body.append(control); await tick();
    assert.ok(control.shadowRoot.querySelector('.review button'));
    assert.ok(control._pendingExecute);
    control.setAttribute('state-space-id', 'other'); await tick();
    assert.equal(control.shadowRoot.querySelector('.review button'), null);
    assert.match(control.shadowRoot.querySelector('.status').textContent, /Return to its application/);
    await control._prepare();
    assert.equal(preparations, 1);
    control.setAttribute('state-space-id', 'state'); await tick();
    await control._execute();
    assert.equal(executions.length, 2);
    assert.deepEqual(executions[1], executions[0]);
    assert.equal(control._pendingExecute, null);
    assert.equal(control.shadowRoot.querySelector('button').disabled, false);
  } finally { fixture.window.close(); }
});

test('uncertain preparation keeps its original key when the control reconnects', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  const bodies = [];
  fixture.window.fetch = async (url, options = {}) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) {
      bodies.push(JSON.parse(options.body));
      if (bodies.length === 1) throw new Error('lost response');
      return {ok: true, json: async () => ({ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {}, receipt: {id: 'receipt.test'}})};
    }
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await control._prepare();
  control.remove(); fixture.window.document.body.append(control); await tick(); await control._prepare();
  try { assert.equal(bodies.length, 2); assert.deepEqual(bodies[1], bodies[0]); }
  finally { fixture.window.close(); }
});

test('late failed entity discovery cannot erase a newer scope', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  let rejectOld;
  fixture.window.fetch = async url => String(url).includes('/state-spaces/old/')
    ? new Promise((resolve, reject) => { rejectOld = reject; })
    : {ok: true, json: async () => ({items: [{entityId: 'entity.new', name: 'New scope'}]})};
  fixture.window.eval(source);
  const picker = fixture.window.document.createElement('application-entity-picker');
  picker.setAttribute('application-id', 'app'); picker.setAttribute('state-space-id', 'old');
  fixture.window.document.body.append(picker); await tick();
  picker.setAttribute('state-space-id', 'new'); await tick(); rejectOld(new Error('Old scope unavailable')); await tick();
  try {
    assert.match(picker.shadowRoot.textContent, /New scope/);
    assert.doesNotMatch(picker.shadowRoot.textContent, /Old scope unavailable/);
  } finally { fixture.window.close(); }
});

test('late failed action discovery cannot replace a newer descriptor', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  let rejectOld;
  fixture.window.fetch = async url => String(url).includes('/state-spaces/old/')
    ? new Promise((resolve, reject) => { rejectOld = reject; })
    : {ok: true, json: async () => actionDescriptor()};
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'old');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control); await tick();
  control.setAttribute('state-space-id', 'new'); await tick(); rejectOld(new Error('Old scope unavailable')); await tick();
  try {
    assert.match(control.shadowRoot.querySelector('.status').textContent, /Ready:/);
    assert.equal(control.shadowRoot.querySelector('button').disabled, false);
  } finally { fixture.window.close(); }
});

test('application action ignores a late execute outcome after disconnect', async () => {
  const source = await readFile(new URL('application-workspace.js', root), 'utf8');
  const fixture = await dom();
  let resolveExecute;
  fixture.window.fetch = async (url) => {
    if (String(url).endsWith('/mechanics/mechanic.test')) return {ok: true, json: async () => actionDescriptor()};
    if (String(url).endsWith('/prepare')) return {ok: true, json: async () => ({ready: true, proposalFingerprint: 'A'.repeat(64), proposal: {}, receipt: {id: 'receipt.test'}})};
    if (String(url).endsWith('/execute')) return new Promise(resolve => { resolveExecute = () => resolve({ok: true, json: async () => actionOutcome()}); });
    throw new Error(`unexpected ${url}`);
  };
  fixture.window.eval(source);
  const control = fixture.window.document.createElement('application-action-button');
  control.setAttribute('application-id', 'app'); control.setAttribute('state-space-id', 'state');
  control.setAttribute('mechanic-id', 'mechanic.test'); fixture.window.document.body.append(control);
  await tick(); await tick(); control.shadowRoot.querySelector('button').click(); await tick();
  control.shadowRoot.querySelector('.review button').click(); await tick();
  const before = control.shadowRoot.textContent;
  control.remove(); resolveExecute(); await tick();
  try { assert.equal(control.shadowRoot.textContent, before); assert.ok(control._pendingExecute); }
  finally { fixture.window.close(); }
});

test('AI result display keeps independent useful fields when optional entries are malformed', async () => {
  const source = await readFile(new URL('ai-workspace.js', root), 'utf8');
  const fixture = await dom();
  fixture.window.__aiClient = {requestJson: async () => ({}), discoverAllApplications: async () => ({applications: []})};
  fixture.window.SystemClientError = class extends Error { constructor(code, message) { super(message); this.code = code; } };
  fixture.window.SystemRequestScope = class {begin() { return {signal: new AbortController().signal, isCurrent: () => true};} cancel() {}};
  fixture.window.systemWebClient = fixture.window.__aiClient;
  const script = source.replace("import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';",
    'const {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} = window;')
    .replace("import '/components/system-publication.js';", '');
  fixture.window.eval(script);
  const workspace = fixture.window.document.createElement('ai-workspace');
  fixture.window.document.body.append(workspace);
  workspace._renderResult({ok: true, provider: 'local', model: 'fixture', assistantMessage: 'Useful answer',
    toolCalls: [null, {name: {}, status: {}}], activities: [null, {kind: {}, summary: {}, status: {}}]});
  try {
    assert.match(workspace.shadowRoot.textContent, /Useful answer/);
    assert.doesNotMatch(workspace.shadowRoot.textContent, /\[object Object\]/);
  } finally { workspace.remove(); await tick(); fixture.window.close(); }
});

test('page administration disables revision-dependent actions when page revisions are missing', async () => {
  const source = await readFile(new URL('page-administration.js', root), 'utf8');
  const fixture = await dom();
  const client = {
    discoverAllApplications: async () => ({applications: [{applicationId: 'app', displayName: 'App', pages: []}]}),
    requestJson: async path => {
      if (path === '/api/control/web/page-migration') return {linkedApplicationPages: 1, unclassifiablePages: 0, contentVerified: true, items: []};
      if (path.endsWith('/pages')) return [{entityId: 'page.home', title: 'Home', navigationLabel: 'Home', slug: 'home', order: 0, visibility: 'public'}];
      if (path.endsWith('/page.home')) return {entityId: 'page.home', title: 'Home', navigationLabel: 'Home', slug: 'home', order: 0, visibility: 'public', enabled: true, content: null};
      throw new Error(`unexpected ${path}`);
    }
  };
  fixture.window.systemWebClient = client;
  const script = source.replace("import {systemWebClient} from '/components/system-client.js';", 'const {systemWebClient} = window;')
    .replace("import '/components/system-publication.js';", '');
  fixture.window.eval(script);
  const admin = fixture.window.document.createElement('page-administration');
  fixture.window.document.body.append(admin);
  await tick(); await tick(); await tick();
  try {
    admin.shadowRoot.querySelector('[data-pages] button').click();
    await tick(); await tick();
    const buttons = [...admin.shadowRoot.querySelectorAll('button')];
    assert.equal(buttons.find(value => value.textContent === 'Save metadata').disabled, true);
    assert.equal(buttons.find(value => value.textContent === 'Permanently remove disabled identity').disabled, true);
    assert.equal(admin.shadowRoot.querySelectorAll('input[type="checkbox"]')[1].disabled, true);
    assert.match(admin.shadowRoot.textContent, /metadata or its revision is unavailable/);
  } finally { fixture.window.close(); }
});

test('AI recovery never silently retries after the prompt changes, but retries exact saved input', async () => {
  const source = await readFile(new URL('ai-workspace.js', root), 'utf8');
  const fixture = await dom();
  const bodies = [];
  let attempts = 0;
  fixture.window.SystemClientError = Error;
  fixture.window.SystemRequestScope = class {
    constructor() { this._current = null; }
    begin() { const controller = new AbortController(); const request = {signal: controller.signal, isCurrent: () => this._current === request}; this._current = request; return request; }
    cancel() { this._current = null; }
  };
  const client = {requestJson: async (path, options = {}) => {
    if (path === '/api/control/ai/requests') {
      bodies.push({...options.body}); attempts++;
      if (attempts === 1) throw new Error('connection lost');
      return {conversationId: 'conversation.test', ok: true, assistantMessage: 'Saved'};
    }
    if (path.includes('/conversations/')) throw new Error('refresh unavailable');
    return {items: []};
  }, discoverAllApplications: async () => ({applications: []})};
  fixture.window.systemWebClient = client;
  fixture.window.interruptedRequestStore = {read: () => null, write: () => true, remove: () => {}};
  const script = source.replace("import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';",
    'const {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} = window;').replace("import '/components/system-publication.js';", '');
  fixture.window.eval(script);
  const workspace = fixture.window.document.createElement('ai-workspace');
  workspace._client = client; workspace._connected = true;
  workspace._trustedRecoveryContext = async () => ({contextFingerprint: 'A'.repeat(64), sourceReferences: ['surface:inner']});
  const providerOption = fixture.window.document.createElement('option'); providerOption.value = 'local'; providerOption.textContent = 'Local';
  const modelOption = fixture.window.document.createElement('option'); modelOption.value = 'fixture'; modelOption.textContent = 'Fixture';
  workspace._provider.control.append(providerOption); workspace._model.control.append(modelOption);
  workspace._provider.control.value = 'local'; workspace._model.control.value = 'fixture';
  assert.equal(workspace._provider.control.value, 'local');
  assert.equal(workspace._model.control.value, 'fixture');
  workspace._operation.control.value = 'message'; workspace._input.value = 'original prompt';
  await workspace._submit();
  workspace._input.value = 'changed prompt';
  await workspace._submit();
  assert.equal(bodies.length, 1);
  workspace._input.value = 'original prompt';
  await workspace._submit();
  try {
    assert.equal(bodies.length, 2);
    assert.equal(bodies[0].idempotencyKey, bodies[1].idempotencyKey);
    assert.equal(bodies[0].input, bodies[1].input);
  } finally { fixture.window.close(); }
});

test('AI saved result reports refresh failure without leaving an old pending submission', async () => {
  const source = await readFile(new URL('ai-workspace.js', root), 'utf8');
  const fixture = await dom();
  fixture.window.SystemClientError = class extends Error { constructor(code, message) { super(message); this.code = code; } };
  fixture.window.SystemRequestScope = class { begin() { return {signal: new AbortController().signal, isCurrent: () => true}; } cancel() {} };
  const client = {requestJson: async (path) => {
    if (path === '/api/control/ai/requests') return {conversationId: 'conversation.test', ok: true, assistantMessage: 'Saved'};
    if (path.includes('/conversations/')) throw new Error('refresh unavailable');
    return {items: []};
  }, discoverAllApplications: async () => ({applications: []})};
  fixture.window.systemWebClient = client;
  fixture.window.interruptedRequestStore = {read: () => null, write: () => true, remove: () => {}};
  const script = source.replace("import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';",
    'const {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} = window;').replace("import '/components/system-publication.js';", '');
  fixture.window.eval(script);
  const workspace = fixture.window.document.createElement('ai-workspace');
  workspace._client = client; workspace._connected = true;
  workspace._trustedRecoveryContext = async () => ({contextFingerprint: 'A'.repeat(64), sourceReferences: ['surface:inner']});
  const providerOption = fixture.window.document.createElement('option'); providerOption.value = 'local'; providerOption.textContent = 'Local';
  const modelOption = fixture.window.document.createElement('option'); modelOption.value = 'fixture'; modelOption.textContent = 'Fixture';
  workspace._provider.control.append(providerOption); workspace._model.control.append(modelOption);
  workspace._provider.control.value = 'local'; workspace._model.control.value = 'fixture';
  assert.equal(workspace._provider.control.value, 'local');
  assert.equal(workspace._model.control.value, 'fixture');
  workspace._operation.control.value = 'message'; workspace._input.value = 'save me';
  await workspace._submit();
  try {
    assert.equal(workspace._pendingSubmission, null);
    assert.match(workspace._feedback.firstElementChild?.error?.message || '', /saved, but the conversation could not be refreshed/i);
  } finally { fixture.window.close(); }
});

test('AI recovery ignores a late response after its application scope changes', async () => {
  const source = await readFile(new URL('ai-workspace.js', root), 'utf8');
  const fixture = await dom(); let resolveRecovery; let loadedConversation = false; let removed = false;
  const deferred = new Promise(resolve => { resolveRecovery = resolve; });
  fixture.window.SystemClientError = class extends Error { constructor(code, message) { super(message); this.code = code; } };
  fixture.window.SystemRequestScope = class {
    constructor() { this.current = null; } begin() { const controller = new AbortController(); const request = {signal: controller.signal, isCurrent: () => this.current === request}; this.current = request; return request; } cancel() { this.current = null; }
  };
  fixture.window.interruptedRequestStore = {read: () => ({kind: 'ai', key: 'web-ai.saved', provider: 'local', surface: 'inner', applicationId: 'app', stateSpaceId: 'state', resolutionFingerprint: 'A'.repeat(64), contextFingerprint: 'B'.repeat(64), sourceReferences: ['application:app@' + 'A'.repeat(64), 'state-space:state@' + 'A'.repeat(64), 'surface:inner']}), write: () => true, remove: () => { removed = true; }};
  const client = {requestJson: async (path) => {
    if (path.includes('/recoveries/')) return deferred;
    if (path.includes('/conversations/')) { loadedConversation = true; return {}; }
    return {items: [], providers: []};
  }, discoverAllApplications: async () => ({applications: []})};
  fixture.window.systemWebClient = client;
  const script = source.replace("import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';",
    'const {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} = window;').replace("import '/components/system-publication.js';", '');
  fixture.window.eval(script);
  const workspace = fixture.window.document.createElement('ai-workspace');
  workspace.setAttribute('surface', 'inner'); workspace.setAttribute('application-id', 'app'); workspace.setAttribute('state-space-id', 'state');
  workspace._client = client; workspace._connected = true;
  const recovery = workspace._recoverInterruptedRequest(); await tick();
  workspace.setAttribute('application-id', 'other'); resolveRecovery({conversationId: 'conversation.saved', turnId: 'turn.saved', status: 'completed'});
  await recovery;
  try { assert.equal(loadedConversation, false); assert.equal(removed, false); assert.equal(workspace._recoveryBlocked, true); }
  finally { fixture.window.close(); }
});

test('AI claims the operation before real context hashing, preventing double-posts and disconnected late sends', async () => {
  const source = await readFile(new URL('ai-workspace.js', root), 'utf8');
  const fixture = await dom(); let releaseDigest; let digests = 0; let posts = 0;
  const digestGate = new Promise(resolve => { releaseDigest = resolve; });
  Object.defineProperty(fixture.window, 'crypto', {configurable: true, value: {
    randomUUID: () => 'digest-race', getRandomValues: values => webcrypto.getRandomValues(values),
    subtle: {digest: async (...args) => { digests++; await digestGate; return webcrypto.subtle.digest(...args); }}
  }});
  fixture.window.TextEncoder = TextEncoder;
  fixture.window.SystemClientError = class extends Error { constructor(code, message) { super(message); this.code = code; } };
  fixture.window.SystemRequestScope = class { constructor() { this.current = null; } begin() { const controller = new AbortController(); const request = {signal: controller.signal, isCurrent: () => this.current === request}; this.current = request; return request; } cancel() { this.current = null; } };
  fixture.window.interruptedRequestStore = {read: () => null, write: () => true, remove: () => {}};
  const client = {requestJson: async (path) => { if (path === '/api/control/ai/requests') posts++; return {items: []}; }, discoverAllApplications: async () => ({applications: []})};
  fixture.window.systemWebClient = client;
  const script = source.replace("import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';",
    'const {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} = window;').replace("import '/components/system-publication.js';", '');
  fixture.window.eval(script);
  const workspace = fixture.window.document.createElement('ai-workspace'); workspace._client = client; workspace._connected = true;
  const provider = fixture.window.document.createElement('option'); provider.value = 'local'; workspace._provider.control.append(provider); workspace._provider.control.value = 'local';
  const model = fixture.window.document.createElement('option'); model.value = 'fixture'; workspace._model.control.append(model); workspace._model.control.value = 'fixture'; workspace._input.value = 'safe request';
  const first = workspace._submit(); await tick(); const duplicate = workspace._submit(); workspace.disconnectedCallback(); releaseDigest();
  await Promise.all([first, duplicate]);
  try { assert.equal(digests, 1); assert.equal(posts, 0); }
  finally { fixture.window.close(); }
});

test('AI recovery safely restores an unconstrained saved application and state selector from trusted bindings', async () => {
  const source = await readFile(new URL('ai-workspace.js', root), 'utf8');
  const fixture = await dom();
  Object.defineProperty(fixture.window, 'crypto', {configurable: true, value: webcrypto}); fixture.window.TextEncoder = TextEncoder;
  fixture.window.SystemClientError = Error; fixture.window.SystemRequestScope = class { begin() { return {signal: new AbortController().signal, isCurrent: () => true}; } cancel() {} };
  fixture.window.interruptedRequestStore = {read: () => null, write: () => true, remove: () => {}}; fixture.window.systemWebClient = {requestJson: async () => ({items: []}), discoverAllApplications: async () => ({applications: []})};
  const script = source.replace("import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';",
    'const {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} = window;').replace("import '/components/system-publication.js';", '');
  fixture.window.eval(script); const workspace = fixture.window.document.createElement('ai-workspace');
  const appFingerprint = 'A'.repeat(64); const stateFingerprint = 'B'.repeat(64);
  workspace._applications = [{applicationId: 'app', resolutionFingerprint: appFingerprint}];
  workspace._stateSpaceBindings = new Map([['state', stateFingerprint]]);
  const provider = fixture.window.document.createElement('option'); provider.value = 'local';
  workspace._provider.control.append(provider); workspace._provider.control.value = 'local';
  workspace._application.control.append(fixture.window.document.createElement('option')); workspace._application.control.options[0].value = 'app';
  workspace._stateSpace.control.append(fixture.window.document.createElement('option')); workspace._stateSpace.control.options[0].value = 'state';
  const body = {surface: 'inner', provider: 'local', applicationId: 'app', resolutionFingerprint: appFingerprint, stateSpaceId: 'state'};
  const trusted = await workspace._trustedRecoveryContext(body);
  workspace._loadStateSpaces = async () => {};
  const restored = await workspace._restoreRecoverySelectors({kind: 'ai', key: 'web-ai.saved', provider: 'local', ...body, ...trusted});
  try { assert.equal(restored, true); assert.equal(workspace._application.control.value, 'app'); assert.equal(workspace._stateSpace.control.value, 'state'); }
  finally { fixture.window.close(); }
});

async function recoveryAiFixture({crypto = webcrypto, saved = null, requestJson = async () => ({items: []})} = {}) {
  const fixture = await dom();
  const clientSource = await readFile(new URL('system-client.js', root), 'utf8');
  const shared = await import(`data:text/javascript;base64,${Buffer.from(clientSource).toString('base64')}`);
  const source = await readFile(new URL('ai-workspace.js', root), 'utf8');
  const metadata = {value: saved, removed: false};
  Object.defineProperty(fixture.window, 'crypto', {configurable: true, value: crypto});
  fixture.window.TextEncoder = TextEncoder;
  fixture.window.__shared = {...shared, interruptedRequestStore: {
    read: () => metadata.value, write: (_slot, value) => { metadata.value = value; return true; },
    remove: () => { metadata.value = null; metadata.removed = true; },
  }, systemWebClient: {requestJson, discoverAllApplications: async () => ({applications: []})}};
  fixture.window.eval(source.replace(
    "import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';",
    'const {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} = window.__shared;')
    .replace("import '/components/system-publication.js';", ''));
  const workspace = fixture.window.document.createElement('ai-workspace');
  workspace._connected = true;
  for (const [control, value] of [[workspace._provider.control, 'local'], [workspace._model.control, 'fixture']]) {
    const option = fixture.window.document.createElement('option'); option.value = value; control.append(option); control.value = value;
  }
  workspace._input.value = 'safe request';
  return {fixture, workspace, metadata};
}

test('AI recovery restores an offered Codex provider before loading its terminal conversation', async () => {
  const saved = {kind: 'ai', key: 'web-ai.codex', provider: 'codex', surface: 'inner', applicationId: null,
    stateSpaceId: null, resolutionFingerprint: null, contextFingerprint: null, sourceReferences: ['surface:inner']};
  const calls = [];
  const {fixture, workspace, metadata} = await recoveryAiFixture({saved, requestJson: async path => {
    calls.push(path);
    if (path.includes('/recoveries/')) return {conversationId: 'conversation.codex', turnId: 'turn.codex',
      status: 'completed', idempotencyKey: saved.key, provider: 'codex', scope: 'system',
      fingerprint: 'A'.repeat(64), sourceReferences: ['surface:inner']};
    if (path.includes('/providers/codex/models')) return {models: [{id: 'codex.default',
      displayName: 'Codex', reasoningEfforts: ['none'], capabilities: [], isDefault: true}]};
    if (path.includes('/conversations?provider=codex')) return {items: []};
    if (path.includes('/conversations/conversation.codex')) return {summary: {id: 'conversation.codex',
      provider: 'codex', scope: 'system', title: 'Recovered', revision: 1, status: 'completed'}, messages: []};
    throw new Error(`unexpected ${path}`);
  }});
  try {
    workspace._provider.control.options[0].value = 'ollama';
    workspace._provider.control.value = 'ollama';
    const codex = fixture.window.document.createElement('option'); codex.value = 'codex'; codex.textContent = 'Codex';
    workspace._provider.control.append(codex);
    await workspace._recoverInterruptedRequest();
    assert.equal(workspace._provider.control.value, 'codex');
    assert.equal(workspace._conversation?.summary?.provider, 'codex');
    assert.ok(calls.some(path => path.includes('/providers/codex/models')));
    assert.ok(calls.some(path => path.includes('/conversations?provider=codex')));
    assert.equal(metadata.removed, true);
  } finally { fixture.window.close(); }
});

test('AI recovery stays fenced when its saved provider is no longer offered', async () => {
  const saved = {kind: 'ai', key: 'web-ai.missing-provider', provider: 'codex', surface: 'inner', applicationId: null,
    stateSpaceId: null, resolutionFingerprint: null, contextFingerprint: null, sourceReferences: ['surface:inner']};
  let calls = 0;
  const {fixture, workspace, metadata} = await recoveryAiFixture({saved, requestJson: async () => { calls++; return {}; }});
  try {
    await workspace._recoverInterruptedRequest();
    assert.equal(calls, 0);
    assert.equal(metadata.removed, false);
    assert.equal(workspace._recoveryBlocked, true);
    assert.match(workspace._feedback.firstElementChild?.error?.message || '', /metadata is invalid/i);
  } finally { fixture.window.close(); }
});

test('AI recovery accepts Ollama public provider with its durable local conversation lane', async () => {
  const saved = {kind: 'ai', key: 'web-ai.ollama', provider: 'ollama', surface: 'inner', applicationId: null,
    stateSpaceId: null, resolutionFingerprint: null, contextFingerprint: null, sourceReferences: ['surface:inner']};
  const {fixture, workspace, metadata} = await recoveryAiFixture({saved, requestJson: async path => {
    if (path.includes('/recoveries/')) return {conversationId: 'conversation.ollama', turnId: 'turn.ollama',
      status: 'completed', idempotencyKey: saved.key, provider: 'ollama', scope: 'system',
      fingerprint: 'A'.repeat(64), sourceReferences: ['surface:inner']};
    if (path.includes('/conversations/conversation.ollama')) return {summary: {id: 'conversation.ollama',
      provider: 'local', scope: 'system', title: 'Recovered', revision: 1, status: 'completed'}, messages: []};
    throw new Error(`unexpected ${path}`);
  }});
  try {
    workspace._provider.control.options[0].value = 'ollama';
    workspace._provider.control.value = 'ollama';
    await workspace._recoverInterruptedRequest();
    assert.equal(workspace._conversation?.summary?.provider, 'local');
    assert.equal(metadata.removed, true);
  } finally { fixture.window.close(); }
});

test('AI terminal recovery keeps a visible refresh failure instead of replacing it with success', async () => {
  const saved = {kind: 'ai', key: 'web-ai.refresh-failure', provider: 'local', surface: 'inner', applicationId: null,
    stateSpaceId: null, resolutionFingerprint: null, contextFingerprint: null, sourceReferences: ['surface:inner']};
  const {fixture, workspace, metadata} = await recoveryAiFixture({saved, requestJson: async path => {
    if (path.includes('/recoveries/')) return {conversationId: 'conversation.missing', turnId: 'turn.saved',
      status: 'completed', idempotencyKey: saved.key, provider: 'local', scope: 'system',
      fingerprint: 'A'.repeat(64), sourceReferences: ['surface:inner']};
    if (path.includes('/conversations/conversation.missing')) throw new Error('conversation unavailable');
    throw new Error(`unexpected ${path}`);
  }});
  try {
    await workspace._recoverInterruptedRequest();
    assert.equal(metadata.removed, true);
    assert.equal(workspace._recoveryBlocked, false);
    assert.match(workspace._feedback.firstElementChild?.error?.message || '', /recovered, but its conversation could not be refreshed/i);
    assert.doesNotMatch(workspace._feedback.textContent, /earlier AI request was recovered\.$/i);
  } finally { fixture.window.close(); }
});

test('AI digest failure releases Send and never issues a request', async () => {
  let calls = 0;
  const {fixture, workspace} = await recoveryAiFixture({crypto: {
    randomUUID: () => 'digest-failed', subtle: {digest: async () => { throw new Error('denied'); }},
  }, requestJson: async () => { calls++; return {}; }});
  try {
    await workspace._submit();
    assert.equal(calls, 0);
    assert.equal(workspace._submitButton.disabled, false);
    assert.equal(workspace._pendingSubmission, null);
    assert.match(workspace._feedback.firstElementChild?.error?.message || '', /cannot be safely bound/);
  } finally { fixture.window.close(); }
});

test('AI recovery claims the operation before discovery and cannot revive it after disconnect', async () => {
  let finish; let calls = 0;
  const saved = {kind: 'ai', key: 'web-ai.saved', provider: 'local', surface: 'inner', applicationId: null,
    stateSpaceId: null, resolutionFingerprint: null, contextFingerprint: null, sourceReferences: ['surface:inner']};
  const {fixture, workspace, metadata} = await recoveryAiFixture({saved, requestJson: async () => { calls++; return {}; }});
  workspace._setupPromise = new Promise(resolve => { finish = resolve; });
  try {
    const recovery = workspace._recoverInterruptedRequest();
    assert.equal(workspace._submitButton.disabled, true);
    assert.equal(workspace._recoveryBlocked, true);
    await workspace._submit();
    workspace.disconnectedCallback(); finish(); await recovery;
    assert.equal(calls, 0);
    assert.equal(metadata.removed, false);
  } finally { fixture.window.close(); }
});

test('HTTP AI recovery compares complete binding references without requiring secure-context hashing', async () => {
  for (const wrongReference of [false, true]) {
    let lookups = 0; let posts = 0; let loaded = false;
    const {fixture, workspace, metadata} = await recoveryAiFixture({crypto: {
      getRandomValues: value => webcrypto.getRandomValues(value),
    }, requestJson: async (path, options = {}) => {
      if (options.method === 'POST') { posts++; throw new Error('unexpected POST'); }
      if (path.includes('/recoveries/')) {
        lookups++;
        return {conversationId: 'conversation.saved', turnId: 'turn.saved', status: 'completed',
          idempotencyKey: 'web-ai.saved', provider: 'local', scope: 'system', fingerprint: 'C'.repeat(64),
          sourceReferences: [wrongReference ? 'surface:outer' : 'surface:inner']};
      }
      return {};
    }});
    try {
      const body = {surface: 'inner', provider: 'local', applicationId: null, stateSpaceId: null,
        resolutionFingerprint: null, idempotencyKey: 'web-ai.saved'};
      const trusted = await workspace._trustedRecoveryContext(body);
      assert.equal(trusted.contextFingerprint, null);
      assert.equal(workspace._rememberInterruptedRequest(body, trusted), true);
      workspace._loadConversation = async () => { loaded = true; return true; };
      await workspace._recoverInterruptedRequest();
      assert.equal(lookups, 1); assert.equal(posts, 0);
      assert.equal(loaded, !wrongReference); assert.equal(metadata.removed, !wrongReference);
      assert.equal(workspace._recoveryBlocked, wrongReference);
    } finally { fixture.window.close(); }
  }
});

test('HTTP recovery rejects stale runtime references before querying any saved result', async () => {
  let calls = 0;
  const {fixture, workspace} = await recoveryAiFixture({crypto: {}, requestJson: async () => { calls++; return {}; }});
  const appFingerprint = 'A'.repeat(64); const stateFingerprint = 'B'.repeat(64);
  workspace._applications = [{applicationId: 'app', resolutionFingerprint: appFingerprint}];
  workspace._stateSpaceBindings = new Map([['state', stateFingerprint]]);
  for (const [control, value] of [[workspace._application.control, 'app'], [workspace._stateSpace.control, 'state']]) {
    const option = fixture.window.document.createElement('option'); option.value = value; control.append(option);
  }
  workspace._loadStateSpaces = async () => {};
  try {
    assert.equal(await workspace._restoreRecoverySelectors({kind: 'ai', key: 'web-ai.saved', provider: 'local', surface: 'inner',
      applicationId: 'app', stateSpaceId: 'state', resolutionFingerprint: appFingerprint, contextFingerprint: null,
      sourceReferences: [`application:app@${appFingerprint}`, `state-space:state@${'D'.repeat(64)}`, 'surface:inner']}), false);
    assert.equal(calls, 0);
  } finally { fixture.window.close(); }
});

test('system conversation retries an uncertain send with the original idempotency key', async () => {
  const source = await readFile(new URL('system-workspace.js', root), 'utf8');
  const fixture = await dom();
  const bodies = [];
  let sendAttempts = 0;
  fixture.window.systemWebClient = {requestJson: async (path, options = {}) => {
    if (path.includes('/conversations?')) return {items: []};
    if (path === '/api/control/system/conversations') {
      bodies.push(options.body); sendAttempts++;
      if (sendAttempts === 1) throw new Error('connection lost');
      return {summary: {id: `system-conversation.${'a'.repeat(32)}`, provider: 'local', scope: 'system', title: 'Question', revision: 1}, messages: [], turns: []};
    }
    throw new Error(`unexpected ${path}`);
  }};
  fixture.window.validSystemIdentifier = value => typeof value === 'string' && value.length > 0;
  fixture.window.interruptedRequestStore = {read: () => null, write: () => true, remove: () => {}};
  fixture.window.eval(source.replace(/^import[^;]+;\s*/gm, '').replace(/import[^;]+;\s*/g, ''));
  const chat = fixture.window.document.createElement('system-chat');
  fixture.window.document.body.append(chat);
  await tick();
  const message = chat.shadowRoot.querySelector('textarea');
  message.value = 'Ask once';
  chat.shadowRoot.querySelector('[part="send"]').click();
  await tick();
  chat.shadowRoot.querySelector('[part="send"]').click();
  await tick();
  try {
    assert.equal(bodies.length, 2);
    assert.equal(bodies[0].idempotencyKey, bodies[1].idempotencyKey);
    assert.equal(bodies[0].message, bodies[1].message);
  } finally { fixture.window.close(); }
});

test('system conversation recovery ignores a delayed result after disconnect', async () => {
  const source = await readFile(new URL('system-workspace.js', root), 'utf8');
  const fixture = await dom(); let release; let opened = false;
  const delayed = new Promise(resolve => { release = resolve; });
  fixture.window.systemWebClient = {requestJson: async path => path.includes('/recoveries/') ? delayed : {items: []}};
  fixture.window.validSystemIdentifier = value => typeof value === 'string' && value.length > 0;
  fixture.window.interruptedRequestStore = {read: () => ({kind: 'system-conversation', key: 'system-chat.saved'}), write: () => true, remove: () => {}};
  fixture.window.eval(source.replace(/^import[^;]+;\s*/gm, '').replace(/import[^;]+;\s*/g, ''));
  const chat = fixture.window.document.createElement('system-chat'); chat._connected = true; chat._open = async () => { opened = true; };
  const recovery = chat._recoverInterruptedQuestion(); await tick(); chat.disconnectedCallback();
  release({conversationId: 'conversation.saved', turnId: 'turn.saved', status: 'completed', idempotencyKey: 'system-chat.saved', provider: 'local', scope: 'system'});
  await recovery;
  try { assert.equal(opened, false); }
  finally { fixture.window.close(); }
});

test('shared navigation links and marks the canonical control center route', async () => {
  const source = await readFile(new URL('system-workspace.js', root), 'utf8');
  const fixture = new JSDOM('<!doctype html><body></body>', {
    url: 'https://system.example.test/ui/control-center#/assistants', runScripts: 'outside-only'
  });
  fixture.window.systemWebClient = {discoverAllApplications: async () => ({
    applications: [], systemPages: [], unavailableFields: [], pageCount: 0,
    resolutionFingerprints: []
  })};
  fixture.window.validSystemIdentifier = value => typeof value === 'string' && value.length > 0;
  fixture.window.interruptedRequestStore = {read: () => null, write: () => true, remove: () => {}};
  fixture.window.eval(source.replace(/^import[^;]+;\s*/gm, '').replace(/import[^;]+;\s*/g, ''));
  const navigation = fixture.window.document.createElement('system-navigation');
  fixture.window.document.body.append(navigation);
  await tick();
  try {
    const control = navigation.shadowRoot.querySelector('[part="control-link"]');
    assert.equal(control.getAttribute('href'), '/ui/control-center');
    assert.equal(control.getAttribute('aria-current'), 'page');
  } finally { navigation.remove(); fixture.window.close(); }
});

test('system conversation shell does not render an absent recovery control as undefined text', async () => {
  const source = await readFile(new URL('system-workspace.js', root), 'utf8');
  const fixture = await dom();
  fixture.window.systemWebClient = {requestJson: async () => ({items: []})};
  fixture.window.validSystemIdentifier = value => typeof value === 'string' && value.length > 0;
  fixture.window.interruptedRequestStore = {read: () => null, write: () => true, remove: () => {}};
  fixture.window.eval(source.replace(/^import[^;]+;\s*/gm, '').replace(/import[^;]+;\s*/g, ''));
  const chat = fixture.window.document.createElement('system-chat');
  chat._accept({
    summary: {id: `conversation.${'a'.repeat(32)}`, provider: 'local', scope: 'system',
      title: 'Historical local conversation', revision: 1},
    messages: [],
    turns: [{status: 'failed', errorMessage: 'Ollama returned no message or tool call.'}]
  });
  try {
    assert.equal(chat.shadowRoot.querySelector('[part="status"]').textContent,
      'Ollama returned no message or tool call.');
    assert.equal(chat.shadowRoot.querySelector('button')?.textContent, 'New conversation');
    assert.equal(chat.shadowRoot.querySelectorAll('button').length, 3);
    assert.doesNotMatch(chat.shadowRoot.textContent, /\bundefined\b/);
  } finally { fixture.window.close(); }
});
