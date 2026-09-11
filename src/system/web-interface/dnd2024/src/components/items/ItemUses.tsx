import { useEffect, useState } from "react";
import { itemFacetKey, selectItemFacet, selectItemRefreshEpoch, useHubSelector } from "../../data/hub-store";
import { ViewReadError } from "../../data/view-read-client";
import type { ItemDetailsRequest } from "../../server/item-view-client";
import { type ItemUsesRequest, type ItemUsesResult, type UseEntry, type UseGroup } from "../../server/item-uses-client";
import type { ItemUsesLoader } from "./ConnectedItemView";
import { focusItemPanel } from "./ItemView";

const availability: Record<UseEntry["availability"], string> = { "not-evaluated": "Availability not evaluated", available: "Requirements met", "requirements-not-met": "Requirements not met", "definition-incomplete": "Activity definition incomplete" };
const execution: Record<UseEntry["executionSupport"], string> = { supported: "Execution supported", "adjudication-required": "DM adjudication required", unsupported: "Execution support not established" };
const reasons = { "inventory-bound": "Some inventory contents are outside this view.", "source-incomplete": "Some recorded use information is incomplete.", "dependency-unavailable": "Some supporting details are unavailable.", "page-limit": "More uses are available on the next page.", "byte-limit": "More uses are available on the next page." };
function UseList({ group, next }: { group: UseGroup; next: () => void }) {
  return <section aria-label="Known uses"><h2>Known uses</h2>
    {group.state === "empty" ? <p>No known uses are recorded.</p> : null}
    {group.state === "partial" ? <div className="item-details__notice" role="status"><strong>Use list is partial</strong><ul>{group.reasons.map(r => <li key={r}>{reasons[r]}</li>)}</ul></div> : null}
    {group.entries.map(entry => <article className="item-recipes__entry" key={entry.id}><h3>{entry.name}</h3>
      <p className="item-recipes__status">{entry.kind === "canonical-activity" ? "Canonical activity" : entry.kind === "recorded-application" ? "Recorded statement" : "Activity kind unavailable"} · {entry.knowledgeState}</p>
      <p>{execution[entry.executionSupport]} · {availability[entry.availability]}</p>
      {entry.observerKnowledge !== null ? <p>Character knowledge: {entry.observerKnowledge}</p> : null}
      {entry.description ? <p>{entry.description}</p> : null}
      {([["Costs", entry.costs], ["Requirements", entry.requirements]] as const).map(([title, values]) => values.length ? <div key={title}><h4>{title}</h4><dl className="item-details__facts">{values.map((r,i) => <div key={i}><dt>{r.label}</dt><dd>{typeof r.value === "boolean" ? r.value ? "Yes" : "No" : String(r.value)}{r.unit ? (' ' + r.unit) : ""}
        {r.observerKnowledge !== null ? <small className="item-details__knowledge">Character knowledge: {r.observerKnowledge}</small> : null}
        {r.sources.length ? <small className="item-details__knowledge">{r.sources.map(s => (s.label + ' (' + s.knowledgeState + ')')).join("; ")}</small> : null}
      </dd></div>)}</dl></div> : null)}
      {entry.effects.length ? <div><h4>Recorded effects</h4><ul>{entry.effects.map((effect,i) => <li key={i}>{effect}</li>)}</ul></div> : null}
      {entry.sources.length ? <details className="item-details__sources"><summary>Sources</summary><ul>{entry.sources.map((s,i) => <li key={i}>{s.label} ({s.knowledgeState})</li>)}</ul></details> : null}
    </article>)}
    {group.nextOffset !== null ? <button type="button" onClick={next}>Next page of uses</button> : null}
  </section>;
}
export function ItemUses({ request, scope, loadUses, active }: { request: ItemDetailsRequest; scope: string;
  loadUses?: ItemUsesLoader; active: boolean }) {
  const [page, setPage] = useState({ offset: 0, expectedSourceRevision: null as string | null });
  const [retry, setRetry] = useState(0);
  const [loading, setLoading] = useState(false);
  const [refreshFailed, setRefreshFailed] = useState(false);
  const full: ItemUsesRequest = { ...request, ...page };
  const key = itemFacetKey("uses", full);
  const current = useHubSelector(selectItemFacet(scope, key)) as ItemUsesResult | null;
  const refreshEpoch = useHubSelector(selectItemRefreshEpoch);
  const hasConfirmedResult = current !== null;
  useEffect(() => {
    if (!active) return;
    const controller = new AbortController(); setLoading(true);
    if (!loadUses) { setLoading(false); return () => controller.abort(); }
    void loadUses(full, controller.signal, retry === 0).then((value) => {
      if (!controller.signal.aborted) setRefreshFailed(value.status !== "ready");
    }).catch((error) => {
      if (!controller.signal.aborted && !(error instanceof ViewReadError && error.category === "cancelled")) setRefreshFailed(true);
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [active, scope, key, loadUses, retry, hasConfirmedResult, refreshEpoch]);
  const expiresAt = current?.status === "ready" ? current.expiresAt : null;
  useEffect(() => {
    if (!active || expiresAt === null) return;
    const timer = window.setTimeout(() => setRetry((value) => value + 1), Math.max(0, expiresAt - Date.now()));
    return () => window.clearTimeout(timer);
  }, [active, key, expiresAt]);
  const data = current?.status === "ready" ? current.data : null;
  const refresh = () => { focusItemPanel(); setRefreshFailed(false); setPage({ offset: 0, expectedSourceRevision: null }); setRetry(v => v + 1); };
  const next = () => { if (!data || current?.status !== "ready" || data.uses.nextOffset === null) return;
    focusItemPanel(); setPage({ offset: data.uses.nextOffset, expectedSourceRevision: current.sourceRevision }); };
  if (!data) return <div role="status" aria-busy={loading}><h2>{loading ? "Loading known uses" :
    current?.status === "forbidden" || current?.status === "unavailable" ? "Uses unavailable" : "Uses need a refresh"}</h2>
    <p>{loading ? "Reading uses known to the selected character…" : "Refresh to read the current uses. Previous use details are no longer shown."}</p>
    {!loading ? <button type="button" onClick={refresh}>Refresh uses</button> : null}</div>;
  return <div className="item-recipes"><p>Activities and statements available to this character. Execution support does not mean current requirements are met.</p>
    {refreshFailed ? <div className="item-details__notice" role="status"><strong>Could not refresh uses</strong><p>The last available uses remain visible.</p><button type="button" onClick={refresh}>Try again</button></div> : null}
    {page.offset > 0 ? <button type="button" onClick={refresh}>Back to first uses</button> : null}
    <UseList group={data.uses} next={next} />
  </div>;
}
