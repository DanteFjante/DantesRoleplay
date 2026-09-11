import {systemWebClient} from '/components/system-client.js';
import '/components/system-publication.js';

const encode = encodeURIComponent;

function validIdentity(value, maximum = 200) {
  return typeof value === 'string' && value.length > 0 && value.length <= maximum &&
    value.trim() === value && !/[\u0000-\u001f\u007f/\\]/.test(value);
}

function displayText(value, fallback, maximum = 400) {
  return typeof value === 'string' && value.length <= maximum ? value : fallback;
}

function normalizeAdminPage(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || !validIdentity(value.entityId)) return null;
  const unavailableFields = [];
  const title = displayText(value.title, value.entityId, 200);
  const navigationLabel = displayText(value.navigationLabel, title, 100);
  const slug = validIdentity(value.slug, 120) ? value.slug : '';
  const order = Number.isInteger(value.order) && value.order >= -1000000 && value.order <= 1000000 ? value.order : 0;
  const visibility = value.visibility === 'public' || value.visibility === 'hidden' ? value.visibility : 'public';
  for (const [key, valid] of [['title', typeof value.title === 'string' && value.title.length > 0 && value.title.length <= 200],
    ['navigationLabel', typeof value.navigationLabel === 'string' && value.navigationLabel.length > 0 && value.navigationLabel.length <= 100],
    ['slug', slug !== ''], ['order', order === value.order], ['visibility', value.visibility === 'public' || value.visibility === 'hidden']])
    if (!valid) unavailableFields.push(key);
  const normalized = {...value, title, navigationLabel, slug, order, visibility};
  normalized.metadataReady = unavailableFields.length === 0 && Number.isInteger(value.pageComponentRevision) && value.pageComponentRevision > 0;
  normalized.entityReady = Number.isInteger(value.entityRevision) && value.entityRevision > 0;
  normalized.contentReady = value.content === null || (value.content && typeof value.content === 'object' &&
    Number.isInteger(value.content.latestRevision) && value.content.latestRevision > 0 &&
    Number.isInteger(value.content.activeRevision) && value.content.activeRevision > 0);
  if (unavailableFields.length) normalized.unavailableFields = unavailableFields;
  return normalized;
}

class PageAdministration extends HTMLElement {
  constructor() {
    super();
    this._client = systemWebClient;
    this._applications = [];
    this._pages = [];
    this._application = null;
    this._page = null;
    this._connected = false;
    this.attachShadow({mode: 'open'});
  }

  connectedCallback() {
    if (this._connected) return;
    this._connected = true;
    this._shell();
    this._load();
  }

  disconnectedCallback() { this._connected = false; }

  _shell() {
    this.shadowRoot.innerHTML = `
      <style>
        :host { display:block; color:inherit; font:inherit; }
        header, .row, .actions { display:flex; flex-wrap:wrap; gap:.65rem; align-items:end; }
        header { justify-content:space-between; align-items:center; }
        section { border:1px solid color-mix(in srgb, currentColor 22%, transparent); border-radius:.8rem; margin:1rem 0; padding:1rem; }
        label { display:grid; gap:.25rem; min-width:10rem; }
        input, select, textarea, button { box-sizing:border-box; font:inherit; }
        input, select, textarea { border:1px solid color-mix(in srgb, currentColor 32%, transparent); border-radius:.45rem; padding:.55rem; }
        textarea { min-height:16rem; resize:vertical; width:100%; }
        button { border:1px solid currentColor; border-radius:.5rem; cursor:pointer; padding:.55rem .8rem; }
        button:disabled { cursor:not-allowed; opacity:.55; }
        button:focus-visible, input:focus-visible, select:focus-visible, textarea:focus-visible { outline:3px solid #76a8ff; outline-offset:2px; }
        [data-pages] { display:grid; gap:.35rem; list-style:none; padding:0; }
        [data-pages] button { text-align:left; width:100%; }
        [aria-current='true'] { box-shadow:0 0 0 2px #76a8ff; }
        .muted { opacity:.75; }
        .danger { color:#a72b2b; }
        [hidden] { display:none !important; }
      </style>
      <header><div><h2>Application pages</h2><p class="muted">Page identity, navigation, content history, and publishing are managed together.</p></div><button data-refresh>Refresh</button></header>
      <system-progress data-progress label="Loading page administration"></system-progress>
      <system-error data-error hidden></system-error>
      <section data-main hidden>
        <label>Application<select data-application></select></label>
        <div class="row actions"><button data-new>Create page</button><a data-open hidden target="_blank" rel="noopener">Open published page</a></div>
        <ul data-pages></ul>
      </section>
      <section data-editor hidden></section>
      <section data-migration><h3>Legacy page review</h3><p class="muted">Every old content identity remains preserved until an operator classifies it.</p><div data-migration-body></div></section>`;
    this.$ = selector => this.shadowRoot.querySelector(selector);
    this.$('[data-refresh]').addEventListener('click', () => this._load());
    this.$('[data-application]').addEventListener('change', event => this._selectApplication(event.target.value));
    this.$('[data-new]').addEventListener('click', () => this._renderCreate());
  }

  async _load() {
    this._state('loading');
    try {
      const discovery = await this._client.discoverAllApplications();
      this._applications = [...discovery.applications];
      const select = this.$('[data-application]');
      select.replaceChildren(...this._applications.map(application =>
        new Option(application.displayName, application.applicationId)));
      this.$('[data-main]').hidden = this._applications.length === 0;
      if (this._applications.length === 0) {
        this._state('empty', 'No applications are registered.');
      } else {
        await this._selectApplication(select.value || this._applications[0].applicationId);
        this._state('ready');
      }
      await this._loadMigration();
    } catch (error) { this._state('error', error.message); }
  }

  async _selectApplication(applicationId) {
    this._application = this._applications.find(value => value.applicationId === applicationId) ?? null;
    this._page = null;
    this.$('[data-editor]').hidden = true;
    if (!this._application) return;
    this.$('[data-application]').value = applicationId;
    const pages = await this._request(`/api/control/web/applications/${encode(applicationId)}/pages`);
    if (!Array.isArray(pages) || pages.length > 1000) throw new Error('The application page list is unavailable.');
    this._pages = pages.map(normalizeAdminPage).filter(Boolean);
    this._pagesPartial = this._pages.length !== pages.length;
    this._renderPages();
  }

  _renderPages() {
    const list = this.$('[data-pages]');
    list.replaceChildren();
    if (this._pagesPartial) {
      const warning = document.createElement('li');
      warning.className = 'danger';
      warning.textContent = 'Some page identities are unavailable for this read.';
      list.append(warning);
    }
    if (this._pages.length === 0) {
      const empty = document.createElement('li');
      empty.textContent = 'This application has no page identities yet.';
      list.append(empty);
      return;
    }
    for (const page of this._pages) {
      const item = document.createElement('li');
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = `${page.navigationLabel || page.entityId}${page.isIndexPage ? ' · landing page' : ''}${page.enabled === false ? ' · disabled' : ''}${page.unavailableFields?.length ? ' · partial' : ''}`;
      button.setAttribute('aria-current', String(this._page?.entityId === page.entityId));
      button.addEventListener('click', () => this._selectPage(page.entityId));
      item.append(button);
      list.append(item);
    }
  }

  async _selectPage(entityId) {
    this._page = normalizeAdminPage(await this._request(this._root(entityId)));
    if (!this._page) throw new Error('The selected page identity is unavailable.');
    this._renderPages();
    this._renderEditor();
  }

  _renderCreate() {
    const section = this.$('[data-editor]');
    section.hidden = false;
    section.replaceChildren();
    section.append(this._heading('Create application page'));
    const fields = this._pageFields({order: 0, visibility: 'public'});
    const entity = this._field('Entity ID', 'text', 'web-page:');
    const html = this._htmlField('<!doctype html>\n<html lang="en"><head><meta charset="utf-8"><title>New page</title></head><body><main><h1>New page</h1></main></body></html>');
    const index = this._checkbox('Use as application landing page', false);
    const save = this._button('Create page', async () => {
      await this._request(this._applicationRoot(), {
        method: 'POST', body: {entityId: entity.input.value, ...fields.value(), html: html.value, isIndexPage: index.input.checked}
      });
      await this._selectApplication(this._application.applicationId);
    });
    section.append(entity.wrapper, fields.element, index.wrapper, html, this._actions(save));
  }

  async _renderEditor() {
    const page = this._page;
    const section = this.$('[data-editor]');
    section.hidden = false;
    section.replaceChildren();
    section.append(this._heading(page.title || page.entityId));
    if (Array.isArray(page.errors) && page.errors.length) {
      const errors = document.createElement('p');
      errors.className = 'danger';
      errors.textContent = page.errors.map(error => displayText(error?.message, 'Page evidence unavailable.', 800)).join(' ');
      section.append(errors);
    }
    const fields = this._pageFields(page);
    const saveMetadata = this._button('Save metadata', async () => {
      this._page = await this._request(`${this._root()}/metadata`, {
        method: 'PUT', body: {expectedComponentRevision: page.pageComponentRevision, ...fields.value()}
      });
      await this._selectApplication(this._application.applicationId);
      await this._selectPage(page.entityId);
    });
    saveMetadata.disabled = !page.metadataReady;
    if (!page.metadataReady) {
      const warning = document.createElement('p');
      warning.className = 'danger';
      warning.textContent = 'Some page metadata or its revision is unavailable; metadata editing is disabled until refreshed.';
      section.append(warning);
    }
    const index = this._checkbox('Application landing page', page.isIndexPage);
    index.input.addEventListener('change', async () => {
      this._page = await this._request(`${this._root()}/index`, {method: 'PUT', body: {isIndexPage: index.input.checked}});
      await this._selectApplication(this._application.applicationId);
      await this._selectPage(page.entityId);
    });
    const enabled = this._checkbox('Enabled', page.enabled);
    enabled.input.disabled = !page.entityReady;
    enabled.input.addEventListener('change', async () => {
      if (!page.entityReady) return;
      this._page = await this._request(`${this._root()}/enabled`, {
        method: 'PUT', body: {expectedEntityRevision: page.entityRevision, enabled: enabled.input.checked}
      });
      await this._selectApplication(this._application.applicationId);
      await this._selectPage(page.entityId);
    });
    const remove = this._button('Permanently remove disabled identity', async () => {
      if (page.enabled || !confirm(`Permanently remove ${page.entityId}? Its versioned content will remain preserved.`)) return;
      await this._request(this._root(), {method: 'DELETE'});
      await this._selectApplication(this._application.applicationId);
    });
    remove.disabled = page.enabled !== false;
    section.append(fields.element, index.wrapper, enabled.wrapper, this._actions(saveMetadata, remove));
    this._renderPublishedLink(page);
    await this._renderRevisions(section, page);
  }

  async _renderRevisions(section, page) {
    if (!page.content) return;
    if (!page.contentReady) {
      const warning = document.createElement('p');
      warning.className = 'danger';
      warning.textContent = 'Content revision metadata is unavailable; revision actions are disabled until refreshed.';
      section.append(warning);
      return;
    }
    const history = document.createElement('section');
    history.append(this._heading('Content revisions'));
    const revisions = await this._request(`${this._root()}/revisions?limit=100`);
    if (!revisions || !Array.isArray(revisions.revisions) || revisions.revisions.length > 100) {
      const warning = document.createElement('p');
      warning.className = 'danger';
      warning.textContent = 'Content revision history is unavailable.';
      section.append(warning);
      return;
    }
    const validRevisions = revisions.revisions.filter(value => value && Number.isInteger(value.revision) &&
      value.revision > 0 && typeof value.isActive === 'boolean');
    if (validRevisions.length !== revisions.revisions.length) {
      const warning = document.createElement('p');
      warning.className = 'danger';
      warning.textContent = 'Some content revisions are unavailable for this read.';
      section.append(warning);
    }
    const select = document.createElement('select');
    for (const revision of validRevisions) {
      select.append(new Option(`Revision ${revision.revision}${revision.isActive ? ' · active' : ''}`, revision.revision));
    }
    if (!validRevisions.length) { section.append(select); return; }
    const html = this._htmlField('');
    const load = async () => {
      const document = await this._request(`${this._root()}/revisions/${encode(select.value)}`);
      if (!document || typeof document.html !== 'string') throw new Error('The selected revision content is unavailable.');
      html.value = document.html;
    };
    select.addEventListener('change', load);
    await load();
    const draft = this._button('Save inactive draft', async () => {
      const created = await this._request(`${this._root()}/drafts`, {
        method: 'POST', body: {expectedLatestRevision: page.content.latestRevision, baseRevision: Number(select.value), html: html.value}
      });
      await this._selectPage(page.entityId);
      this._notice(`Draft revision ${created.summary.revision} was preserved without changing the live page.`);
    });
    const activate = this._button('Make selected revision active', async () => {
      await this._request(`${this._root()}/active`, {
        method: 'PUT', body: {expectedActiveRevision: page.content.activeRevision, revision: Number(select.value)}
      });
      await this._selectPage(page.entityId);
    });
    history.append(select, html, this._actions(draft, activate));
    section.append(history);
  }

  async _loadMigration() {
    const body = this.$('[data-migration-body]');
    body.replaceChildren();
    try {
      const report = await this._request('/api/control/web/page-migration');
      const summary = document.createElement('p');
      summary.textContent = `${report.linkedApplicationPages} linked application page(s); ${report.unclassifiablePages} awaiting or retaining explicit review. Content verified: ${report.contentVerified ? 'yes' : 'not yet'}.`;
      body.append(summary);
      for (const item of report.items.filter(value => value.classification === 'review-required' || value.classification === 'reviewed-unclassifiable')) {
        const row = document.createElement('p');
        row.textContent = `${item.pageId}: ${item.message}`;
        body.append(row);
      }
    } catch (error) {
      body.textContent = error.message;
    }
  }

  _pageFields(value) {
    const element = document.createElement('div'); element.className = 'row';
    const title = this._field('Title', 'text', value.title ?? '');
    const label = this._field('Navigation label', 'text', value.navigationLabel ?? '');
    const slug = this._field('Route slug', 'text', value.slug ?? '');
    const order = this._field('Navigation order', 'number', value.order ?? 0);
    const visibility = document.createElement('label'); visibility.textContent = 'Visibility';
    const select = document.createElement('select');
    select.append(new Option('Public', 'public'), new Option('Hidden', 'hidden')); select.value = value.visibility ?? 'public'; visibility.append(select);
    element.append(title.wrapper, label.wrapper, slug.wrapper, order.wrapper, visibility);
    return {element, value: () => ({title: title.input.value, navigationLabel: label.input.value, slug: slug.input.value,
      order: Number(order.input.value), visibility: select.value})};
  }

  _field(label, type, value) {
    const wrapper = document.createElement('label'); wrapper.textContent = label;
    const input = document.createElement('input'); input.type = type; input.value = value; wrapper.append(input);
    return {wrapper, input};
  }

  _checkbox(label, checked) {
    const wrapper = document.createElement('label');
    const input = document.createElement('input'); input.type = 'checkbox'; input.checked = checked;
    wrapper.append(input, document.createTextNode(label)); return {wrapper, input};
  }

  _htmlField(value) { const textarea = document.createElement('textarea'); textarea.value = value; textarea.setAttribute('aria-label', 'Page HTML'); return textarea; }
  _heading(value) { const heading = document.createElement('h3'); heading.textContent = value; return heading; }
  _button(label, action) { const button = document.createElement('button'); button.type = 'button'; button.textContent = label; button.addEventListener('click', () => action().catch(error => this._state('error', error.message))); return button; }
  _actions(...children) { const actions = document.createElement('div'); actions.className = 'actions'; actions.append(...children); return actions; }
  _applicationRoot() { return `/api/control/web/applications/${encode(this._application.applicationId)}/pages`; }
  _root(entityId = this._page.entityId) { return `${this._applicationRoot()}/${encode(entityId)}`; }

  _renderPublishedLink(page) {
    const link = this.$('[data-open]');
    const candidates = [this._application.indexPage, ...(this._application.pages ?? [])].filter(Boolean);
    const published = candidates.find(value => value.entityId === page.entityId);
    link.hidden = !published; link.removeAttribute('href');
    if (published) link.href = published.url;
  }

  async _request(path, options = {}) { return this._client.requestJson(path, {retry: options.method == null, ...options}); }
  _notice(message) { this.dispatchEvent(new CustomEvent('page-administration-change', {bubbles:true, composed:true, detail:{message}})); }
  _state(state, message = '') {
    this.$('[data-progress]').hidden = state !== 'loading';
    const error = this.$('[data-error]'); error.hidden = state !== 'error';
    if (state === 'error') error.error = {code: 'PAGE_ADMINISTRATION_FAILED', message};
  }
}

if (!customElements.get('page-administration')) customElements.define('page-administration', PageAdministration);
