import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import test from 'node:test';

const source = await readFile(new URL('../../../../../DantesRoleplay.Web/BrowserComponents/composition-bindings.js', import.meta.url), 'utf8');
const {describeInvocation, unavailableResult, renderInvocation, renderOperatorSections, CompositionBindings} =
  await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const hash = 'A'.repeat(64);
const receipt = {operationId: 'a'.repeat(32), requestFingerprint: hash, effects: [], effectDetailsAvailable: false};
const envelope = (tag, extra = {}) => ({...unavailableResult('RESULT', 'A safe message'), tag, ...extra});
const read = () => envelope('completed', {dataJson: '{"value":1}', readEvidence: Object.fromEntries(
  ['stateSpaceFingerprint', 'resolutionFingerprint', 'outputSchemaHash', 'resultFingerprint', 'sourceRevisionFingerprint'].map(key => [key, hash]))});
const committed = () => envelope('committed', {receipt});
const tick = () => new Promise(resolve => setTimeout(resolve, 80));

test('all shared tags preserve their different evidence meanings', () => {
  assert.equal(describeInvocation(read()).title, 'Read completed');
  const output = describeInvocation(envelope('completed', {dataJson: '{"summary":"done"}', completionEvidenceReference: 'worker.result'}));
  assert.match(output.title, /no mutation receipt/);
  assert.equal(output.refresh, false);
  assert.equal(describeInvocation(committed()).refresh, true);
  const priorOutput = envelope('completed', {dataJson: '{}', completionEvidenceReference: 'worker.result', previousCommits: [receipt]});
  assert.equal(describeInvocation(priorOutput).tag, 'completed');
  assert.equal(describeInvocation(priorOutput).refresh, true);
  assert.match(describeInvocation(priorOutput).title, /no mutation receipt/);
  assert.equal(describeInvocation(envelope('pending', {pending: {taskId: 'task', commandId: 'command'}, previousCommits: [receipt]})).tag, 'pending');
  assert.match(describeInvocation(envelope('proposed', {proposal: {command: 'propose', steps: []}})).title, /awaiting a separate commit/);
  assert.equal(describeInvocation(envelope('pending', {pending: {taskId: 'task', commandId: 'command'}})).title, 'Task pending');
  for (const tag of ['failed', 'cancelled', 'unavailable']) assert.equal(describeInvocation(envelope(tag)).tag, tag);
});

test('false success and malformed evidence fail closed without a refresh', () => {
  for (const result of [{}, {status: 'success'}, envelope('committed'),
    envelope('completed', {dataJson: '{}'}), envelope('completed', {dataJson: '{}', readEvidence: {}}),
    envelope('pending', {pending: {taskId: 'task'}}),
    envelope('failed', {receipt}), envelope('completed', {...read(), receipt}),
    envelope('unavailable', {previousCommits: [receipt]}), envelope('proposed', {proposal: {}}),
    envelope('pending', {pending: {taskId: 'task', commandId: 'command', summary: 'done'}}),
    envelope('failed', {message: 'x'.repeat(1001)}), envelope('failed', {previousCommits: Array(17).fill(receipt)}),
    {...committed(), receipt: {...receipt, effects: [{}], effectDetailsAvailable: true}},
    {...read(), previousCommits: [receipt]}]) {
    const view = describeInvocation(result);
    assert.equal(view.code, 'INVALID_INVOCATION_RESULT');
    assert.equal(view.refresh, false);
  }
});

// A deliberately small DOM checks the renderer uses only text nodes. It has no HTML parser.
class Element {
  constructor(tag, doc) { this.tagName = tag; this.ownerDocument = doc; this.children = []; this.attrs = {}; this.text = ''; }
  set innerHTML(_) { throw new Error('HTML interpolation is forbidden'); }
  set textContent(value) { this.text = value; }
  get textContent() { return this.text + this.children.map(node => node.textContent).join(' '); }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = [...nodes]; }
  setAttribute(name, value) { this.attrs[name] = value; }
}
function container() { const doc = {createElement: tag => new Element(tag, doc)}; return doc.createElement('div'); }

test('declared controls stay disabled without a host selection and dispatch only the selected binding', async () => {
  class Button extends EventTarget {
    constructor(name) { super(); this.attrs = {'data-web-action': name}; this.disabled = true; }
    getAttribute(name) { return this.attrs[name]; }
    setAttribute(name, value) { this.attrs[name] = value; }
  }
  const selected = new Button('save'), other = new Button('unavailable');
  const root = {querySelectorAll: () => [selected, other]};
  new CompositionBindings().bindControls(root, ['save']);
  assert.equal(selected.disabled, true);
  let calls = [];
  const bindings = new CompositionBindings({dispatch: async (name, input) => { calls.push({name, input}); return committed(); }});
  bindings.bindControls(root, ['save'], () => ({value: 2}));
  assert.equal(selected.disabled, false); assert.equal(other.disabled, true);
  other.dispatchEvent(new Event('click')); selected.dispatchEvent(new Event('click'));
  await tick();
  assert.deepEqual(calls, [{name: 'save', input: {value: 2}}]);
  bindings.dispose(); assert.equal(selected.disabled, true);
  selected.dispatchEvent(new Event('click')); await tick(); assert.equal(calls.length, 1);
});

test('operator presentation escapes text and labels earlier commits, recovery, and unavailable effects', () => {
  const node = container();
  renderInvocation(node, committed());
  assert.match(node.textContent, /does not mean no effects/);
  renderInvocation(node, envelope('cancelled', {message: '<img onerror=alert(1)>', previousCommits: [receipt],
    recoveryIdentity: {operationId: receipt.operationId, requestFingerprint: hash}}));
  assert.match(node.textContent, /Earlier committed operation/);
  assert.match(node.textContent, /has not been rolled back/);
  assert.match(node.textContent, /Reconcile operation/);
  assert.match(node.textContent, /<img onerror/);
  assert.equal(node.children[0].children.some(child => child.tagName === 'img'), false);
});

test('missing owner readback is displayed unavailable in every operator section', () => {
  const node = container(); renderOperatorSections(node);
  assert.equal(node.children.length, 5);
  assert.equal((node.textContent.match(/Capability unavailable/g) || []).length, 5);
});

test('committed actions refresh authoritative data once; absent dispatch is unavailable', async () => {
  let writes = 0, reads = 0;
  const bindings = new CompositionBindings({dispatch: async () => { writes++; return committed(); },
    read: async () => { reads++; return read(); }});
  await bindings.invoke('save');
  assert.equal(writes, 1); assert.equal(reads, 1);
  await bindings.invoke('save');
  assert.equal(writes, 2); assert.equal(reads, 2);
  assert.equal((await new CompositionBindings().invoke('save')).tag, 'unavailable');
});

test('uncertain, recovery-bearing and pending writes remain fenced without automatic retry', async () => {
  for (const dispatch of [async () => { throw new Error('network failure'); },
    async () => envelope('unavailable', {recoveryIdentity: {operationId: receipt.operationId, requestFingerprint: hash}}),
    async () => envelope('pending', {pending: {taskId: 'task', commandId: 'command'}})]) {
    let writes = 0;
    const bindings = new CompositionBindings({dispatch: async () => { writes++; return dispatch(); }});
    await bindings.invoke('save'); await bindings.invoke('save');
    assert.equal(writes, 1);
  }
});

test('duplicate clicks do not dispatch twice and disposed pages ignore late responses', async () => {
  let resolve, writes = 0, results = 0;
  const bindings = new CompositionBindings({dispatch: () => { writes++; return new Promise(done => { resolve = done; }); },
    onResult: () => results++});
  const pending = bindings.invoke('save'); await bindings.invoke('save');
  bindings.dispose(); resolve(committed()); await pending;
  assert.equal(writes, 1); assert.equal(results, 0);
});

test('stale audience reads cannot replace newer data and pagination is bounded', async () => {
  const pending = [], seen = [];
  const bindings = new CompositionBindings({read: options => new Promise(resolve => pending.push({options, resolve})),
    onData: value => seen.push(value)});
  const first = bindings.refresh(); const second = bindings.refresh('next', 12);
  assert.equal(pending[0].options.signal.aborted, true);
  pending[1].resolve('second'); await second;
  pending[0].resolve('first'); await first;
  assert.deepEqual(seen, ['second']);
  assert.equal(pending[1].options.cursor, 'next'); assert.equal(pending[1].options.pageSize, 12);
  await assert.rejects(bindings.refresh(null, 101));
  bindings.dispose();
});

test('existing scoped events invalidate reads; reconnect refreshes and new page revision fences old controls', async () => {
  const stream = new EventTarget(); let reads = 0, contentChanges = 0;
  const bindings = new CompositionBindings({read: async () => { reads++; return read(); }, onContentChanged: () => contentChanges++});
  bindings.connectChanges(stream);
  stream.dispatchEvent(new Event('open')); stream.dispatchEvent(new Event('object-change')); await tick();
  assert.equal(reads, 1);
  stream.dispatchEvent(new Event('open')); await tick(); assert.equal(reads, 2);
  stream.dispatchEvent(new Event('page-revision')); assert.equal(contentChanges, 1);
  stream.dispatchEvent(new Event('invalidate')); await tick(); assert.equal(reads, 2);
  assert.equal(await bindings.invoke('save'), undefined);
});

test('task terminal readback and earlier workflow commits refresh; a pending summary does not', async () => {
  let reads = 0;
  const bindings = new CompositionBindings({read: async () => { reads++; return read(); }});
  await bindings.taskResult(envelope('pending', {pending: {taskId: 'task', commandId: 'command'}}));
  assert.equal(reads, 0);
  await bindings.taskResult(envelope('pending', {pending: {taskId: 'task', commandId: 'command'}, previousCommits: [receipt]}));
  assert.equal(reads, 1);
  await bindings.taskResult(envelope('completed', {dataJson: '{}', completionEvidenceReference: 'task.result'}));
  assert.equal(reads, 2);
  await bindings.taskResult(envelope('failed', {previousCommits: [receipt]}));
  assert.equal(reads, 3);
});
