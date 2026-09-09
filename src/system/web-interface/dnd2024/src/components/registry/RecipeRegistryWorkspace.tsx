import { FormEvent, useEffect, useMemo, useRef, useState } from "react";

import { navigateHubRoute } from "../../data/hub-route";
import { navigateItemRoute, readItemReturn, type RegistryRecipeRoute,
  type RegistryReturnContext } from "../../data/item-view-route";
import type { Perspective } from "../../data/hub-types";
import { ViewReadError } from "../../data/view-read-client";
import type { RecipeDefinition, RecipeDefinitionRequest, RecipeRegistryPage,
  RecipeRegistryRecord, RecipeRegistryRequest } from "../../server/recipe-registry";
import { Icon } from "../Icon";
import { RecipeEntryPresentation } from "../items/ItemRecipes";

export type RecipeRegistryPageLoader = (request: RecipeRegistryRequest, signal: AbortSignal,
  preferCached?: boolean) => Promise<RecipeRegistryPage>;
export type RecipeDefinitionLoader = (request: RecipeDefinitionRequest, signal: AbortSignal,
  preferCached?: boolean) => Promise<RecipeDefinition>;

function listError(error: unknown) {
  if (error instanceof ViewReadError && error.category === "stale-data")
    return "The recipe registry changed while this page was loading. Reload the list to continue.";
  if (error instanceof ViewReadError && error.category === "incompatible-data")
    return "The registry returned recipe data this version of the site cannot display.";
  return "The recipe registry could not be loaded. Check the connection and try again.";
}

function cleanName(value: string) {
  return value.replace(/\s+\([^()]*(?:definition|record)\s+v\d+\)$/iu, "");
}

export function RecipeRegistryDirectory({ campaignId, perspective, loadPage, relatedItemId = null,
  embedded = false }: { campaignId: string; perspective: Perspective; loadPage: RecipeRegistryPageLoader;
    relatedItemId?: string | null; embedded?: boolean }) {
  const restored = readItemReturn(window.history.state);
  const returnContext = !embedded && restored?.kind === "registry" && restored.section === "recipes"
    && restored.campaignId === campaignId && restored.perspective === perspective ? restored : null;
  const [query, setQuery] = useState(returnContext?.query ?? "");
  const [draftQuery, setDraftQuery] = useState(returnContext?.query ?? "");
  const [pages, setPages] = useState<RecipeRegistryPage[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [restorePages, setRestorePages] = useState(returnContext?.pageCount ?? 1);
  const [requestVersion, setRequestVersion] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const preferCached = useRef(true);
  const restoredFocus = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    const firstPage = pages[0] ?? null;
    const request: RecipeRegistryRequest = { query, cursor, relatedItemId,
      expectedResolutionFingerprint: cursor ? firstPage?.resolutionFingerprint ?? null : null };
    const useCache = preferCached.current;
    preferCached.current = true;
    setLoading(true); setError("");
    void loadPage(request, controller.signal, useCache).then((page) => {
      if (!controller.signal.aborted) setPages((current) => cursor ? [...current, page] : [page]);
    }).catch((reason: unknown) => {
      if (!controller.signal.aborted) setError(listError(reason));
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
    // Page accumulation must not retrigger the current cursor.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cursor, loadPage, query, relatedItemId, requestVersion]);

  const records = useMemo(() => pages.flatMap((page) => page.records), [pages]);
  const lastPage = pages.at(-1) ?? null;
  useEffect(() => {
    if (embedded || loading || error || pages.length === 0) return;
    if (pages.length < restorePages && lastPage?.nextCursor) setCursor(lastPage.nextCursor);
    else if (!restoredFocus.current && returnContext) {
      restoredFocus.current = true;
      window.requestAnimationFrame(() => {
        [...document.querySelectorAll<HTMLElement>("[data-recipe-entry]")]
          .find((entry) => entry.dataset.recipeEntry === returnContext.focusEntryId)
          ?.focus({ preventScroll: true });
        window.scrollTo({ top: returnContext.scrollY });
      });
    }
  }, [embedded, error, lastPage, loading, pages.length, restorePages, returnContext]);

  const replaceQuery = (next: string) => {
    restoredFocus.current = true; setPages([]); setCursor(null); setRestorePages(1); setQuery(next);
  };
  const submitSearch = (event: FormEvent) => { event.preventDefault(); replaceQuery(draftQuery.trim()); };
  const retry = () => { preferCached.current = false; setRequestVersion((value) => value + 1); };
  const openRecipe = (record: RecipeRegistryRecord) => {
    let context = readItemReturn(window.history.state);
    if (!embedded) {
      context = { kind: "registry", campaignId, perspective, section: "recipes", query,
        pageCount: Math.max(1, pages.length), focusEntryId: record.id,
        scrollY: window.scrollY } satisfies RegistryReturnContext;
      window.history.replaceState({ ...window.history.state, itemReturnContext: context }, "", window.location.href);
    }
    navigateItemRoute({ kind: "registry-recipe", campaignId, perspective, recipeId: record.id,
      collection: record.collection, contentFingerprint: record.contentFingerprint }, false, context);
  };

  return <section className={embedded ? "recipe-registry recipe-registry--embedded" : "recipe-registry"}
    aria-label={embedded ? "Recipes involving this item" : "Recipe definitions"}>
    {embedded ? <header className="recipe-registry__embedded-heading"><div><span className="eyebrow">Cross-references</span>
      <h2>Recipes involving this item</h2></div>
      {pages[0] ? <strong>{records.length} of {pages[0].totalCount} loaded</strong> : null}</header> : <>
      <div className="party-registry__filter-status" role="status"><span>Active view</span>
        <strong>All recorded recipe definitions{pages[0] ? ` · ${records.length} of ${pages[0].totalCount} loaded` : ""}</strong>
        <small>Recipes are listed independently of carried items.</small></div>
      <form className="party-registry__search" onSubmit={submitSearch} role="search">
        <label><span>Search recipes</span><span className="party-registry__search-field"><Icon name="Search" size={18} />
          <input maxLength={256} onChange={(event) => setDraftQuery(event.target.value)}
            placeholder="Name, source, input, or output" type="search" value={draftQuery} /></span></label>
        <button type="submit">Search</button>
        {query ? <button className="party-registry__clear" onClick={() => { setDraftQuery(""); replaceQuery(""); }}
          type="button">Clear</button> : null}
      </form>
    </>}
    {!pages.length && loading ? <p className="party-registry__notice" role="status">Loading recipe definitions…</p> : null}
    {error ? <div className="party-registry__notice party-registry__notice--error" role="alert">
      <p>{error}</p><button type="button" onClick={retry}>Try again</button></div> : null}
    {pages.length && !records.length && !loading && !error ? <div className="party-registry__empty">
      <Icon name="CookingPot" size={28} /><div><h2>{embedded ? "No linked recipes" : "No matching recipes"}</h2>
        <p>{embedded ? "No recorded recipe refers to this item definition." : "Try a broader name, source, input, or output."}</p></div>
    </div> : null}
    {records.length ? <div className="party-registry__items" aria-label="Recorded recipe definitions">
      {records.map((record) => <button className="party-registry-card party-registry-card--recipe"
        data-recipe-entry={record.id} key={record.id} onClick={() => openRecipe(record)} type="button">
        <span className="party-registry-card__icon"><Icon name="CookingPot" size={22} /></span>
        <span className="party-registry-card__body"><small>{record.sourceLabel}</small>
          <strong>{cleanName(record.name)}</strong><span>Readable recipe · Record v{record.version}</span></span>
        <span className="party-registry-card__action">View recipe <Icon name="ChevronRight" size={18} /></span>
      </button>)}
    </div> : null}
    {lastPage?.nextCursor ? <button className="party-registry__more" disabled={loading}
      onClick={() => setCursor(lastPage.nextCursor)} type="button">
      {loading ? "Loading next page…" : `Load more (${records.length} of ${lastPage.totalCount})`}</button> : null}
  </section>;
}

export function RecipeRegistryDetails({ route, loadDefinition }: {
  route: RegistryRecipeRoute; loadDefinition: RecipeDefinitionLoader;
}) {
  const [state, setState] = useState<"loading" | "ready" | "error" | "stale">("loading");
  const [definition, setDefinition] = useState<RecipeDefinition | null>(null);
  const [retry, setRetry] = useState(0);
  const preferCached = useRef(true);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, [route.recipeId]);
  useEffect(() => {
    const controller = new AbortController(); setState("loading");
    void loadDefinition({ id: route.recipeId, collection: route.collection,
      expectedContentFingerprint: route.contentFingerprint, sourceLabel: null }, controller.signal,
    preferCached.current).then((result) => {
      if (!controller.signal.aborted) { setDefinition(result); setState("ready"); }
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setState(error instanceof ViewReadError && error.category === "stale-data" ? "stale" : "error");
    });
    preferCached.current = true;
    return () => controller.abort();
  }, [loadDefinition, retry, route.collection, route.contentFingerprint, route.recipeId]);
  const back = () => readItemReturn(window.history.state)?.kind === "registry" ? window.history.back()
    : navigateHubRoute("party", "overview", true, { partySection: "registry" });
  const retryRead = () => { preferCached.current = false; setDefinition(null); setRetry((value) => value + 1); };
  const linked = new Map(definition?.linkedItems.map((item) => [item.id, item]) ?? []);
  const openItem = (definitionId: string) => {
    const item = linked.get(definitionId);
    if (!item) return;
    navigateItemRoute({ kind: "registry-item", campaignId: route.campaignId, perspective: route.perspective,
      itemId: item.id, collection: item.collection, contentFingerprint: item.contentFingerprint, tab: "details" },
    false, readItemReturn(window.history.state));
  };
  return <section className="recipe-page" aria-labelledby="recipe-view-heading">
    <nav aria-label="Breadcrumb" className="item-page__breadcrumbs"><ol>
      <li><button type="button" onClick={() => navigateHubRoute("party")}>Party</button></li>
      <li><button type="button" onClick={() => navigateHubRoute("party", "overview", false,
        { partySection: "registry" })}>Registry</button></li>
      <li><button type="button" onClick={back}>Recipes</button></li>
      <li aria-current="page">{definition ? cleanName(definition.entry.name) : "Recipe details"}</li>
    </ol></nav>
    <header><span className="eyebrow">Crafting recipe registry</span>
      <h2 id="recipe-view-heading" ref={heading} tabIndex={-1}>{definition ? cleanName(definition.entry.name) : "Recipe"}</h2></header>
    {definition ? <div className="item-recipes"><p className="recipe-page__scope">Shared table record. Requirements are readable references, not an eligibility check.</p>
      <RecipeEntryPresentation entry={definition.entry} linkedDefinitionIds={new Set(linked.keys())}
        onDefinition={openItem} materialLabel="Inputs" /></div>
      : <div className="party-registry__notice" role={state === "loading" ? "status" : "alert"} aria-busy={state === "loading"}>
        <h2>{state === "loading" ? "Loading recipe" : state === "stale" ? "Recipe changed" : "Recipe unavailable"}</h2>
        <p>{state === "loading" ? "Reading the selected recipe record…"
          : state === "stale" ? "Return to the Registry to open the current recipe."
            : "This recipe record could not be read."}</p>
        {state !== "loading" ? <button type="button" onClick={state === "stale" ? back : retryRead}>
          {state === "stale" ? "Back to current registry" : "Try again"}</button> : null}
      </div>}
  </section>;
}
