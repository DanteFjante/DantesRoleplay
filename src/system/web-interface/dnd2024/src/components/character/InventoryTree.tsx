import { useMemo, useState, type SyntheticEvent } from "react";

import type { CharacterInventoryItemV2 } from "../../data/hub-types";
import { Icon } from "../Icon";
import { MediaImage } from "../MediaImage";

function ItemIdentity({ item }: { item: CharacterInventoryItemV2 }) {
  const visual = item.media?.illustration ?? item.media?.icon;
  return (
    <>
      <span className="character-inventory__item-media">
        <MediaImage fallback={<Icon name="PackageOpen" size={19} />} media={visual} />
      </span>
      <span className="character-inventory__item-copy">
        <strong>{item.name}</strong>
        <small>{item.definition.label}{item.quantity > 1 ? ` · ${item.quantity}` : ""}</small>
      </span>
      {item.equipmentSlots.length ? <span className="character-inventory__equipped">Equipped</span> : null}
    </>
  );
}

function InventoryBranch({
  childrenByParent,
  item,
  onDisclosure,
}: {
  childrenByParent: Map<string | null, CharacterInventoryItemV2[]>;
  item: CharacterInventoryItemV2;
  onDisclosure: (item: CharacterInventoryItemV2, expanded: boolean) => void;
}) {
  const children = childrenByParent.get(item.id) ?? [];
  const canExpand = children.length > 0 || item.deeperContentsOmitted;
  const contents = children.length ? (
    <ul>
      {children.map((child) => (
        <li key={child.id}>
          <InventoryBranch childrenByParent={childrenByParent} item={child} onDisclosure={onDisclosure} />
        </li>
      ))}
    </ul>
  ) : item.deeperContentsOmitted ? <p className="character-inventory__omission">Deeper contents were intentionally omitted.</p> : null;

  if (!canExpand) return <div className="character-inventory__leaf"><ItemIdentity item={item} /></div>;
  return (
    <details
      className="character-inventory__branch"
      onToggle={(event: SyntheticEvent<HTMLDetailsElement>) => onDisclosure(item, event.currentTarget.open)}
    >
      <summary><ItemIdentity item={item} /></summary>
      {contents}
    </details>
  );
}

export function InventoryTree({ items }: { items: CharacterInventoryItemV2[] }) {
  const [announcement, setAnnouncement] = useState("");
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
                item={item}
                onDisclosure={(changed, expanded) => setAnnouncement(
                  `${changed.name} ${expanded ? "expanded" : "collapsed"}. ${changed.childCount} contained ${changed.childCount === 1 ? "item" : "items"}.`,
                )}
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
