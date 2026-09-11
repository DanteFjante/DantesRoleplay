import {SystemClientError, SystemRequestScope, systemWebClient, interruptedRequestStore} from '/components/system-client.js';
import '/components/system-publication.js';

const OPERATIONS = [
  ['message', 'Message or question'],
  ['structured-request', 'Structured request'],
  ['task', 'Task'],
  ['plan', 'Plan'],
  ['recipe-execution', 'Recipe execution'],
  ['scheduled-task', 'Scheduled task'],
  ['continued-subtask', 'Continued or recursive subtask']
];

function emit(element, name, detail) {
  element.dispatchEvent(new CustomEvent(name, {detail, bubbles: true, composed: true}));
}

function option(value, label) {
  const item = document.createElement('option');
  item.value = value;
  item.textContent = label;
  return item;
}

function own(value, key) {
  return value && typeof value === 'object' && !Array.isArray(value) && Object.hasOwn(value, key)
    ? value[key] : undefined;
}

function validId(value, maximum = 200) {
  return typeof value === 'string' && value.length > 0 && value.length <= maximum &&
    value.trim() === value && !/[\u0000-\u001f\u007f/\\]/.test(value);
}

function displayText(value, fallback = '', maximum = 4000) {
  return typeof value === 'string' && value.length <= maximum ? value : fallback;
}

function providerRow(value) {
  return value && typeof value === 'object' && !Array.isArray(value) && validId(value.id, 120) &&
    typeof value.displayName === 'string' && value.displayName.length <= 200
    ? {id: value.id, displayName: value.displayName} : null;
}

// The public provider selector intentionally differs from the durable
// conversation lane for Ollama. Keep that translation at the browser/API
// boundary, never infer it from a returned conversation.
function durableConversationProvider(provider) {
  return provider === 'ollama' ? 'local' : provider;
}

function modelRow(value) {
  return value && typeof value === 'object' && !Array.isArray(value) && validId(value.id, 160) &&
    typeof value.displayName === 'string' && value.displayName.length <= 200
    ? {...value, displayName: value.displayName} : null;
}

function conversationRow(value) {
  return value && typeof value === 'object' && !Array.isArray(value) && validId(value.id, 200) &&
    typeof value.title === 'string' && value.title.length <= 200 &&
    typeof value.status === 'string' && value.status.length <= 80
    ? value : null;
}

function randomKey(prefix) {
  if (typeof crypto.randomUUID === 'function') return `${prefix}.${crypto.randomUUID()}`;
  const values = new Uint32Array(4);
  crypto.getRandomValues(values);
  return `${prefix}.${Array.from(values, value => value.toString(16)).join('')}`;
}

class AiWorkspace extends HTMLElement {
  static get observedAttributes() {
    return ['surface', 'application-id', 'state-space-id'];
  }

  constructor() {
    super();
    this._client = systemWebClient;
    this._scopes = {
      setup: new SystemRequestScope(), models: new SystemRequestScope(), state: new SystemRequestScope(),
      history: new SystemRequestScope(), conversation: new SystemRequestScope(), execution: new SystemRequestScope()
    };
    this._connected = false;
    this._applications = [];
    this._stateSpaceBindings = new Map();
    this._models = [];
    this._conversation = null;
    this._pendingSubmission = null;
    this._interruptedRecovery = null;
    this._recoveryBlocked = false;
    this._setupPartial = false;
    this.attachShadow({mode: 'open'});
    this._renderShell();
  }

  connectedCallback() {
    if (this._connected) return;
    this._connected = true;
    if (!this.hasAttribute('surface')) this.setAttribute('surface', 'inner');
    this._configureSurface();
    this._setupPromise = this._loadSetup();
    this._recoverInterruptedRequest();
  }

  disconnectedCallback() {
    this._connected = false;
    for (const scope of Object.values(this._scopes)) scope.cancel();
  }

  attributeChangedCallback(name) {
    if (!this._connected) return;
    this._scopes.execution.cancel();
    this._scopes.conversation.cancel();
    if (name === 'application-id') {
      this._selectDeclaredApplication();
    } else if (name === 'state-space-id') {
      this._selectDeclaredStateSpace();
    } else if (name === 'surface') {
      this._resetConversation();
      this._configureSurface();
      this._loadHistory();
    }
  }

  set client(value) {
    if (!value || typeof value.requestJson !== 'function' ||
        typeof value.discoverAllApplications !== 'function') throw new TypeError(
      'ai-workspace requires the shared system browser client.');
    this._client = value;
    if (this._connected) this._setupPromise = this._loadSetup();
  }

  get client() { return this._client; }

  _renderShell() {
    const style = document.createElement('style');
    style.textContent = `:host{display:grid;gap:.8rem;color:var(--ai-color,inherit);font:inherit}
      form,[part='setup'],[part='row'],[part='results']{display:grid;gap:.65rem}
      [part='setup']{grid-template-columns:repeat(auto-fit,minmax(12rem,1fr))}
      label{display:grid;gap:.3rem;font-weight:600}select,textarea,input,button{box-sizing:border-box;font:inherit;padding:.55rem}
      textarea{min-height:7rem;resize:vertical;width:100%}[part='schema'],[part='structured-input']{min-height:9rem;font-family:ui-monospace,monospace}
      [part='actions']{display:flex;flex-wrap:wrap;gap:.55rem}button{cursor:pointer}button:disabled{cursor:wait;opacity:.65}
      [part='transcript'],[part='activity'],[part='tools'],[part='confirmations']{border:1px solid var(--ai-border-color,currentColor);border-radius:.55rem;padding:.7rem}
      [part='media']{display:grid;grid-template-columns:repeat(auto-fit,minmax(12rem,1fr));gap:.7rem}
      figure{overflow:hidden;margin:0;border:1px solid var(--ai-border-color,currentColor);border-radius:.55rem}
      figure img{display:block;width:100%;height:auto;max-height:24rem;object-fit:contain;background:#111}
      figcaption{display:grid;gap:.15rem;padding:.55rem}.media-role{font-size:.75rem;text-transform:capitalize;opacity:.75}
      h3,p,ol,ul{margin:.2rem 0}.message{white-space:pre-wrap}.muted{color:var(--ai-muted-color,inherit);font-size:.85rem}
      [data-status='failed']{color:var(--ai-error-color,#9f2525)}[hidden]{display:none!important}`;
    const form = document.createElement('form');
    form.addEventListener('submit', event => { event.preventDefault(); this._submit(); });
    const setup = document.createElement('div');
    setup.setAttribute('part', 'setup');
    this._provider = this._select('Provider', 'provider');
    this._model = this._select('Model', 'model');
    this._reasoning = this._select('Reasoning', 'reasoning');
    this._operation = this._select('Operation', 'operation');
    for (const [value, label] of OPERATIONS) this._operation.control.append(option(value, label));
    this._application = this._select('Application context', 'application');
    this._stateSpace = this._select('Runtime state space', 'state-space');
    setup.append(this._provider.label, this._model.label, this._reasoning.label,
      this._operation.label, this._application.label, this._stateSpace.label);
    this._input = document.createElement('textarea');
    this._input.required = true;
    this._input.maxLength = 8000;
    this._input.setAttribute('aria-label', 'AI request');
    this._input.placeholder = 'What should the AI do?';
    const inputLabel = document.createElement('label');
    this._inputLabelText = document.createTextNode('Request');
    inputLabel.append(this._inputLabelText, this._input);
    this._structuredInput = document.createElement('textarea');
    this._structuredInput.setAttribute('part', 'structured-input');
    this._structuredInput.setAttribute('aria-label', 'Structured request input');
    this._structuredInput.placeholder = '{\n  "example": true\n}';
    const structuredLabel = document.createElement('label');
    structuredLabel.append(document.createTextNode('Structured input (optional JSON)'), this._structuredInput);
    this._schema = document.createElement('textarea');
    this._schema.setAttribute('part', 'schema');
    this._schema.setAttribute('aria-label', 'Response JSON Schema');
    this._schema.placeholder = '{\n  "type": "object",\n  "additionalProperties": false\n}';
    const schemaLabel = document.createElement('label');
    schemaLabel.append(document.createTextNode('Response JSON Schema'), this._schema);
    this._structuredLabel = structuredLabel;
    this._schemaLabel = schemaLabel;
    const actions = document.createElement('div');
    actions.setAttribute('part', 'actions');
    this._submitButton = document.createElement('button');
    this._submitButton.type = 'submit';
    this._submitButton.textContent = 'Send';
    this._new = document.createElement('button');
    this._new.type = 'button';
    this._new.textContent = 'New conversation';
    this._new.addEventListener('click', () => this._resetConversation());
    this._remove = document.createElement('button');
    this._remove.type = 'button';
    this._remove.textContent = 'Remove conversation';
    this._remove.disabled = true;
    this._remove.addEventListener('click', () => this._deleteConversation());
    this._history = document.createElement('select');
    this._history.setAttribute('aria-label', 'AI conversations');
    this._history.addEventListener('change', () => this._loadConversation(this._history.value));
    actions.append(this._submitButton, this._new, this._remove, this._history);
    form.append(setup, inputLabel, structuredLabel, schemaLabel, actions);
    this._feedback = document.createElement('div');
    this._feedback.setAttribute('part', 'feedback');
    this._results = document.createElement('div');
    this._results.setAttribute('part', 'results');
    this.shadowRoot.append(style, form, this._feedback, this._results);
    this._provider.control.addEventListener('change', () => { this._resetConversation(); this._loadModels(); });
    this._model.control.addEventListener('change', () => this._updateReasoning());
    this._operation.control.addEventListener('change', () => this._updateOperation());
    this._application.control.addEventListener('change', () => { this._resetConversation(); this._loadStateSpaces(); });
    this._stateSpace.control.addEventListener('change', () => this._resetConversation());
    this._updateOperation();
  }

  _configureSurface() {
    const outer = this.getAttribute('surface') === 'outer';
    this._operation.label.hidden = outer;
    if (outer) this._operation.control.value = 'message';
    this._inputLabelText.textContent = outer ? 'Message' : 'Request';
    this._input.setAttribute('aria-label', outer ? 'Chat message' : 'AI request');
    this._input.placeholder = outer ? 'What would you like to talk through?' : 'What should the AI do?';
    this._updateOperation();
  }

  _select(labelText, part) {
    const label = document.createElement('label');
    const control = document.createElement('select');
    control.setAttribute('part', part);
    label.append(document.createTextNode(labelText), control);
    return {label, control};
  }

  async _loadSetup() {
    const request = this._scopes.setup.begin();
    this._setupPartial = false;
    this._showProgress('Discovering AI providers and applications…');
    try {
      const [providers, applications] = await Promise.all([
        this._client.requestJson('/api/control/ai/providers', {signal: request.signal}),
        this._client.discoverAllApplications({signal: request.signal})
      ]);
      if (!request.isCurrent()) return;
      if (!providers || !Array.isArray(providers.providers) || providers.providers.length > 100) throw new SystemClientError(
        'AI_PROVIDER_RESPONSE_INVALID', 'The AI provider response is invalid.');
      const providerRows = providers.providers.map(providerRow);
      const applicationRows = Array.isArray(applications?.applications)
        ? applications.applications.filter(value => validId(value?.applicationId, 63) &&
          typeof value.displayName === 'string' && value.displayName.length <= 200) : [];
      this._setupPartial = providerRows.some(value => value === null) || providerRows.length !== providers.providers.length ||
        applicationRows.length !== (applications?.applications?.length ?? 0);
      this._provider.control.replaceChildren(...providerRows.filter(Boolean).map(value => option(value.id, value.displayName)));
      this._applications = applicationRows;
      this._application.control.replaceChildren(option('', 'System only'),
        ...this._applications.map(value => option(value.applicationId, value.displayName)));
      const declared = this.getAttribute('application-id') || '';
      if (Array.from(this._application.control.options).some(value => value.value === declared))
        this._application.control.value = declared;
      await Promise.all([this._loadModels(), this._loadStateSpaces()]);
      if (request.isCurrent()) this._showProgress(this._setupPartial
        ? 'AI workspace ready; some optional entries are unavailable.' : 'AI workspace ready.', 'ready');
    } catch (error) { if (error?.name !== 'AbortError') this._showError(error, () => this._loadSetup()); }
  }

  async _loadModels() {
    const provider = this._provider.control.value;
    if (!provider) { this._models = []; this._model.control.replaceChildren(); return false; }
    const request = this._scopes.models.begin();
    try {
      const value = await this._client.requestJson(
        `/api/control/ai/providers/${encodeURIComponent(provider)}/models`, {signal: request.signal});
      if (!request.isCurrent() || !value || !Array.isArray(value.models)) return false;
      if (value.models.length > 100) throw new SystemClientError('AI_MODEL_RESPONSE_INVALID', 'The AI model list is too large.');
      const models = value.models.map(modelRow);
      this._models = models.filter(Boolean);
      this._setupPartial = this._setupPartial || models.some(model => model === null);
      this._model.control.replaceChildren(...this._models.map(model =>
        option(model.id, model.displayName + (model.isDefault === true ? ' (default)' : ''))));
      const preferred = this._models.find(model => model.isDefault === true);
      if (preferred) this._model.control.value = preferred.id;
      this._updateReasoning();
      return await this._loadHistory();
    } catch (error) {
      if (error?.name !== 'AbortError') this._showError(error, () => this._loadModels());
      return false;
    }
  }

  _updateReasoning() {
    const model = this._models.find(value => value.id === this._model.control.value);
    const efforts = Array.isArray(model?.reasoningEfforts)
      ? model.reasoningEfforts.filter(value => typeof value === 'string' && value.length > 0 && value.length <= 40)
      : ['none'];
    this._reasoning.control.replaceChildren(...efforts.map(value => option(value, value)));
    const supported = Array.isArray(model?.capabilities) && model.capabilities.includes('reasoning') &&
      efforts.some(value => value !== 'none');
    this._reasoning.label.hidden = !supported;
    if (!supported) this._reasoning.control.value = 'none';
  }

  _updateOperation() {
    const structured = this._operation.control.value === 'structured-request';
    this._structuredLabel.hidden = !structured;
    this._schemaLabel.hidden = !structured;
    this._schema.required = structured;
  }

  _selectDeclaredApplication() {
    const declared = this.getAttribute('application-id') || '';
    if (Array.from(this._application.control.options).some(value => value.value === declared))
      this._application.control.value = declared;
    this._loadStateSpaces();
  }

  async _loadStateSpaces() {
    const applicationId = this._application.control.value;
    this._stateSpace.control.replaceChildren(option('', 'No runtime state'));
    if (!applicationId) return;
    const request = this._scopes.state.begin();
    try {
      const value = await this._client.requestJson(
        `/api/control/structure/applications/${encodeURIComponent(applicationId)}/state-spaces`,
        {signal: request.signal});
      if (!request.isCurrent() || !value || !Array.isArray(value.items)) return;
      const runtime = value.items.filter(item => item && item.scope === 'runtime' && item.isCurrent === true &&
        validId(item.stateSpaceId, 120) && typeof item.resolutionFingerprint === 'string' && /^[0-9A-F]{64}$/.test(item.resolutionFingerprint));
      this._stateSpaceBindings = new Map(runtime.map(item => [item.stateSpaceId, item.resolutionFingerprint]));
      this._stateSpace.control.append(...runtime.map(item => option(item.stateSpaceId, item.stateSpaceId)));
      this._selectDeclaredStateSpace();
    } catch (error) { if (error?.name !== 'AbortError') this._showError(error, () => this._loadStateSpaces()); }
  }

  _selectDeclaredStateSpace() {
    const declared = this.getAttribute('state-space-id') || '';
    if (Array.from(this._stateSpace.control.options).some(value => value.value === declared))
      this._stateSpace.control.value = declared;
  }

  async _loadHistory() {
    const provider = this._provider.control.value;
    if (!provider) return false;
    const request = this._scopes.history.begin();
    try {
      const value = await this._client.requestJson(
        `/api/control/ai/conversations?provider=${encodeURIComponent(provider)}` +
        `&surface=${encodeURIComponent(this.getAttribute('surface') || 'inner')}`, {signal: request.signal});
      if (!request.isCurrent() || !value || !Array.isArray(value.items) || value.items.length > 100) return false;
      const rows = value.items.map(conversationRow);
      this._setupPartial = this._setupPartial || rows.some(item => item === null);
      this._history.replaceChildren(option('', 'Past conversations'), ...rows.filter(Boolean).map(item =>
        option(item.id, `${item.title} · ${item.status}`)));
      return true;
    } catch (error) {
      if (error?.name !== 'AbortError') this._showError(error, () => this._loadHistory());
      return false;
    }
  }

  _submissionSnapshot() {
    return JSON.stringify({
      surface: this.getAttribute('surface') || 'inner',
      provider: this._provider.control.value,
      model: this._model.control.value,
      operation: this._conversation ? 'continued-subtask' : this._operation.control.value,
      input: this._input.value,
      applicationId: this._application.control.value || null,
      stateSpaceId: this._stateSpace.control.value || null,
      reasoning: this._reasoning.control.value || 'none',
      structuredInput: this._structuredInput.value,
      responseSchema: this._schema.value,
      conversationId: this._conversation?.summary?.id || null,
      expectedRevision: this._conversation?.summary?.revision || null
    });
  }

  async _loadConversation(id, render = true, options = {}) {
    if (!id) return;
    if (this._pendingSubmission && options.allowDuringRecovery !== true) {
      this._showError(new SystemClientError('AI_REQUEST_RECOVERY_REQUIRED',
        'The previous AI request may have been saved. Retry it before loading another conversation.'));
      return false;
    }
    const request = this._scopes.conversation.begin();
    this._showProgress('Loading conversation…');
    try {
      const value = await this._client.requestJson(
        `/api/control/ai/conversations/${encodeURIComponent(id)}`, {signal: request.signal});
      if (!request.isCurrent() || !this._connected) return false;
      if (options.expectedProvider && (!value?.summary || value.summary.id !== id ||
          value.summary.provider !== options.expectedProvider)) throw new SystemClientError(
        'AI_CONVERSATION_BINDING_INVALID', 'The recovered conversation did not match its original provider.');
      this._conversation = value;
      this._remove.disabled = false;
      if (render) this._renderConversation(value);
      this._showProgress('Conversation ready.', 'ready');
      return true;
    } catch (error) {
      if (error?.name !== 'AbortError' && request.isCurrent() && this._connected)
        this._showError(error, () => this._loadConversation(id, render, options));
      return false;
    }
  }

  async _deleteConversation() {
    const conversation = this._conversation;
    if (!conversation?.summary?.id || !Number.isInteger(conversation.summary.revision)) return;
    if (!globalThis.confirm('Remove this conversation and all of its retained messages?')) return;
    const request = this._scopes.execution.begin();
    this._setBusy(true);
    this._showProgress('Removing conversation…');
    try {
      await this._client.requestJson(
        `/api/control/ai/conversations/${encodeURIComponent(conversation.summary.id)}`,
        {method: 'DELETE', body: {expectedRevision: conversation.summary.revision}, signal: request.signal});
      if (!request.isCurrent()) return;
      this._resetConversation();
      await this._loadHistory();
      this._showProgress('Conversation removed.', 'ready');
      emit(this, 'ai-conversation-removed', {conversationId: conversation.summary.id});
    } catch (error) {
      if (error?.name !== 'AbortError') this._showError(error, () => this._deleteConversation());
    } finally { if (request.isCurrent()) this._setBusy(false); }
  }

  _resetConversation() {
    if (this._pendingSubmission) {
      this._showError(new SystemClientError('AI_REQUEST_RECOVERY_REQUIRED',
        'The previous AI request may have been saved. Retry it before starting another conversation.'));
      return;
    }
    this._conversation = null;
    this._history.value = '';
    this._remove.disabled = true;
    this._renderConversation(null);
  }

  async _submit() {
    if (this._submitButton.disabled) return;
    if (this._recoveryBlocked && !this._pendingSubmission) {
      this._showError(new SystemClientError('AI_RECOVERY_REQUIRED',
        'An earlier request remains uncertain. Recheck it or reload; no new request was sent.'),
      () => this._recoverInterruptedRequest());
      return;
    }
    if (this._pendingSubmission) {
      const pending = this._pendingSubmission;
      if (pending.snapshot !== this._submissionSnapshot()) {
        this._showError(new SystemClientError('AI_REQUEST_RECOVERY_REQUIRED',
          'The previous AI request may have been saved. Restore its inputs to retry the exact request, or confirm its result before starting a new one.'),
        () => this._submit());
        return;
      }
      const request = this._scopes.execution.begin();
      if (!this._rememberInterruptedRequest(pending.body, this._interruptedRecovery)) {
        this._setBusy(false);
        this._showError(new SystemClientError('AI_RECOVERY_UNAVAILABLE',
          'The request cannot be safely sent because interrupted-request recovery is unavailable.'));
        return;
      }
      this._setBusy(true);
      this._showProgress('Retrying the same AI request…');
      try {
        const result = await this._client.requestJson('/api/control/ai/requests', {
          method: 'POST', body: pending.body, signal: request.signal
        });
        if (!request.isCurrent()) return;
        this._pendingSubmission = null; this._clearInterruptedRequest();
        this._input.value = '';
        const refreshed = await this._loadConversation(result.conversationId, false);
        if (!request.isCurrent() || !this._connected) return;
        this._renderResult(result);
        if (refreshed === false) this._showError(new SystemClientError('AI_CONVERSATION_REFRESH_FAILED',
          'The AI request was saved, but the conversation could not be refreshed.'),
        () => this._loadConversation(result.conversationId, false));
        emit(this, 'ai-result', result);
      } catch (error) {
        if (error?.name !== 'AbortError' && request.isCurrent() && this._connected) { this._rememberInterruptedRequest(pending.body); this._showError(new SystemClientError('AI_REQUEST_RECOVERY_REQUIRED',
          'The AI request may have been saved. Retry the same request before changing its inputs.'),
        () => this._submit()); }
      } finally { if (request.isCurrent()) this._setBusy(false); }
      return;
    }
    if (!this._input.reportValidity()) return;
    if (!validId(this._provider.control.value, 120) || !validId(this._model.control.value, 160)) {
      this._showError(new SystemClientError('AI_REQUEST_INPUT_INVALID', 'Choose a current AI provider and model first.'));
      return;
    }
    let structuredInput = null;
    let responseSchema = null;
    try {
      if (this._structuredInput.value.trim()) structuredInput = JSON.parse(this._structuredInput.value);
      if (this._operation.control.value === 'structured-request') responseSchema = JSON.parse(this._schema.value);
      if (responseSchema !== null && (!responseSchema || Array.isArray(responseSchema) || typeof responseSchema !== 'object'))
        throw new Error('The response schema must be one JSON object.');
    } catch (error) {
      this._showError(new SystemClientError('AI_STRUCTURED_INPUT_INVALID', error.message));
      return;
    }
    const applicationId = this._application.control.value || null;
    const application = this._applications.find(value => value.applicationId === applicationId);
    const operation = this._conversation ? 'continued-subtask' : this._operation.control.value;
    const body = {
      surface: this.getAttribute('surface') || 'inner',
      provider: this._provider.control.value,
      model: this._model.control.value,
      operation,
      input: this._input.value,
      idempotencyKey: randomKey('web-ai'),
      applicationId,
      resolutionFingerprint: application?.resolutionFingerprint || null,
      stateSpaceId: this._stateSpace.control.value || null,
      reasoning: this._reasoning.control.value || 'none',
      structuredInput,
      responseSchema,
      conversationId: this._conversation?.summary?.id || null,
      expectedRevision: this._conversation?.summary?.revision || null,
      maximumToolRounds: 4,
      maximumOutputTokens: 2048
    };
    const snapshot = this._submissionSnapshot();
    const request = this._scopes.execution.begin();
    this._setBusy(true);
    this._showProgress('Binding the current context for safe recovery…');
    let recovery = null;
    try { recovery = await this._trustedRecoveryContext(body); }
    catch { /* A rejected browser digest cannot strand the send control or issue a POST. */ }
    if (!request.isCurrent() || !this._connected || snapshot !== this._submissionSnapshot()) {
      if (request.isCurrent()) this._setBusy(false);
      return;
    }
    if (!recovery) {
      this._setBusy(false);
      this._showError(new SystemClientError('AI_RECOVERY_CONTEXT_UNAVAILABLE',
        'The current context cannot be safely bound for interrupted-request recovery. Refresh and try again.'));
      return;
    }
    this._pendingSubmission = {operation, conversationId: body.conversationId, body: {...body}, snapshot};
    if (!this._rememberInterruptedRequest(body, recovery)) {
      this._pendingSubmission = null;
      this._setBusy(false);
      this._showError(new SystemClientError('AI_RECOVERY_UNAVAILABLE',
        'The request cannot be safely sent because interrupted-request recovery is unavailable.'));
      return;
    }
    this._showProgress('AI request running…');
    emit(this, 'ai-progress', {phase: 'running', operation, applicationId});
    try {
      const result = await this._client.requestJson('/api/control/ai/requests', {
        method: 'POST', body, signal: request.signal
      });
      if (!request.isCurrent()) return;
      this._pendingSubmission = null; this._clearInterruptedRequest();
      this._input.value = '';
      const refreshed = await this._loadConversation(result.conversationId, false);
      if (!request.isCurrent() || !this._connected) return;
      this._renderResult(result);
      if (refreshed === false) {
        this._showError(new SystemClientError('AI_CONVERSATION_REFRESH_FAILED',
          'The AI request was saved, but the conversation could not be refreshed.'),
        () => this._loadConversation(result.conversationId, false));
      }
      emit(this, 'ai-result', result);
    } catch (error) {
      if (error?.name !== 'AbortError' && request.isCurrent() && this._connected) {
        this._rememberInterruptedRequest(body);
        this._showError(new SystemClientError('AI_REQUEST_RECOVERY_REQUIRED',
          'The AI request may have been saved. Retry the same request before changing its inputs.'),
        () => this._submit());
        emit(this, 'ai-error', {code: error.code, message: error.message});
      }
    } finally { if (request.isCurrent()) this._setBusy(false); }
  }

  _recoverySlot() { return `ai-workspace-${this.getAttribute('surface') || 'inner'}`; }
  _rememberInterruptedRequest(body, trusted = null) {
    if (!body || !validId(body.idempotencyKey, 100) || !validId(body.provider, 120) ||
        !validId(body.surface, 20) || (body.applicationId !== null && !validId(body.applicationId, 63)) ||
        (body.stateSpaceId !== null && !validId(body.stateSpaceId, 120)) || !trusted ||
        !(trusted.contextFingerprint === null || /^[0-9A-F]{64}$/.test(trusted.contextFingerprint)) ||
        !Array.isArray(trusted.sourceReferences)) return false;
    this._interruptedRecovery = {kind: 'ai', key: body.idempotencyKey, provider: body.provider,
      surface: body.surface, applicationId: body.applicationId || null, stateSpaceId: body.stateSpaceId || null,
      resolutionFingerprint: body.resolutionFingerprint || null, contextFingerprint: trusted.contextFingerprint,
      sourceReferences: trusted.sourceReferences};
    return interruptedRequestStore?.write(this._recoverySlot(), this._interruptedRecovery) === true;
  }
  async _trustedRecoveryContext(body) {
    const surface = body?.surface;
    if (surface !== 'inner' && surface !== 'outer') return null;
    let sourceReferences; let seed;
    if (!body.applicationId) {
      if (body.stateSpaceId) return null;
      sourceReferences = [`surface:${surface}`]; seed = `${surface}\0system-ai-context-v1`;
    } else {
      const application = this._applications.find(value => value.applicationId === body.applicationId);
      if (!application || application.resolutionFingerprint !== body.resolutionFingerprint ||
          !/^[0-9A-F]{64}$/.test(body.resolutionFingerprint || '')) return null;
      const stateFingerprint = body.stateSpaceId ? this._stateSpaceBindings.get(body.stateSpaceId) : '';
      if (body.stateSpaceId && !/^[0-9A-F]{64}$/.test(stateFingerprint || '')) return null;
      sourceReferences = [`application:${body.applicationId}@${body.resolutionFingerprint}`, `surface:${surface}`];
      if (body.stateSpaceId) sourceReferences.push(`state-space:${body.stateSpaceId}@${stateFingerprint}`);
      sourceReferences.sort(); seed = `${surface}\0${body.applicationId}\0${body.resolutionFingerprint}\0${body.stateSpaceId || ''}\0${stateFingerprint}`;
    }
    // The exact, canonical references contain every binding input (surface,
    // application revision and runtime revision). They can be compared on HTTP
    // too. The server always computes and enforces the authoritative fingerprint;
    // a client digest is an additional check, not a replacement for authorization.
    if (!globalThis.crypto?.subtle) return {contextFingerprint: null, sourceReferences};
    const bytes = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(seed));
    return {contextFingerprint: Array.from(new Uint8Array(bytes), value => value.toString(16).padStart(2, '0')).join('').toUpperCase(), sourceReferences};
  }
  _clearInterruptedRequest() { this._interruptedRecovery = null; interruptedRequestStore?.remove(this._recoverySlot()); }
  async _recoverInterruptedRequest() {
    const pending = interruptedRequestStore?.read(this._recoverySlot());
    if (pending?.malformed === true) {
      this._recoveryBlocked = true;
      this._showError(new SystemClientError('AI_RECOVERY_METADATA_INVALID',
        'Saved interrupted-request metadata is invalid. Reload before starting another request.'));
      return;
    }
    if (!pending) return;
    // Claim recovery before any discovery/selector/digest await. A new Send or
    // late setup response must not create a second operation during recovery.
    this._recoveryBlocked = true;
    const request = this._scopes.execution.begin();
    this._setBusy(true);
    this._showProgress('Checking an earlier AI request without sending it again…');
    try {
      if (this._setupPromise) await this._setupPromise;
      if (!request.isCurrent() || !this._connected) return;
      const sameScope = await this._restoreRecoverySelectors(pending,
        () => request.isCurrent() && this._connected);
      if (!request.isCurrent() || !this._connected) return;
      if (!sameScope || !validId(pending.key, 100) || !validId(pending.provider, 120)) {
        this._showError(new SystemClientError('AI_RECOVERY_METADATA_INVALID',
          'Saved interrupted-request metadata is invalid. Reload before starting another request.')); return;
      }
      this._interruptedRecovery = pending;
      const url = new URL(`/api/control/ai/recoveries/${encodeURIComponent(pending.key)}`, window.location.origin);
      url.searchParams.set('provider', pending.provider); url.searchParams.set('surface', pending.surface);
      if (pending.applicationId) url.searchParams.set('applicationId', pending.applicationId);
      if (pending.resolutionFingerprint) url.searchParams.set('resolutionFingerprint', pending.resolutionFingerprint);
      if (pending.stateSpaceId) url.searchParams.set('stateSpaceId', pending.stateSpaceId);
      const recovered = await this._client.requestJson(url.pathname + url.search, {signal: request.signal});
      if (!request.isCurrent() || !this._connected || this._interruptedRecovery !== pending) return;
      if (!recovered || !validId(recovered.conversationId) || !validId(recovered.turnId) ||
          typeof recovered.status !== 'string' || !['completed', 'failed', 'cancelled'].includes(recovered.status)) throw new SystemClientError(
        'AI_RECOVERY_INVALID', 'The saved request recovery response is invalid.');
      if (recovered.idempotencyKey !== pending.key || recovered.provider !== pending.provider || recovered.scope !== 'system' ||
          !/^[0-9A-F]{64}$/.test(recovered.fingerprint || '') ||
          (pending.contextFingerprint !== null && recovered.fingerprint !== pending.contextFingerprint) ||
          !Array.isArray(recovered.sourceReferences) ||
          recovered.sourceReferences.length !== pending.sourceReferences.length ||
          recovered.sourceReferences.some((value, index) => value !== pending.sourceReferences[index])) throw new SystemClientError(
        'AI_RECOVERY_BINDING_INVALID', 'The saved request recovery response did not match its original bound context.');
      this._clearInterruptedRequest();
      this._recoveryBlocked = false;
      const loaded = await this._loadConversation(recovered.conversationId, true, {
        allowDuringRecovery: true, expectedProvider: durableConversationProvider(pending.provider)
      });
      if (!request.isCurrent() || !this._connected) return;
      if (!loaded) {
        this._showError(new SystemClientError('AI_CONVERSATION_REFRESH_FAILED',
          'The earlier AI request was recovered, but its conversation could not be refreshed.'),
        () => this._loadConversation(recovered.conversationId, true, {
          expectedProvider: durableConversationProvider(pending.provider)
        }));
        return;
      }
      this._showProgress('The earlier AI request was recovered.');
    } catch (error) {
      if (error?.name === 'AbortError' || !request.isCurrent() || !this._connected) return;
      if (error?.status === 403) {
        this._clearInterruptedRequest();
        this._recoveryBlocked = true;
        this._showError(new SystemClientError('AI_RECOVERY_DENIED',
          'Authorization changed, so the earlier request was discarded without replaying it. Reload before starting a new request.'));
      } else this._showError(new SystemClientError('AI_RECOVERY_UNCERTAIN',
        'The earlier AI request is still uncertain. Check again later; no new request was sent.'),
      () => this._recoverInterruptedRequest());
    } finally { if (request.isCurrent()) this._setBusy(false); }
  }

  async _restoreRecoverySelectors(pending, isCurrent = () => true) {
    if (pending.kind !== 'ai' || pending.surface !== (this.getAttribute('surface') || 'inner') ||
        !(pending.contextFingerprint === null || /^[0-9A-F]{64}$/.test(pending.contextFingerprint || '')) ||
        !Array.isArray(pending.sourceReferences) || !isCurrent() ||
        (pending.resolutionFingerprint !== null && !/^[0-9A-F]{64}$/.test(pending.resolutionFingerprint))) return false;
    const declaredApplication = this.getAttribute('application-id') || null;
    const declaredState = this.getAttribute('state-space-id') || null;
    if (declaredApplication && declaredApplication !== pending.applicationId || declaredState && declaredState !== pending.stateSpaceId) return false;
    if (!Array.from(this._provider.control.options).some(value => value.value === pending.provider)) return false;
    if (this._provider.control.value !== pending.provider) {
      this._provider.control.value = pending.provider;
      if (!await this._loadModels() || !isCurrent() || this._provider.control.value !== pending.provider) return false;
    }
    if (pending.applicationId === null) {
      if (pending.stateSpaceId !== null) return false;
      this._application.control.value = ''; this._stateSpace.control.value = '';
    } else {
      const application = this._applications.find(value => value.applicationId === pending.applicationId &&
        value.resolutionFingerprint === pending.resolutionFingerprint);
      if (!application) return false;
      this._application.control.value = application.applicationId;
      await this._loadStateSpaces();
      if (!isCurrent()) return false;
      if (pending.stateSpaceId) {
        if (!this._stateSpaceBindings.has(pending.stateSpaceId)) return false;
        this._stateSpace.control.value = pending.stateSpaceId;
      } else this._stateSpace.control.value = '';
    }
    if (this._application.control.value !== (pending.applicationId || '') ||
        this._stateSpace.control.value !== (pending.stateSpaceId || '')) return false;
    const trusted = await this._trustedRecoveryContext({surface: pending.surface, provider: pending.provider,
      applicationId: pending.applicationId, resolutionFingerprint: pending.resolutionFingerprint,
      stateSpaceId: pending.stateSpaceId});
    return isCurrent() && trusted !== null &&
      (pending.contextFingerprint === null || trusted.contextFingerprint === null ||
        trusted.contextFingerprint === pending.contextFingerprint) &&
      trusted.sourceReferences.length === pending.sourceReferences.length &&
      trusted.sourceReferences.every((value, index) => value === pending.sourceReferences[index]);
  }

  _renderResult(result) {
    this._results.replaceChildren();
    const assistantMessage = displayText(own(result, 'assistantMessage'));
    const reasoningSummary = displayText(own(result, 'reasoningSummary'));
    if (assistantMessage) this._results.append(this._section('Assistant', assistantMessage));
    if (reasoningSummary) this._results.append(this._section('Reasoning summary', reasoningSummary));
    this._renderMedia(own(result, 'mediaAttachments'));
    if (own(result, 'structuredDataValidated') === true && own(result, 'structuredData') !== null &&
        own(result, 'structuredData') !== undefined) {
      const section = this._headingSection('Structured result');
      const view = document.createElement('system-data-view');
      view.value = result.structuredData;
      section.append(view);
      this._results.append(section);
    }
    const toolCalls = Array.isArray(own(result, 'toolCalls')) ? own(result, 'toolCalls').slice(0, 64) : [];
    if (toolCalls.length) {
      const section = this._headingSection('Direct tool calls', 'tools');
      const list = document.createElement('ul');
      for (const call of toolCalls) {
        if (!call || typeof call !== 'object' || Array.isArray(call)) continue;
        const item = document.createElement('li');
        item.textContent = `${displayText(own(call, 'name'), 'Tool unavailable', 200)}: ${displayText(own(call, 'status'), 'Status unavailable', 80)}${own(call, 'inputValidated') === true ? ' · input validated' : ''}${displayText(own(call, 'errorCode'), '', 120) ? ` · ${call.errorCode}` : ''}`;
        list.append(item);
      }
      section.append(list); this._results.append(section);
    }
    this._renderActivities(own(result, 'activities'));
    const confirmations = Array.isArray(own(result, 'requiredConfirmations'))
      ? own(result, 'requiredConfirmations').filter(value => typeof value === 'string' && value.length <= 200).slice(0, 24) : [];
    if (confirmations.length) {
      const section = this._headingSection('Required confirmations', 'confirmations');
      const text = document.createElement('p');
      text.textContent = `Review through the existing operator confirmation workflow: ${confirmations.join(', ')}.`;
      section.append(text); this._results.append(section);
    }
    if (own(result, 'ok') !== true) this._showError(new SystemClientError(own(result, 'errorCode'),
      displayText(own(result, 'errorMessage'), 'The AI request did not complete.')));
    else this._showProgress(`Completed with ${displayText(own(result, 'provider'), 'the provider', 120)} · ${displayText(own(result, 'model'), 'the model', 160)}.`, 'ready');
  }

  _renderConversation(value) {
    this._results.replaceChildren();
    if (!value) return;
    const transcript = this._headingSection('Conversation', 'transcript');
    const messages = Array.isArray(own(value, 'messages')) ? own(value, 'messages').slice(0, 500) : [];
    let partial = !Array.isArray(own(value, 'messages')) || own(value, 'messages').length > 500;
    for (const message of messages) {
      if (!message || typeof message !== 'object' || Array.isArray(message) ||
          !['user', 'assistant'].includes(own(message, 'role'))) { partial = true; continue; }
      const row = document.createElement('p');
      row.className = 'message';
      const rawContent = own(message, 'content');
      const content = displayText(rawContent, 'Message content unavailable.');
      if (typeof rawContent !== 'string' || rawContent.length > 4000) partial = true;
      row.textContent = `${message.role === 'assistant' ? 'Assistant' : 'You'}: ${content}`;
      transcript.append(row);
    }
    if (partial) transcript.append(this._section('Notice', 'Some conversation messages are unavailable for this read.'));
    this._results.append(transcript);
    this._renderActivities(own(value, 'activities'));
  }

  _renderActivities(activities) {
    if (!Array.isArray(activities) || activities.length === 0) return;
    const section = this._headingSection('Task and tool progress', 'activity');
    const list = document.createElement('ol');
    for (const activity of activities.slice(0, 100)) {
      if (!activity || typeof activity !== 'object' || Array.isArray(activity)) continue;
      const item = document.createElement('li');
      const status = displayText(own(activity, 'status'), 'unavailable', 80);
      item.dataset.status = status;
      item.textContent = `${displayText(own(activity, 'kind'), 'Activity', 160)}: ${displayText(own(activity, 'summary'), 'Details unavailable.')} (${status})`;
      list.append(item);
    }
    section.append(list); this._results.append(section);
  }

  _renderMedia(attachments) {
    if (!Array.isArray(attachments) || attachments.length === 0) return;
    const allowedRoles = new Set(['portrait', 'setting', 'map', 'illustration', 'icon', 'scene', 'handout']);
    const allowedTypes = new Set(['image/png', 'image/jpeg', 'image/webp']);
    const valid = attachments.slice(0, 64).filter(value => value && typeof value === 'object' &&
      validId(value.entityId) && validId(value.mediaId) && allowedRoles.has(value.role) && allowedTypes.has(value.mediaType) &&
      Number.isInteger(value.width) && value.width > 0 && value.width <= 10000 &&
      Number.isInteger(value.height) && value.height > 0 && value.height <= 10000 &&
      typeof value.alt === 'string' && value.alt.length > 0 && value.alt.length <= 500 &&
      (value.caption === undefined || (typeof value.caption === 'string' && value.caption.length <= 1000)) &&
      typeof value.contentUrl === 'string' && value.contentUrl.startsWith('/api/applications/') &&
      value.contentUrl.endsWith('/content'));
    if (valid.length === 0) return;
    const section = this._headingSection('Images from the system');
    const gallery = document.createElement('div');
    gallery.setAttribute('part', 'media');
    for (const attachment of valid) {
      const card = document.createElement('figure');
      const image = document.createElement('img');
      image.alt = attachment.alt;
      image.src = attachment.contentUrl;
      image.width = attachment.width;
      image.height = attachment.height;
      image.loading = 'lazy';
      image.decoding = 'async';
      const caption = document.createElement('figcaption');
      const role = document.createElement('span');
      role.className = 'media-role';
      role.textContent = attachment.role;
      const text = document.createElement('span');
      text.textContent = displayText(attachment.caption, '', 1000) || attachment.alt;
      caption.append(role, text);
      card.append(image, caption);
      gallery.append(card);
    }
    section.append(gallery);
    this._results.append(section);
  }

  _headingSection(title, part = '') {
    const section = document.createElement('section');
    if (part) section.setAttribute('part', part);
    const heading = document.createElement('h3');
    heading.textContent = title;
    section.append(heading);
    return section;
  }

  _section(title, content) {
    const section = this._headingSection(title);
    const text = document.createElement('p');
    text.className = 'message';
    text.textContent = content;
    section.append(text);
    return section;
  }

  _showProgress(message, phase = 'loading') {
    const progress = document.createElement('system-progress');
    progress.progress = {phase, message};
    this._feedback.replaceChildren(progress);
  }

  _showError(error, retry = null) {
    const view = document.createElement('system-error');
    view.error = error instanceof SystemClientError ? error : new SystemClientError(
      error?.code, error?.message || 'The AI request failed.', {retryable: Boolean(retry)});
    if (retry) view.addEventListener('system-retry', retry, {once: true});
    this._feedback.replaceChildren(view);
  }

  _setBusy(value) {
    this._submitButton.disabled = value;
    this._provider.control.disabled = value;
    this._model.control.disabled = value;
    this._operation.control.disabled = value;
    this._application.control.disabled = value;
    this._stateSpace.control.disabled = value;
    this._new.disabled = value;
    this._remove.disabled = value || !this._conversation;
    this._history.disabled = value;
  }
}

class OuterAi extends AiWorkspace {
  connectedCallback() { this.setAttribute('surface', 'outer'); super.connectedCallback(); }
}

class InnerAi extends AiWorkspace {
  connectedCallback() { this.setAttribute('surface', 'inner'); super.connectedCallback(); }
}

if (!customElements.get('ai-workspace')) customElements.define('ai-workspace', AiWorkspace);
if (!customElements.get('outer-ai')) customElements.define('outer-ai', OuterAi);
if (!customElements.get('inner-ai')) customElements.define('inner-ai', InnerAi);
