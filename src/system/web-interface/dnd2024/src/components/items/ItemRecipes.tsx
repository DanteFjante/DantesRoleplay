import { useEffect, useState } from "react";
import { focusItemPanel } from "./ItemView";
import { ViewReadError } from "../../data/view-read-client";
import type { ItemDetailsRequest, ItemViewClient } from "../../server/item-view-client";
import { recipesKey, type ItemRecipesRequest, type ItemRecipesResult, type RecipeEntry, type RecipeGroup } from "../../server/item-recipes-client";
const availability: Record<RecipeEntry["availability"], string> = { "not-evaluated": "Availability not evaluated", available: "Requirements met", "requirements-not-met": "Requirements not met", "definition-incomplete": "Recipe definition incomplete" };
const reasons = { "inventory-bound": "Some inventory contents are outside this view.", "source-incomplete": "Some recorded recipe information is incomplete.", "dependency-unavailable": "Some supporting details are unavailable.", "page-limit": "More recipes are available on the next page.", "byte-limit": "More recipes are available on the next page." };
function Group({ group, title, next }: { group: RecipeGroup; title: string; next: () => void }) {
  return <section className="item-recipes__group" aria-label={title}><h2>{title}</h2>
    {group.state === "empty" ? <p>No known recipes in this group.</p> : null}
    {group.state === "partial" ? <div className="item-details__notice" role="status"><strong>Recipe list is partial</strong>
      <ul>{[...new Set(group.reasons.map(reason => reasons[reason]))].map(reason => <li key={reason}>{reason}</li>)}</ul></div> : null}
    {group.entries.map(entry => <article className="item-recipes__entry" key={entry.id}><h3>{entry.name}</h3>
      <p className="item-recipes__status">{availability[entry.availability]} · {entry.knowledgeState}</p>
      {entry.observerKnowledge !== null ? <p>Character knowledge: {entry.observerKnowledge}</p> : null}
      {entry.description ? <p>{entry.description}</p> : null}
      <dl className="item-details__facts">
        {entry.duration ? <div><dt>Duration</dt><dd>{entry.duration}</dd></div> : null}
        {entry.tools.length ? <div><dt>Tools</dt><dd>{entry.tools.join(", ")}</dd></div> : null}
      </dl>
      {([["Outputs", entry.outputs], ["Materials", entry.materials]] as const).map(([label, values]) => values.length ? <div key={label}><h4>{label}</h4><ul>{values.map((value, i) => <li key={i}>{value.quantity} × {value.name}</li>)}</ul></div> : null)}
      {entry.requirements.length ? <div><h4>Requirements</h4><ul>{entry.requirements.map((r, i) => <li key={i}>{r.label}: {typeof r.value === "boolean" ? r.value ? "Yes" : "No" : String(r.value)}{r.unit ? ` ${r.unit}` : ""}
        {r.observerKnowledge !== null ? <small className="item-details__knowledge">Character knowledge: {r.observerKnowledge}</small> : null}
        {r.sources.length ? <small className="item-details__knowledge">{r.sources.map(s => `${s.label} (${s.knowledgeState})`).join("; ")}</small> : null}
      </li>)}</ul></div> : null}
      {entry.sources.length ? <details className="item-details__sources"><summary>Sources</summary><ul>{entry.sources.map((s, i) => <li key={i}>{s.label} ({s.knowledgeState})</li>)}</ul></details> : null}
    </article>)}
    {group.nextOffset !== null ? <button type="button" onClick={next}>Next page: {title.toLowerCase()}</button> : null}
  </section>;
}
export function ItemRecipes({ client, request, active }: { client: ItemViewClient; request: ItemDetailsRequest; active: boolean }) {
  const [page, setPage] = useState({ makesOffset: 0, usesOffset: 0, expectedSourceRevision: null as string | null });
  const [retry, setRetry] = useState(0);
  const full: ItemRecipesRequest = { ...request, ...page }, key = recipesKey(client.identity, full);
  const [loaded, setLoaded] = useState<{ key: string; result: ItemRecipesResult } | null>(null);
  const [refreshFailed, setRefreshFailed] = useState(false);
  const current = loaded?.key === key ? loaded.result : null;
  useEffect(() => {
    if (!active) return;
    let live = true; let timer: ReturnType<typeof setTimeout> | undefined;
    const controller = new AbortController();
    const accept = (result: ItemRecipesResult, schedule = true) => {
      if (!live) return; setLoaded({ key, result });
      setRefreshFailed(false);
      if (schedule && result.status === "ready") timer = setTimeout(() => {
        if (live) setRetry((value) => value + 1);
      }, Math.max(0, result.expiresAt - Date.now()));
    };
    const cached = client.recipes.peek(full);
    if (cached) accept(cached.value);
    else {
      const previous = client.recipes.state(full);
      if ((previous.status === "ready" || previous.status === "stale") && previous.result.value.status === "ready")
        setLoaded({ key, result: previous.result.value });
      else setLoaded(null);
      void client.recipes.load(full, { signal: controller.signal }).then(value => accept(value.value)).catch((error) => {
        if (!live || error instanceof ViewReadError && error.category === "cancelled") return;
        const retained = client.recipes.state(full);
        if (retained.status === "stale" && retained.result.value.status === "ready") {
          setLoaded({ key, result: retained.result.value });
          setRefreshFailed(true);
        } else accept({ status: "unavailable", data: null }, false);
      });
    }
    return () => { live = false; clearTimeout(timer); controller.abort(); };
  }, [client, key, active, retry]);
  const data = current?.status === "ready" ? current.data : null;
  const refresh = () => { focusItemPanel(); client.recipes.invalidate(undefined, "manual"); setLoaded(null); setPage({ makesOffset: 0, usesOffset: 0, expectedSourceRevision: null }); setRetry(v => v + 1); };
  const next = (group: "makes" | "uses") => { if (!data || current?.status !== "ready" || data[group].nextOffset === null) return;
    focusItemPanel();
    setPage({ ...page, [group === "makes" ? "makesOffset" : "usesOffset"]: data[group].nextOffset, expectedSourceRevision: current.sourceRevision }); };
  if (!data) return <div role="status" aria-busy={!current}><h2>{!current ? "Loading known recipes" : current.status === "stale" || current.status === "ready" ? "Recipes need a refresh" : "Recipes unavailable"}</h2>
    <p>{!current ? "Reading the selected character’s recipe knowledge…" : "Refresh to read the current recipes. Previous recipe details are no longer shown."}</p>
    {current ? <button type="button" onClick={refresh}>Refresh recipes</button> : null}</div>;
  return <div className="item-recipes"><p>Recipes recorded in this character’s knowledge.</p>
    {refreshFailed ? <div className="item-details__notice" role="status"><strong>Could not refresh recipes</strong><p>The last available recipes remain visible.</p><button type="button" onClick={refresh}>Try again</button></div> : null}
    {(page.makesOffset > 0 || page.usesOffset > 0) ? <button type="button" onClick={refresh}>Back to first recipes</button> : null}
    <Group group={data.makes} title="Makes this item" next={() => next("makes")} />
    <Group group={data.uses} title="Uses this item" next={() => next("uses")} />
  </div>;
}
