import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';
import {createRequire} from 'node:module';

const require = createRequire(new URL('../../dnd2024/package.json', import.meta.url));
const {JSDOM} = require('jsdom');
const bindingsSource = await readFile(
  new URL('../../../../../DantesRoleplay.Web/BrowserComponents/composition-bindings.js', import.meta.url), 'utf8');
const bindingsUrl = `data:text/javascript;base64,${Buffer.from(bindingsSource).toString('base64')}`;
const componentSource = (await readFile(
  new URL('../../../../../DantesRoleplay.Web/BrowserComponents/application-capability-center.js', import.meta.url), 'utf8'))
  .replace("'/components/composition-bindings.js'", `'${bindingsUrl}'`);

const hash = 'A'.repeat(64);
const baseResult = {tag: 'unavailable', code: 'UNAVAILABLE', message: 'Unavailable.', dataJson: null,
  readEvidence: null, receipt: null, proposal: null, pending: null, completionEvidenceReference: null,
  previousCommits: [], recoveryIdentity: null};
const capability = (id, mode) => ({id, version: 1, sourceFingerprint: hash,
  owner: 'system-task-orchestration', description: `Fixture ${id}`, mode,
  inputSchemaJson: '{"type":"object","additionalProperties":false}',
  outputSchemaJson: '{"type":"object"}', procedureIds: [`procedure.${id}`],
  requiredStandingGrantCapability: 'readTask', requiredStandingGrantCapabilities: ['readTask'],
  requiresConfirmation: false, requiresIdempotencyKey: mode === 'write'});
const response = (value, status = 200) => ({ok: status >= 200 && status < 300, status,
  json: async () => value});
const waitFor = async predicate => {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 5));
  }
  assert.fail('application capability fixture did not settle');
};

test('selected application capabilities invoke the shared task-result presentation without control authority', async () => {
  const dom = new JSDOM('<!doctype html><body></body>', {url: 'http://control.test/', runScripts: 'outside-only'});
  globalThis.window = dom.window;
  globalThis.document = dom.window.document;
  globalThis.HTMLElement = dom.window.HTMLElement;
  globalThis.customElements = dom.window.customElements;
  const calls = [];
  let deferRead = false;
  let resolveRead;
  const descriptors = [capability('system.inner-worker.submit', 'write'),
    capability('system.inner-worker.read', 'read'), capability('system.inner-worker.cancel', 'write')];
  globalThis.fetch = dom.window.fetch = async (path, options = {}) => {
    const url = String(path);
    if (url.endsWith('/authoring/capabilities')) {
      const applicationId = decodeURIComponent(url.split('/')[3]);
      return response({applicationId, capabilities: applicationId === 'alpha' ? descriptors : []});
    }
    const id = decodeURIComponent(url.slice(url.lastIndexOf('/') + 1));
    calls.push({id, body: JSON.parse(options.body)});
    if (id === 'system.inner-worker.submit') return response({ok: true, capabilityId: id, mode: 'write',
      data: {...baseResult, tag: 'pending', code: 'SYSTEM_TASK_PENDING', message: 'Task accepted.',
        pending: {taskId: 'task.1', commandId: 'command.1'}}});
    if (id === 'system.inner-worker.read') {
      const result = response({ok: true, capabilityId: id, mode: 'read',
        data: {...baseResult, tag: 'completed', code: 'SYSTEM_TASK_COMPLETED', message: 'Task completed.',
          dataJson: '{"summary":"done"}', completionEvidenceReference: 'task.result.1',
          previousCommits: [{operationId: 'a'.repeat(32), requestFingerprint: hash,
            effects: [], effectDetailsAvailable: false}]}});
      if (deferRead) return new Promise(resolve => { resolveRead = () => resolve(result); });
      return result;
    }
    return response({ok: true, capabilityId: id, mode: 'write', data: {...baseResult, tag: 'failed',
      code: 'SYSTEM_TASK_RECONCILIATION_REQUIRED', message: 'Cancellation outcome is uncertain.',
      recoveryIdentity: {operationId: 'b'.repeat(32), requestFingerprint: hash}}});
  };
  await import(`data:text/javascript;base64,${Buffer.from(componentSource).toString('base64')}`);
  const center = dom.window.document.createElement('application-capability-center');
  center.setAttribute('application-id', 'alpha');
  dom.window.document.body.append(center);
  await waitFor(() => center.shadowRoot.querySelectorAll('article').length === 3);

  const invoke = async (id, input) => {
    center.shadowRoot.querySelector(`button[data-capability-id="${id}"]`).click();
    const form = center.shadowRoot.querySelector('form');
    form.querySelector('textarea').value = JSON.stringify(input);
    form.dispatchEvent(new dom.window.Event('submit', {bubbles: true, cancelable: true}));
    await waitFor(() => center.shadowRoot.querySelector('[part="result"]')?.textContent.length > 0);
    return center.shadowRoot.querySelector('[part="result"]').textContent;
  };

  const pending = await invoke('system.inner-worker.submit', {stateSpaceId: 'space.1'});
  assert.match(pending, /Task: task\.1 · command command\.1/);
  assert.match(pending, /Progress is separate from an operation receipt/);
  const completed = await invoke('system.inner-worker.read', {stateSpaceId: 'space.1', taskId: 'task.1', commandId: 'command.1'});
  assert.match(completed, /"summary": "done"/);
  assert.match(completed, /Earlier committed operation/);
  const recovery = await invoke('system.inner-worker.cancel', {stateSpaceId: 'space.1', taskId: 'task.1', commandId: 'command.1'});
  assert.match(recovery, /Reconcile operation/);
  assert.deepEqual(calls.map(call => call.id), descriptors.map(value => value.id));
  assert.equal(calls[0].body.idempotencyKey.startsWith('web.application-capability.'), true);
  assert.equal(Object.hasOwn(calls[1].body, 'idempotencyKey'), false);
  assert.equal(String(componentSource).includes('/api/control'), false);
  assert.equal(String(componentSource).includes('/mcp'), false);

  deferRead = true;
  center.shadowRoot.querySelector('button[data-capability-id="system.inner-worker.read"]').click();
  const staleForm = center.shadowRoot.querySelector('form');
  staleForm.dispatchEvent(new dom.window.Event('submit', {bubbles: true, cancelable: true}));
  await waitFor(() => typeof resolveRead === 'function');
  center.setAttribute('application-id', 'beta');
  await waitFor(() => /No application capabilities/.test(center.shadowRoot.querySelector('[part="status"]').textContent));
  resolveRead();
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(center.shadowRoot.querySelector('[part="runner"]').hidden, true);
  assert.match(center.shadowRoot.querySelector('[part="status"]').textContent, /No application capabilities/);
});
