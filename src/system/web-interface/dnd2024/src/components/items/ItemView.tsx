import { useEffect, useRef, type ReactNode } from "react";
import type { ItemTab } from "../../data/item-view-route";

const tabs: { id: ItemTab; label: string }[] = [
  { id: "details", label: "Details" }, { id: "recipes", label: "Known recipes" }, { id: "uses", label: "Known uses" },
];

// Only ConnectedItemView supplies identity from a validated, current response.
export function ItemView({ tab, onTab, onBack, name, details, recipes }: { tab: ItemTab; onTab: (tab: ItemTab) => void; onBack: () => void; name?: string; details?: ReactNode; recipes?: ReactNode }) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  return <section className="item-page" onKeyDown={(event) => {
    if (event.key === "Escape" && !event.defaultPrevented && !event.altKey && !event.ctrlKey && !event.metaKey) {
      event.preventDefault(); onBack();
    }
  }}>
    <button className="item-page__back" type="button" onClick={onBack}>Back to inventory</button>
    <header><span className="eyebrow">Inventory</span><h1 id="main-view-heading" ref={heading} tabIndex={-1}>{name ?? "Item"}</h1></header>
    <div className="item-page__tabs" role="tablist" aria-label="Item sections">
      {tabs.map((candidate, index) => <button key={candidate.id} type="button" role="tab"
        id={`item-tab-${candidate.id}`} aria-controls="item-panel" aria-selected={tab === candidate.id}
        tabIndex={tab === candidate.id ? 0 : -1} onClick={() => onTab(candidate.id)}
        onKeyDown={(event) => {
          const next = event.key === "ArrowRight" ? (index + 1) % tabs.length : event.key === "ArrowLeft" ? (index + tabs.length - 1) % tabs.length
            : event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : null;
          if (next === null) return;
          event.preventDefault(); onTab(tabs[next].id);
          document.getElementById(`item-tab-${tabs[next].id}`)?.focus();
        }}>{candidate.label}</button>)}
    </div>
    <section id="item-panel" role="tabpanel" aria-labelledby={`item-tab-${tab}`} tabIndex={0}>
      <div hidden={tab !== "recipes"}>{recipes}</div>
      {tab === "recipes" && recipes ? null : tab === "details" && details ? details : <><h2>{tabs.find((candidate) => candidate.id === tab)?.label} unavailable</h2>
      <p>This information is not available in this view yet.</p></>}
    </section>
  </section>;
}
