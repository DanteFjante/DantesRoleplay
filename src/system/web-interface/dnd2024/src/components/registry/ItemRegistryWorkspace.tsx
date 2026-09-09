import { FormEvent, useEffect, useMemo, useRef, useState } from "react";

import { navigateHubRoute } from "../../data/hub-route";
import { navigateItemRoute, readItemReturn, type ItemNavigationRoute,
  type RegistryItemRoute, type RegistryReturnContext } from "../../data/item-view-route";
import { ViewReadError } from "../../data/view-read-client";
import type { Perspective } from "../../data/hub-types";
import type { ItemDefinition, ItemDefinitionRequest, ItemRegistryPage,
  ItemRegistryRecord, ItemRegistryRequest } from "../../server/item-registry";
import { Icon } from "../Icon";
import { ItemDetails } from "../items/ItemDetails";
import { ItemView } from "../items/ItemView";
import { RecipeRegistryDetails, RecipeRegistryDirectory, type RecipeDefinitionLoader,
  type RecipeRegistryPageLoader } from "./RecipeRegistryWorkspace";

export type ItemRegistryPageLoader = (request: ItemRegistryRequest, signal: AbortSignal,
  preferCached?: boolean) => Promise<ItemRegistryPage>;
export type ItemDefinitionLoader = (request: ItemDefinitionRequest, signal: AbortSignal,
  preferCached?: boolean) => Promise<ItemDefinition>;

function cleanName(value: string) {
  return value.replace(/\s+\([^()]*(?:definition|record)\s+v\d+\)$/iu, "");
}

function listError(error: unknown) {
  if (error instanceof ViewReadError && error.category === "stale-data")
    return "The item registry changed while this page was loading. Reload the list to continue.";
  if (error instanceof ViewReadError && error.category === "incompatible-data")
    return "The registry returned an item list this version of the site cannot display.";
  return "The item registry could not be loaded. Check the connection and try again.";
}

function ItemRegistryList({ campaignId, perspective, loadPage, loadRecipePage }: {
  campaignId: string; perspective: Perspective; loadPage: ItemRegistryPageLoader;
  loadRecipePage?: RecipeRegistryPageLoader;
}) {
  const restored = readItemReturn(window.history.state);
  const returnContext = restored?.kind === "registry" && restored.campaignId === campaignId
    && restored.perspective === perspective ? restored : null;
  const [section, setSection] = useState<"items" | "recipes">(returnContext?.section ?? "items");
  const [query, setQuery] = useState(returnContext?.query ?? "");
  const [draftQuery, setDraftQuery] = useState(returnContext?.query ?? "");
  const [pages, setPages] = useState<ItemRegistryPage[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [restorePages, setRestorePages] = useState(returnContext?.pageCount ?? 1);
  const [requestVersion, setRequestVersion] = useState(0);
  const [loading, setLoading] = useState(section === "items");
  const [error, setError] = useState("");
  const preferCached = useRef(true);
  const restoredFocus = useRef(false);

  useEffect(() => {
    if (section !== "items") return;
    const controller = new AbortController();
    const firstPage = pages[0] ?? null;
    const request: ItemRegistryRequest = {
      query,
      cursor,
      expectedResolutionFingerprint: cursor ? firstPage?.resolutionFingerprint ?? null : null,
    };
    const useCache = preferCached.current;
    preferCached.current = true;
    setLoading(true);
    setError("");
    void loadPage(request, controller.signal, useCache).then((page) => {
      if (controller.signal.aborted) return;
      setPages((current) => cursor ? [...current, page] : [page]);
    }).catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(listError(reason));
    }).finally(() => {
      if (!controller.signal.aborted) setLoading(false);
    });
    return () => controller.abort();
    // Page accumulation must not retrigger the current cursor.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cursor, loadPage, query, requestVersion, section]);

  const records = useMemo(() => pages.flatMap((page) => page.records), [pages]);
  const lastPage = pages.at(-1) ?? null;

  useEffect(() => {
    if (section !== "items" || loading || error || pages.length === 0) return;
    if (pages.length < restorePages && lastPage?.nextCursor) setCursor(lastPage.nextCursor);
    else if (!restoredFocus.current && returnContext) {
      restoredFocus.current = true;
      window.requestAnimationFrame(() => {
        [...document.querySelectorAll<HTMLElement>("[data-registry-entry]")]
          .find((entry) => entry.dataset.registryEntry === returnContext.focusEntryId)
          ?.focus({ preventScroll: true });
        window.scrollTo({ top: returnContext.scrollY });
      });
    }
  }, [error, lastPage, loading, pages.length, restorePages, returnContext, section]);

  const replaceQuery = (next: string) => {
    restoredFocus.current = true;
    setPages([]);
    setCursor(null);
    setRestorePages(1);
    setQuery(next);
  };

  const submitSearch = (event: FormEvent) => {
    event.preventDefault();
    replaceQuery(draftQuery.trim());
  };

  const retry = () => {
    preferCached.current = false;
    setRequestVersion((value) => value + 1);
  };

  const openItem = (record: ItemRegistryRecord) => {
    const context: RegistryReturnContext = { kind: "registry", campaignId, perspective,
      section: "items", query,
      pageCount: Math.max(1, pages.length), focusEntryId: record.id, scrollY: window.scrollY };
    window.history.replaceState({ ...window.history.state, itemReturnContext: context }, "", window.location.href);
    navigateItemRoute({ kind: "registry-item", campaignId, perspective, itemId: record.id,
      collection: record.collection, contentFingerprint: record.contentFingerprint, tab: "details" }, false, context);
  };

  return <section className="party-registry" aria-labelledby="main-view-heading">
    <nav aria-label="Breadcrumb" className="party-registry__breadcrumbs"><ol>
      <li><button type="button" onClick={() => navigateHubRoute("party")}>Party</button></li>
      <li aria-current="page">Registry</li>
    </ol></nav>
    <header className="party-registry__heading">
      <div><span className="eyebrow">Party reference library</span>
        <h1 id="main-view-heading" tabIndex={-1}>Registry</h1>
        <p>Browse recorded definitions independently from the items anyone carries.</p></div>
      {section === "items" && pages[0] ? <strong>{records.length} of {pages[0].totalCount} loaded</strong> : null}
    </header>
    <nav aria-label="Registry sections" className="party-registry__tabs">
      <button aria-current={section === "items" ? "page" : undefined} onClick={() => setSection("items")} type="button">
        <Icon name="PackageSearch" size={18} /> Items
      </button>
      <button aria-current={section === "recipes" ? "page" : undefined} onClick={() => setSection("recipes")} type="button">
        <Icon name="CookingPot" size={18} /> Recipes
      </button>
    </nav>
    {section === "recipes" ? loadRecipePage
      ? <RecipeRegistryDirectory campaignId={campaignId} perspective={perspective} loadPage={loadRecipePage} />
      : <section className="party-registry__empty"><Icon name="CookingPot" size={28} />
        <div><h2>Recipe registry unavailable</h2><p>No bounded recipe directory is connected to this view.</p></div></section> : <>
      <div className="party-registry__filter-status" role="status">
        <span>Active view</span><strong>All recorded item definitions</strong>
        <small>Possession does not imply discovery or knowledge.</small>
      </div>
      <form className="party-registry__search" onSubmit={submitSearch} role="search">
        <label><span>Search items</span><span className="party-registry__search-field">
          <Icon name="Search" size={18} />
          <input maxLength={256} onChange={(event) => setDraftQuery(event.target.value)}
            placeholder="Name, source, or type" type="search" value={draftQuery} />
        </span></label>
        <button type="submit">Search</button>
        {query ? <button className="party-registry__clear" onClick={() => {
          setDraftQuery(""); replaceQuery("");
        }} type="button">Clear</button> : null}
      </form>
      {!pages.length && loading ? <p className="party-registry__notice" role="status">Loading item definitions…</p> : null}
      {error ? <div className="party-registry__notice party-registry__notice--error" role="alert">
        <p>{error}</p><button type="button" onClick={retry}>Try again</button>
      </div> : null}
      {pages.length && !records.length && !loading && !error ? <div className="party-registry__empty">
        <Icon name="PackageSearch" size={28} /><div><h2>No matching item definitions</h2>
          <p>Try a broader name or source.</p></div>
      </div> : null}
      {records.length ? <div className="party-registry__items" aria-label="Known item definitions">
        {records.map((record) => <button className="party-registry-card" data-registry-entry={record.id}
          key={record.id} onClick={() => openItem(record)} type="button">
          <span className="party-registry-card__icon"><Icon name="Package" size={22} /></span>
          <span className="party-registry-card__body"><small>{record.sourceLabel}</small>
            <strong>{cleanName(record.name)}</strong>
            <span>{record.classification === "core" ? "Core definition" : `${record.classification} definition`} · Record v{record.version}</span>
          </span>
          <span className="party-registry-card__action">View details <Icon name="ChevronRight" size={18} /></span>
        </button>)}
      </div> : null}
      {lastPage?.nextCursor ? <button className="party-registry__more" disabled={loading}
        onClick={() => setCursor(lastPage.nextCursor)} type="button">
        {loading ? "Loading next page…" : `Load more (${records.length} of ${lastPage.totalCount})`}
      </button> : null}
    </>}
  </section>;
}

function RegistryItemDetails({ route, loadDefinition, loadRecipePage }: {
  route: RegistryItemRoute; loadDefinition: ItemDefinitionLoader; loadRecipePage?: RecipeRegistryPageLoader;
}) {
  const fromRules = window.history.state?.itemMainTab === "rules";
  const [state, setState] = useState<"loading" | "ready" | "error" | "stale">("loading");
  const [definition, setDefinition] = useState<ItemDefinition | null>(null);
  const [retry, setRetry] = useState(0);
  const preferCached = useRef(true);
  useEffect(() => {
    const controller = new AbortController();
    setState("loading");
    void loadDefinition({ id: route.itemId, collection: route.collection,
      expectedContentFingerprint: route.contentFingerprint, sourceLabel: null }, controller.signal,
    preferCached.current).then((result) => {
      if (!controller.signal.aborted) { setDefinition(result); setState("ready"); }
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setState(error instanceof ViewReadError && error.category === "stale-data" ? "stale" : "error");
    });
    preferCached.current = true;
    return () => controller.abort();
  }, [loadDefinition, retry, route.collection, route.contentFingerprint, route.itemId]);
  const back = () => {
    const context = readItemReturn(window.history.state);
    if (context?.kind === "registry" || fromRules) window.history.back();
    else navigateHubRoute("party", "overview", true, { partySection: "registry" });
  };
  const retryRead = () => { preferCached.current = false; setDefinition(null); setRetry((value) => value + 1); };
  return <ItemView context="registry" tab={route.tab} onTab={(tab) => navigateItemRoute({ ...route,
    tab: tab === "recipes" ? "recipes" : "details" }, true, readItemReturn(window.history.state),
    fromRules ? "rules" : "party")} onBack={back}
    parent={fromRules ? { label: "Rules", onNavigate: back } : undefined}
    onParty={() => navigateHubRoute("party")} onRegistry={() => navigateHubRoute("party", "overview", false,
      { partySection: "registry" })} name={definition ? cleanName(definition.details.name) : undefined}
    details={definition ? <ItemDetails data={definition.details} scopeKey={`registry:${definition.record.contentFingerprint}`} />
      : <div className="party-registry__notice" role={state === "loading" ? "status" : "alert"} aria-busy={state === "loading"}>
        <h2>{state === "loading" ? "Loading item definition" : state === "stale" ? "Item definition changed" : "Item definition unavailable"}</h2>
        <p>{state === "loading" ? "Reading the selected registry record…"
          : state === "stale" ? "Return to the registry to open the current definition."
            : "This registry record could not be read."}</p>
        {state !== "loading" ? <button type="button" onClick={state === "stale" ? back : retryRead}>
          {state === "stale" ? "Back to current registry" : "Try again"}</button> : null}
      </div>}
    recipes={loadRecipePage ? <RecipeRegistryDirectory embedded campaignId={route.campaignId}
      perspective={route.perspective} relatedItemId={route.itemId} loadPage={loadRecipePage} />
      : <div className="party-registry__notice" role="status"><h2>Linked recipes unavailable</h2>
        <p>No bounded recipe directory is connected to this build.</p></div>} />;
}

export function ItemRegistryWorkspace({ route, campaignId, perspective, loadPage, loadDefinition,
  loadRecipePage, loadRecipeDefinition }: {
  route: ItemNavigationRoute; campaignId: string; perspective: Perspective;
  loadPage: ItemRegistryPageLoader; loadDefinition: ItemDefinitionLoader;
  loadRecipePage?: RecipeRegistryPageLoader; loadRecipeDefinition?: RecipeDefinitionLoader;
}) {
  if (route.kind === "registry-recipe") return loadRecipeDefinition
    ? <RecipeRegistryDetails route={route} loadDefinition={loadRecipeDefinition} />
    : <section className="view-unavailable" role="alert"><h1>Recipe unavailable</h1>
      <p>The recipe registry is not connected to this build.</p></section>;
  return route.kind === "registry-item"
    ? <RegistryItemDetails route={route} loadDefinition={loadDefinition} loadRecipePage={loadRecipePage} />
    : <ItemRegistryList campaignId={campaignId} perspective={perspective} loadPage={loadPage}
      loadRecipePage={loadRecipePage} />;
}
