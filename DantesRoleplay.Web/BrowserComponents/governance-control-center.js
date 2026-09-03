const CAPABILITY_INDEX = '/api/control/system/capabilities';

function object(value) {
  return value !== null && !Array.isArray(value) && typeof value === 'object';
}

function text(value, maximum = 500) {
  return typeof value === 'string' && value.length > 0 && value.length <= maximum;
}

function capability(value) {
  if (!object(value) || !text(value.id, 120) || !Number.isInteger(value.version) || value.version < 1 ||
      !text(value.fingerprint, 64) || !text(value.owner, 100) || !text(value.description) ||
      !['read', 'write'].includes(value.mode) || !object(value.inputSchema) ||
      !object(value.outputSchema) || !object(value.contract)) {
    throw new Error('The capability index returned an invalid contract.');
  }
  return value;
}

function pre(value) {
  const node = document.createElement('pre');
  node.textContent = JSON.stringify(value, null, 2);
  return node;
}

function details(label, value, open = false) {
  const node = document.createElement('details');
  node.open = open;
  const summary = document.createElement('summary');
  summary.textContent = label;
  node.append(summary, pre(value));
  return node;
}

class GovernanceControlCenter extends HTMLElement {
  constructor() {
    super();
    this._connected = false;
    this._request = null;
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
    if (this._request) this._request.abort();
    this._request = null;
  }

  _renderShell() {
    const style = document.createElement('style');
    style.textContent = `
      :host { color: inherit; display: grid; font: inherit; gap: .85rem; }
      header, article, [part='runner'] { background: var(--governance-panel-background, #121622); border: 1px solid var(--governance-border-color, #303a53); border-radius: .6rem; padding: .8rem; }
      header { display: grid; gap: .6rem; }
      h2, h3, p { margin: 0; }
      h2 { font-size: 1rem; }
      h3 { font-size: .95rem; overflow-wrap: anywhere; }
      [part='intro'], [part='status'], [part='meta'] { color: var(--governance-muted-color, #aeb9d1); }
      [part='status'] { min-height: 1.35rem; }
      input { box-sizing: border-box; width: 100%; border: 1px solid var(--governance-border-color, #4b5877); border-radius: .45rem; background: var(--governance-input-background, #0e121c); color: inherit; font: inherit; padding: .55rem; }
      [part='groups'] { display: grid; gap: .85rem; }
      section { display: grid; gap: .55rem; }
      [part='cards'] { display: grid; gap: .65rem; grid-template-columns: repeat(auto-fit, minmax(min(100%, 20rem), 1fr)); }
      article { display: grid; align-content: start; gap: .55rem; min-width: 0; }
      [part='mode'] { border: 1px solid var(--governance-border-color, #4b5877); border-radius: 999px; font-size: .75rem; justify-self: start; padding: .15rem .5rem; }
      summary { cursor: pointer; }
      pre { background: var(--governance-input-background, #0e121c); border-radius: .4rem; font-size: .75rem; max-height: 18rem; overflow: auto; padding: .55rem; white-space: pre-wrap; overflow-wrap: anywhere; }
      button { justify-self: start; border: 1px solid var(--governance-border-color, #4b5877); border-radius: .45rem; background: transparent; color: inherit; cursor: pointer; font: inherit; padding: .45rem .65rem; }
      button:hover { background: rgba(185, 206, 255, .12); }
      [part='runner'][hidden], [hidden] { display: none !important; }
      system-form { --system-form-border-color: var(--governance-border-color, #4b5877); --system-form-input-background: var(--governance-input-background, #0e121c); }
    `;
    const header = document.createElement('header');
    const title = document.createElement('h2');
    title.textContent = 'Human control center';
    const intro = document.createElement('p');
    intro.setAttribute('part', 'intro');
    intro.textContent = 'Explore and run the same live, authorized capability contracts available to AI clients. Forms, evidence, and operations are generated from those contracts.';
    this._search = document.createElement('input');
    this._search.type = 'search';
    this._search.placeholder = 'Filter by capability, owner, mode, or description';
    this._search.setAttribute('aria-label', 'Filter system capabilities');
    this._search.addEventListener('input', () => this._renderCapabilities());
    this._status = document.createElement('p');
    this._status.setAttribute('part', 'status');
    this._status.setAttribute('aria-live', 'polite');
    header.append(title, intro, this._search, this._status);
    this._groups = document.createElement('div');
    this._groups.setAttribute('part', 'groups');
    this._runner = document.createElement('section');
    this._runner.setAttribute('part', 'runner');
    this._runner.hidden = true;
    this.shadowRoot.append(style, header, this._runner, this._groups);
  }

  async _load() {
    const request = new AbortController();
    this._request = request;
    this._status.textContent = 'Loading current authorized capabilities…';
    try {
      const response = await fetch(CAPABILITY_INDEX, {
        cache: 'no-store', credentials: 'same-origin', signal: request.signal
      });
      if (!response.ok) throw new Error(response.status === 403
        ? 'This browser is not authorized to inspect system capabilities.'
        : 'System capability discovery is unavailable.');
      const body = await response.json();
      if (!Array.isArray(body) || body.length > 200) throw new Error('The capability index is malformed.');
      this._capabilities = body.map(capability);
      this._renderCapabilities();
    } catch (error) {
      if (error.name === 'AbortError') return;
      this._status.textContent = error.message || 'System capability discovery is unavailable.';
      this._status.setAttribute('role', 'alert');
    } finally {
      if (this._request === request) this._request = null;
    }
  }

  _renderCapabilities() {
    const query = this._search.value.trim().toLocaleLowerCase();
    const visible = this._capabilities.filter(value => !query ||
      [value.id, value.owner, value.mode, value.description].some(item =>
        item.toLocaleLowerCase().includes(query)));
    this._groups.replaceChildren();
    const owners = new Map();
    for (const item of visible) {
      if (!owners.has(item.owner)) owners.set(item.owner, []);
      owners.get(item.owner).push(item);
    }
    for (const [owner, items] of [...owners].sort(([left], [right]) => left.localeCompare(right))) {
      const section = document.createElement('section');
      const heading = document.createElement('h2');
      heading.textContent = owner;
      const cards = document.createElement('div');
      cards.setAttribute('part', 'cards');
      for (const item of items) cards.append(this._card(item));
      section.append(heading, cards);
      this._groups.append(section);
    }
    this._status.removeAttribute('role');
    this._status.textContent = `${visible.length} of ${this._capabilities.length} current authorized capabilities shown.`;
  }

  _card(item) {
    const card = document.createElement('article');
    const heading = document.createElement('h3');
    heading.textContent = item.id;
    const mode = document.createElement('span');
    mode.setAttribute('part', 'mode');
    mode.textContent = `${item.mode} · v${item.version}`;
    const description = document.createElement('p');
    description.textContent = item.description;
    const meta = document.createElement('p');
    meta.setAttribute('part', 'meta');
    meta.textContent = `Fingerprint ${item.fingerprint} · confirmation ${item.requiresConfirmation ? 'required' : 'not required'}`;
    const examples = item.contract.examples || item.contract.Examples || [];
    const errors = item.contract.errors || item.contract.Errors || [];
    const recovery = item.contract.recoveryActions || item.contract.RecoveryActions || [];
    const use = document.createElement('button');
    use.type = 'button';
    use.textContent = item.mode === 'read' ? 'Open evidence view' : 'Review operation';
    use.addEventListener('click', () => this._open(item));
    card.append(heading, mode, description, meta,
      details('Input schema', item.inputSchema),
      details('Output schema', item.outputSchema),
      details('Examples', examples),
      details('Stable errors and recovery', {errors, recoveryActions: recovery}), use);
    return card;
  }

  _open(item) {
    this._runner.hidden = false;
    this._runner.replaceChildren();
    const heading = document.createElement('h2');
    heading.textContent = item.id;
    const close = document.createElement('button');
    close.type = 'button';
    close.textContent = 'Close';
    close.addEventListener('click', () => {
      this._runner.hidden = true;
      this._runner.replaceChildren();
    });
    const form = document.createElement('system-form');
    form.setAttribute('capability-id', item.id);
    this._runner.append(heading, close, form);
    this._runner.scrollIntoView({behavior: 'smooth', block: 'start'});
  }
}

if (!customElements.get('governance-control-center'))
  customElements.define('governance-control-center', GovernanceControlCenter);
