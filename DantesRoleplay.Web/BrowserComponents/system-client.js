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
      !Object.hasOwn(value, 'entityId') || !Object.hasOwn(value, 'slug') || !Object.hasOwn(value, 'url') ||
      !validSystemIdentifier(value.entityId) || !validSystemIdentifier(value.slug) || typeof value.url !== 'string') return null;
  let url;
  try { url = new URL(value.url, origin); }
  catch { return null; }
  if (url.origin !== origin || !url.pathname.startsWith('/ui/') || url.search || url.hash) return null;
  const unavailableFields = Object.hasOwn(value, 'unavailableFields') && Array.isArray(value.unavailableFields)
    ? value.unavailableFields.filter(field => typeof field === 'string' && field.length > 0 && field.length <= 120).slice(0, 32)
    : [];
  const title = Object.hasOwn(value, 'title') && boundedText(value.title, 200) ? value.title.trim() : null;
  const navigationLabel = Object.hasOwn(value, 'navigationLabel') && boundedText(value.navigationLabel, 200) ? value.navigationLabel.trim() : null;
  if (title === null) unavailableFields.push('title');
  if (navigationLabel === null) unavailableFields.push('navigationLabel');
  const displayTitle = title ?? navigationLabel ?? value.slug;
  const displayNavigationLabel = navigationLabel ?? title ?? value.slug;
  const order = Object.hasOwn(value, 'order') && Number.isInteger(value.order) ? value.order : null;
  if (order === null) unavailableFields.push('order');
  const visibility = Object.hasOwn(value, 'visibility') && (value.visibility === 'public' || value.visibility === 'hidden') ? value.visibility : null;
  if (visibility === null) unavailableFields.push('visibility');
  const enabled = Object.hasOwn(value, 'enabled') && (value.enabled === true || value.enabled === false) ? value.enabled : null;
  if (enabled === null) unavailableFields.push('enabled');
  const contentPageId = !Object.hasOwn(value, 'contentPageId') || value.contentPageId == null ? null : validSystemIdentifier(value.contentPageId) ? value.contentPageId : null;
  if (Object.hasOwn(value, 'contentPageId') && value.contentPageId != null && contentPageId === null) unavailableFields.push('contentPageId');
  const isIndexPage = Object.hasOwn(value, 'isIndexPage') && (value.isIndexPage === true || value.isIndexPage === false) ? value.isIndexPage : null;
  if (isIndexPage === null) unavailableFields.push('isIndexPage');
  return Object.freeze({
    entityId: value.entityId,
    slug: value.slug,
    title: displayTitle,
    navigationLabel: displayNavigationLabel,
    order,
    visibility,
    url: url.pathname,
    contentPageId,
    isIndexPage,
    enabled,
    ...(unavailableFields.length ? {unavailableFields: Object.freeze(unavailableFields)} : {})
  });
}

export function normalizePublishedApplication(value, origin = currentOrigin()) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      !Object.hasOwn(value, 'applicationId') || !validSystemIdentifier(value.applicationId)) return null;
  const unavailableFields = Object.hasOwn(value, 'unavailableFields') && Array.isArray(value.unavailableFields)
    ? value.unavailableFields.filter(field => typeof field === 'string' && field.length > 0 && field.length <= 120).slice(0, 64)
    : [];
  let pageCoverage = Object.hasOwn(value, 'pageCoverage') && value.pageCoverage === 'partial';
  if (Object.hasOwn(value, 'coverage') && value.coverage === 'partial' && unavailableFields.length === 0)
    unavailableFields.push('publication');
  const displayName = Object.hasOwn(value, 'displayName') && boundedText(value.displayName, 200) ? value.displayName.trim() : value.applicationId;
  if (!Object.hasOwn(value, 'displayName') || !boundedText(value.displayName, 200)) unavailableFields.push('displayName');
  const rawPages = Object.hasOwn(value, 'pages') && Array.isArray(value.pages) ? value.pages : [];
  if (!Object.hasOwn(value, 'pages') || !Array.isArray(value.pages)) {
    unavailableFields.push('pages');
    pageCoverage = true;
  }
  const normalizedPages = rawPages.map(page => normalizePublishedPage(page, origin));
  const pageCounts = new Map();
  const entityCounts = new Map();
  const slugCounts = new Map();
  const rawEntityCounts = new Map();
  const rawSlugCounts = new Map();
  for (const page of rawPages) {
    if (!page || typeof page !== 'object' || Array.isArray(page)) continue;
    if (Object.hasOwn(page, 'entityId') && validSystemIdentifier(page.entityId))
      rawEntityCounts.set(page.entityId, (rawEntityCounts.get(page.entityId) ?? 0) + 1);
    if (Object.hasOwn(page, 'slug') && validSystemIdentifier(page.slug))
      rawSlugCounts.set(page.slug, (rawSlugCounts.get(page.slug) ?? 0) + 1);
  }
  for (const page of normalizedPages) if (page) {
    const identity = `${page.entityId}\n${page.slug}`;
    pageCounts.set(identity, (pageCounts.get(identity) ?? 0) + 1);
    entityCounts.set(page.entityId, (entityCounts.get(page.entityId) ?? 0) + 1);
    slugCounts.set(page.slug, (slugCounts.get(page.slug) ?? 0) + 1);
  }
  const rawIndexPage = !Object.hasOwn(value, 'indexPage') || value.indexPage == null ? null : normalizePublishedPage(value.indexPage, origin);
  const indexSource = Object.hasOwn(value, 'indexPage') && value.indexPage && typeof value.indexPage === 'object' && !Array.isArray(value.indexPage)
    ? value.indexPage : null;
  const indexEntityId = indexSource && Object.hasOwn(indexSource, 'entityId') && validSystemIdentifier(indexSource.entityId) ? indexSource.entityId : null;
  const indexSlug = indexSource && Object.hasOwn(indexSource, 'slug') && validSystemIdentifier(indexSource.slug) ? indexSource.slug : null;
  if (Object.hasOwn(value, 'indexPage') && value.indexPage != null && !rawIndexPage) unavailableFields.push('indexPage');
  const indexIdentity = rawIndexPage ? `${rawIndexPage.entityId}\n${rawIndexPage.slug}` : null;
  const rawIndexConflict = (indexEntityId !== null && rawEntityCounts.has(indexEntityId)) ||
    (indexSlug !== null && rawSlugCounts.has(indexSlug));
  const visiblePages = [];
  for (const page of normalizedPages) {
    if (!page) {
      unavailableFields.push('pages');
      pageCoverage = true;
      continue;
    }
    const identity = `${page.entityId}\n${page.slug}`;
    const duplicateIdentity = pageCounts.get(identity) !== 1 || entityCounts.get(page.entityId) !== 1 || slugCounts.get(page.slug) !== 1 ||
      rawEntityCounts.get(page.entityId) !== 1 || rawSlugCounts.get(page.slug) !== 1;
    const unknownAdmission = page.visibility === null || page.enabled === null;
    const knownSuppressed = page.visibility === 'hidden' || page.enabled === false;
    if (duplicateIdentity || page.isIndexPage !== false ||
        page.entityId === indexEntityId || page.slug === indexSlug ||
        page.visibility !== 'public' || page.enabled !== true) {
      if (duplicateIdentity || page.isIndexPage !== false || page.entityId === indexEntityId || page.slug === indexSlug || unknownAdmission ||
          (!knownSuppressed && page.unavailableFields?.length)) unavailableFields.push('pages');
      if (duplicateIdentity || page.isIndexPage !== false || page.entityId === indexEntityId || page.slug === indexSlug || unknownAdmission ||
          (!knownSuppressed && page.unavailableFields?.length)) pageCoverage = true;
      continue;
    }
    visiblePages.push(page);
    if (page.unavailableFields?.length) unavailableFields.push(...page.unavailableFields.map(field => `pages.${field}`));
  }
  const indexPage = rawIndexPage && rawIndexPage.isIndexPage === true && rawIndexPage.visibility === 'public' && rawIndexPage.enabled === true &&
    (!indexIdentity || pageCounts.get(indexIdentity) === undefined) &&
    !rawIndexConflict
    ? rawIndexPage : null;
  if (rawIndexPage && !indexPage) {
    const knownSuppressedIndex = rawIndexPage.isIndexPage === true &&
      (rawIndexPage.visibility === 'hidden' || rawIndexPage.visibility === 'public') &&
      (rawIndexPage.enabled === false || rawIndexPage.enabled === true);
    if (!knownSuppressedIndex || rawIndexConflict) {
      unavailableFields.push('indexPage');
      pageCoverage = true;
    }
  }
  if (rawIndexPage?.unavailableFields?.length && rawIndexPage.visibility !== 'hidden' && rawIndexPage.enabled !== false)
    unavailableFields.push(...rawIndexPage.unavailableFields.map(field => `indexPage.${field}`));
  const pages = visiblePages;
  pages.sort((left, right) => (left.order === null ? (right.order === null ? 0 : 1) : right.order === null ? -1 : left.order - right.order) ||
    left.navigationLabel.localeCompare(right.navigationLabel, undefined, {sensitivity: 'base'}) ||
    left.entityId.localeCompare(right.entityId));
  const fingerprint = !Object.hasOwn(value, 'resolutionFingerprint') || value.resolutionFingerprint == null ? null : value.resolutionFingerprint;
  if (fingerprint !== null && !/^[0-9A-Fa-f]{64}$/.test(fingerprint)) return null;
  const publicationStatus = Object.hasOwn(value, 'publicationStatus') && boundedText(value.publicationStatus, 80) ? value.publicationStatus : 'invalid';
  if (!Object.hasOwn(value, 'publicationStatus') || (publicationStatus === 'invalid' && value.publicationStatus !== 'invalid')) unavailableFields.push('publicationStatus');
  const isPublishable = Object.hasOwn(value, 'isPublishable') && value.isPublishable === true;
  if (!Object.hasOwn(value, 'isPublishable') || (value.isPublishable !== true && value.isPublishable !== false)) unavailableFields.push('isPublishable');
  const isClickable = Object.hasOwn(value, 'isClickable') && value.isClickable === true && isPublishable === true && indexPage !== null;
  if (!Object.hasOwn(value, 'isClickable') || (value.isClickable !== true && value.isClickable !== false)) unavailableFields.push('isClickable');
  return Object.freeze({
    applicationId: value.applicationId,
    displayName,
    publicationStatus,
    isPublishable,
    isClickable,
    hasAdditionalPages: Object.hasOwn(value, 'hasAdditionalPages') && value.hasAdditionalPages === true || pages.length > 0,
    resolutionFingerprint: fingerprint,
    indexPage,
    pages: Object.freeze(pages),
    ...(pageCoverage ? {pageCoverage: 'partial'} : {}),
    ...(unavailableFields.length ? {coverage: 'partial', unavailableFields: Object.freeze([...new Set(unavailableFields)])} : {})
  });
}

function normalizeSystemPage(value, origin) {
  if (!value || typeof value !== 'object' || Array.isArray(value) ||
      !Object.hasOwn(value, 'pageId') || !Object.hasOwn(value, 'title') || !Object.hasOwn(value, 'url') ||
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
    if (!value || typeof value !== 'object' || !Object.hasOwn(value, 'applications') || !Object.hasOwn(value, 'systemPages') ||
        !Array.isArray(value.applications) || !Array.isArray(value.systemPages)) {
      throw new SystemClientError('WEB_PUBLICATION_RESPONSE_INVALID', 'The application publication response is invalid.');
    }
    const applications = value.applications.map(item => normalizePublishedApplication(item, this._origin));
    const systemPages = value.systemPages.map(item => normalizeSystemPage(item, this._origin));
    const applicationsPartial = applications.some(item => item === null) ||
      applications.some(item => item?.coverage === 'partial');
    const systemPagesPartial = systemPages.some(item => item === null);
    const partial = applicationsPartial || systemPagesPartial;
    const nextCursor = !Object.hasOwn(value, 'nextCursor') || value.nextCursor == null ? null : value.nextCursor;
    if (nextCursor !== null && !boundedText(nextCursor, MAXIMUM_CURSOR_LENGTH)) {
      throw new SystemClientError('WEB_PUBLICATION_CURSOR_INVALID', 'The application publication response contains an invalid cursor.');
    }
    const unavailableFields = [
      ...(applicationsPartial ? ['applications'] : []),
      ...(systemPagesPartial ? ['systemPages'] : [])
    ];
    return Object.freeze({applications: Object.freeze(applications.filter(Boolean)), systemPages: Object.freeze(systemPages.filter(Boolean)), nextCursor,
      ...(partial ? {coverage: 'partial', unavailableFields: Object.freeze(unavailableFields)} : {})});
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
    let partialApplications = false;
    let partialSystemPages = false;
    do {
      if (pageCount >= maximumPages) throw new SystemClientError(
        'WEB_PUBLICATION_PAGE_LIMIT', 'Application discovery exceeded its bounded page limit.');
      const page = await this.discoverApplications({cursor, limit: options.limit ?? DEFAULT_PAGE_SIZE, signal: options.signal});
      pageCount += 1;
      partialApplications ||= page.unavailableFields?.includes('applications') === true;
      partialSystemPages ||= page.unavailableFields?.includes('systemPages') === true;
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
    const unavailableFields = [
      ...(partialApplications ? ['applications'] : []),
      ...(partialSystemPages ? ['systemPages'] : [])
    ];
    return Object.freeze({
      applications: Object.freeze(applications),
      systemPages: Object.freeze(systemPages ?? []),
      pageCount,
      resolutionFingerprints: Object.freeze(Object.fromEntries(fingerprints)),
      ...(unavailableFields.length ? {coverage: 'partial', unavailableFields: Object.freeze(unavailableFields)} : {})
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
    return Object.freeze({application: verifiedApplication, page: known,
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

// Session-only correlation metadata for interrupted writes. Callers must never put request bodies,
// prompts, structured input, or authorization material in this store.
export class InterruptedRequestStore {
  constructor(key = 'dantes.interrupted-requests.v1') { this._key = key; }

  read(slot) {
    if (!validSystemIdentifier(slot)) return null;
    try {
      const storage = globalThis.sessionStorage;
      if (!storage) return {malformed: true};
      const raw = storage.getItem(this._key);
      if (!raw) return null;
      if (raw.length > 8192) return {malformed: true};
      const values = JSON.parse(raw);
      if (!values || typeof values !== 'object' || Array.isArray(values)) return {malformed: true};
      const value = values[slot];
      return value === undefined ? null : validInterruptedMetadata(value) ? value : {malformed: true};
    } catch { return {malformed: true}; }
  }

  write(slot, value) {
    if (!validSystemIdentifier(slot) || !validInterruptedMetadata(value)) return false;
    try {
      const raw = globalThis.sessionStorage?.getItem(this._key);
      const values = raw ? JSON.parse(raw) : {};
      if (!values || typeof values !== 'object' || Array.isArray(values)) return false;
      values[slot] = value;
      const encoded = JSON.stringify(values);
      if (new TextEncoder().encode(encoded).length > 8192) return false;
      const storage = globalThis.sessionStorage;
      if (!storage) return false;
      storage.setItem(this._key, encoded);
      return storage.getItem(this._key) === encoded;
    } catch { return false; }
  }

  remove(slot) {
    if (!validSystemIdentifier(slot)) return;
    try {
      const raw = globalThis.sessionStorage?.getItem(this._key);
      const values = raw ? JSON.parse(raw) : null;
      if (!values || typeof values !== 'object' || Array.isArray(values)) return;
      delete values[slot];
      globalThis.sessionStorage?.setItem(this._key, JSON.stringify(values));
    } catch { }
  }
}

function validInterruptedMetadata(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const own = Object.keys(value).sort();
  const exact = expected => own.length === expected.length && own.every((key, index) => key === expected[index]);
  const valid = candidate => validSystemIdentifier(candidate) && candidate.length <= 200;
  if (value.kind === 'ai') return exact(['applicationId', 'contextFingerprint', 'key', 'kind', 'provider', 'resolutionFingerprint', 'sourceReferences', 'stateSpaceId', 'surface'])
    && valid(value.key) && valid(value.provider) && valid(value.surface)
    && (value.applicationId === null || valid(value.applicationId))
    && (value.stateSpaceId === null || valid(value.stateSpaceId))
    && (value.resolutionFingerprint === null || /^[0-9A-F]{64}$/.test(value.resolutionFingerprint))
    && (value.contextFingerprint === null || /^[0-9A-F]{64}$/.test(value.contextFingerprint)) && Array.isArray(value.sourceReferences) &&
    value.sourceReferences.length > 0 && value.sourceReferences.length <= 24 &&
    value.sourceReferences.every(item => validSystemIdentifier(item, 320)) &&
    value.sourceReferences.every((item, index) => index === 0 || value.sourceReferences[index - 1] < item);
  if (value.kind === 'system-conversation') return exact(['key', 'kind']) && valid(value.key);
  return value.kind === 'system-task' && exact(['key', 'kind', 'taskId'])
    && valid(value.key) && (value.taskId === null || valid(value.taskId));
}

export const interruptedRequestStore = new InterruptedRequestStore();
