const DEFAULT_MAXIMUM_RESPONSE_BYTES = 1024 * 1024;
const DEFAULT_MAXIMUM_PAGES = 10;
const DEFAULT_MAXIMUM_APPLICATIONS = 1000;
const DEFAULT_PAGE_SIZE = 100;
const MAXIMUM_CURSOR_LENGTH = 1024;
const TRANSIENT_STATUSES = new Set([408, 425, 429, 502, 503, 504]);

function currentOrigin() {
  if (typeof window === 'undefined' || !window.location?.origin) return 'http://localhost';
  return window.location.origin;
}

function boundedText(value, maximum) {
  return typeof value === 'string' && value.trim() === value && value.length > 0 && value.length <= maximum;
}

export function validSystemIdentifier(value, maximum = 200) {
  return boundedText(value, maximum) && !value.includes('/') && !value.includes('\\') &&
    !/[\u0000-\u001f\u007f]/.test(value);
}

function errorCode(value, fallback = 'SYSTEM_REQUEST_FAILED') {
  return typeof value === 'string' && /^[A-Z][A-Z0-9_]{0,99}$/.test(value) ? value : fallback;
}

export class SystemClientError extends Error {
  constructor(code, message, options = {}) {
    super(typeof message === 'string' && message.length > 0 ? message : 'The system request failed.');
    this.name = 'SystemClientError';
    this.code = errorCode(code);
    this.status = Number.isInteger(options.status) ? options.status : null;
    this.retryable = options.retryable === true;
    this.details = options.details ?? null;
  }
}

export class SystemRequestScope {
  constructor() {
    this._controller = null;
    this._sequence = 0;
  }

  begin() {
    this.cancel();
    const controller = new AbortController();
    const sequence = ++this._sequence;
    this._controller = controller;
    return Object.freeze({
      signal: controller.signal,
      isCurrent: () => this._controller === controller && this._sequence === sequence && !controller.signal.aborted
    });
  }

  cancel() {
    this._controller?.abort();
    this._controller = null;
  }
}

export function normalizePublishedPage(value, origin = currentOrigin()) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      !validSystemIdentifier(value.entityId) || !validSystemIdentifier(value.slug) ||
      !boundedText(value.title, 200) || !boundedText(value.navigationLabel, 200) ||
      !Number.isInteger(value.order) || typeof value.url !== 'string') return null;
  let url;
  try { url = new URL(value.url, origin); }
  catch { return null; }
  if (url.origin !== origin || !url.pathname.startsWith('/ui/') || url.search || url.hash) return null;
  return Object.freeze({
    entityId: value.entityId,
    slug: value.slug,
    title: value.title.trim(),
    navigationLabel: value.navigationLabel.trim(),
    order: value.order,
    visibility: value.visibility === 'hidden' ? 'hidden' : 'public',
    url: url.pathname,
    contentPageId: typeof value.contentPageId === 'string' ? value.contentPageId : null,
    isIndexPage: value.isIndexPage === true,
    enabled: value.enabled !== false
  });
}

export function normalizePublishedApplication(value, origin = currentOrigin()) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      !validSystemIdentifier(value.applicationId) || !boundedText(value.displayName, 200) ||
      !Array.isArray(value.pages)) return null;
  const indexPage = value.indexPage == null ? null : normalizePublishedPage(value.indexPage, origin);
  if (value.indexPage != null && !indexPage) return null;
  const pages = value.pages.map(page => normalizePublishedPage(page, origin));
  if (pages.some(page => page === null)) return null;
  const identities = new Set();
  for (const page of pages) {
    const identity = `${page.entityId}\n${page.slug}`;
    if (identities.has(identity) || (indexPage &&
        (page.entityId === indexPage.entityId || page.slug === indexPage.slug))) return null;
    identities.add(identity);
  }
  pages.sort((left, right) => left.order - right.order ||
    left.navigationLabel.localeCompare(right.navigationLabel, undefined, {sensitivity: 'base'}) ||
    left.entityId.localeCompare(right.entityId));
  const fingerprint = value.resolutionFingerprint == null ? null : value.resolutionFingerprint;
  if (fingerprint !== null && !/^[0-9A-Fa-f]{64}$/.test(fingerprint)) return null;
  return Object.freeze({
    applicationId: value.applicationId,
    displayName: value.displayName.trim(),
    publicationStatus: boundedText(value.publicationStatus, 80) ? value.publicationStatus : 'invalid',
    isPublishable: value.isPublishable === true,
    isClickable: value.isClickable === true && indexPage !== null,
    hasAdditionalPages: value.hasAdditionalPages === true || pages.length > 0,
    resolutionFingerprint: fingerprint,
    indexPage,
    pages: Object.freeze(pages)
  });
}

function normalizeSystemPage(value, origin) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      !validSystemIdentifier(value.pageId) || !boundedText(value.title, 200) || typeof value.url !== 'string') return null;
  let url;
  try { url = new URL(value.url, origin); }
  catch { return null; }
  if (url.origin !== origin || (!url.pathname.startsWith('/ui/') && url.pathname !== '/') || url.search || url.hash) return null;
  return Object.freeze({pageId: value.pageId, title: value.title.trim(), url: url.pathname});
}

function delay(milliseconds, signal) {
  if (milliseconds <= 0) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(resolve, milliseconds);
    signal?.addEventListener('abort', () => {
      clearTimeout(timer);
      reject(new DOMException('The request was cancelled.', 'AbortError'));
    }, {once: true});
  });
}

export class SystemWebClient {
  constructor(options = {}) {
    this._fetch = options.fetch ?? (typeof fetch === 'function' ? fetch.bind(globalThis) : null);
    this._origin = options.origin ?? currentOrigin();
    this._maximumResponseBytes = options.maximumResponseBytes ?? DEFAULT_MAXIMUM_RESPONSE_BYTES;
    this._maximumRetries = options.maximumRetries ?? 2;
    this._retryDelayMilliseconds = options.retryDelayMilliseconds ?? 75;
    this._fingerprints = new Map();
    if (typeof this._fetch !== 'function') throw new TypeError('A fetch implementation is required.');
    if (!Number.isInteger(this._maximumRetries) || this._maximumRetries < 0 || this._maximumRetries > 5) {
      throw new TypeError('maximumRetries must be between zero and five.');
    }
  }

  createRequestScope() { return new SystemRequestScope(); }

  async requestJson(input, options = {}) {
    const url = this._apiUrl(input);
    const method = (options.method ?? (options.body === undefined ? 'GET' : 'POST')).toUpperCase();
    const body = options.body === undefined ? undefined : JSON.stringify(options.body);
    const retry = options.retry ?? method === 'GET';
    let attempt = 0;
    while (true) {
      try {
        const response = await this._fetch(url, {
          method,
          headers: body === undefined
            ? {accept: 'application/json', ...(options.headers ?? {})}
            : {accept: 'application/json', 'content-type': 'application/json', ...(options.headers ?? {})},
          body,
          signal: options.signal
        });
        const value = await this._readJson(response);
        if (!response.ok) {
          const retryable = TRANSIENT_STATUSES.has(response.status);
          if (retry && retryable && attempt < this._maximumRetries) {
            attempt += 1;
            await delay(this._retryDelayMilliseconds * attempt, options.signal);
            continue;
          }
          throw new SystemClientError(
            errorCode(value?.error, `HTTP_${response.status}`),
            boundedText(value?.message, 500) ? value.message : 'The system request was rejected.',
            {status: response.status, retryable, details: value});
        }
        return value;
      } catch (error) {
        if (error?.name === 'AbortError' || options.signal?.aborted) throw error;
        if (error instanceof SystemClientError) throw error;
        if (retry && attempt < this._maximumRetries) {
          attempt += 1;
          await delay(this._retryDelayMilliseconds * attempt, options.signal);
          continue;
        }
        throw new SystemClientError('SYSTEM_NETWORK_UNAVAILABLE', 'The local system is temporarily unavailable.',
          {retryable: true, details: error});
      }
    }
  }

  async discoverApplications(options = {}) {
    const url = new URL('/api/web/applications', this._origin);
    const limit = options.limit ?? DEFAULT_PAGE_SIZE;
    if (!Number.isInteger(limit) || limit < 1 || limit > 100) {
      throw new SystemClientError('WEB_PUBLICATION_LIMIT_INVALID', 'The application page size must be between 1 and 100.');
    }
    url.searchParams.set('limit', String(limit));
    if (options.cursor != null) {
      if (!boundedText(options.cursor, MAXIMUM_CURSOR_LENGTH)) {
        throw new SystemClientError('WEB_PUBLICATION_CURSOR_INVALID', 'The application cursor is invalid.');
      }
      url.searchParams.set('cursor', options.cursor);
    }
    const value = await this.requestJson(url, {signal: options.signal});
    if (!value || !Array.isArray(value.applications) || !Array.isArray(value.systemPages)) {
      throw new SystemClientError('WEB_PUBLICATION_RESPONSE_INVALID', 'The application publication response is invalid.');
    }
    const applications = value.applications.map(item => normalizePublishedApplication(item, this._origin));
    const systemPages = value.systemPages.map(item => normalizeSystemPage(item, this._origin));
    if (applications.some(item => item === null) || systemPages.some(item => item === null)) {
      throw new SystemClientError('WEB_PUBLICATION_RESPONSE_INVALID', 'The application publication response contains invalid entries.');
    }
    const nextCursor = value.nextCursor == null ? null : value.nextCursor;
    if (nextCursor !== null && !boundedText(nextCursor, MAXIMUM_CURSOR_LENGTH)) {
      throw new SystemClientError('WEB_PUBLICATION_CURSOR_INVALID', 'The application publication response contains an invalid cursor.');
    }
    return Object.freeze({applications: Object.freeze(applications), systemPages: Object.freeze(systemPages), nextCursor});
  }

  async discoverAllApplications(options = {}) {
    const maximumPages = options.maximumPages ?? DEFAULT_MAXIMUM_PAGES;
    const maximumApplications = options.maximumApplications ?? DEFAULT_MAXIMUM_APPLICATIONS;
    const applications = [];
    const applicationIds = new Set();
    const cursors = new Set();
    const fingerprints = new Map();
    let systemPages = null;
    let cursor = null;
    let pageCount = 0;
    do {
      if (pageCount >= maximumPages) throw new SystemClientError(
        'WEB_PUBLICATION_PAGE_LIMIT', 'Application discovery exceeded its bounded page limit.');
      const page = await this.discoverApplications({cursor, limit: options.limit ?? DEFAULT_PAGE_SIZE, signal: options.signal});
      pageCount += 1;
      systemPages ??= page.systemPages;
      for (const application of page.applications) {
        if (applicationIds.has(application.applicationId)) throw new SystemClientError(
          'WEB_PUBLICATION_RESPONSE_INVALID', 'Application discovery returned a duplicate application.');
        applicationIds.add(application.applicationId);
        applications.push(application);
        if (applications.length > maximumApplications) throw new SystemClientError(
          'WEB_PUBLICATION_APPLICATION_LIMIT', 'Application discovery exceeded its bounded result limit.');
        if (application.resolutionFingerprint) fingerprints.set(
          application.applicationId, application.resolutionFingerprint);
      }
      cursor = page.nextCursor;
      if (cursor !== null) {
        if (cursors.has(cursor)) throw new SystemClientError(
          'WEB_PUBLICATION_CURSOR_INVALID', 'Application discovery returned a repeated cursor.');
        cursors.add(cursor);
      }
    } while (cursor !== null);
    applications.sort((left, right) => left.displayName.localeCompare(right.displayName, undefined, {sensitivity: 'base'}) ||
      left.applicationId.localeCompare(right.applicationId));
    for (const [applicationId, fingerprint] of fingerprints) this._fingerprints.set(applicationId, fingerprint);
    return Object.freeze({
      applications: Object.freeze(applications),
      systemPages: Object.freeze(systemPages ?? []),
      pageCount,
      resolutionFingerprints: Object.freeze(Object.fromEntries(fingerprints))
    });
  }

  async getApplication(applicationId, options = {}) {
    this._requireId(applicationId, 'application ID');
    const value = await this.requestJson(`/api/web/applications/${encodeURIComponent(applicationId)}`, {signal: options.signal});
    const application = normalizePublishedApplication(value, this._origin);
    if (!application) throw new SystemClientError(
      'WEB_PUBLICATION_RESPONSE_INVALID', 'The application publication response is invalid.');
    const expected = options.expectedResolutionFingerprint ?? this._fingerprints.get(applicationId) ?? null;
    if (expected && application.resolutionFingerprint && expected !== application.resolutionFingerprint) {
      this._fingerprints.set(applicationId, application.resolutionFingerprint);
      throw new SystemClientError('WEB_RESOLUTION_FINGERPRINT_STALE',
        'The application changed while its publication was being loaded.', {status: 409, retryable: true});
    }
    if (application.resolutionFingerprint) this._fingerprints.set(applicationId, application.resolutionFingerprint);
    return application;
  }

  async getPage(applicationId, slug, options = {}) {
    this._requireId(applicationId, 'application ID');
    this._requireId(slug, 'page slug');
    const value = await this.requestJson(
      `/api/web/applications/${encodeURIComponent(applicationId)}/pages/${encodeURIComponent(slug)}`,
      {signal: options.signal});
    const page = normalizePublishedPage(value, this._origin);
    if (!page || page.slug !== slug) throw new SystemClientError(
      'WEB_PUBLICATION_RESPONSE_INVALID', 'The published page response is invalid.');
    return page;
  }

  async loadPublishedPage(applicationId, slug, options = {}) {
    const application = await this.getApplication(applicationId, options);
    const page = await this.getPage(applicationId, slug, options);
    const verifiedApplication = application.resolutionFingerprint
      ? await this.getApplication(applicationId, {...options,
        expectedResolutionFingerprint: application.resolutionFingerprint})
      : application;
    const known = [verifiedApplication.indexPage, ...verifiedApplication.pages].filter(Boolean)
      .find(candidate => candidate.entityId === page.entityId && candidate.slug === page.slug);
    if (!known) throw new SystemClientError('WEB_PAGE_NOT_PUBLISHED',
      'This page is not part of the current application publication.');
    return Object.freeze({application: verifiedApplication, page,
      resolutionFingerprint: verifiedApplication.resolutionFingerprint});
  }

  _requireId(value, label) {
    if (!validSystemIdentifier(value)) throw new SystemClientError(
      'SYSTEM_REQUEST_INVALID', `A valid ${label} is required.`);
  }

  _apiUrl(input) {
    let url;
    try { url = input instanceof URL ? new URL(input.href) : new URL(input, this._origin); }
    catch { throw new SystemClientError('SYSTEM_REQUEST_INVALID', 'The system request URL is invalid.'); }
    if (url.origin !== this._origin || !url.pathname.startsWith('/api/')) throw new SystemClientError(
      'SYSTEM_REQUEST_INVALID', 'The browser client only accepts same-origin system API requests.');
    return url;
  }

  async _readJson(response) {
    const text = await response.text();
    if (new TextEncoder().encode(text).length > this._maximumResponseBytes) throw new SystemClientError(
      'SYSTEM_RESPONSE_TOO_LARGE', 'The system response exceeded the browser safety limit.');
    if (!text) return null;
    try { return JSON.parse(text); }
    catch { throw new SystemClientError('SYSTEM_RESPONSE_INVALID', 'The system returned invalid structured data.',
      {status: response.status}); }
  }
}

export const systemWebClient = new SystemWebClient();
