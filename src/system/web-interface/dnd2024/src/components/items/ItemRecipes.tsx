import { useEffect, useState } from "react";
import { focusItemPanel } from "./ItemView";
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
  const current = loaded?.key === key ? loaded.result : null;
  useEffect(() => {
    if (!active) return;
    let live = true; let timer: ReturnType<typeof setTimeout> | undefined;
    const accept = (result: ItemRecipesResult) => {
      if (!live) return; setLoaded({ key, result });
      if (result.status === "ready") timer = setTimeout(() => { client.recipes.invalidate(full); if(live) setLoaded({ key, result: { status: "stale", data: null } }); }, Math.max(0, result.expiresAt - Date.now()));
    };
    if (document.visibilityState === "hidden") accept({ status: "stale", data: null });
    else {
      const cached = client.recipes.peek(full);
      if (cached?.value.status === "ready" && cached.value.expiresAt > Date.now()) accept(cached.value);
      else { setLoaded(null); void client.recipes.load(full).then(value => accept(value.value)).catch(() => accept({ status: "unavailable", data: null })); }
    }
    return () => { live = false; clearTimeout(timer); client.recipes.cancel(); };
  }, [client, key, active, retry]);
  const data = current?.status === "ready" && current.expiresAt > Date.now() ? current.data : null;
  const refresh = () => { focusItemPanel(); client.recipes.invalidate(); setLoaded(null); setPage({ makesOffset: 0, usesOffset: 0, expectedSourceRevision: null }); setRetry(v => v + 1); };
  const next = (group: "makes" | "uses") => { if (!data || current?.status !== "ready" || data[group].nextOffset === null) return;
    focusItemPanel();
    setPage({ ...page, [group === "makes" ? "makesOffset" : "usesOffset"]: data[group].nextOffset, expectedSourceRevision: current.sourceRevision }); };
  if (!data) return <div role="status" aria-busy={!current}><h2>{!current ? "Loading known recipes" : current.status === "stale" || current.status === "ready" ? "Recipes need a refresh" : "Recipes unavailable"}</h2>
    <p>{!current ? "Reading the selected character’s recipe knowledge…" : "Refresh to read the current recipes. Previous recipe details are no longer shown."}</p>
    {current ? <button type="button" onClick={refresh}>Refresh recipes</button> : null}</div>;
  return <div className="item-recipes"><p>Recipes recorded in this character’s knowledge.</p>
    {(page.makesOffset > 0 || page.usesOffset > 0) ? <button type="button" onClick={refresh}>Back to first recipes</button> : null}
    <Group group={data.makes} title="Makes this item" next={() => next("makes")} />
    <Group group={data.uses} title="Uses this item" next={() => next("uses")} />
  </div>;
}
