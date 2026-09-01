import {
  SystemClientError,
  SystemRequestScope,
  normalizePublishedApplication,
  systemWebClient,
  validSystemIdentifier
} from '/components/system-client.js';

let applicationNavigationSequence = 0;

function emit(element, name, detail, cancelable = false) {
  return element.dispatchEvent(new CustomEvent(name, {
    detail,
    bubbles: true,
    composed: true,
    cancelable
  }));
}

function routePath(path) {
  const value = path.replace(/\/+$/, '') || '/';
  return value.endsWith('/index.html') ? value.slice(0, -'/index.html'.length) || '/' : value;
}

function publicationStateMessage(status) {
  switch (status) {
    case 'missing-publication': return 'Application unavailable.';
    case 'missing-index-page': return 'No landing page installed.';
    case 'index-page-hidden': return 'Landing page hidden.';
    case 'index-page-disabled': return 'Landing page disabled.';
    case 'index-content-missing': return 'Referenced content missing.';
    case 'invalid': return 'Publication configuration invalid.';
    default: return 'No landing page installed.';
  }
}

export class SystemProgress extends HTMLElement {
  static get observedAttributes() { return ['phase', 'message']; }

  constructor() {
    super();
    this.attachShadow({mode: 'open'});
    const style = document.createElement('style');
    style.textContent = `:host{display:block;color:var(--system-muted-color,inherit);font:inherit}
      [part='progress']{align-items:center;display:flex;gap:.5rem;margin:0}
      [part='indicator']{animation:system-progress-spin .8s linear infinite;border:2px solid currentColor;border-right-color:transparent;border-radius:50%;height:.8rem;width:.8rem}
      :host([phase='ready']) [part='indicator'],:host([phase='idle']) [part='indicator']{display:none}
      @media (prefers-reduced-motion:reduce){[part='indicator']{animation:none}}
      @keyframes system-progress-spin{to{transform:rotate(360deg)}}`;
    this._row = document.createElement('p');
    this._row.setAttribute('part', 'progress');
    this._row.setAttribute('role', 'status');
    this._row.setAttribute('aria-live', 'polite');
    const indicator = document.createElement('span');
    indicator.setAttribute('part', 'indicator');
    indicator.setAttribute('aria-hidden', 'true');
    this._message = document.createElement('span');
    this._message.setAttribute('part', 'message');
    this._row.append(indicator, this._message);
    this.shadowRoot.append(style, this._row);
    this._render();
  }

  attributeChangedCallback() { this._render(); }

  set progress(value) {
    this.setAttribute('phase', value?.phase ?? 'loading');
    this.setAttribute('message', value?.message ?? 'Working…');
  }

  _render() {
    const phase = this.getAttribute('phase') || 'loading';
    const message = this.getAttribute('message') || (phase === 'ready' ? 'Ready.' : 'Loading…');
    this._message.textContent = message;
    this._row.setAttribute('aria-busy', phase === 'ready' || phase === 'idle' ? 'false' : 'true');
  }
}

export class SystemErrorElement extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({mode: 'open'});
    const style = document.createElement('style');
    style.textContent = `:host{display:block;color:var(--system-error-color,#8a1c1c);font:inherit}
      [part='error']{border:1px solid currentColor;border-radius:.55rem;padding:.75rem}
      p{margin:.2rem 0}[part='code']{font-family:ui-monospace,monospace;font-size:.78em}
      button{font:inherit;margin-top:.45rem}`;
    this._box = document.createElement('section');
    this._box.setAttribute('part', 'error');
    this._box.setAttribute('role', 'alert');
    this._message = document.createElement('p');
    this._message.setAttribute('part', 'message');
    this._code = document.createElement('p');
    this._code.setAttribute('part', 'code');
    this._retry = document.createElement('button');
    this._retry.type = 'button';
    this._retry.textContent = 'Try again';
    this._retry.addEventListener('click', () => emit(this, 'system-retry', {code: this._value.code}));
    this._box.append(this._message, this._code, this._retry);
    this.shadowRoot.append(style, this._box);
    this.error = new SystemClientError('SYSTEM_REQUEST_FAILED', 'The system request failed.');
  }

  set error(value) {
    this._value = value instanceof SystemClientError ? value : new SystemClientError(
      value?.code, value?.message ?? 'The system request failed.', {retryable: value?.retryable === true});
    this._message.textContent = this._value.message;
    this._code.textContent = this._value.code;
    this._retry.hidden = !this._value.retryable;
  }

  get error() { return this._value; }
}

export class SystemEmptyState extends HTMLElement {
  static get observedAttributes() { return ['heading', 'message', 'code']; }

  constructor() {
    super();
    this.attachShadow({mode: 'open'});
    const style = document.createElement('style');
    style.textContent = `:host{display:block;color:var(--system-muted-color,inherit);font:inherit}
      [part='empty']{border:1px dashed currentColor;border-radius:.55rem;padding:1rem}
      h2,p{margin:.2rem 0}[part='code']{font-family:ui-monospace,monospace;font-size:.78em}`;
    this._box = document.createElement('section');
    this._box.setAttribute('part', 'empty');
    this._box.setAttribute('role', 'status');
    this._heading = document.createElement('h2');
    this._message = document.createElement('p');
    this._code = document.createElement('p');
    this._code.setAttribute('part', 'code');
    this._box.append(this._heading, this._message, this._code);
    this.shadowRoot.append(style, this._box);
    this._render();
  }

  attributeChangedCallback() { this._render(); }

  set state(value) {
    this.setAttribute('heading', value?.heading ?? 'Nothing to show');
    this.setAttribute('message', value?.message ?? 'This part of the system is not configured yet.');
    if (value?.code) this.setAttribute('code', value.code); else this.removeAttribute('code');
  }

  _render() {
    this._heading.textContent = this.getAttribute('heading') || 'Nothing to show';
    this._message.textContent = this.getAttribute('message') || 'This part of the system is not configured yet.';
    const code = this.getAttribute('code');
    this._code.textContent = code || '';
    this._code.hidden = !code;
  }
}

export class SystemDataView extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({mode: 'open'});
    const style = document.createElement('style');
    style.textContent = `:host{display:block;font:inherit}pre{background:var(--system-data-background,rgba(127,127,127,.08));border-radius:.45rem;margin:0;max-height:32rem;overflow:auto;padding:.75rem;white-space:pre-wrap;word-break:break-word}`;
    this._pre = document.createElement('pre');
    this._pre.setAttribute('part', 'data');
    this.shadowRoot.append(style, this._pre);
    this.value = null;
  }

  set value(value) {
    let text;
    try { text = JSON.stringify(value, null, 2); }
    catch { text = 'The structured response cannot be displayed.'; }
    if (typeof text !== 'string') text = String(value ?? '');
    this._pre.textContent = text.length <= 100000 ? text : `${text.slice(0, 100000)}\n… response truncated`;
  }
}

export class ApplicationNavigation extends HTMLElement {
  static get observedAttributes() { return ['current-path', 'selected']; }

  constructor() {
    super();
    this._application = null;
    this._open = false;
    this._outsidePointer = event => {
      if (this._open && !event.composedPath().includes(this)) this._closeMenu();
    };
    this.attachShadow({mode: 'open'});
  }

  connectedCallback() { document.addEventListener('pointerdown', this._outsidePointer); this._render(); }
  disconnectedCallback() { document.removeEventListener('pointerdown', this._outsidePointer); }
  attributeChangedCallback() { if (this.isConnected) this._updateCurrent(); }

  set application(value) {
    const normalized = normalizePublishedApplication(value);
    if (!normalized) throw new SystemClientError(
      'WEB_PUBLICATION_RESPONSE_INVALID', 'The application navigation input is invalid.');
    this._application = normalized;
    if (this.isConnected) this._render();
  }

  get application() { return this._application; }

  _render() {
    this._closeMenu();
    this.shadowRoot.replaceChildren();
    if (!this._application) return;
    const application = this._application;
    const style = document.createElement('style');
    style.textContent = `:host{display:inline-flex;font:inherit;position:relative}
      [part='application']{align-items:center;display:inline-flex;gap:.15rem;position:relative}
      a,button{border:1px solid transparent;border-radius:var(--system-navigation-radius,999px);color:inherit;font:inherit;padding:var(--system-navigation-padding,.5rem .75rem);text-decoration:none}
      a:hover,a:focus-visible,button:hover:not(:disabled),button:focus-visible{border-color:currentColor;outline:2px solid currentColor;outline-offset:2px}
      [aria-current='page'],[data-current='true']{background:var(--system-navigation-current-background,rgba(128,170,128,.16));border-color:currentColor}
      button:disabled{cursor:not-allowed;opacity:.58}[part='menu-trigger']{padding-inline:.55rem}
      [part='menu']{background:var(--system-navigation-menu-background,Canvas);border:1px solid currentColor;border-radius:.6rem;box-shadow:0 .5rem 1.5rem rgba(0,0,0,.2);display:grid;gap:.15rem;left:0;min-width:12rem;padding:.3rem;position:absolute;top:calc(100% + .25rem);z-index:100}
      [part='menu'] a{border-radius:.4rem;white-space:nowrap}[part='state']{font-size:.72rem;max-width:12rem}[hidden]{display:none!important}`;
    const item = document.createElement('span');
    item.setAttribute('part', 'application');
    item.dataset.applicationId = application.applicationId;
    let primary;
    if (application.isClickable) {
      primary = this._link(application.indexPage.url, application.displayName, application.indexPage);
      primary.setAttribute('part', 'application-link');
    } else {
      primary = document.createElement('button');
      primary.type = 'button';
      primary.disabled = true;
      primary.textContent = application.displayName;
      primary.setAttribute('part', 'application-link');
      primary.setAttribute('aria-disabled', 'true');
      primary.title = publicationStateMessage(application.publicationStatus);
    }
    item.append(primary);
    if (application.isPublishable && application.pages.length > 0) {
      const sequence = ++applicationNavigationSequence;
      const trigger = document.createElement('button');
      trigger.type = 'button';
      trigger.id = `application-navigation-trigger-${sequence}`;
      trigger.textContent = 'More';
      trigger.setAttribute('part', 'menu-trigger');
      trigger.setAttribute('aria-label', `${application.displayName} pages`);
      trigger.setAttribute('aria-haspopup', 'menu');
      trigger.setAttribute('aria-expanded', 'false');
      const menu = document.createElement('span');
      menu.id = `application-navigation-menu-${sequence}`;
      menu.hidden = true;
      menu.setAttribute('part', 'menu');
      menu.setAttribute('role', 'menu');
      menu.setAttribute('aria-labelledby', trigger.id);
      trigger.setAttribute('aria-controls', menu.id);
      for (const page of application.pages) {
        const link = this._link(page.url, page.navigationLabel, page);
        link.setAttribute('part', 'page-link');
        link.setAttribute('role', 'menuitem');
        link.title = page.title;
        menu.append(link);
      }
      trigger.addEventListener('click', () => this._setMenu(menu.hidden));
      trigger.addEventListener('keydown', event => {
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
          this._setMenu(true, event.key === 'ArrowUp' ? 'last' : 'first');
          event.preventDefault();
        }
      });
      menu.addEventListener('keydown', event => this._menuKeydown(event));
      item.addEventListener('focusout', () => queueMicrotask(() => {
        if (this._open && !this.shadowRoot.activeElement) this._closeMenu();
      }));
      this._trigger = trigger;
      this._menu = menu;
      item.append(trigger, menu);
    }
    if (!application.isClickable) {
      const state = document.createElement('span');
      state.setAttribute('part', 'state');
      state.textContent = publicationStateMessage(application.publicationStatus);
      item.append(state);
    }
    this._item = item;
    this.shadowRoot.append(style, item);
    this._updateCurrent();
  }

  _link(href, label, page) {
    const link = document.createElement('a');
    link.href = href;
    link.textContent = label;
    link.dataset.applicationId = this._application.applicationId;
    link.dataset.pageSlug = page.slug;
    link.dataset.entityId = page.entityId;
    link.addEventListener('click', () => {
      this._closeMenu();
      emit(this, 'system-navigate', {
        applicationId: this._application.applicationId,
        pageSlug: page.slug,
        entityId: page.entityId,
        url: page.url
      });
    });
    return link;
  }

  _setMenu(open, focusTarget = null) {
    if (!this._menu) return;
    this._open = open;
    this._menu.hidden = !open;
    this._trigger.setAttribute('aria-expanded', String(open));
    if (open && focusTarget) {
      const links = this._menu.querySelectorAll('[role="menuitem"]');
      const target = focusTarget === 'last' ? links[links.length - 1] : links[0];
      target?.focus();
    }
  }

  _closeMenu() { if (this._menu) this._setMenu(false); else this._open = false; }

  _menuKeydown(event) {
    const links = Array.from(this._menu.querySelectorAll('[role="menuitem"]'));
    const current = links.indexOf(this.shadowRoot.activeElement);
    let next = null;
    if (event.key === 'ArrowDown') next = links[(current + 1) % links.length];
    else if (event.key === 'ArrowUp') next = links[(current - 1 + links.length) % links.length];
    else if (event.key === 'Home') next = links[0];
    else if (event.key === 'End') next = links[links.length - 1];
    else if (event.key === 'Escape') { this._closeMenu(); this._trigger.focus(); event.preventDefault(); return; }
    else if (event.key === 'Tab') { this._closeMenu(); return; }
    if (next) { next.focus(); event.preventDefault(); }
  }

  _updateCurrent() {
    if (!this._item) return;
    const path = routePath(this.getAttribute('current-path') || window.location.pathname);
    const selected = this.getAttribute('selected');
    this._item.dataset.current = String(selected === this._application.applicationId);
    for (const link of this.shadowRoot.querySelectorAll('a')) {
      if (routePath(new URL(link.href).pathname) === path) link.setAttribute('aria-current', 'page');
      else link.removeAttribute('aria-current');
    }
  }
}

export class ApplicationPageHost extends HTMLElement {
  static get observedAttributes() { return ['application-id', 'page-slug']; }

  constructor() {
    super();
    this._connected = false;
    this._scope = new SystemRequestScope();
    this._client = systemWebClient;
    this.attachShadow({mode: 'open'});
  }

  connectedCallback() { this._connected = true; this._load(); }
  disconnectedCallback() { this._connected = false; this._scope.cancel(); }
  attributeChangedCallback() { if (this._connected) this._load(); }

  set client(value) {
    if (!value || typeof value.loadPublishedPage !== 'function') throw new TypeError(
      'application-page-host requires a publication client.');
    this._client = value;
    if (this._connected) this._load();
  }

  get client() { return this._client; }

  async _load() {
    const applicationId = this.getAttribute('application-id');
    const slug = this.getAttribute('page-slug');
    if (!validSystemIdentifier(applicationId) || !validSystemIdentifier(slug)) {
      this._showEmpty('WEB_PAGE_IDENTITY_REQUIRED', 'Page not selected',
        'Choose an application and page before loading published content.');
      return;
    }
    const request = this._scope.begin();
    this._showProgress('Resolving published page…');
    emit(this, 'system-progress', {phase: 'loading', applicationId, pageSlug: slug});
    try {
      const result = await this._client.loadPublishedPage(applicationId, slug, {signal: request.signal});
      if (!request.isCurrent() || !this._connected) return;
      const path = routePath(window.location.pathname);
      if (path === routePath(result.page.url)) {
        this._showReady(result);
        return;
      }
      const allowed = emit(this, 'system-navigate', {
        applicationId,
        pageSlug: slug,
        entityId: result.page.entityId,
        url: result.page.url,
        resolutionFingerprint: result.resolutionFingerprint
      }, true);
      if (allowed && !this.hasAttribute('manual')) window.location.assign(result.page.url);
      else this._showReady(result);
    } catch (error) {
      if (error?.name === 'AbortError' || !request.isCurrent()) return;
      const clientError = error instanceof SystemClientError ? error : new SystemClientError(
        'WEB_PAGE_LOAD_FAILED', 'The published page could not be loaded.');
      if (clientError.status === 404) this._showEmpty(clientError.code, 'Page unavailable', clientError.message);
      else this._showError(clientError);
      emit(this, 'system-error', {code: clientError.code, message: clientError.message, recoverable: clientError.retryable});
    }
  }

  _showProgress(message) {
    const progress = document.createElement('system-progress');
    progress.progress = {phase: 'loading', message};
    this.shadowRoot.replaceChildren(progress);
    this.dataset.state = 'loading';
  }

  _showEmpty(code, heading, message) {
    this._scope.cancel();
    const empty = document.createElement('system-empty-state');
    empty.state = {code, heading, message};
    this.shadowRoot.replaceChildren(empty);
    this.dataset.state = 'empty';
  }

  _showError(error) {
    const view = document.createElement('system-error');
    view.error = error;
    view.addEventListener('system-retry', () => this._load());
    this.shadowRoot.replaceChildren(view);
    this.dataset.state = 'error';
  }

  _showReady(result) {
    const container = document.createElement('section');
    container.setAttribute('part', 'ready');
    const slot = document.createElement('slot');
    const link = document.createElement('a');
    link.href = result.page.url;
    link.textContent = `Open ${result.page.title}`;
    link.setAttribute('part', 'page-link');
    link.addEventListener('click', () => emit(this, 'system-navigate', {
      applicationId: result.application.applicationId,
      pageSlug: result.page.slug,
      entityId: result.page.entityId,
      url: result.page.url,
      resolutionFingerprint: result.resolutionFingerprint
    }));
    container.append(slot, link);
    this.shadowRoot.replaceChildren(container);
    this.dataset.state = 'ready';
    emit(this, 'system-progress', {phase: 'ready', applicationId: result.application.applicationId,
      pageSlug: result.page.slug, resolutionFingerprint: result.resolutionFingerprint});
  }
}

if (!customElements.get('system-progress')) customElements.define('system-progress', SystemProgress);
if (!customElements.get('system-error')) customElements.define('system-error', SystemErrorElement);
if (!customElements.get('system-empty-state')) customElements.define('system-empty-state', SystemEmptyState);
if (!customElements.get('system-data-view')) customElements.define('system-data-view', SystemDataView);
if (!customElements.get('application-navigation')) customElements.define('application-navigation', ApplicationNavigation);
if (!customElements.get('application-page-host')) customElements.define('application-page-host', ApplicationPageHost);
