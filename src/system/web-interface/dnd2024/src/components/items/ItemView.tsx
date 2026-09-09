import { useEffect, useRef, type ReactNode } from "react";
import type { ItemTab } from "../../data/item-view-route";

const tabs: { id: ItemTab; label: string }[] = [
  { id: "details", label: "Details" }, { id: "recipes", label: "Known recipes" }, { id: "uses", label: "Known uses" },
];

// Explicit reads replace their trigger buttons. Keep keyboard users at the
// stable panel instead of letting focus fall back to the document body.
export function focusItemPanel() { document.getElementById("item-panel")?.focus(); }

// Only ConnectedItemView supplies identity from a validated, current response.
export function ItemView({ tab, onTab, onBack, onParty, onRegistry, context = "inventory",
  characterName, name, details, recipes, uses }: {
  tab: ItemTab; onTab: (tab: ItemTab) => void; onBack: () => void; onParty?: () => void;
  onRegistry?: () => void; context?: "inventory" | "registry";
  characterName?: string; name?: string; details?: ReactNode; recipes?: ReactNode; uses?: ReactNode;
}) {
  const visibleTabs = context === "registry" ? [tabs[0], { ...tabs[1], label: "Recipes" }] : tabs;
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  return <section className="item-page" onKeyDown={(event) => {
    if (event.key === "Escape" && !event.defaultPrevented && !event.altKey && !event.ctrlKey && !event.metaKey) {
      event.preventDefault(); onBack();
    }
  }}>
    <nav aria-label="Breadcrumb" className="item-page__breadcrumbs"><ol>
      <li><button type="button" onClick={onParty ?? onBack}>Party</button></li>
      {context === "registry" ? <li><button type="button" onClick={onRegistry ?? onBack}>Registry</button></li> : null}
      {characterName ? <li><button type="button" onClick={onParty ?? onBack}>{characterName}</button></li> : null}
      <li><button type="button" onClick={onBack}>{context === "registry" ? "Items" : "Inventory"}</button></li>
      <li aria-current="page">{name ?? "Item details"}</li>
    </ol></nav>
    <header><span className="eyebrow">{context === "registry" ? "Known item registry" : "Inventory item"}</span><h2 id="item-view-heading" ref={heading} tabIndex={-1}>{name ?? "Item"}</h2></header>
    <div className="item-page__tabs" role="tablist" aria-label="Item sections">
      {visibleTabs.map((candidate, index) => <button key={candidate.id} type="button" role="tab"
        id={`item-tab-${candidate.id}`} aria-controls="item-panel" aria-selected={tab === candidate.id}
        tabIndex={tab === candidate.id ? 0 : -1} onClick={() => onTab(candidate.id)}
        onKeyDown={(event) => {
          const next = event.key === "ArrowRight" ? (index + 1) % visibleTabs.length : event.key === "ArrowLeft" ? (index + visibleTabs.length - 1) % visibleTabs.length
            : event.key === "Home" ? 0 : event.key === "End" ? visibleTabs.length - 1 : null;
          if (next === null) return;
          event.preventDefault(); onTab(visibleTabs[next].id);
          document.getElementById(`item-tab-${visibleTabs[next].id}`)?.focus();
        }}>{candidate.label}</button>)}
    </div>
    <section id="item-panel" role="tabpanel" aria-labelledby={`item-tab-${tab}`} tabIndex={0}>
      <div hidden={tab !== "uses"}>{uses}</div>
      <div hidden={tab !== "recipes"}>{recipes}</div>
      {tab === "uses" && uses ? null : tab === "recipes" && recipes ? null : tab === "details" && details ? details : <><h2>{tabs.find((candidate) => candidate.id === tab)?.label} unavailable</h2>
      <p>This information is not available in this view yet.</p></>}
    </section>
  </section>;
}
