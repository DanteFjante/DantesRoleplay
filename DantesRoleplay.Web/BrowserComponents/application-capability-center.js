import {renderInvocation, unavailableResult} from '/components/composition-bindings.js';

const MAXIMUM_CAPABILITIES = 64;
const MAXIMUM_JSON_BYTES = 65_536;
const MAXIMUM_IDEMPOTENCY_KEY = 200;

const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const text = (value, maximum) => typeof value === 'string' && value.length > 0 && value.length <= maximum;

function descriptor(value) {
  if (!object(value) || !text(value.id, 240) || !Number.isInteger(value.version) || value.version < 1 ||
      !text(value.sourceFingerprint, 64) || !/^[A-F0-9]{64}$/.test(value.sourceFingerprint) ||
      !text(value.owner, 200) || !text(value.description, 5_000) || !['read', 'write'].includes(value.mode) ||
      !text(value.inputSchemaJson, MAXIMUM_JSON_BYTES) || !text(value.outputSchemaJson, MAXIMUM_JSON_BYTES) ||
      typeof value.requiresIdempotencyKey !== 'boolean') {
    throw new Error('The application capability index returned an invalid contract.');
  }
  for (const schema of [value.inputSchemaJson, value.outputSchemaJson]) {
    const parsed = JSON.parse(schema);
    if (!object(parsed)) throw new Error('The application capability schema is invalid.');
  }
  return value;
}

function element(document, tag, content, part) {
  const node = document.createElement(tag);
  if (content !== undefined) node.textContent = content;
  if (part) node.setAttribute('part', part);
  return node;
}

function errorResult(code, message, failed = false) {
  return {...unavailableResult(code || 'APPLICATION_CAPABILITY_UNAVAILABLE',
    message || 'The application capability is unavailable.'), tag: failed ? 'failed' : 'unavailable'};
}

function commandKey() {
  if (typeof globalThis.crypto?.randomUUID === 'function')
    return `web.application-capability.${globalThis.crypto.randomUUID()}`;
  const values = new Uint32Array(4);
  globalThis.crypto.getRandomValues(values);
  return `web.application-capability.${Array.from(values, value => value.toString(16)).join('')}`;
}

class ApplicationCapabilityCenter extends HTMLElement {
  static get observedAttributes() { return ['application-id']; }

  constructor() {
    super();
    this._connected = false;
    this._request = null;
    this._invocationRequest = null;
    this._capabilities = [];
    this.attachShadow({mode: 'open'});
    this._renderShell();
  }

  connectedCallback() {
    if (this._connected) return;
    this._connected = true;
    this._load();
  }

  disconnectedCallback() {
    this._connected = false;
    this._request?.abort();
    this._invocationRequest?.abort();
    this._request = null;
    this._invocationRequest = null;
  }

  attributeChangedCallback(_name, before, after) {
    if (this._connected && before !== after) this._load();
  }

  _renderShell() {
    const document = this.ownerDocument;
    const style = element(document, 'style');
    style.textContent = `
      :host{display:grid;gap:.65rem;min-width:0}
      header,article,[part='runner']{background:var(--application-capability-background,#1d1d20);border:1px solid var(--application-capability-border,#685d4d);border-radius:.65rem;padding:.7rem}
      header,[part='cards'],article,[part='runner'],form{display:grid;gap:.55rem}
      h2,h3,p{margin:0} h2{font-size:.95rem} h3{font-size:.82rem;overflow-wrap:anywhere}
      [part='status'],[part='meta']{color:var(--application-capability-muted,#aaa08e);font-size:.75rem}
      input,textarea{box-sizing:border-box;width:100%;background:var(--application-capability-input,#141416);border:1px solid var(--application-capability-border,#685d4d);border-radius:.4rem;color:inherit;font:inherit;padding:.45rem}
      textarea{min-height:8rem;resize:vertical} label{display:grid;gap:.25rem;font-size:.75rem;font-weight:700}
      button{justify-self:start;background:transparent;border:1px solid var(--application-capability-border,#8e7044);border-radius:.4rem;color:inherit;cursor:pointer;font:inherit;padding:.4rem .6rem}
      button:disabled{cursor:not-allowed;opacity:.6} details{font-size:.75rem} summary{cursor:pointer}
      pre{background:var(--application-capability-input,#141416);border-radius:.35rem;max-height:16rem;overflow:auto;padding:.45rem;white-space:pre-wrap;overflow-wrap:anywhere}
      [part='runner'][hidden]{display:none}.error{color:var(--application-capability-error,#f2b6ae)}
    `;
    const header = element(document, 'header');
    header.append(element(document, 'h2', 'Application capabilities'),
      element(document, 'p', 'Discover and invoke the current application’s shared capability contracts. The host selects authority and execution policy.', 'meta'));
    this._status = element(document, 'p', 'Choose an application.', 'status');
    this._status.setAttribute('aria-live', 'polite');
    header.append(this._status);
    this._cards = element(document, 'div', undefined, 'cards');
    this._runner = element(document, 'section', undefined, 'runner');
    this._runner.hidden = true;
    this.shadowRoot.append(style, header, this._cards, this._runner);
  }

  async _load() {
    this._request?.abort();
    this._invocationRequest?.abort();
    this._invocationRequest = null;
    this._runner.hidden = true;
    this._runner.replaceChildren();
    this._cards.replaceChildren();
    const applicationId = this.getAttribute('application-id')?.trim() || '';
    if (!text(applicationId, 63) || /[\u0000-\u001f\u007f/\\]/.test(applicationId)) {
      this._setStatus('Choose one valid application before loading its capabilities.', true);
      return;
    }
    const request = new AbortController();
    this._request = request;
    this._setStatus('Loading current application capabilities…');
    try {
      const response = await fetch(`/api/applications/${encodeURIComponent(applicationId)}/authoring/capabilities`,
        {cache: 'no-store', credentials: 'same-origin', headers: {accept: 'application/json'}, signal: request.signal});
      const body = await response.json().catch(() => null);
      if (!response.ok) throw new Error(body?.message || 'Application capability discovery is unavailable.');
      if (!object(body) || body.applicationId !== applicationId || !Array.isArray(body.capabilities) ||
          body.capabilities.length > MAXIMUM_CAPABILITIES) throw new Error('The application capability index is malformed.');
      const capabilities = body.capabilities.map(descriptor);
      if (!this._connected || this._request !== request ||
          this.getAttribute('application-id')?.trim() !== applicationId) return;
      this._capabilities = capabilities;
      this._renderCapabilities();
      this._setStatus(capabilities.length
        ? `${capabilities.length} current application capabilities available.`
        : 'No application capabilities are currently available.');
    } catch (error) {
      if (error.name === 'AbortError' || request.signal.aborted || !this._connected || this._request !== request) return;
      this._capabilities = [];
      this._setStatus(error.message || 'Application capability discovery is unavailable.', true);
    } finally {
      if (this._request === request) this._request = null;
    }
  }

  _renderCapabilities() {
    const document = this.ownerDocument;
    this._cards.replaceChildren();
    for (const item of this._capabilities) {
      const card = element(document, 'article');
      const open = element(document, 'button', item.mode === 'read' ? 'Open read' : 'Open operation');
      open.type = 'button';
      open.dataset.capabilityId = item.id;
      open.addEventListener('click', () => this._open(item));
      card.append(element(document, 'h3', item.id), element(document, 'p', item.description),
        element(document, 'p', `${item.mode} · v${item.version} · ${item.owner}`, 'meta'), open);
      this._cards.append(card);
    }
  }

  _open(item) {
    const document = this.ownerDocument;
    this._invocationRequest?.abort();
    this._invocationRequest = null;
    this._runnerCapabilityId = item.id;
    this._runner.hidden = false;
    this._runner.replaceChildren();
    const close = element(document, 'button', 'Close');
    close.type = 'button';
    close.addEventListener('click', () => { this._runner.hidden = true; this._runner.replaceChildren(); });
    const schema = document.createElement('details');
    schema.append(element(document, 'summary', 'Exact input and output schemas'),
      element(document, 'pre', `${item.inputSchemaJson}\n\n${item.outputSchemaJson}`));
    const form = document.createElement('form');
    const input = document.createElement('textarea');
    input.value = '{}';
    input.maxLength = MAXIMUM_JSON_BYTES;
    input.setAttribute('aria-label', `${item.id} input JSON`);
    const inputLabel = element(document, 'label', 'Input JSON');
    inputLabel.append(input);
    let idempotency = null;
    if (item.requiresIdempotencyKey) {
      idempotency = document.createElement('input');
      idempotency.value = commandKey();
      idempotency.maxLength = MAXIMUM_IDEMPOTENCY_KEY;
      idempotency.setAttribute('aria-label', `${item.id} idempotency key`);
      const keyLabel = element(document, 'label', 'Stable idempotency key');
      keyLabel.append(idempotency);
      form.append(keyLabel);
    }
    const run = element(document, 'button', item.mode === 'read' ? 'Read current result' : 'Run operation');
    run.type = 'submit';
    const status = element(document, 'p', 'Enter one JSON object.', 'status');
    status.setAttribute('aria-live', 'polite');
    const result = element(document, 'div', undefined, 'result');
    input.addEventListener('input', () => {
      if (idempotency) idempotency.value = commandKey();
      result.replaceChildren();
      status.textContent = 'Input changed; a new command identity has been prepared.';
    });
    form.append(inputLabel, run, status, result);
    form.addEventListener('submit', event => {
      event.preventDefault();
      void this._invoke(item, input, idempotency, run, status, result);
    });
    this._runner.append(element(document, 'h3', item.id), close, schema, form);
  }

  async _invoke(item, inputControl, idempotencyControl, run, status, result) {
    if (run.disabled) return;
    let input;
    try {
      input = JSON.parse(inputControl.value);
      if (!object(input) || new TextEncoder().encode(inputControl.value).length > MAXIMUM_JSON_BYTES)
        throw new Error('invalid');
    } catch {
      status.textContent = 'Input must be one bounded JSON object.';
      status.classList.add('error');
      return;
    }
    const applicationId = this.getAttribute('application-id')?.trim() || '';
    const body = {input};
    if (item.requiresIdempotencyKey) {
      const key = idempotencyControl?.value.trim() || '';
      if (!text(key, MAXIMUM_IDEMPOTENCY_KEY) || /[\u0000-\u001f\u007f]/.test(key)) {
        status.textContent = 'A bounded stable idempotency key is required.';
        status.classList.add('error');
        return;
      }
      body.idempotencyKey = key;
    }
    run.disabled = true;
    inputControl.disabled = true;
    if (idempotencyControl) idempotencyControl.disabled = true;
    status.classList.remove('error');
    status.textContent = item.mode === 'read' ? 'Reading current owner result…' : 'Sending this command once…';
    const request = new AbortController();
    this._invocationRequest?.abort();
    this._invocationRequest = request;
    try {
      const response = await fetch(`/api/applications/${encodeURIComponent(applicationId)}/authoring/capabilities/${encodeURIComponent(item.id)}`,
        {method: 'POST', credentials: 'same-origin', headers: {accept: 'application/json', 'content-type': 'application/json'},
          body: JSON.stringify(body), signal: request.signal});
      const value = await response.json().catch(() => null);
      if (!this._connected || this._invocationRequest !== request || request.signal.aborted ||
          this.getAttribute('application-id')?.trim() !== applicationId || this._runnerCapabilityId !== item.id) return;
      if (!response.ok) {
        renderInvocation(result, errorResult(value?.code, value?.message, response.status >= 400 && response.status < 500));
        status.textContent = value?.recovery || value?.message || 'The application capability was rejected.';
        status.classList.add('error');
        return;
      }
      if (!object(value) || value.ok !== true || value.capabilityId !== item.id || value.mode !== item.mode || !object(value.data)) {
        renderInvocation(result, unavailableResult('APPLICATION_CAPABILITY_RESPONSE_INVALID',
          'The application capability returned an invalid shared result.'));
        status.textContent = 'The result could not be verified.';
        status.classList.add('error');
        return;
      }
      const view = renderInvocation(result, value.data);
      status.textContent = view.message;
      status.classList.toggle('error', ['failed', 'cancelled', 'unavailable'].includes(view.tag));
    } catch (error) {
      if (error.name === 'AbortError' || request.signal.aborted || this._invocationRequest !== request) return;
      renderInvocation(result, unavailableResult('APPLICATION_CAPABILITY_RESULT_UNRESOLVED',
        'The result is unknown. Keep the displayed idempotency key and reconcile before retrying.'));
      status.textContent = 'No automatic retry was sent.';
      status.classList.add('error');
    } finally {
      if (this._invocationRequest === request) {
        this._invocationRequest = null;
        run.disabled = false;
        inputControl.disabled = false;
        if (idempotencyControl) idempotencyControl.disabled = false;
      }
    }
  }

  _setStatus(message, error = false) {
    this._status.textContent = message;
    this._status.classList.toggle('error', error);
  }
}

if (!customElements.get('application-capability-center'))
  customElements.define('application-capability-center', ApplicationCapabilityCenter);
