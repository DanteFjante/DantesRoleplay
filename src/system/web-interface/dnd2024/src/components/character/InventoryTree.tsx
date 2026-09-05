import { useMemo, useState, type SyntheticEvent } from "react";

import type { CharacterDossierDefinition, CharacterInventoryItemV2 } from "../../data/hub-types";
import { Icon } from "../Icon";
import { MediaImage } from "../MediaImage";

function ItemIdentity({ definition, item }: { definition?: CharacterDossierDefinition; item: CharacterInventoryItemV2 }) {
  const visual = item.media?.illustration ?? item.media?.icon;
  return (
    <>
      <span className="character-inventory__item-media">
        <MediaImage fallback={<Icon name="PackageOpen" size={19} />} media={visual} />
      </span>
      <span className="character-inventory__item-copy">
        <strong>{item.name}</strong>
        <small>{item.definition.label}{item.quantity > 1 ? ` · ${item.quantity}` : ""}</small>
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
}: {
  childrenByParent: Map<string | null, CharacterInventoryItemV2[]>;
  definitions: Map<string, CharacterDossierDefinition>;
  item: CharacterInventoryItemV2;
  onDisclosure: (item: CharacterInventoryItemV2, expanded: boolean) => void;
  onOpenItem?: (itemId: string) => void;
  expandedIds?: string[];
}) {
  const children = childrenByParent.get(item.id) ?? [];
  const canExpand = children.length > 0 || item.deeperContentsOmitted;
  const contents = children.length ? (
    <ul>
      {children.map((child) => (
        <li key={child.id}>
          <InventoryBranch childrenByParent={childrenByParent} definitions={definitions} item={child} onDisclosure={onDisclosure} onOpenItem={onOpenItem} expandedIds={expandedIds} />
        </li>
      ))}
    </ul>
  ) : item.deeperContentsOmitted ? <p className="character-inventory__omission">Deeper contents were intentionally omitted.</p> : null;

  const identity = <ItemIdentity definition={definitions.get(item.definition.id)} item={item} />;
  const opening = onOpenItem ? <button type="button" className="character-inventory__open" data-item-open={item.id}
    aria-label={`View ${item.name}`} onClick={() => onOpenItem(item.id)}>{identity}</button> : null;
  if (!canExpand) return opening ? <div className="character-inventory__node">{opening}</div>
    : <div className="character-inventory__leaf">{identity}</div>;
  return (
    <div className={opening ? "character-inventory__node" : undefined}>
    {opening}
    <details
      className="character-inventory__branch"
      open={expandedIds?.includes(item.id)}
      onToggle={(event: SyntheticEvent<HTMLDetailsElement>) => onDisclosure(item, event.currentTarget.open)}
    >
      <summary>{opening ? <span>Contents<span className="sr-only"> of {item.name}</span></span> : identity}</summary>
      {contents}
    </details>
    </div>
  );
}

export function InventoryTree({ definitions: values, items, onOpenItem, expandedIds, onExpandedChange }: {
  definitions: CharacterDossierDefinition[]; items: CharacterInventoryItemV2[];
  onOpenItem?: (itemId: string) => void; expandedIds?: string[]; onExpandedChange?: (itemId: string, expanded: boolean) => void;
}) {
  const [announcement, setAnnouncement] = useState("");
  const definitions = useMemo(() => new Map(values.map((value) => [value.id, value])), [values]);
  const childrenByParent = useMemo(() => {
    const groups = new Map<string | null, CharacterInventoryItemV2[]>();
    for (const item of items) {
      const siblings = groups.get(item.parentItemId) ?? [];
      siblings.push(item);
      groups.set(item.parentItemId, siblings);
    }
    for (const siblings of groups.values()) siblings.sort((left, right) => left.order - right.order);
    return groups;
  }, [items]);
  const roots = childrenByParent.get(null) ?? [];

  return (
    <section aria-labelledby="character-inventory-heading" className="character-inventory">
      <header>
        <div><span className="eyebrow">Carried & equipped</span><h3 id="character-inventory-heading">Inventory</h3></div>
        <p>{items.length} {items.length === 1 ? "item" : "items"}</p>
      </header>
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
                onDisclosure={(changed, expanded) => { onExpandedChange?.(changed.id, expanded); setAnnouncement(
                  `${changed.name} ${expanded ? "expanded" : "collapsed"}. ${changed.childCount} contained ${changed.childCount === 1 ? "item" : "items"}.`,
                ); }}
              />
            </li>
          ))}
        </ul>
      ) : (
        <div className="character-inventory__empty">
          <Icon name="PackageOpen" size={25} />
          <div><strong>No carried items</strong><p>The canonical inventory is currently empty.</p></div>
        </div>
      )}
    </section>
  );
}
