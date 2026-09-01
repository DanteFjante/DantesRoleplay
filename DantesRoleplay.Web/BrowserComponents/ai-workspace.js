import {SystemClientError, SystemRequestScope, systemWebClient} from '/components/system-client.js';
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
    this._models = [];
    this._conversation = null;
    this.attachShadow({mode: 'open'});
    this._renderShell();
  }

  connectedCallback() {
    if (this._connected) return;
    this._connected = true;
    if (!this.hasAttribute('surface')) this.setAttribute('surface', 'inner');
    this._configureSurface();
    this._loadSetup();
  }

  disconnectedCallback() {
    this._connected = false;
    for (const scope of Object.values(this._scopes)) scope.cancel();
  }

  attributeChangedCallback(name) {
    if (!this._connected) return;
    if (name === 'application-id') {
      this._scopes.execution.cancel();
      this._selectDeclaredApplication();
    } else if (name === 'state-space-id') {
      this._scopes.execution.cancel();
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
    if (this._connected) this._loadSetup();
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
    this._showProgress('Discovering AI providers and applications…');
    try {
      const [providers, applications] = await Promise.all([
        this._client.requestJson('/api/control/ai/providers', {signal: request.signal}),
        this._client.discoverAllApplications({signal: request.signal})
      ]);
      if (!request.isCurrent()) return;
      if (!providers || !Array.isArray(providers.providers)) throw new SystemClientError(
        'AI_PROVIDER_RESPONSE_INVALID', 'The AI provider response is invalid.');
      this._provider.control.replaceChildren(...providers.providers.map(value => option(value.id, value.displayName)));
      this._applications = applications.applications;
      this._application.control.replaceChildren(option('', 'System only'),
        ...this._applications.map(value => option(value.applicationId, value.displayName)));
      const declared = this.getAttribute('application-id') || '';
      if (Array.from(this._application.control.options).some(value => value.value === declared))
        this._application.control.value = declared;
      await Promise.all([this._loadModels(), this._loadStateSpaces()]);
      if (request.isCurrent()) this._showProgress('AI workspace ready.', 'ready');
    } catch (error) { if (error?.name !== 'AbortError') this._showError(error, () => this._loadSetup()); }
  }

  async _loadModels() {
    const provider = this._provider.control.value;
    if (!provider) { this._models = []; this._model.control.replaceChildren(); return; }
    const request = this._scopes.models.begin();
    try {
      const value = await this._client.requestJson(
        `/api/control/ai/providers/${encodeURIComponent(provider)}/models`, {signal: request.signal});
      if (!request.isCurrent() || !value || !Array.isArray(value.models)) return;
      this._models = value.models;
      this._model.control.replaceChildren(...value.models.map(model =>
        option(model.id, model.displayName + (model.isDefault ? ' (default)' : ''))));
      const preferred = value.models.find(model => model.isDefault);
      if (preferred) this._model.control.value = preferred.id;
      this._updateReasoning();
      await this._loadHistory();
    } catch (error) { if (error?.name !== 'AbortError') this._showError(error, () => this._loadModels()); }
  }

  _updateReasoning() {
    const model = this._models.find(value => value.id === this._model.control.value);
    const efforts = Array.isArray(model?.reasoningEfforts) ? model.reasoningEfforts : ['none'];
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
      const runtime = value.items.filter(item => item.scope === 'runtime' && item.isCurrent === true);
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
    if (!provider) return;
    const request = this._scopes.history.begin();
    try {
      const value = await this._client.requestJson(
        `/api/control/ai/conversations?provider=${encodeURIComponent(provider)}` +
        `&surface=${encodeURIComponent(this.getAttribute('surface') || 'inner')}`, {signal: request.signal});
      if (!request.isCurrent() || !value || !Array.isArray(value.items)) return;
      this._history.replaceChildren(option('', 'Past conversations'), ...value.items.map(item =>
        option(item.id, `${item.title} · ${item.status}`)));
    } catch (error) { if (error?.name !== 'AbortError') this._showError(error, () => this._loadHistory()); }
  }

  async _loadConversation(id, render = true) {
    if (!id) return;
    const request = this._scopes.conversation.begin();
    this._showProgress('Loading conversation…');
    try {
      const value = await this._client.requestJson(
        `/api/control/ai/conversations/${encodeURIComponent(id)}`, {signal: request.signal});
      if (!request.isCurrent()) return;
      this._conversation = value;
      this._remove.disabled = false;
      if (render) this._renderConversation(value);
      this._showProgress('Conversation ready.', 'ready');
    } catch (error) { if (error?.name !== 'AbortError') this._showError(error, () => this._loadConversation(id)); }
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
    this._conversation = null;
    this._history.value = '';
    this._remove.disabled = true;
    this._renderConversation(null);
  }

  async _submit() {
    if (!this._input.reportValidity()) return;
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
    const request = this._scopes.execution.begin();
    this._setBusy(true);
    this._showProgress('AI request running…');
    emit(this, 'ai-progress', {phase: 'running', operation, applicationId});
    try {
      const result = await this._client.requestJson('/api/control/ai/requests', {
        method: 'POST', body, signal: request.signal
      });
      if (!request.isCurrent()) return;
      this._input.value = '';
      await this._loadConversation(result.conversationId, false);
      this._renderResult(result);
      emit(this, 'ai-result', result);
    } catch (error) {
      if (error?.name !== 'AbortError') {
        this._showError(error, () => this._submit());
        emit(this, 'ai-error', {code: error.code, message: error.message});
      }
    } finally { if (request.isCurrent()) this._setBusy(false); }
  }

  _renderResult(result) {
    this._results.replaceChildren();
    if (result.assistantMessage) this._results.append(this._section('Assistant', result.assistantMessage));
    if (result.reasoningSummary) this._results.append(this._section('Reasoning summary', result.reasoningSummary));
    this._renderMedia(result.mediaAttachments || []);
    if (result.structuredDataValidated && result.structuredData !== null) {
      const section = this._headingSection('Structured result');
      const view = document.createElement('system-data-view');
      view.value = result.structuredData;
      section.append(view);
      this._results.append(section);
    }
    if (Array.isArray(result.toolCalls) && result.toolCalls.length) {
      const section = this._headingSection('Direct tool calls', 'tools');
      const list = document.createElement('ul');
      for (const call of result.toolCalls) {
        const item = document.createElement('li');
        item.textContent = `${call.name}: ${call.status}${call.inputValidated ? ' · input validated' : ''}${call.errorCode ? ` · ${call.errorCode}` : ''}`;
        list.append(item);
      }
      section.append(list); this._results.append(section);
    }
    this._renderActivities(result.activities || []);
    if (Array.isArray(result.requiredConfirmations) && result.requiredConfirmations.length) {
      const section = this._headingSection('Required confirmations', 'confirmations');
      const text = document.createElement('p');
      text.textContent = `Review through the existing operator confirmation workflow: ${result.requiredConfirmations.join(', ')}.`;
      section.append(text); this._results.append(section);
    }
    if (!result.ok) this._showError(new SystemClientError(result.errorCode, result.errorMessage));
    else this._showProgress(`Completed with ${result.provider} · ${result.model}.`, 'ready');
  }

  _renderConversation(value) {
    this._results.replaceChildren();
    if (!value) return;
    const transcript = this._headingSection('Conversation', 'transcript');
    for (const message of value.messages || []) {
      const row = document.createElement('p');
      row.className = 'message';
      row.textContent = `${message.role === 'assistant' ? 'Assistant' : 'You'}: ${message.content}`;
      transcript.append(row);
    }
    this._results.append(transcript);
    this._renderActivities(value.activities || []);
  }

  _renderActivities(activities) {
    if (!Array.isArray(activities) || activities.length === 0) return;
    const section = this._headingSection('Task and tool progress', 'activity');
    const list = document.createElement('ol');
    for (const activity of activities) {
      const item = document.createElement('li');
      item.dataset.status = activity.status;
      item.textContent = `${activity.kind}: ${activity.summary} (${activity.status})`;
      list.append(item);
    }
    section.append(list); this._results.append(section);
  }

  _renderMedia(attachments) {
    if (!Array.isArray(attachments) || attachments.length === 0) return;
    const allowedRoles = new Set(['portrait', 'setting', 'map', 'illustration', 'icon', 'scene', 'handout']);
    const allowedTypes = new Set(['image/png', 'image/jpeg', 'image/webp']);
    const valid = attachments.filter(value => value && typeof value.entityId === 'string' &&
      typeof value.mediaId === 'string' && allowedRoles.has(value.role) && allowedTypes.has(value.mediaType) &&
      Number.isInteger(value.width) && value.width > 0 && value.width <= 10000 &&
      Number.isInteger(value.height) && value.height > 0 && value.height <= 10000 &&
      typeof value.alt === 'string' && value.alt.length > 0 && value.alt.length <= 500 &&
      typeof value.caption === 'string' && value.caption.length <= 1000 &&
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
      text.textContent = attachment.caption || attachment.alt;
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
