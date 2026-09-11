import { useEffect, useState } from "react";
import { focusItemPanel } from "./ItemView";
import { ViewReadError } from "../../data/view-read-client";
import { itemFacetKey, selectItemFacet, selectItemRefreshEpoch, useHubSelector } from "../../data/hub-store";
import type { ItemDetailsRequest } from "../../server/item-view-client";
import { type ItemRecipesRequest, type ItemRecipesResult, type RecipeEntry, type RecipeGroup } from "../../server/item-recipes-client";
import type { ItemRecipesLoader } from "./ConnectedItemView";
const availability: Record<RecipeEntry["availability"], string> = { "not-evaluated": "Availability not evaluated", available: "Requirements met", "requirements-not-met": "Requirements not met", "definition-incomplete": "Recipe definition incomplete" };
const reasons = { "inventory-bound": "Some inventory contents are outside this view.", "source-incomplete": "Some recorded recipe information is incomplete.", "dependency-unavailable": "Some supporting details are unavailable.", "page-limit": "More recipes are available on the next page.", "byte-limit": "More recipes are available on the next page." };

export function RecipeEntryPresentation({ entry, linkedDefinitionIds, onDefinition, materialLabel = "Materials" }: {
  entry: RecipeEntry; linkedDefinitionIds?: ReadonlySet<string>;
  onDefinition?: (definitionId: string) => void; materialLabel?: string;
}) {
  const linkedValue = (value: RecipeEntry["outputs"][number], index: number) =>
    value.definitionId && linkedDefinitionIds?.has(value.definitionId) && onDefinition
      ? <li key={`${value.definitionId}:${index}`}><button className="item-recipes__definition-link" type="button"
        onClick={() => onDefinition(value.definitionId!)}>{value.quantity} × {value.name}</button></li>
      : <li key={`${value.definitionId ?? value.name}:${index}`}>{value.quantity} × {value.name}
        {value.definitionId && linkedDefinitionIds
          ? <small className="item-details__knowledge">Definition unavailable</small> : null}</li>;
  return <article className="item-recipes__entry"><h3>{entry.name}</h3>
    <p className="item-recipes__status">{availability[entry.availability]} · {entry.knowledgeState}</p>
    {entry.observerKnowledge !== null ? <p>Character knowledge: {entry.observerKnowledge}</p> : null}
    {entry.description ? <p>{entry.description}</p> : null}
    <dl className="item-details__facts">
      {entry.duration ? <div><dt>Duration</dt><dd>{entry.duration}</dd></div> : null}
      {entry.tools.length ? <div><dt>Tools</dt><dd>{entry.tools.join(", ")}</dd></div> : null}
    </dl>
    {([ ["Outputs", entry.outputs], [materialLabel, entry.materials] ] as const).map(([label, values]) => values.length
      ? <div key={label}><h4>{label}</h4><ul>{values.map(linkedValue)}</ul></div> : null)}
    {entry.requirements.length ? <div><h4>Requirements</h4><ul>{entry.requirements.map((r, i) => <li key={i}>{r.label}: {typeof r.value === "boolean" ? r.value ? "Yes" : "No" : String(r.value)}{r.unit ? ` ${r.unit}` : ""}
      {r.observerKnowledge !== null ? <small className="item-details__knowledge">Character knowledge: {r.observerKnowledge}</small> : null}
      {r.sources.length ? <small className="item-details__knowledge">{r.sources.map(s => `${s.label} (${s.knowledgeState})`).join("; ")}</small> : null}
    </li>)}</ul></div> : null}
    {entry.sources.length ? <details className="item-details__sources"><summary>Sources</summary><ul>{entry.sources.map((s, i) => <li key={i}>{s.label} ({s.knowledgeState})</li>)}</ul></details> : null}
  </article>;
}

function Group({ group, title, next }: { group: RecipeGroup; title: string; next: () => void }) {
  return <section className="item-recipes__group" aria-label={title}><h2>{title}</h2>
    {group.state === "empty" ? <p>No known recipes in this group.</p> : null}
    {group.state === "partial" ? <div className="item-details__notice" role="status"><strong>Recipe list is partial</strong>
      <ul>{[...new Set(group.reasons.map(reason => reasons[reason]))].map(reason => <li key={reason}>{reason}</li>)}</ul></div> : null}
    {group.entries.map(entry => <RecipeEntryPresentation entry={entry} key={entry.id} />)}
    {group.nextOffset !== null ? <button type="button" onClick={next}>Next page: {title.toLowerCase()}</button> : null}
  </section>;
}
export function ItemRecipes({ request, scope, loadRecipes, active }: { request: ItemDetailsRequest; scope: string;
  loadRecipes?: ItemRecipesLoader; active: boolean }) {
  const [page, setPage] = useState({ makesOffset: 0, usesOffset: 0, expectedSourceRevision: null as string | null });
  const [retry, setRetry] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshFailed, setRefreshFailed] = useState(false);
  const full: ItemRecipesRequest = { ...request, ...page }, key = itemFacetKey("recipes", full);
  const current = useHubSelector(selectItemFacet(scope, key)) as ItemRecipesResult | null;
  const refreshEpoch = useHubSelector(selectItemRefreshEpoch);
  const hasConfirmedResult = current !== null;
  useEffect(() => {
    if (!active) return;
    const controller = new AbortController();
    setLoading(true);
    if (!loadRecipes) { setLoading(false); return () => controller.abort(); }
    void loadRecipes(full, controller.signal, retry === 0).then((value) => {
      if (!controller.signal.aborted) setRefreshFailed(value.status !== "ready");
    }).catch((error) => {
      if (!controller.signal.aborted && !(error instanceof ViewReadError && error.category === "cancelled")) setRefreshFailed(true);
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [active, scope, key, loadRecipes, retry, hasConfirmedResult, refreshEpoch]);
  const expiresAt = current?.status === "ready" ? current.expiresAt : null;
  useEffect(() => {
    if (!active || expiresAt === null) return;
    const timer = window.setTimeout(() => setRetry((value) => value + 1), Math.max(0, expiresAt - Date.now()));
    return () => window.clearTimeout(timer);
  }, [active, key, expiresAt]);
  const data = current?.status === "ready" ? current.data : null;
  const refresh = () => { focusItemPanel(); setRefreshFailed(false); setPage({ makesOffset: 0, usesOffset: 0, expectedSourceRevision: null }); setRetry(v => v + 1); };
  const next = (group: "makes" | "uses") => { if (!data || current?.status !== "ready" || data[group].nextOffset === null) return;
    focusItemPanel();
    setPage({ ...page, [group === "makes" ? "makesOffset" : "usesOffset"]: data[group].nextOffset, expectedSourceRevision: current.sourceRevision }); };
  if (!data) return <div role="status" aria-busy={loading}><h2>{loading ? "Loading known recipes" : current?.status === "forbidden" ? "Recipes unavailable" : "Recipes need a refresh"}</h2>
    <p>{loading ? "Reading the selected character’s recipe knowledge…" : "Refresh to read the current recipes. Previous recipe details are no longer shown."}</p>
    {!loading ? <button type="button" onClick={refresh}>Refresh recipes</button> : null}</div>;
  return <div className="item-recipes"><p>Recipes recorded in this character’s knowledge.</p>
    {refreshFailed ? <div className="item-details__notice" role="status"><strong>Could not refresh recipes</strong><p>The last available recipes remain visible.</p><button type="button" onClick={refresh}>Try again</button></div> : null}
    {(page.makesOffset > 0 || page.usesOffset > 0) ? <button type="button" onClick={refresh}>Back to first recipes</button> : null}
    <Group group={data.makes} title="Makes this item" next={() => next("makes")} />
    <Group group={data.uses} title="Uses this item" next={() => next("uses")} />
  </div>;
}
