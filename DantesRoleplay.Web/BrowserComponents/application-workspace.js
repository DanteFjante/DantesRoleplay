(() => {
  'use strict';

  const MAXIMUM_ID_LENGTH = 200;
  const MAXIMUM_ENTITY_PAGE = 100;
  const MAXIMUM_ROLES = 32;
  const MAXIMUM_FIELDS = 12;

  class ApplicationWorkspaceError extends Error {
    constructor(code, message) { super(message); this.code = code; }
  }

  function validId(value, maximum = MAXIMUM_ID_LENGTH) {
    return typeof value === 'string' && value.length > 0 && value.length <= maximum &&
      value.trim() === value && !/[\u0000-\u001f\u007f]/.test(value);
  }

  function copyObject(value, code = 'APPLICATION_CONTROL_INPUT_INVALID') {
    if (!value || typeof value !== 'object' || Array.isArray(value)) {
      throw new ApplicationWorkspaceError(code, 'This control requires one JSON object.');
    }
    try { return JSON.parse(JSON.stringify(value)); }
    catch { throw new ApplicationWorkspaceError(code, 'This control input cannot be copied safely.'); }
  }

  function requestKey(prefix) {
    if (typeof globalThis.crypto?.randomUUID === 'function') return `${prefix}.${globalThis.crypto.randomUUID()}`;
    const values = new Uint32Array(4);
    globalThis.crypto.getRandomValues(values);
    return `${prefix}.${Array.from(values, value => value.toString(16)).join('')}`;
  }

  function applicationRoot(applicationId, stateSpaceId) {
    return `/api/applications/${encodeURIComponent(applicationId)}/state-spaces/${encodeURIComponent(stateSpaceId)}`;
  }

  async function readJson(input, options = {}) {
    const response = await fetch(input, {headers: {accept: 'application/json', ...(options.headers || {})},
      method: options.method, body: options.body, signal: options.signal});
    let value = null;
    try { value = await response.json(); } catch { }
    if (!response.ok) {
      throw new ApplicationWorkspaceError(value?.error || `HTTP_${response.status}`,
        value?.message || 'The application control request is unavailable.');
    }
    return value;
  }

  function element(name, className, text) {
    const node = document.createElement(name);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function descriptorValue(value, mechanicId) {
    if (!value || typeof value !== 'object' || Array.isArray(value) ||
      !validId(value.qualifiedMechanicId) || value.qualifiedMechanicId !== mechanicId ||
      typeof value.name !== 'string' || typeof value.description !== 'string' ||
      !value.input || typeof value.input !== 'object' || Array.isArray(value.input) ||
      !value.capability || typeof value.capability !== 'object' || Array.isArray(value.capability) ||
      value.capability.id !== mechanicId || !value.capability.input ||
      typeof value.capability.input.schemaJson !== 'string' ||
      !Array.isArray(value.roles)) {
      throw new ApplicationWorkspaceError('APPLICATION_DESCRIPTOR_INVALID',
        'The current application action contract is invalid.');
    }
    const names = new Set();
    for (const role of value.roles) {
      if (!role || typeof role !== 'object' || !validId(role.name) || names.has(role.name) ||
        typeof role.required !== 'boolean' || typeof role.description !== 'string') {
        throw new ApplicationWorkspaceError('APPLICATION_DESCRIPTOR_INVALID',
          'The current application action roles are invalid.');
      }
      names.add(role.name);
    }
    return value;
  }

  class ApplicationEntityPicker extends HTMLElement {
    static get observedAttributes() { return ['application-id', 'state-space-id', 'role-name', 'entity-id']; }

    constructor() {
      super();
      this._connected = false;
      this._request = null;
      this._entities = [];
      this.attachShadow({mode: 'open'});
      this._renderShell();
    }

    connectedCallback() { if (!this._connected) { this._connected = true; this._load(); } }
    disconnectedCallback() { this._connected = false; this._request?.abort(); this._request = null; }
    attributeChangedCallback(name, before, after) {
      if (!this._connected || before === after) return;
      if (name === 'entity-id') this._renderEntities(); else this._load();
    }

    get value() { return this.getAttribute('entity-id') || ''; }
    set value(value) {
      if (value === null || value === undefined || value === '') this.removeAttribute('entity-id');
      else if (validId(String(value))) this.setAttribute('entity-id', String(value));
      else throw new ApplicationWorkspaceError('APPLICATION_ENTITY_ID_INVALID', 'The selected entity ID is invalid.');
    }

    _scope() {
      const applicationId = this.getAttribute('application-id')?.trim() || '';
      const stateSpaceId = this.getAttribute('state-space-id')?.trim() || '';
      if (!validId(applicationId, 63) || !validId(stateSpaceId)) return null;
      return {applicationId, stateSpaceId};
    }

    async _load() {
      this._request?.abort();
      const scope = this._scope();
      if (!scope) { this._entities = []; this._setStatus('Choose an application and state space first.', true); this._renderEntities(); return; }
      const request = new AbortController();
      this._request = request;
      this._setStatus('Finding current entities…');
      try {
        const url = new URL(applicationRoot(scope.applicationId, scope.stateSpaceId) + '/entities', window.location.origin);
        url.searchParams.set('limit', String(MAXIMUM_ENTITY_PAGE));
        const page = await readJson(url, {signal: request.signal});
        if (!Array.isArray(page?.items) || page.items.length > MAXIMUM_ENTITY_PAGE ||
          page.items.some(item => !item || !validId(item.entityId) || typeof item.name !== 'string')) {
          throw new ApplicationWorkspaceError('APPLICATION_ENTITY_PAGE_INVALID', 'The current entity list is invalid.');
        }
        if (!this._connected || this._request !== request) return;
        this._entities = page.items;
        this._boundary = typeof page.nextCursor === 'string' && page.nextCursor.length > 0;
        this._setStatus(this._boundary ? 'Showing the first 100 current entities.' : 'Choose an entity for this role.');
        this._renderEntities();
      } catch (error) {
        if (error.name === 'AbortError') return;
        this._entities = [];
        this._boundary = false;
        this._setStatus(error.message || 'The current entities are unavailable.', true);
        this._renderEntities();
        this._emit('application-error', {code: error.code || 'APPLICATION_ENTITIES_UNAVAILABLE'});
      } finally { if (this._request === request) this._request = null; }
    }

    _renderShell() {
      const style = element('style');
      style.textContent = `:host{display:grid;gap:.4rem;font:inherit}.label{color:var(--application-control-label-color,#d9d0bd);font-size:.72rem;font-weight:800;letter-spacing:.07em;text-transform:uppercase}.picker{align-items:center;background:var(--application-control-card-background,#242326);border:1px solid var(--application-control-border,#685d4d);border-radius:.7rem;display:grid;gap:.5rem;grid-template-columns:minmax(0,1fr) auto;padding:.55rem}.picker:focus-within{border-color:var(--application-control-accent,#e0b968);box-shadow:0 0 0 2px color-mix(in srgb,var(--application-control-accent,#e0b968) 22%,transparent)}select{appearance:none;background:transparent;border:0;color:inherit;font:inherit;min-width:0;outline:0;padding:.18rem}.token{border:1px solid var(--application-control-token-border,#8e7044);border-radius:999px;color:var(--application-control-accent,#e0b968);font-size:.63rem;padding:.25rem .43rem;white-space:nowrap}.status{color:var(--application-control-muted,#aaa08e);font-size:.7rem;line-height:1.4;margin:0}.status[role='alert']{color:var(--application-control-error,#f2b6ae)}`;
      this._label = element('span', 'label');
      this._select = document.createElement('select');
      this._select.addEventListener('change', () => { this.value = this._select.value; this._emit('application-entity-change', {roleName: this.getAttribute('role-name') || '', entityId: this.value}); });
      const picker = element('div', 'picker');
      picker.append(this._select, element('span', 'token', 'Entity'));
      this._status = element('p', 'status');
      this._status.setAttribute('aria-live', 'polite');
      this.shadowRoot.append(style, this._label, picker, this._status);
      this._renderEntities();
    }

    _renderEntities() {
      if (!this._select) return;
      this._label.textContent = this.getAttribute('role-name')?.trim() || 'Entity';
      this._select.replaceChildren();
      const none = document.createElement('option');
      none.value = ''; none.textContent = 'Choose an entity';
      this._select.append(none);
      for (const entity of this._entities) {
        const option = document.createElement('option');
        option.value = entity.entityId;
        option.textContent = entity.name ? `${entity.name} · ${entity.entityId}` : entity.entityId;
        this._select.append(option);
      }
      this._select.value = this._entities.some(item => item.entityId === this.value) ? this.value : '';
      this._select.disabled = !this._entities.length;
    }

    _setStatus(message, error = false) { this._status.textContent = message; this._status.setAttribute('role', error ? 'alert' : 'status'); }
    _emit(name, detail) { this.dispatchEvent(new CustomEvent(name, {detail, bubbles: true, composed: true})); }
  }

  class ApplicationActionControl extends HTMLElement {
    static get observedAttributes() { return ['application-id', 'state-space-id', 'mechanic-id']; }

    constructor() {
      super();
      this._connected = false;
      this._descriptorRequest = null;
      this._operationRequest = null;
      this._descriptor = null;
      this._roles = {};
      this._input = {};
      this._prepared = null;
      this._busy = false;
      this.attachShadow({mode: 'open'});
    }

    connectedCallback() { if (!this._connected) { this._connected = true; this._loadDescriptor(); } }
    disconnectedCallback() { this._connected = false; this._descriptorRequest?.abort(); this._operationRequest?.abort(); }
    attributeChangedCallback(name, before, after) { if (this._connected && before !== after) this._loadDescriptor(); }

    get roleEntityIds() { return copyObject(this._roles, 'APPLICATION_ACTION_ROLES_INVALID'); }
    set roleEntityIds(value) { this._roles = this._copyRoles(value); this._discardPrepared(); this._rolesChanged(); }
    get input() { return copyObject(this._input); }
    set input(value) { this._input = copyObject(value); this._discardPrepared(); this._inputChanged(); }

    _scope() {
      const applicationId = this.getAttribute('application-id')?.trim() || '';
      const stateSpaceId = this.getAttribute('state-space-id')?.trim() || '';
      const mechanicId = this.getAttribute('mechanic-id')?.trim() || '';
      if (!validId(applicationId, 63) || !validId(stateSpaceId) || !validId(mechanicId)) return null;
      return {applicationId, stateSpaceId, mechanicId};
    }

    async _loadDescriptor() {
      this._descriptorRequest?.abort(); this._operationRequest?.abort();
      this._descriptor = null; this._prepared = null; this._busy = false;
      const scope = this._scope();
      if (!scope) { this._setStatus('Set one application, state space, and action before using this control.', true); this._renderUnavailable(); return; }
      const request = new AbortController(); this._descriptorRequest = request;
      this._setStatus('Loading the current action contract…'); this._emit('application-progress', {phase: 'loading', ...scope});
      try {
        const value = await readJson(applicationRoot(scope.applicationId, scope.stateSpaceId) + `/mechanics/${encodeURIComponent(scope.mechanicId)}`, {signal: request.signal});
        const descriptor = descriptorValue(value, scope.mechanicId);
        if (!this._connected || this._descriptorRequest !== request) return;
        this._descriptor = descriptor;
        this._renderDescriptor(descriptor);
        this._setStatus(`Ready: ${descriptor.description}`);
        this._emit('application-progress', {phase: 'ready', ...scope, version: descriptor.version, fingerprint: descriptor.contentFingerprint});
      } catch (error) {
        if (error.name === 'AbortError') return;
        this._setStatus(error.message || 'The current action contract is unavailable.', true);
        this._renderUnavailable();
        this._emit('application-error', {code: error.code || 'APPLICATION_ACTION_UNAVAILABLE'});
      } finally { if (this._descriptorRequest === request) this._descriptorRequest = null; }
    }

    _copyRoles(value) {
      const roles = copyObject(value, 'APPLICATION_ACTION_ROLES_INVALID');
      const entries = Object.entries(roles);
      if (entries.length > MAXIMUM_ROLES || entries.some(([name, entityId]) => !validId(name) || !validId(entityId))) {
        throw new ApplicationWorkspaceError('APPLICATION_ACTION_ROLES_INVALID', 'The selected action roles are invalid.');
      }
      return Object.fromEntries(entries);
    }

    _validatedRoles() {
      if (!this._descriptor) throw new ApplicationWorkspaceError('APPLICATION_ACTION_UNAVAILABLE', 'The current action contract is unavailable.');
      const allowed = new Map(this._descriptor.roles.map(role => [role.name, role]));
      const selected = {};
      for (const [name, entityId] of Object.entries(this._roles)) {
        if (!allowed.has(name)) throw new ApplicationWorkspaceError('APPLICATION_ACTION_ROLE_UNKNOWN', 'The selected role is not declared by this action.');
        selected[name] = entityId;
      }
      const missing = this._descriptor.roles.filter(role => role.required && !selected[role.name]);
      if (missing.length) throw new ApplicationWorkspaceError('APPLICATION_ACTION_ROLE_REQUIRED', `Choose an entity for ${missing.map(role => role.name).join(', ')} before preparing this action.`);
      return selected;
    }

    async _prepare() {
      if (this._busy) return;
      let scope; let roles; let input;
      try { scope = this._scope(); if (!scope) throw new ApplicationWorkspaceError('APPLICATION_ACTION_SCOPE_INVALID', 'The action scope is invalid.'); roles = this._validatedRoles(); input = copyObject(this._input); }
      catch (error) { this._setStatus(error.message, true); this._emit('application-error', {code: error.code || 'APPLICATION_ACTION_PREPARE_INVALID'}); return; }
      const request = new AbortController(); this._operationRequest = request; this._setBusy(true);
      this._setStatus('Preparing an exact action for review…'); this._emit('application-progress', {phase: 'preparing', ...scope});
      try {
        const result = await readJson(applicationRoot(scope.applicationId, scope.stateSpaceId) + `/mechanics/${encodeURIComponent(scope.mechanicId)}/prepare`, {method: 'POST', headers: {'content-type': 'application/json'}, body: JSON.stringify({idempotencyKey: requestKey('application-prepare'), roleEntityIds: roles, input}), signal: request.signal});
        if (!result?.ready || typeof result.proposalFingerprint !== 'string' || !result.proposal || typeof result.proposal !== 'object' || typeof result.receipt?.id !== 'string') {
          throw new ApplicationWorkspaceError(result?.code || 'APPLICATION_ACTION_NOT_READY', result?.safeSummary || 'The action could not be prepared for review.');
        }
        if (!this._connected || this._operationRequest !== request) return;
        this._prepared = {scope, roles, input, proposalFingerprint: result.proposalFingerprint, proposal: result.proposal, receiptId: result.receipt.id, safeSummary: result.safeSummary || 'Review the exact prepared action.', evidence: Array.isArray(result.evidence) ? result.evidence : []};
        this._renderPrepared(); this._setStatus(this._prepared.safeSummary);
        this._emit('application-proposal', {phase: 'prepared', ...scope, receiptId: this._prepared.receiptId, proposalFingerprint: this._prepared.proposalFingerprint});
      } catch (error) {
        if (error.name !== 'AbortError') { this._setStatus(error.message || 'The action could not be prepared.', true); this._emit('application-error', {code: error.code || 'APPLICATION_ACTION_PREPARE_FAILED'}); }
      } finally { if (this._operationRequest === request) this._operationRequest = null; this._setBusy(false); }
    }

    async _execute() {
      if (this._busy || !this._prepared) return;
      const prepared = this._prepared; const current = this._scope();
      if (!current || current.applicationId !== prepared.scope.applicationId || current.stateSpaceId !== prepared.scope.stateSpaceId || current.mechanicId !== prepared.scope.mechanicId) { this._discardPrepared(); this._setStatus('The prepared action no longer matches this control.', true); return; }
      const request = new AbortController(); this._operationRequest = request; this._setBusy(true);
      this._setStatus('Executing the exact reviewed action…'); this._emit('application-progress', {phase: 'executing', ...current, receiptId: prepared.receiptId});
      try {
        const result = await readJson(applicationRoot(current.applicationId, current.stateSpaceId) + `/mechanics/${encodeURIComponent(current.mechanicId)}/execute`, {method: 'POST', headers: {'content-type': 'application/json'}, body: JSON.stringify({resolutionReceiptId: prepared.receiptId, proposalFingerprint: prepared.proposalFingerprint, idempotencyKey: requestKey('application-execute'), proposal: prepared.proposal}), signal: request.signal});
        if (!this._connected || this._operationRequest !== request) return;
        this._prepared = null; this._renderOutcome(result);
        const summary = typeof result?.safeSummary === 'string' ? result.safeSummary : 'The reviewed action completed.';
        this._setStatus(summary, result?.successful === false);
        this._emit('application-receipt', {phase: 'complete', ...current, status: result?.code || 'APPLICATION_ACTION_COMPLETE', receiptId: result?.receipt?.receipt?.id || null});
      } catch (error) {
        if (error.name !== 'AbortError') { this._setStatus(error.message || 'The reviewed action could not be executed.', true); this._emit('application-error', {code: error.code || 'APPLICATION_ACTION_EXECUTE_FAILED'}); }
      } finally { if (this._operationRequest === request) this._operationRequest = null; this._setBusy(false); }
    }

    _discardPrepared() { if (this._prepared) { this._prepared = null; this._clearReview(); } }
    _setBusy(value) { this._busy = value; this._setReady(!value && !!this._descriptor); }
    _setStatus(message, error = false) { this._status.textContent = message; this._status.setAttribute('role', error ? 'alert' : 'status'); }
    _emit(name, detail) { this.dispatchEvent(new CustomEvent(name, {detail, bubbles: true, composed: true})); }
    _rolesChanged() { }
    _inputChanged() { }
  }

  class ApplicationActionButton extends ApplicationActionControl {
    constructor() { super(); this._renderShell(); }

    _renderShell() {
      const style = actionStyle();
      this._button = element('button', 'action-button', 'Prepare action'); this._button.type = 'button'; this._button.disabled = true;
      this._button.addEventListener('click', () => this._prepare());
      this._status = element('p', 'status'); this._status.setAttribute('aria-live', 'polite');
      this._review = element('section', 'review'); this._review.setAttribute('aria-label', 'Application action review');
      this.shadowRoot.append(style, this._button, this._status, this._review);
    }

    _renderDescriptor(descriptor) { this._button.textContent = this.textContent.trim() || `Prepare ${descriptor.name}`; this._clearReview(); }
    _renderUnavailable() { this._button.disabled = true; this._clearReview(); }
    _setReady(value) { this._button.disabled = !value; }
    _clearReview() { this._review.replaceChildren(); }
    _renderPrepared() { renderPreparedReview(this._review, this._prepared, () => this._execute()); }
    _renderOutcome(value) { renderOutcome(this._review, value); }
  }

  class ApplicationForm extends ApplicationActionControl {
    constructor() { super(); this._renderShell(); }

    _renderShell() {
      this._style = actionStyle();
      this._form = element('form', 'form');
      this._form.addEventListener('submit', event => { event.preventDefault(); this._prepare(); });
      this._status = element('p', 'status'); this._status.setAttribute('aria-live', 'polite');
      this._review = element('section', 'review'); this._review.setAttribute('aria-label', 'Application action review');
      this.shadowRoot.append(this._style, this._form, this._status, this._review);
    }

    _renderDescriptor(descriptor) {
      this._form.replaceChildren(); this._clearReview();
      const heading = element('div', 'form-heading'); heading.append(element('strong', '', descriptor.name), element('span', '', descriptor.description)); this._form.append(heading);
      const roles = element('section', 'role-grid'); roles.append(element('p', 'section-label', 'Choose roles'));
      for (const role of descriptor.roles) {
        const field = element('div', 'role-card');
        const copy = element('div'); copy.append(element('strong', '', role.name), element('small', '', role.description || (role.required ? 'Required role' : 'Optional role')));
        if (role.required) copy.append(element('span', 'required', 'Required'));
        const picker = document.createElement('application-entity-picker');
        picker.setAttribute('application-id', this.getAttribute('application-id') || '');
        picker.setAttribute('state-space-id', this.getAttribute('state-space-id') || '');
        picker.setAttribute('role-name', role.name);
        if (this._roles[role.name]) picker.value = this._roles[role.name];
        picker.addEventListener('application-entity-change', event => {
          const entityId = event.detail?.entityId || ''; const next = this.roleEntityIds;
          if (entityId) next[role.name] = entityId; else delete next[role.name];
          this.roleEntityIds = next;
        });
        field.append(copy, picker); roles.append(field);
      }
      this._form.append(roles, this._renderFields(descriptor));
      this._submit = element('button', 'action-button', `Prepare ${descriptor.name}`); this._submit.type = 'submit'; this._form.append(this._submit);
      this._setReady(true);
    }

    _renderFields(descriptor) {
      const fields = element('section', 'fields'); fields.append(element('p', 'section-label', 'Action details'));
      const inputContract = descriptor.capability.input;
      if (!['authored', 'generated', 'generic'].includes(inputContract.status)) {
        throw new ApplicationWorkspaceError('APPLICATION_DESCRIPTOR_INVALID',
          'The current application action input contract is invalid.');
      }
      if (inputContract.status === 'generic') {
        fields.append(element('p', 'field-note', 'This action has no published entry fields. It will be reviewed with an empty input object.'));
        this._input = {}; return fields;
      }
      let schema;
      try { schema = JSON.parse(inputContract.schemaJson); } catch { schema = null; }
      const properties = schema && schema.type === 'object' && schema.properties && typeof schema.properties === 'object' && !Array.isArray(schema.properties) ? Object.entries(schema.properties) : [];
      if (properties.length > MAXIMUM_FIELDS || properties.some(([, value]) => !value || typeof value !== 'object' || !['string', 'number', 'integer', 'boolean'].includes(value.type))) {
        fields.append(element('p', 'field-note', 'The published input form is not supported by this generic control.'));
        this._input = {}; this._schemaUnsupported = true; return fields;
      }
      this._schemaUnsupported = false; const required = new Set(Array.isArray(schema.required) ? schema.required : []); const current = {};
      for (const [name, definition] of properties) {
        if (!validId(name, 120)) { this._schemaUnsupported = true; continue; }
        const field = element('label', 'field'); field.append(element('span', '', `${name}${required.has(name) ? ' *' : ''}`));
        const input = definition.type === 'boolean' ? document.createElement('input') : document.createElement('input');
        input.name = name; input.required = required.has(name);
        if (definition.type === 'boolean') input.type = 'checkbox';
        else { input.type = definition.type === 'string' ? 'text' : 'number'; if (definition.type === 'integer') input.step = '1'; }
        input.addEventListener('input', () => {
          if (definition.type === 'boolean') current[name] = input.checked;
          else if (input.value === '') delete current[name];
          else current[name] = definition.type === 'string' ? input.value : Number(input.value);
          this.input = current;
        });
        field.append(input); fields.append(field);
      }
      this._input = {}; return fields;
    }

    _renderUnavailable() { this._form.replaceChildren(); this._clearReview(); }
    _setReady(value) { if (this._submit) this._submit.disabled = !value || this._schemaUnsupported; }
    _clearReview() { this._review.replaceChildren(); }
    _renderPrepared() { renderPreparedReview(this._review, this._prepared, () => this._execute()); }
    _renderOutcome(value) { renderOutcome(this._review, value); }
    _rolesChanged() { this._discardPrepared(); }
    _inputChanged() { this._discardPrepared(); }
  }

  function actionStyle() {
    const style = element('style');
    style.textContent = `:host{color:var(--application-control-color,#f6eedb);display:grid;font:inherit;gap:.65rem}.action-button{align-items:center;background:linear-gradient(135deg,var(--application-control-button,#7f3d34),var(--application-control-button-deep,#4f2828));border:1px solid var(--application-control-accent,#e0b968);border-radius:.7rem;color:inherit;cursor:pointer;font:inherit;font-weight:800;justify-self:start;letter-spacing:.02em;padding:.65rem .85rem}.action-button:disabled{cursor:not-allowed;opacity:.55}.status{color:var(--application-control-muted,#aaa08e);font-size:.76rem;line-height:1.45;margin:0}.status[role='alert']{color:var(--application-control-error,#f2b6ae)}.review{display:grid;gap:.55rem}.review-card{background:var(--application-control-card-background,#242326);border:1px solid var(--application-control-border,#685d4d);border-radius:.75rem;display:grid;gap:.55rem;padding:.75rem}.review-card h3,.review-card p{margin:0}.review-card h3{font-family:Georgia,serif;font-size:1rem}.evidence{color:var(--application-control-muted,#aaa08e);font-size:.7rem;margin:0;padding-left:1rem}.fingerprint{color:var(--application-control-muted,#aaa08e);font-family:ui-monospace,monospace;font-size:.62rem;overflow-wrap:anywhere}.form{display:grid;gap:.75rem}.form-heading{display:grid;gap:.2rem}.form-heading strong{font-family:Georgia,serif;font-size:1.12rem}.form-heading span,.field-note{color:var(--application-control-muted,#aaa08e);font-size:.76rem;line-height:1.45;margin:0}.section-label{color:var(--application-control-accent,#e0b968);font-size:.67rem;font-weight:800;letter-spacing:.09em;margin:0;text-transform:uppercase}.role-grid,.fields{display:grid;gap:.55rem}.role-card{background:var(--application-control-card-background,#242326);border:1px solid var(--application-control-border,#685d4d);border-radius:.75rem;display:grid;gap:.55rem;padding:.65rem}.role-card strong,.role-card small{display:block}.role-card small{color:var(--application-control-muted,#aaa08e);font-size:.7rem;line-height:1.35;margin-top:.16rem}.required{color:var(--application-control-accent,#e0b968);font-size:.62rem;font-weight:800;letter-spacing:.07em;text-transform:uppercase}.field{display:grid;gap:.3rem;font-size:.76rem;font-weight:700}.field input{background:var(--application-control-input-background,#151417);border:1px solid var(--application-control-border,#685d4d);border-radius:.5rem;color:inherit;font:inherit;padding:.5rem}.field input[type='checkbox']{height:1rem;width:1rem}`;
    return style;
  }

  function renderPreparedReview(target, prepared, confirm) {
    target.replaceChildren();
    const card = element('article', 'review-card'); card.append(element('h3', '', 'Review action'), element('p', '', prepared.safeSummary));
    if (prepared.evidence.length) { const list = element('ul', 'evidence'); for (const value of prepared.evidence.slice(0, 12)) list.append(element('li', '', String(value))); card.append(list); }
    card.append(element('p', 'fingerprint', `Proposal ${prepared.proposalFingerprint}`));
    const button = element('button', 'action-button', 'Confirm and execute'); button.type = 'button'; button.addEventListener('click', () => confirm()); card.append(button); target.append(card);
  }

  function renderOutcome(target, value) {
    target.replaceChildren(); const card = element('article', 'review-card');
    const successful = value?.successful !== false;
    card.append(element('h3', '', successful ? 'Action result' : 'Action not completed'), element('p', '', typeof value?.safeSummary === 'string' ? value.safeSummary : 'The application returned its action outcome.'));
    const narrations = Array.isArray(value?.actionResults) ? value.actionResults
      .map(result => typeof result?.narration === 'string' ? result.narration.trim() : '')
      .filter(Boolean).slice(0, 12) : [];
    if (narrations.length) {
      const list = element('ul', 'evidence');
      for (const narration of narrations) list.append(element('li', '', narration));
      card.append(list);
    }
    if (typeof value?.code === 'string') card.append(element('p', 'fingerprint', value.code));
    target.append(card);
  }

  customElements.define('application-entity-picker', ApplicationEntityPicker);
  customElements.define('application-action-button', ApplicationActionButton);
  customElements.define('application-form', ApplicationForm);
})();
