import { FormEvent, useEffect, useMemo, useRef, useState } from "react";

import { ViewReadError } from "../data/view-read-client";
import type {
  InstalledContentPage,
  InstalledContentRequest,
} from "../server/effective-content";

const BADGE_LABELS = {
  homebrew: "Homebrew",
  compatibility: "Compatibility",
  "third-party": "Third-party",
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
}: {
  loadContent: InstalledContentLoader;
  resolutionFingerprint: string | null;
}) {
  const [filters, setFilters] = useState<Filters>(EMPTY_FILTERS);
  const [draftQuery, setDraftQuery] = useState("");
  const [pages, setPages] = useState<InstalledContentPage[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [requestVersion, setRequestVersion] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const preferCached = useRef(true);

  useEffect(() => {
    const controller = new AbortController();
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
    setLoading(true);
    setError("");
    void loadContent(request, controller.signal, useCache).then((page) => {
      if (controller.signal.aborted) return;
      if (cursor && firstPage && (page.resolutionFingerprint !== firstPage.resolutionFingerprint
          || !sameExtensions(firstPage, page))) {
        setError("Installed content changed while this page was loading. Reload the list to continue.");
        return;
      }
      setPages((current) => cursor ? [...current, page] : [page]);
    }).catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(errorMessage(reason));
    }).finally(() => {
      if (!controller.signal.aborted) setLoading(false);
    });
    return () => controller.abort();
    // A filter transition resets pages before this effect executes. Pages must not retrigger reads.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cursor, filters, loadContent, requestVersion, resolutionFingerprint]);

  const content = pages[0] ?? null;
  const records = useMemo(() => pages.flatMap((page) => page.records), [pages]);
  const lastPage = pages.at(-1) ?? null;

  const replaceFilters = (next: Filters) => {
    setPages([]);
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
    preferCached.current = false;
    setPages([]);
    setCursor(null);
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
                <p>{item.description}</p>
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
          {content ? <span>Showing {records.length} of {content.totalCount}</span> : null}
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
            <button className="secondary-button" onClick={cursor ? retryCurrent : reloadList} type="button">
              {cursor ? "Retry page" : "Reload list"}
            </button>
          </div>
        ) : null}
        {!content && loading ? <p className="rules-notice" role="status">Loading extension contributions…</p> : null}
        {content && records.length === 0 && !loading && !error ? (
          <p className="rules-notice">No extension contributions match these filters.</p>
        ) : null}

        {records.length > 0 ? (
          <div className="installed-content-records">
            {records.map((record) => (
              <article className="installed-content-record" key={record.id}>
                <div className="installed-content-record__meta">
                  <span className={`content-badge content-badge--${record.classification}`}>
                    {BADGE_LABELS[record.classification]}
                  </span>
                  <span className="content-kind">{record.isAdditive ? "Addition" : "Override"}</span>
                  <span className="content-kind">{record.kind}</span>
                </div>
                <div className="installed-content-record__body">
                  <h3>{record.name}</h3>
                  <p>{record.description}</p>
                </div>
                <small>{record.sourceLabel} · {record.presentationRoles.join(" · ")}</small>
              </article>
            ))}
          </div>
        ) : null}

        {lastPage?.nextCursor ? (
          <button className="secondary-button installed-content-more" disabled={loading}
            onClick={() => setCursor(lastPage.nextCursor)} type="button">
            {loading ? "Loading next page…" : `Load more (${records.length} of ${lastPage.totalCount})`}
          </button>
        ) : null}
      </section>
    </section>
  );
}
