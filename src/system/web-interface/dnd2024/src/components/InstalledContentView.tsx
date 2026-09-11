import { FormEvent, useEffect, useMemo, useRef, useState } from "react";

import { ViewReadError } from "../data/view-read-client";
import { selectInstalledContentPages, useHubSelector } from "../data/hub-store";
import type {
  InstalledContentPage,
  InstalledContentRequest,
} from "../server/effective-content";
import { installedContentRequestKey } from "../server/effective-content";

const BADGE_LABELS = {
  homebrew: "Homebrew",
  compatibility: "Compatibility",
  "third-party": "Third-party",
  unknown: "Classification unavailable",
} as const;

export type InstalledContentLoader = (
  request: InstalledContentRequest,
  signal: AbortSignal,
  preferCached?: boolean,
) => Promise<InstalledContentPage>;

type Filters = {
  ownerId: string;
  kind: string;
  query: string;
};

const EMPTY_FILTERS: Filters = { ownerId: "", kind: "", query: "" };

function errorMessage(error: unknown) {
  if (error instanceof ViewReadError && error.category === "authorization")
    return "Installed content is not available for this audience.";
  if (error instanceof ViewReadError && error.category === "stale-data")
    return "Installed content changed while this page was loading. Reload the list to continue.";
  if (error instanceof ViewReadError && error.category === "incompatible-data")
    return "Installed content returned data this version of the site cannot display.";
  return "Installed content could not be loaded. Check the connection and try again.";
}

function sameExtensions(left: InstalledContentPage, right: InstalledContentPage) {
  return JSON.stringify(left.extensions) === JSON.stringify(right.extensions);
}

export function InstalledContentView({
  loadContent,
  resolutionFingerprint,
  contentScope,
}: {
  loadContent: InstalledContentLoader;
  resolutionFingerprint: string | null;
  /** Connected production view: Redux exclusively owns completed result pages. */
  contentScope?: string;
}) {
  const [filters, setFilters] = useState<Filters>(EMPTY_FILTERS);
  const [draftQuery, setDraftQuery] = useState("");
  const [fallbackPages, setFallbackPages] = useState<InstalledContentPage[]>([]);
  const [pageKeys, setPageKeys] = useState<string[]>([]);
  const contentSelector = useMemo(() => selectInstalledContentPages(contentScope ?? "", pageKeys), [contentScope, pageKeys]);
  const confirmedPages = useHubSelector(contentSelector);
  const confirmedChainComplete = !contentScope || confirmedPages.length === pageKeys.length;
  // A page that was evicted or cleared must not become a new apparent first
  // page. In particular, a denied continuation clears every private page.
  const pages = contentScope ? confirmedChainComplete ? confirmedPages : [] : fallbackPages;
  const [cursor, setCursor] = useState<string | null>(null);
  const [requestVersion, setRequestVersion] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [chainInvalid, setChainInvalid] = useState(false);
  const preferCached = useRef(true);
  const observedOwner = useRef({ scope: contentScope, loader: loadContent });
  const skipNextRead = useRef(false);
  const loadingRef = useRef(false);
  const pendingOwnerReload = useRef(false);
  const attemptedScope = useRef<string | undefined>(undefined);
  const confirmedScope = useRef<string | undefined>(undefined);
  const requestSequence = useRef(0);
  const [ownerRevision, setOwnerRevision] = useState(0);

  useEffect(() => {
    const previous = observedOwner.current;
    const scopeChanged = previous.scope !== contentScope;
    const loaderChanged = previous.loader !== loadContent;
    observedOwner.current = { scope: contentScope, loader: loadContent };
    if (!scopeChanged && !loaderChanged) return;
    if (!scopeChanged) {
      if (loadingRef.current) {
        pendingOwnerReload.current = true;
        return;
      }
      // A callback created while Dnd wires the first owner is not another
      // response owner. A later replacement after a failed/cleared read is.
      if (attemptedScope.current !== contentScope || confirmedScope.current === contentScope) return;
    }
    // A fresh scoped owner must not continue a cursor from an older scope or
    // retain completed page bodies outside Redux.
    setFallbackPages([]);
    setPageKeys([]);
    setCursor(null);
    setError("");
    setChainInvalid(false);
    setOwnerRevision((value) => value + 1);
  }, [contentScope, loadContent]);

  useEffect(() => {
    if (!contentScope || pageKeys.length === 0 || confirmedChainComplete) return;
    setPageKeys([]);
    setCursor(null);
    // Capacity eviction is recoverable, but only from the authoritative first
    // page. Never continue from a cursor whose preceding page disappeared.
    preferCached.current = false;
    setRequestVersion((value) => value + 1);
  }, [confirmedChainComplete, contentScope, pageKeys.length]);

  useEffect(() => {
    if (skipNextRead.current) {
      // Clearing a denied continuation changes its cursor to null. That state
      // transition must not turn into an implicit retry of the first page.
      skipNextRead.current = false;
      setLoading(false);
      return;
    }
    const controller = new AbortController();
    const requestSequenceId = ++requestSequence.current;
    attemptedScope.current = contentScope;
    let succeeded = false;
    let authorizationFailure = false;
    const firstPage = pages[0] ?? null;
    const request: InstalledContentRequest = {
      ownerId: filters.ownerId || null,
      kinds: filters.kind ? [filters.kind] : [],
      query: filters.query,
      cursor,
      expectedResolutionFingerprint: cursor
        ? firstPage?.resolutionFingerprint ?? resolutionFingerprint
        : resolutionFingerprint,
    };
    const useCache = preferCached.current;
    preferCached.current = true;
    loadingRef.current = true;
    setLoading(true);
    setError("");
    void loadContent(request, controller.signal, useCache).then((page) => {
      if (controller.signal.aborted || requestSequenceId !== requestSequence.current) return;
      if (cursor && firstPage && (page.resolutionFingerprint !== firstPage.resolutionFingerprint
          || !sameExtensions(firstPage, page) || page.totalCount !== firstPage.totalCount)) {
        skipNextRead.current = true;
        setPageKeys([]);
        setFallbackPages([]);
        setCursor(null);
        setChainInvalid(true);
        setError("Installed content changed while this page was loading. Reload the list to continue.");
        return;
      }
      if (contentScope) {
        const key = installedContentRequestKey(request);
        setPageKeys((current) => cursor
          ? current.includes(key) ? current : [...current, key]
          : [key]);
      } else setFallbackPages((current) => cursor ? [...current, page] : [page]);
      confirmedScope.current = contentScope;
      succeeded = true;
    }).catch((reason: unknown) => {
      if (controller.signal.aborted || requestSequenceId !== requestSequence.current) return;
      if (reason instanceof ViewReadError && reason.category === "authorization") {
        authorizationFailure = true;
        // Redux already cleared every scoped content page. Clear the local
        // continuation chain too so it cannot render a later page as a base
        // result or issue an automatic unauthorized continuation retry.
        skipNextRead.current = cursor !== null;
        setFallbackPages([]);
        setPageKeys([]);
        setCursor(null);
        setChainInvalid(false);
      } else if (reason instanceof ViewReadError && reason.category === "stale-data") {
        // A stale continuation cannot be retried against its old cursor. Its
        // fingerprint/paging evidence must be re-established from page one.
        skipNextRead.current = cursor !== null;
        setFallbackPages([]);
        setPageKeys([]);
        setCursor(null);
        setChainInvalid(true);
      }
      setError(errorMessage(reason));
    }).finally(() => {
      if (controller.signal.aborted || requestSequenceId !== requestSequence.current) return;
      loadingRef.current = false;
      setLoading(false);
      if (pendingOwnerReload.current && !succeeded && !authorizationFailure) {
        pendingOwnerReload.current = false;
        setFallbackPages([]);
        setPageKeys([]);
        setCursor(null);
        setError("");
        setOwnerRevision((value) => value + 1);
      } else if (succeeded || authorizationFailure) pendingOwnerReload.current = false;
    });
    return () => {
      controller.abort();
      if (requestSequenceId === requestSequence.current) loadingRef.current = false;
    };
    // A filter transition resets pages before this effect executes. Pages must not retrigger reads.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cursor, filters, ownerRevision, requestVersion, resolutionFingerprint]);

  const content = pages[0] ?? null;
  const contentCoverage = pages.some((page) => page.coverage === "partial") ? "partial" : content?.coverage;
  const contentNotices = useMemo(() => [...new Set(pages.flatMap((page) => page.notices))], [pages]);
  const { records, duplicateRecords } = useMemo(() => {
    const seen = new Set<string>();
    const retained: InstalledContentPage["records"] = [];
    let duplicates = false;
    for (const record of pages.flatMap((page) => page.records)) {
      if (seen.has(record.id)) { duplicates = true; continue; }
      seen.add(record.id); retained.push(record);
    }
    return { records: retained, duplicateRecords: duplicates };
  }, [pages]);
  const lastPage = pages.at(-1) ?? null;

  const replaceFilters = (next: Filters) => {
    setFallbackPages([]);
    setPageKeys([]);
    setCursor(null);
    setFilters(next);
  };

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    replaceFilters({ ...filters, query: draftQuery.trim() });
  };

  const retryCurrent = () => {
    preferCached.current = false;
    setRequestVersion((value) => value + 1);
  };

  const reloadList = () => {
    skipNextRead.current = false;
    preferCached.current = false;
    setFallbackPages([]);
    setPageKeys([]);
    setCursor(null);
    setChainInvalid(false);
    setRequestVersion((value) => value + 1);
  };

  return (
    <section aria-labelledby="main-view-heading" className="installed-content-view">
      <header className="view-heading installed-content-view__heading">
        <div>
          <span className="eyebrow">Effective application content</span>
          <h1 id="main-view-heading" tabIndex={-1}>Installed content</h1>
          <p>See active extensions and browse their effective additions and overrides without loading the core catalog.</p>
        </div>
      </header>

      {content ? (
        <section aria-labelledby="installed-sources-heading">
          <div className="installed-section-heading">
            <div>
              <span className="eyebrow">Active sources</span>
              <h2 id="installed-sources-heading">Core and extensions</h2>
            </div>
            <span>{content.extensions.length + 1} installed</span>
          </div>
          <div className="installed-extension-grid">
            <article className="installed-extension-card">
              <span className="content-badge content-badge--core">Core</span>
              <h3>D&amp;D 2024 core</h3>
              <p>The active base application content.</p>
            </article>
            {content.extensions.map((item) => (
              <article className="installed-extension-card" key={item.extensionId}>
                <span className={`content-badge content-badge--${item.classification}`}>
                  {BADGE_LABELS[item.classification]}
                </span>
                <h3>{item.displayName}</h3>
                <p>{item.description ?? "Description unavailable."}</p>
              </article>
            ))}
          </div>
        </section>
      ) : null}

      <section aria-labelledby="installed-contributions-heading" className="installed-content-list">
        <div className="installed-content-list__heading">
          <div>
            <span className="eyebrow">Extension contributions</span>
            <h2 id="installed-contributions-heading">Effective additions and overrides</h2>
          </div>
          {content ? <span>{content.totalCount === null
            ? `Showing ${records.length}; total unavailable`
            : `Showing ${records.length} of ${content.totalCount}`}</span> : null}
        </div>

        <form className="installed-content-filters" onSubmit={submitSearch} role="search">
          <label>
            <span>Source</span>
            <select value={filters.ownerId} onChange={(event) => replaceFilters({
              ...filters, ownerId: event.target.value, kind: "",
            })}>
              <option value="">All extensions</option>
              {(content?.extensions ?? []).map((item) => (
                <option key={item.extensionId} value={item.extensionId}>{item.displayName}</option>
              ))}
            </select>
          </label>
          <label>
            <span>Type</span>
            <select value={filters.kind} onChange={(event) => replaceFilters({
              ...filters, kind: event.target.value,
            })}>
              <option value="">All types</option>
              {(content?.availableKinds ?? []).map((kind) => (
                <option key={kind} value={kind}>{kind}</option>
              ))}
            </select>
          </label>
          <label className="installed-content-filters__search">
            <span>Search contributions</span>
            <input maxLength={256} onChange={(event) => setDraftQuery(event.target.value)}
              placeholder="Name, description, or source" type="search" value={draftQuery} />
          </label>
          <button className="secondary-button" type="submit">Search</button>
          {(filters.ownerId || filters.kind || filters.query) ? (
            <button className="text-button" onClick={() => {
              setDraftQuery("");
              replaceFilters(EMPTY_FILTERS);
            }} type="button">Clear filters</button>
          ) : null}
        </form>

        {error ? (
          <div className="installed-content-error" role="alert">
            <p>{error}</p>
            <button className="secondary-button" onClick={!chainInvalid && cursor ? retryCurrent : reloadList} type="button">
              {!chainInvalid && cursor ? "Retry page" : "Reload list"}
            </button>
          </div>
        ) : null}
        {!content && loading ? <p className="rules-notice" role="status">Loading extension contributions…</p> : null}
        {contentCoverage === "partial" ? <p className="rules-notice" role="status">
          Some descriptive contribution fields were unavailable; identity and paging evidence remain checked.
        </p> : null}
        {contentNotices.map((notice) => <p className="rules-notice" key={notice} role="status">{notice}</p>)}
        {duplicateRecords ? <p className="rules-notice" role="status">
          Duplicate contribution identities were omitted from this paged display.
        </p> : null}
        {content && records.length === 0 && !loading && !error ? (
          <p className="rules-notice">No extension contributions match these filters.</p>
        ) : null}

        {records.length > 0 ? (
          <div className="installed-content-records">
            {records.map((record) => (
              <article className="installed-content-record" data-record-id={record.id} key={record.id}>
                <div className="installed-content-record__meta">
                  <span className={`content-badge content-badge--${record.classification}`}>
                    {BADGE_LABELS[record.classification]}
                  </span>
                  <span className="content-kind">{record.isAdditive === null
                    ? "Contribution type unavailable" : record.isAdditive ? "Addition" : "Override"}</span>
                  <span className="content-kind">{record.kind}</span>
                </div>
                <div className="installed-content-record__body">
                  <h3>{record.name}</h3>
                  <p>{record.description ?? "Description unavailable."}</p>
                </div>
                <small>{record.sourceLabel ?? record.ownerId}{record.presentationRoles.length
                  ? ` · ${record.presentationRoles.join(" · ")}` : " · roles unavailable"}</small>
              </article>
            ))}
          </div>
        ) : null}

        {lastPage?.nextCursor && lastPage.nextCursor !== cursor ? (
          <button className="secondary-button installed-content-more" disabled={loading}
            onClick={() => setCursor(lastPage.nextCursor)} type="button">
            {loading ? "Loading next page…" : lastPage.totalCount === null
              ? `Load more (${records.length} loaded)`
              : `Load more (${records.length} of ${lastPage.totalCount})`}
          </button>
        ) : null}
      </section>
    </section>
  );
}
