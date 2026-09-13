import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useCollectionValue } from "../data/use-collection-value";
import { ViewReadError } from "../data/view-read-client";
import { LORE_CATEGORIES, LORE_KINDS, type LorePageLoader, type LorePageRequest, type WorldLorePage } from "../data/world-lore-page";
import { LoreCard } from "./LoreCard";

type PagePosition = Pick<LorePageRequest, "cursor" | "expectedSourceRevision" | "expectedGraphRevision" | "expectedSelectionFingerprint">;
const firstPage: PagePosition = { cursor: null };

type PagedWorldLoreProps = {
  worldName: string; scopeKey: string; loadPage: LorePageLoader;
  onOpenLocation: (id: string) => void; onOpenFaction: (id: string) => void; onOpenHistory: () => void;
};

export function PagedWorldLore(props: PagedWorldLoreProps) {
  return <LoreScope key={props.scopeKey} {...props} />;
}

function LoreScope({ worldName, scopeKey, loadPage, onOpenLocation, onOpenFaction, onOpenHistory }: PagedWorldLoreProps) {
  const [draft, setDraft] = useState("");
  const [filters, setFilters] = useState({ query: "", category: "", kind: "" });
  const [positions, setPositions] = useState<PagePosition[]>([firstPage]);
  const [pageIndex, setPageIndex] = useState(0);
  const [retry, setRetry] = useState(0);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState("");
  const [stale, setStale] = useState(false);
  const request = useMemo(() => ({ ...filters, ...(positions[pageIndex] ?? firstPage) }), [filters, positions, pageIndex]);
  const key = JSON.stringify(["world-lore-page", scopeKey, request]);
  const [page, setPage, epoch, fresh, ready, deny, , denied, invalidate] = useCollectionValue<WorldLorePage | null>(key, null);
  const previousEpoch = useRef(epoch);
  const force = useRef(false);

  useEffect(() => {
    if (stale) { setBusy(false); return; }
    if (previousEpoch.current !== epoch) {
      previousEpoch.current = epoch;
      force.current = true;
      if (request.cursor !== null) { setPositions([firstPage]); setPageIndex(0); return; }
    }
    if (denied) { setBusy(false); setError("Lore is unavailable for this viewing permission. Refresh the table to reconnect."); return; }
    if (!ready) return;
    if (fresh && page && !force.current) { setBusy(false); setError(""); return; }
    const controller = new AbortController();
    setBusy(true); setError("");
    const preferCached = !force.current;
    force.current = false;
    void loadPage(request, controller.signal, preferCached).then((value) => {
      if (controller.signal.aborted) return;
      setPage(value); setBusy(false);
    }).catch((failure: unknown) => {
      if (controller.signal.aborted) return;
      if (failure instanceof ViewReadError && failure.category === "authorization") deny();
      if (failure instanceof ViewReadError && failure.category === "stale-data") {
        setStale(true); invalidate();
      }
      setError(failure instanceof ViewReadError && failure.category === "stale-data"
        ? "Lore changed while browsing. Refresh to return to the first page."
        : "This lore page could not be loaded. Try again.");
      setBusy(false);
    });
    return () => controller.abort();
  }, [key, epoch, ready, denied, retry, fresh, loadPage, stale]);

  const changeFilters = (next: typeof filters) => {
    setFilters(next); setPositions([firstPage]); setPageIndex(0); setError("");
  };
  const submit = (event: FormEvent) => { event.preventDefault(); changeFilters({ ...filters, query: draft.trim() }); };
  const refresh = () => {
    force.current = true; setStale(false); setPositions([firstPage]); setPageIndex(0); setRetry((value) => value + 1);
  };
  const next = () => {
    if (!page?.nextCursor || busy) return;
    const position: PagePosition = { cursor: page.nextCursor, expectedSourceRevision: page.sourceRevision,
      expectedGraphRevision: page.graphRevision, expectedSelectionFingerprint: page.selectionFingerprint };
    setPositions((values) => [...values.slice(0, pageIndex + 1), position]); setPageIndex((value) => value + 1);
  };
  const filterable = page?.filterable === true;
  const entries = denied ? [] : page?.entries ?? [];

  return <div className="world-directory-view" aria-busy={busy}>
    <header className="atlas-heading"><div><span className="eyebrow">An encyclopedia of {worldName}</span>
      <h1 id="main-view-heading" tabIndex={-1}>Lore</h1></div>
      <p>{page ? `${entries.length} entries on page ${pageIndex + 1}${page.totalCount === null ? "" : ` · ${page.totalCount} matching entries`}` : "Loading lore page…"}</p>
    </header>
    <p className="world-directory-introduction">Browse the world's histories, places, relationships, and recorded knowledge.</p>
    {filterable ? <form className="world-directory-controls" role="search" aria-label="Search world lore" onSubmit={submit}>
      <label className="world-directory-search"><span>Search world lore</span><input type="search" maxLength={120}
        placeholder="Search titles and summaries" value={draft} onChange={(event) => setDraft(event.target.value)} /></label>
      <label><span>Category</span><select value={filters.category} onChange={(event) => changeFilters({ ...filters, category: event.target.value })}>
        <option value="">All categories</option>{Object.entries(LORE_CATEGORIES).map(([value, label]) =>
          <option key={value} value={value}>{label}{page?.facets.category[value] === undefined ? "" : ` (${page.facets.category[value]})`}</option>)}</select></label>
      <label><span>Kind</span><select value={filters.kind} onChange={(event) => changeFilters({ ...filters, kind: event.target.value })}>
        <option value="">All kinds</option>{Object.entries(LORE_KINDS).map(([value, label]) =>
          <option key={value} value={value}>{label}{page?.facets.kind[value] === undefined ? "" : ` (${page.facets.kind[value]})`}</option>)}</select></label>
      <button type="submit" disabled={busy}>Search</button>
      {filters.query || filters.category || filters.kind ? <button type="button" onClick={() => {
        setDraft(""); changeFilters({ query: "", category: "", kind: "" });
      }}>Clear filters</button> : null}
    </form> : null}
    <nav className="world-directory-controls" aria-label="Lore pages">
      <button type="button" disabled={busy || stale || pageIndex === 0} onClick={() => setPageIndex((value) => Math.max(0, value - 1))}>Previous page</button>
      <span>Page {pageIndex + 1}</span>
      <button type="button" disabled={busy || stale || !page?.nextCursor} onClick={next}>Next page</button>
      <button type="button" disabled={busy} onClick={refresh}>Refresh lore</button>
    </nav>
    {error ? <section role="alert"><p>{error}</p><button type="button" onClick={refresh}>Retry lore</button></section>
      : busy ? <p role="status">Loading lore page…</p>
      : page?.coverage === "partial" ? <p role="status">Some fields in this page are unavailable.</p> : null}
    {entries.length ? <div className="lore-grid">{entries.map((entry) => <LoreCard key={entry.id} entry={entry}
      onOpenLocation={onOpenLocation} onOpenFaction={onOpenFaction} onOpenHistory={onOpenHistory} />)}</div>
      : !busy && !error ? <p>{page?.nextCursor
        ? "This page has no visible entries. The next page may contain more lore."
        : page?.coverage === "partial" ? "This page has no readable entries. Other pages may contain more lore."
        : "No lore matches these filters."}</p> : null}
  </div>;
}
