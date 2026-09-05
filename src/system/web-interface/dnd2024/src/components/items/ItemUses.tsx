import { useEffect, useState } from "react";
import type { ItemDetailsRequest, ItemViewClient } from "../../server/item-view-client";
import { usesKey, type ItemUsesRequest, type ItemUsesResult, type UseEntry, type UseGroup } from "../../server/item-uses-client";
const availability: Record<UseEntry["availability"], string> = { "not-evaluated": "Availability not evaluated", available: "Requirements met", "requirements-not-met": "Requirements not met", "definition-incomplete": "Activity definition incomplete" };
const execution: Record<UseEntry["executionSupport"], string> = { supported: "Execution supported", "adjudication-required": "DM adjudication required", unsupported: "Execution support not established" };
const reasons = { "inventory-bound": "Some inventory contents are outside this view.", "source-incomplete": "Some recorded use information is incomplete.", "dependency-unavailable": "Some supporting details are unavailable.", "page-limit": "More uses are available on the next page.", "byte-limit": "More uses are available on the next page." };
function UseList({ group, next }: { group: UseGroup; next: () => void }) {
  return <section aria-label="Known uses"><h2>Known uses</h2>
    {group.state === "empty" ? <p>No known uses are recorded.</p> : null}
    {group.state === "partial" ? <div className="item-details__notice" role="status"><strong>Use list is partial</strong><ul>{group.reasons.map(r => <li key={r}>{reasons[r]}</li>)}</ul></div> : null}
    {group.entries.map(entry => <article className="item-recipes__entry" key={entry.id}><h3>{entry.name}</h3>
      <p className="item-recipes__status">{entry.kind === "canonical-activity" ? "Canonical activity" : "Recorded statement"} · {entry.knowledgeState}</p>
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
export function ItemUses({ client, request, active }: { client: ItemViewClient; request: ItemDetailsRequest; active: boolean }) {
  const [page, setPage] = useState({ offset: 0, expectedSourceRevision: null as string | null });
  const [retry, setRetry] = useState(0);
  const full: ItemUsesRequest = { ...request, ...page }, key = usesKey(client.identity, full);
  const [loaded, setLoaded] = useState<{ key: string; result: ItemUsesResult } | null>(null);
  const current = loaded?.key === key ? loaded.result : null;
  useEffect(() => {
    if (!active) return;
    let live = true; let timer: ReturnType<typeof setTimeout> | undefined;
    const accept = (result: ItemUsesResult) => {
      if (!live) return; setLoaded({ key, result });
      if (result.status === "ready") timer = setTimeout(() => { client.uses.invalidate(full); if(live) setLoaded({ key, result: { status: "stale", data: null } }); }, Math.max(0, result.expiresAt - Date.now()));
    };
    if (document.visibilityState === "hidden") accept({ status: "stale", data: null });
    else {
      const cached = client.uses.peek(full);
      if (cached?.value.status === "ready" && cached.value.expiresAt > Date.now()) accept(cached.value);
      else { setLoaded(null); void client.uses.load(full).then(value => accept(value.value)).catch(() => accept({ status: "unavailable", data: null })); }
    }
    return () => { live = false; clearTimeout(timer); client.uses.cancel(); };
  }, [client, key, active, retry]);
  const data = current?.status === "ready" && current.expiresAt > Date.now() ? current.data : null;
  const refresh = () => { client.uses.invalidate(); setLoaded(null); setPage({ offset: 0, expectedSourceRevision: null }); setRetry(v => v + 1); };
  const next = () => { if (!data || current?.status !== "ready" || data.uses.nextOffset === null) return;
    setPage({ offset: data.uses.nextOffset, expectedSourceRevision: current.sourceRevision }); };
  if (!data) return <div role="status" aria-busy={!current}><h2>{!current ? "Loading known uses" : current.status === "stale" || current.status === "ready" ? "Uses need a refresh" : "Uses unavailable"}</h2>
    <p>{!current ? "Reading uses known to the selected character…" : "Refresh to read the current uses. Previous use details are no longer shown."}</p>
    {current ? <button type="button" onClick={refresh}>Refresh uses</button> : null}</div>;
  return <div className="item-recipes"><p>Activities and statements available to this character. Execution support does not mean current requirements are met.</p>
    {page.offset > 0 ? <button type="button" onClick={refresh}>Back to first uses</button> : null}
    <UseList group={data.uses} next={next} />
  </div>;
}
