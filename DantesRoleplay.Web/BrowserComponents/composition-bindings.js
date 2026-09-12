// Presentation over the shared invocation envelope. The coordinator supplies authorized
// read/dispatch adapters; this module creates no endpoint, execution authority, or task state.
const tags = new Set(['completed', 'proposed', 'committed', 'pending', 'failed', 'cancelled', 'unavailable']);
const presentationBytes = 1024 * 1024;
const maximumDepth = 32;
const maximumEnvelopeDepth = 40;
const maximumNodes = 16_384;
const text = (value, limit = 1000) => typeof value === 'string' && value.length > 0 && value.length <= limit;
const hash = value => typeof value === 'string' && /^[A-F0-9]{64}$/.test(value);
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const keys = (value, names) => object(value) && Object.keys(value).length === names.length && names.every(name => Object.hasOwn(value, name));
const nullableRevision = value => value === null || Number.isInteger(value) && value >= 0;
const identifier = value => text(value, 200);
const operationId = value => typeof value === 'string' && /^[a-f0-9]{32}$/.test(value);
const commandId = value => typeof value === 'string' && value.length > 0 && value.length <= 128 && /^[A-Za-z0-9._:-]+$/.test(value);
const invocationIdentity = value => keys(value, ['commandId', 'operationId']) && commandId(value.commandId) && operationId(value.operationId);
const copyIdentity = value => invocationIdentity(value) ? Object.freeze({commandId: value.commandId, operationId: value.operationId}) : null;
const sameIdentity = (left, right) => invocationIdentity(left) && invocationIdentity(right) &&
  left.commandId === right.commandId && left.operationId === right.operationId;
// Count the compact JSON representation before allocating it. Read descriptors one at a
// time, never invoke accessors/toJSON, and count nulls and repeated references too.
const inspectJson = (value, byteLimit = presentationBytes, depthLimit = maximumEnvelopeDepth) => {
  let bytes = 0, nodes = 0;
  const stack = new WeakSet();
  const spend = count => { bytes += count; if (bytes > byteLimit) throw 'limit'; };
  const string = value => {
    if (value.length > byteLimit - bytes) throw 'limit';
    spend(2);
    for (let index = 0; index < value.length; index++) {
      const code = value.charCodeAt(index);
      if (code === 34 || code === 92 || [8, 9, 10, 12, 13].includes(code)) spend(2);
      else if (code < 32) spend(6);
      else if (code < 0x80) spend(1);
      else if (code < 0x800) spend(2);
      else if (code >= 0xd800 && code <= 0xdbff && index + 1 < value.length &&
          value.charCodeAt(index + 1) >= 0xdc00 && value.charCodeAt(index + 1) <= 0xdfff) { spend(4); index++; }
      else spend(code >= 0xd800 && code <= 0xdfff ? 6 : 3);
    }
  };
  const visit = (item, depth) => {
    if (++nodes > maximumNodes) throw 'limit';
    if (item === null) { spend(4); return item; }
    if (typeof item === 'string') { string(item); return item; }
    if (typeof item === 'boolean') { spend(item ? 4 : 5); return item; }
    if (typeof item === 'number' && Number.isFinite(item)) { spend(String(item).length); return item; }
    if (typeof item !== 'object' || stack.has(item)) throw 'invalid';
    if (depth >= depthLimit) throw 'limit';
    const array = Array.isArray(item), prototype = Object.getPrototypeOf(item);
    if (array ? prototype !== Array.prototype : prototype !== Object.prototype && prototype !== null) throw 'invalid';
    stack.add(item);
    spend(2);
    const copy = array ? [] : Object.create(null);
    const property = key => {
      const descriptor = Object.getOwnPropertyDescriptor(item, key);
      if (!descriptor || !Object.hasOwn(descriptor, 'value')) throw 'invalid';
      copy[key] = visit(descriptor.value, depth + 1);
    };
    if (array) {
      if (item.length > maximumNodes - nodes) throw 'limit';
      for (let index = 0; index < item.length; index++) { if (index) spend(1); property(String(index)); }
    } else {
      let count = 0;
      for (const key in item) {
        if (!Object.hasOwn(item, key)) continue;
        if (count++) spend(1);
        string(key); spend(1); property(key);
      }
    }
    stack.delete(item);
    return copy;
  };
  try { return {status: 'valid', value: visit(value, 0)}; }
  catch (error) { return {status: error === 'limit' ? 'limit' : 'invalid'}; }
};
const effect = value => object(value) && Number.isInteger(value.index) && value.index >= 0 &&
  text(value.type, 200) && typeof value.entityId === 'string' && typeof value.qualifiedTypeId === 'string' &&
  nullableRevision(value.revision) && nullableRevision(value.removedRevision);
const receipt = value => object(value) && operationId(value.operationId) &&
  hash(value.requestFingerprint) && Array.isArray(value.effects) && value.effects.every(effect) && typeof value.effectDetailsAvailable === 'boolean' &&
  (value.effectDetailsAvailable || value.effects.length === 0);
const absent = value => value === null;
const emptyFields = (value, fields) => fields.every(field => absent(value[field]));
const boundedJson = value => {
  if (!text(value, 65536) || new TextEncoder().encode(value).length > 65536) return null;
  try { const parsed = JSON.parse(value); return inspectJson(parsed, 65536, maximumDepth).status === 'valid' ? {value: parsed} : null; } catch { return null; }
};
const pointer = value => typeof value === 'string' && value.length <= 1000 && (value.length === 0 || value.startsWith('/'));
const proposalStep = step => object(step) && text(step.stepId, 200) && text(step.kind, 200) &&
  text(step.qualifiedId, 200) && Number.isInteger(step.version) && step.version > 0 && hash(step.fingerprint) &&
  Array.isArray(step.dependsOn) && step.dependsOn.length <= 16 && step.dependsOn.every(identifier) &&
  object(step.roleBindings) && Object.keys(step.roleBindings).length <= 32 &&
  Object.entries(step.roleBindings).every(([role, entity]) => identifier(role) && text(entity, 1000)) &&
  Object.hasOwn(step, 'input') && inspectJson(step.input, 65536, maximumDepth).status === 'valid' && Array.isArray(step.resultBindings) && step.resultBindings.length <= 32 &&
  step.resultBindings.every(binding => object(binding) && identifier(binding.fromStepId) && pointer(binding.fromPointer) &&
    ((identifier(binding.toRole) && !Object.hasOwn(binding, 'toInputPointer')) ||
      (pointer(binding.toInputPointer) && !Object.hasOwn(binding, 'toRole'))));

export function unavailableResult(code = 'COMPOSITION_ADAPTER_UNAVAILABLE', message = 'This capability is unavailable.') {
  return {tag: 'unavailable', code, message, dataJson: null, readEvidence: null, receipt: null,
    proposal: null, pending: null, completionEvidenceReference: null, previousCommits: [], recoveryIdentity: null};
}

export function describeInvocation(value) {
  const invalid = (code = 'INVALID_INVOCATION_RESULT', message = 'The response does not match the shared result contract.') => ({tag: 'unavailable', title: 'Result unavailable',
    message, code,
    data: null, receipt: null, previousCommits: [], pending: null, recoveryIdentity: null, refresh: false});
  const inspection = inspectJson(value);
  if (inspection.status === 'limit') return invalid('PRESENTATION_RESPONSE_LIMIT', 'The response exceeds this page’s display budget. Its outcome must be read back from the host.');
  if (inspection.status !== 'valid') return invalid();
  value = inspection.value;
  if (!keys(value, ['tag', 'code', 'message', 'dataJson', 'readEvidence', 'receipt', 'proposal', 'pending',
      'completionEvidenceReference', 'previousCommits', 'recoveryIdentity']) || !tags.has(value.tag) || !text(value.code, 200) || !text(value.message) ||
      !Array.isArray(value.previousCommits) || value.previousCommits.length > 16 ||
      !value.previousCommits.every(receipt)) return invalid();
  const prior = value.previousCommits;
  if (!['failed', 'cancelled', 'completed', 'pending'].includes(value.tag) && prior.length) return invalid();
  const recovery = value.recoveryIdentity;
  if (recovery !== null && (!['failed', 'cancelled', 'unavailable'].includes(value.tag) ||
      !object(recovery) || !/^[a-f0-9]{32}$/.test(recovery.operationId) || !hash(recovery.requestFingerprint))) return invalid();
  const fields = ['dataJson', 'readEvidence', 'receipt', 'proposal', 'pending', 'completionEvidenceReference'];
  let title, data = null;
  switch (value.tag) {
    case 'completed': {
      const parsed = boundedJson(value.dataJson);
      if (parsed === null ||
          !emptyFields(value, ['receipt', 'proposal', 'pending'])) return invalid();
      data = parsed.value;
      const read = object(value.readEvidence) && ['stateSpaceFingerprint', 'resolutionFingerprint', 'outputSchemaHash',
        'resultFingerprint', 'sourceRevisionFingerprint'].every(key => hash(value.readEvidence[key]));
      const computation = text(value.completionEvidenceReference, 200);
      if (!(read && absent(value.completionEvidenceReference) || computation && absent(value.readEvidence))) return invalid();
      if (read && prior.length) return invalid();
      title = read ? 'Read completed' : 'Output completed — no mutation receipt';
      break;
    }
    case 'committed':
      if (!receipt(value.receipt) || !emptyFields(value, fields.filter(key => key !== 'receipt'))) return invalid();
      title = 'Operation committed'; break;
    case 'proposed':
      if (!keys(value.proposal, ['command', 'steps']) || !text(value.proposal.command, 200) ||
          !Array.isArray(value.proposal.steps) || value.proposal.steps.length > 16 || !value.proposal.steps.every(proposalStep) ||
          !emptyFields(value, fields.filter(key => key !== 'proposal'))) return invalid();
      title = 'Proposal — awaiting a separate commit'; break;
    case 'pending':
      if (!keys(value.pending, ['taskId', 'commandId']) || !text(value.pending.taskId, 200) || !text(value.pending.commandId, 128) ||
          !emptyFields(value, fields.filter(key => key !== 'pending'))) return invalid();
      title = 'Task pending'; break;
    default:
      if (!emptyFields(value, fields)) return invalid();
      title = {failed: 'Operation failed', cancelled: 'Operation cancelled', unavailable: 'Capability unavailable'}[value.tag];
  }
  return {tag: value.tag, title, code: value.code, message: value.message, data, receipt: value.receipt,
    previousCommits: prior, pending: value.pending, recoveryIdentity: recovery,
    refresh: value.tag === 'committed' || prior.length > 0};
}

export function renderInvocation(container, result) {
  const view = describeInvocation(result);
  const doc = container.ownerDocument;
  const section = doc.createElement('section');
  section.setAttribute('aria-live', 'polite');
  section.setAttribute('data-result-tag', view.tag);
  const add = (tag, content) => { const node = doc.createElement(tag); node.textContent = content; section.append(node); };
  add('h3', view.title);
  add('p', view.message);
  add('small', view.code);
  if (view.data !== null) add('pre', JSON.stringify(view.data, null, 2));
  if (view.receipt) {
    add('p', `Committed operation: ${view.receipt.operationId}`);
    add('p', view.receipt.effectDetailsAvailable ? `${view.receipt.effects.length} recorded effects.` :
      'Effect details are unavailable; this does not mean no effects occurred.');
  }
  if (view.pending) add('p', `Task: ${view.pending.taskId} · command ${view.pending.commandId}. Progress is separate from an operation receipt.`);
  for (const previous of view.previousCommits)
    add('p', `Earlier committed operation: ${previous.operationId}. This change has not been rolled back.`);
  if (view.recoveryIdentity) add('p', `Reconcile operation ${view.recoveryIdentity.operationId} before retrying.`);
  container.replaceChildren(section);
  return view;
}

// Sections consume owner readback results without interpreting manuals, grants, or task lifecycle.
export function renderOperatorSections(container, results = {}) {
  const doc = container.ownerDocument;
  container.replaceChildren();
  for (const [key, title] of [['capabilities', 'Capabilities and manuals'], ['drafts', 'Drafts and activation'],
    ['grants', 'Effective grants'], ['schedules', 'Schedules and observers'], ['tasks', 'Child tasks']]) {
    const section = doc.createElement('section');
    const heading = doc.createElement('h2'); heading.textContent = title;
    const body = doc.createElement('div'); section.append(heading, body); container.append(section);
    renderInvocation(body, Object.hasOwn(results, key) ? results[key] : unavailableResult());
  }
}

export class CompositionBindings {
  constructor({read, dispatch, onData = () => {}, onResult = () => {}, onReadError = () => {}, onContentChanged = () => {}} = {}) {
    this.read = read; this.dispatch = dispatch;
    this.onData = onData; this.onResult = onResult; this.onReadError = onReadError;
    this.onContentChanged = onContentChanged;
    this.disposed = false; this.readSequence = 0; this.actions = new Set(); this.fences = new Map(); this.inFlight = new Set(); this.controlStates = new Map(); this.detach = null;
    this.detachControls = null;
  }

  bindControls(root, availableBindings = [], inputFor = () => ({}), hostIdentityFor = null) {
    this.detachControls?.();
    this.controlStates.clear();
    const available = new Set(availableBindings);
    if (available.size > 16 || [...available].some(name => !text(name, 80))) throw new Error('Invalid action binding selection.');
    const removals = [];
    let attached = true;
    for (const button of root.querySelectorAll('button[data-web-action]')) {
      const binding = button.getAttribute('data-web-action');
      const enabled = !this.disposed && typeof this.dispatch === 'function' && typeof hostIdentityFor === 'function' && available.has(binding);
      const state = () => {
        button.disabled = !attached || !enabled || this.disposed || this.actions.has(binding);
        button.setAttribute('aria-disabled', String(button.disabled));
      };
      if (!this.controlStates.has(binding)) this.controlStates.set(binding, []);
      this.controlStates.get(binding).push(state);
      state();
      const click = async event => {
        event.preventDefault();
        if (button.disabled || !enabled || this.disposed) return;
        button.disabled = true;
        button.setAttribute('aria-disabled', 'true');
        let input, hostIdentity;
        try { input = inputFor(binding, button); hostIdentity = hostIdentityFor(binding, button); }
        catch {
          state();
          this.onResult(unavailableResult('COMPOSITION_INPUT_UNAVAILABLE', 'Required action input is unavailable.'));
          return;
        }
        try { await this.invoke(binding, input, hostIdentity); }
        finally { state(); }
      };
      button.addEventListener('click', click);
      removals.push(() => { button.removeEventListener('click', click); button.disabled = true; button.setAttribute('aria-disabled', 'true'); });
    }
    this.detachControls = () => { attached = false; for (const remove of removals) remove(); };
    return this.detachControls;
  }

  updateControls(binding) { for (const state of this.controlStates.get(binding) ?? []) state(); }

  async refresh(cursor = null, pageSize = 100) {
    if (this.disposed) return;
    if (cursor !== null && !text(cursor, 1024) || !Number.isInteger(pageSize) || pageSize < 1 || pageSize > 100)
      throw new Error('Invalid collection page bound.');
    this.readAbort?.abort();
    const controller = new AbortController(); this.readAbort = controller;
    const sequence = ++this.readSequence;
    try {
      const result = this.read ? await this.read({cursor, pageSize, signal: controller.signal}) : unavailableResult();
      if (!this.disposed && sequence === this.readSequence && !controller.signal.aborted) this.onData(result);
      return result;
    } catch {
      if (!this.disposed && sequence === this.readSequence && !controller.signal.aborted)
        this.onReadError(unavailableResult('COMPOSITION_READ_UNAVAILABLE', 'Current page data is unavailable. Refresh to try again.'));
    }
  }

  async invoke(binding, input = {}, hostIdentity = null) {
    if (this.disposed || this.actions.has(binding)) return;
    if (!text(binding, 80)) throw new Error('Invalid action binding.');
    if (!this.dispatch) {
      const result = unavailableResult(); this.onResult(result); return result;
    }
    if (this.dispatch && !invocationIdentity(hostIdentity)) {
      const result = unavailableResult('COMPOSITION_HOST_IDENTITY_REQUIRED', 'The host must supply the action command identity.');
      this.onResult(result);
      return result;
    }
    this.actions.add(binding);
    this.updateControls(binding);
    const identity = copyIdentity(hostIdentity);
    this.fences.set(binding, identity);
    this.inFlight.add(binding);
    let fenced = true;
    try {
      // Writes are dispatched once. An uncertain response never causes an automatic retry.
      let result = await this.dispatch(binding, input, identity);
      if (this.disposed) return result;
      let view = describeInvocation(result);
      if (this.initialIdentityMismatch(view, identity)) {
        result = unavailableResult('COMPOSITION_RECONCILIATION_REQUIRED', 'The result does not match the dispatched operation.');
        view = describeInvocation(result);
      }
      fenced = !this.matchesInitial(view, identity);
      this.onResult(result);
      if (view.refresh) await this.refresh();
      return result;
    } catch {
      fenced = true;
      const result = unavailableResult('COMPOSITION_ACTION_UNRESOLVED', 'The action result is unknown. Reconcile its command before retrying.');
      if (!this.disposed) this.onResult(result);
      // Only explicit host reconciliation can release this unresolved action on this page.
      return result;
    } finally {
      this.inFlight.delete(binding);
      if (!fenced) { this.actions.delete(binding); this.fences.delete(binding); this.updateControls(binding); }
    }
  }

  matchesInitial(view, hostIdentity) {
    if (!invocationIdentity(hostIdentity) || view.code === 'INVALID_INVOCATION_RESULT' || view.recoveryIdentity !== null) return false;
    if (view.tag === 'pending') return false;
    if (view.tag === 'proposed' || view.tag === 'unavailable') return false;
    if (view.tag === 'committed') return view.receipt.operationId === hostIdentity.operationId;
    return ['completed', 'failed', 'cancelled'].includes(view.tag);
  }

  initialIdentityMismatch(view, hostIdentity) {
    return invocationIdentity(hostIdentity) &&
      ((view.tag === 'pending' && view.pending.commandId !== hostIdentity.commandId) ||
       (view.tag === 'committed' && view.receipt.operationId !== hostIdentity.operationId) ||
       (view.recoveryIdentity !== null && view.recoveryIdentity.operationId !== hostIdentity.operationId));
  }

  // The authorized host supplies this correlation; these page-local IDs confer no authority.
  // General task results and refreshes cannot release a command's fence.
  async reconcile(binding, hostIdentity, result) {
    if (this.disposed || this.inFlight.has(binding) || !this.actions.has(binding) || !sameIdentity(this.fences.get(binding), hostIdentity)) return false;
    const view = describeInvocation(result);
    if (view.code === 'INVALID_INVOCATION_RESULT' || view.recoveryIdentity !== null ||
        ['proposed', 'pending', 'unavailable'].includes(view.tag) ||
        (view.tag === 'committed' && view.receipt.operationId !== hostIdentity.operationId)) return false;
    this.actions.delete(binding); this.fences.delete(binding); this.updateControls(binding);
    this.onResult(result);
    if (view.refresh) await this.refresh();
    return true;
  }

  // Called with readback from the owning task service, never a worker's unverified summary.
  async taskResult(result) {
    if (this.disposed) return;
    this.onResult(result);
    const view = describeInvocation(result);
    if (view.refresh || ['completed', 'committed', 'failed', 'cancelled'].includes(view.tag)) await this.refresh();
  }

  connectChanges(source) {
    this.detach?.();
    // Reuse an already scoped existing change stream. EventSource owns reconnection; every
    // reconnect invalidates current data. Event payloads never become authoritative state.
    const refresh = () => {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = setTimeout(() => { void this.refresh(); }, 50);
    };
    const events = ['open', 'object-change', 'invalidate'];
    for (const event of events) source.addEventListener(event, refresh);
    const contentChanged = () => { this.dispose(); this.onContentChanged(); };
    source.addEventListener('page-revision', contentChanged);
    this.detach = () => {
      for (const event of events) source.removeEventListener(event, refresh);
      source.removeEventListener('page-revision', contentChanged);
    };
  }

  dispose() {
    this.disposed = true; this.readSequence++; this.readAbort?.abort(); this.detach?.();
    this.detachControls?.();
    clearTimeout(this.refreshTimer);
  }
}
