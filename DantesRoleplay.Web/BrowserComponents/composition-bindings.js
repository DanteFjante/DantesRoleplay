// Presentation over the shared invocation envelope. The coordinator supplies authorized
// read/dispatch adapters; this module creates no endpoint, execution authority, or task state.
const tags = new Set(['completed', 'proposed', 'committed', 'pending', 'failed', 'cancelled', 'unavailable']);
const text = (value, limit = 1000) => typeof value === 'string' && value.length > 0 && value.length <= limit;
const hash = value => typeof value === 'string' && /^[A-F0-9]{64}$/.test(value);
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const keys = (value, names) => object(value) && Object.keys(value).length === names.length && names.every(name => Object.hasOwn(value, name));
const nullableRevision = value => value === null || Number.isInteger(value) && value >= 0;
const effect = value => object(value) && Number.isInteger(value.index) && value.index >= 0 &&
  text(value.type, 200) && typeof value.entityId === 'string' && typeof value.qualifiedTypeId === 'string' &&
  nullableRevision(value.revision) && nullableRevision(value.removedRevision);
const receipt = value => object(value) && /^[a-f0-9]{32}$/.test(value.operationId) &&
  hash(value.requestFingerprint) && Array.isArray(value.effects) && value.effects.every(effect) && typeof value.effectDetailsAvailable === 'boolean' &&
  (value.effectDetailsAvailable || value.effects.length === 0);
const absent = value => value === null;
const emptyFields = (value, fields) => fields.every(field => absent(value[field]));

export function unavailableResult(code = 'COMPOSITION_ADAPTER_UNAVAILABLE', message = 'This capability is unavailable.') {
  return {tag: 'unavailable', code, message, dataJson: null, readEvidence: null, receipt: null,
    proposal: null, pending: null, completionEvidenceReference: null, previousCommits: [], recoveryIdentity: null};
}

export function describeInvocation(value) {
  const invalid = () => ({tag: 'unavailable', title: 'Result unavailable',
    message: 'The response does not match the shared result contract.', code: 'INVALID_INVOCATION_RESULT',
    data: null, receipt: null, previousCommits: [], pending: null, recoveryIdentity: null, refresh: false});
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
      if (!text(value.dataJson, 65536) || new TextEncoder().encode(value.dataJson).length > 65536 ||
          !emptyFields(value, ['receipt', 'proposal', 'pending'])) return invalid();
      const read = object(value.readEvidence) && ['stateSpaceFingerprint', 'resolutionFingerprint', 'outputSchemaHash',
        'resultFingerprint', 'sourceRevisionFingerprint'].every(key => hash(value.readEvidence[key]));
      const computation = text(value.completionEvidenceReference, 200);
      if (!(read && absent(value.completionEvidenceReference) || computation && absent(value.readEvidence))) return invalid();
      if (read && prior.length) return invalid();
      try { data = JSON.parse(value.dataJson); } catch { return invalid(); }
      title = read ? 'Read completed' : 'Output completed — no mutation receipt';
      break;
    }
    case 'committed':
      if (!receipt(value.receipt) || !emptyFields(value, fields.filter(key => key !== 'receipt'))) return invalid();
      title = 'Operation committed'; break;
    case 'proposed':
      if (!keys(value.proposal, ['command', 'steps']) || !text(value.proposal.command, 200) ||
          !Array.isArray(value.proposal.steps) || value.proposal.steps.length > 16 ||
          !value.proposal.steps.every(step => object(step) && text(step.stepId, 200) && text(step.kind, 200) &&
            text(step.qualifiedId, 200) && Number.isInteger(step.version) && step.version > 0 && hash(step.fingerprint) &&
            Array.isArray(step.dependsOn) && object(step.roleBindings) && Object.hasOwn(step, 'input') && Array.isArray(step.resultBindings)) ||
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
  if (view.pending) add('p', `Task: ${view.pending.taskId}. Progress is separate from an operation receipt.`);
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
    this.disposed = false; this.readSequence = 0; this.actions = new Set(); this.detach = null;
    this.detachControls = null;
  }

  bindControls(root, availableBindings = [], inputFor = () => ({})) {
    this.detachControls?.();
    const available = new Set(availableBindings);
    if (available.size > 16 || [...available].some(name => !text(name, 80))) throw new Error('Invalid action binding selection.');
    const removals = [];
    for (const button of root.querySelectorAll('button[data-web-action]')) {
      const binding = button.getAttribute('data-web-action');
      const enabled = !this.disposed && typeof this.dispatch === 'function' && available.has(binding);
      const state = () => {
        button.disabled = !enabled || this.disposed || this.actions.has(binding);
        button.setAttribute('aria-disabled', String(button.disabled));
      };
      state();
      const click = async event => {
        event.preventDefault();
        if (button.disabled || !enabled || this.disposed) return;
        button.disabled = true;
        button.setAttribute('aria-disabled', 'true');
        let input;
        try { input = inputFor(binding, button); }
        catch {
          state();
          this.onResult(unavailableResult('COMPOSITION_INPUT_UNAVAILABLE', 'Required action input is unavailable.'));
          return;
        }
        try { await this.invoke(binding, input); }
        finally { state(); }
      };
      button.addEventListener('click', click);
      removals.push(() => { button.removeEventListener('click', click); button.disabled = true; button.setAttribute('aria-disabled', 'true'); });
    }
    this.detachControls = () => { for (const remove of removals) remove(); };
    return this.detachControls;
  }

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

  async invoke(binding, input = {}) {
    if (this.disposed || this.actions.has(binding)) return;
    if (!text(binding, 80)) throw new Error('Invalid action binding.');
    this.actions.add(binding);
    let fenced = true;
    try {
      // Writes are dispatched once. An uncertain response never causes an automatic retry.
      const result = this.dispatch ? await this.dispatch(binding, input) : unavailableResult();
      if (this.disposed) return result;
      const view = describeInvocation(result);
      fenced = view.code === 'INVALID_INVOCATION_RESULT' || view.recoveryIdentity !== null ||
        ['pending', 'proposed'].includes(view.tag);
      this.onResult(result);
      if (view.refresh) await this.refresh();
      return result;
    } catch {
      fenced = true;
      const result = unavailableResult('COMPOSITION_ACTION_UNRESOLVED', 'The action result is unknown. Reconcile its command before retrying.');
      if (!this.disposed) this.onResult(result);
      // Keep this binding fenced until a new controller is created after host reconciliation.
      return result;
    } finally {
      if (!fenced) this.actions.delete(binding);
    }
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
