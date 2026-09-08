import { useMemo, useState, type SyntheticEvent } from "react";

import type { CharacterDossierDefinition, CharacterInventoryItemV2, InventoryContainerItem } from "../../data/hub-types";
import { Icon } from "../Icon";
import { MediaImage } from "../MediaImage";

type InventoryTreeItem = CharacterInventoryItemV2 | InventoryContainerItem;

function ItemIdentity({ definition, item }: { definition?: CharacterDossierDefinition; item: InventoryTreeItem }) {
  const visual = item.media?.illustration ?? item.media?.icon;
  return (
    <>
      <span className="character-inventory__item-media">
        <MediaImage fallback={<Icon name="PackageOpen" size={19} />} media={visual} />
      </span>
      <span className="character-inventory__item-copy">
        <strong>{item.name}</strong>
        <small>{item.definition?.label ?? "Unclassified content"}{item.quantity !== null && item.quantity > 1 ? ` · ${item.quantity}` : ""}</small>
        {definition?.summary ? <small>{definition.summary}</small> : null}
        {definition?.source ? <small>{definition.source.locator}</small> : null}
      </span>
      {item.equipmentSlots.length ? <span className="character-inventory__equipped">Equipped</span> : null}
    </>
  );
}

function InventoryBranch({
  childrenByParent,
  definitions,
  item,
  onDisclosure,
  onOpenItem,
  expandedIds,
  queryActive,
  visibleIds,
}: {
  childrenByParent: Map<string | null, InventoryTreeItem[]>;
  definitions: Map<string, CharacterDossierDefinition>;
  item: InventoryTreeItem;
  onDisclosure: (item: InventoryTreeItem, expanded: boolean) => void;
  onOpenItem?: (itemId: string) => void;
  expandedIds?: string[];
  queryActive: boolean;
  visibleIds: Set<string> | null;
}) {
  const children = (childrenByParent.get(item.id) ?? []).filter((child) => !visibleIds || visibleIds.has(child.id));
  const canExpand = children.length > 0 || item.deeperContentsOmitted;
  const contents = children.length ? (
    <ul>
      {children.map((child) => (
        <li key={child.id}>
          <InventoryBranch childrenByParent={childrenByParent} definitions={definitions} item={child} onDisclosure={onDisclosure} onOpenItem={onOpenItem} expandedIds={expandedIds} queryActive={queryActive} visibleIds={visibleIds} />
        </li>
      ))}
    </ul>
  ) : item.deeperContentsOmitted ? <p className="character-inventory__omission">Deeper contents were intentionally omitted.</p> : null;

  const identity = <ItemIdentity definition={item.definition ? definitions.get(item.definition.id) : undefined} item={item} />;
  const opening = onOpenItem ? <button type="button" className="character-inventory__open" data-item-open={item.id}
    aria-label={`View ${item.name}`} onClick={() => onOpenItem(item.id)}>{identity}</button> : null;
  if (!canExpand) return opening ? <div className="character-inventory__node">{opening}</div>
    : <div className="character-inventory__leaf">{identity}</div>;
  return (
    <div className={opening ? "character-inventory__node" : undefined}>
    {opening}
    <details
      className="character-inventory__branch"
      open={queryActive || expandedIds?.includes(item.id)}
      onToggle={(event: SyntheticEvent<HTMLDetailsElement>) => onDisclosure(item, event.currentTarget.open)}
    >
      <summary>{opening ? <span>Contents<span className="sr-only"> of {item.name}</span></span> : identity}</summary>
      {contents}
    </details>
    </div>
  );
}

export function InventoryTree({ complete = true, definitions: values, items, onOpenItem, expandedIds, onExpandedChange, reasons = [] }: {
  complete?: boolean; definitions: CharacterDossierDefinition[]; items: InventoryTreeItem[];
  onOpenItem?: (itemId: string) => void; expandedIds?: string[]; onExpandedChange?: (itemId: string, expanded: boolean) => void;
  reasons?: Array<"depth-limit" | "unclassified-content">;
}) {
  const [announcement, setAnnouncement] = useState("");
  const [query, setQuery] = useState("");
  const definitions = useMemo(() => new Map(values.map((value) => [value.id, value])), [values]);
  const childrenByParent = useMemo(() => {
    const groups = new Map<string | null, InventoryTreeItem[]>();
    for (const item of items) {
      const siblings = groups.get(item.parentItemId) ?? [];
      siblings.push(item);
      groups.set(item.parentItemId, siblings);
    }
    for (const siblings of groups.values()) siblings.sort((left, right) => left.order - right.order);
    return groups;
  }, [items]);
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const visibleIds = useMemo(() => {
    if (!normalizedQuery) return null;
    const byId = new Map(items.map((item) => [item.id, item]));
    const visible = new Set<string>();
    for (const item of items) {
      const values = [item.name, item.definition?.label ?? "unclassified content", item.slot];
      if (!values.some((value) => value.toLocaleLowerCase().includes(normalizedQuery))) continue;
      let current: InventoryTreeItem | undefined = item;
      while (current && !visible.has(current.id)) {
        visible.add(current.id);
        current = current.parentItemId ? byId.get(current.parentItemId) : undefined;
      }
    }
    return visible;
  }, [items, normalizedQuery]);
  const roots = (childrenByParent.get(null) ?? []).filter((item) => !visibleIds || visibleIds.has(item.id));

  return (
    <section aria-labelledby="character-inventory-heading" className="character-inventory">
      <header>
        <div><span className="eyebrow">Carried & equipped</span><h3 id="character-inventory-heading">Inventory</h3></div>
        <p>{items.length} visible {items.length === 1 ? "entry" : "entries"}{complete ? "" : " · partial"}</p>
      </header>
      {items.length > 8 ? <label className="character-search"><Icon name="Search" size={17} />
        <span className="sr-only">Search inventory</span>
        <input onChange={(event) => setQuery(event.target.value.slice(0, 80))}
          placeholder="Search inventory…" type="search" value={query} />
      </label> : null}
      {!complete ? <div className="character-state character-state--stale" role="status">
        <Icon name="Clock3" size={18} /><div><strong>Inventory view is partial</strong>
          <p>{reasons.includes("depth-limit")
            ? "Contents beyond four container levels are not included, so visible totals are not complete."
            : "Some contained records are not classified as items, so visible totals are not complete."}</p></div>
      </div> : null}
      <p aria-live="polite" className="sr-only">{announcement}</p>
      {roots.length ? (
        <ul className="character-inventory__tree">
          {roots.map((item) => (
            <li key={item.id}>
              <InventoryBranch
                childrenByParent={childrenByParent}
                definitions={definitions}
                item={item}
                onOpenItem={onOpenItem}
                expandedIds={expandedIds}
                queryActive={Boolean(normalizedQuery)}
                visibleIds={visibleIds}
                onDisclosure={(changed, expanded) => { onExpandedChange?.(changed.id, expanded); setAnnouncement(
                  `${changed.name} ${expanded ? "expanded" : "collapsed"}. ${changed.childCount} contained ${changed.childCount === 1 ? "item" : "items"}.`,
                ); }}
              />
            </li>
          ))}
        </ul>
      ) : normalizedQuery ? (
        <div className="character-inventory__empty">
          <Icon name="Search" size={25} /><div><strong>No matching inventory entries</strong><p>Try a broader search.</p></div>
        </div>
      ) : (
        <div className="character-inventory__empty">
          <Icon name="PackageOpen" size={25} />
          <div><strong>No carried items</strong><p>The canonical inventory is currently empty.</p></div>
        </div>
      )}
    </section>
  );
}
