import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";

import type {
  CharacterInventoryItemV2,
  InventoryContainerItem,
  InventoryContainerPageItem,
  InventoryContainerPageResult,
} from "../../data/hub-types";
import { Icon } from "../Icon";
import { MediaImage } from "../MediaImage";

type InventoryTreeItem = CharacterInventoryItemV2 | InventoryContainerItem;
type ScopeState = "loading" | "ready" | "error";

function validateGraph(items: InventoryTreeItem[]) {
  const byId = new Map<string, InventoryTreeItem>();
  for (const item of items) {
    if (byId.has(item.id)) throw new Error("Inventory contains a duplicate item.");
    byId.set(item.id, item);
  }
  for (const item of items) {
    const visited = new Set([item.id]);
    let parentId = item.parentItemId;
    while (parentId !== null) {
      if (visited.has(parentId)) throw new Error("Inventory contains a cycle.");
      visited.add(parentId);
      parentId = byId.get(parentId)?.parentItemId ?? null;
    }
  }
}

export function mergeInventoryContainerItems(
  current: InventoryTreeItem[],
  containerId: string,
  page: InventoryContainerPageItem[],
) {
  const container = current.find((item) => item.id === containerId);
  if (!container) throw new Error("Inventory container is no longer available.");
  const existing = new Set(current.map((item) => item.id));
  if (page.some((item) => existing.has(item.id) || item.id === containerId))
    throw new Error("Inventory container repeats an existing item.");
  if (current.length + page.length > 512) throw new Error("This inventory view reached its 512-item display bound.");
  const children: InventoryContainerItem[] = page.map((item) => ({
    ...item,
    parentItemId: containerId,
    depth: container.depth + 1,
    childCount: null,
    deeperContentsOmitted: true,
  }));
  const merged = [...current, ...children];
  validateGraph(merged);
  return merged;
}

function ItemIdentity({ item, parentName, viewCue = false }: { item: InventoryTreeItem; parentName?: string; viewCue?: boolean }) {
  const visual = item.media?.illustration ?? item.media?.icon;
  const metadata = [
    item.quantity !== null ? `Quantity ${item.quantity}` : null,
    item.equipmentSlots.length ? `Equipped: ${item.equipmentSlots.map((slot) => slot.label).join(", ")}` : null,
    parentName ? `Inside ${parentName}` : item.slot && !/^(?:inventory|contents)$/iu.test(item.slot) ? item.slot : null,
  ].filter(Boolean);
  return <>
    <span className="character-inventory__item-media">
      <MediaImage fallback={<Icon name="PackageOpen" size={19} />} media={visual} />
    </span>
    <span className="character-inventory__item-copy">
      <strong>{item.name}</strong>
      {metadata.length ? <small>{metadata.join(" · ")}</small> : null}
      {"classification" in item && item.classification === "unclassified"
        ? <small className="character-inventory__unknown">Unknown item record</small> : null}
    </span>
    {viewCue ? <span className="character-inventory__view-cue">View details <Icon name="ChevronRight" size={17} /></span> : null}
  </>;
}

function InventoryBranch({
  childrenByParent,
  item,
  itemById,
  onDisclosure,
  onOpenItem,
  expandedIds,
  queryActive,
  scopeStates,
  visibleIds,
}: {
  childrenByParent: Map<string | null, InventoryTreeItem[]>;
  item: InventoryTreeItem;
  itemById: ReadonlyMap<string, InventoryTreeItem>;
  onDisclosure: (item: InventoryTreeItem, expanded: boolean) => void;
  onOpenItem?: (itemId: string) => void;
  expandedIds: string[];
  queryActive: boolean;
  scopeStates: ReadonlyMap<string, ScopeState>;
  visibleIds: Set<string> | null;
}) {
  const children = (childrenByParent.get(item.id) ?? []).filter((child) => !visibleIds || visibleIds.has(child.id));
  const expanded = queryActive && children.length > 0 || expandedIds.includes(item.id);
  const state = scopeStates.get(item.id);
  const canInspectContents = item.deeperContentsOmitted || item.childCount === null || item.childCount > 0 || children.length > 0;
  const contentsId = `inventory-contents-${item.id.replace(/[^a-zA-Z0-9_-]/g, "-")}`;
  return <div className="character-inventory__node">
    {onOpenItem ? <button type="button" className="character-inventory__open" data-item-open={item.id}
      aria-label={`View details for ${item.name}`} onClick={() => onOpenItem(item.id)}>
      <ItemIdentity item={item} parentName={item.parentItemId ? itemById.get(item.parentItemId)?.name : undefined} viewCue />
    </button> : <div className="character-inventory__leaf"><ItemIdentity item={item} /></div>}
    {canInspectContents ? <button className="character-inventory__disclosure" type="button"
      aria-controls={contentsId} aria-expanded={expanded} onClick={() => onDisclosure(item, !expanded)}>
      <Icon name="ChevronRight" size={16} />
      <span>{expanded ? "Hide contents" : "Show contents"}<span className="sr-only"> of {item.name}</span></span>
      {item.childCount !== null ? <small>{item.childCount}</small> : null}
    </button> : null}
    {expanded ? <div className="character-inventory__contents" id={contentsId}>
      {state === "loading" ? <p role="status">Loading contents…</p>
        : state === "error" ? <p role="alert">Contents could not be loaded. Collapse and try again.</p>
          : children.length ? <ul>{children.map((child) => <li key={child.id}>
            <InventoryBranch childrenByParent={childrenByParent} item={child} itemById={itemById} onDisclosure={onDisclosure}
              onOpenItem={onOpenItem} expandedIds={expandedIds} queryActive={queryActive}
              scopeStates={scopeStates} visibleIds={visibleIds} />
          </li>)}</ul>
            : <p>{state === "ready" ? "This container is empty." : "No loaded contents."}</p>}
    </div> : null}
  </div>;
}

export function InventoryTree({
  items,
  loadContainer,
  onOpenItem,
  expandedIds: controlledExpanded,
  onExpandedChange,
  query: controlledQuery,
  onQueryChange,
  reasons = [],
  restore,
}: {
  items: InventoryTreeItem[];
  loadContainer?: (containerId: string, signal: AbortSignal) => Promise<InventoryContainerPageResult>;
  onOpenItem?: (itemId: string) => void;
  expandedIds?: string[];
  onExpandedChange?: (itemId: string, expanded: boolean) => void;
  query?: string;
  onQueryChange?: (query: string) => void;
  reasons?: Array<"unclassified-content">;
  restore?: { focusItemId: string; scrollY: number } | null;
}) {
  const [announcement, setAnnouncement] = useState("");
  const [localQuery, setLocalQuery] = useState("");
  const [localExpanded, setLocalExpanded] = useState<string[]>([]);
  const [loadedItems, setLoadedItems] = useState<InventoryTreeItem[]>(items);
  const [scopeStates, setScopeStates] = useState<Map<string, ScopeState>>(new Map());
  const controllers = useRef(new Map<string, AbortController>());
  const restored = useRef(false);
  const query = controlledQuery ?? localQuery;
  const expandedIds = controlledExpanded ?? localExpanded;
  useEffect(() => {
    validateGraph(items);
    setLoadedItems(items);
    setScopeStates(new Map());
    controllers.current.forEach((controller) => controller.abort());
    controllers.current.clear();
    restored.current = false;
  }, [items]);
  useEffect(() => () => controllers.current.forEach((controller) => controller.abort()), []);

  const ensureScope = useCallback((item: InventoryTreeItem) => {
    if (!loadContainer || scopeStates.has(item.id) || childrenLoaded(loadedItems, item.id)) return;
    const controller = new AbortController();
    controllers.current.set(item.id, controller);
    setScopeStates((previous) => new Map(previous).set(item.id, "loading"));
    void loadContainer(item.id, controller.signal).then((result) => {
      if (controller.signal.aborted) return;
      if (result.status !== "ready" || result.data.container.id !== item.id) throw new Error("Container unavailable.");
      setLoadedItems((previous) => mergeInventoryContainerItems(previous, item.id, result.data.items));
      setScopeStates((previous) => new Map(previous).set(item.id, "ready"));
    }).catch((error) => {
      if (controller.signal.aborted || error?.name === "AbortError") return;
      setScopeStates((previous) => new Map(previous).set(item.id, "error"));
    }).finally(() => controllers.current.delete(item.id));
  }, [loadContainer, loadedItems, scopeStates]);

  useEffect(() => {
    for (const id of expandedIds) {
      const item = loadedItems.find((candidate) => candidate.id === id);
      if (item) ensureScope(item);
    }
  }, [ensureScope, expandedIds, loadedItems]);

  const childrenByParent = useMemo(() => {
    const groups = new Map<string | null, InventoryTreeItem[]>();
    for (const item of loadedItems) {
      const siblings = groups.get(item.parentItemId) ?? [];
      siblings.push(item);
      groups.set(item.parentItemId, siblings);
    }
    for (const siblings of groups.values()) siblings.sort((left, right) => left.order - right.order);
    return groups;
  }, [loadedItems]);
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const visibleIds = useMemo(() => {
    if (!normalizedQuery) return null;
    const byId = new Map(loadedItems.map((item) => [item.id, item]));
    const visible = new Set<string>();
    for (const item of loadedItems) {
      const searchable = [item.name, item.definition?.label ?? "unknown item", item.slot];
      if (!searchable.some((value) => value.toLocaleLowerCase().includes(normalizedQuery))) continue;
      let current: InventoryTreeItem | undefined = item;
      while (current && !visible.has(current.id)) {
        visible.add(current.id);
        current = current.parentItemId ? byId.get(current.parentItemId) : undefined;
      }
    }
    return visible;
  }, [loadedItems, normalizedQuery]);
  const roots = (childrenByParent.get(null) ?? []).filter((item) => !visibleIds || visibleIds.has(item.id));
  const itemById = useMemo(() => new Map(loadedItems.map((item) => [item.id, item])), [loadedItems]);

  useLayoutEffect(() => {
    if (restored.current || !restore) return;
    const target = [...document.querySelectorAll<HTMLElement>("[data-item-open]")]
      .find((element) => element.dataset.itemOpen === restore.focusItemId);
    if (!target) return;
    restored.current = true;
    target.focus({ preventScroll: true });
    window.scrollTo(0, restore.scrollY);
  }, [loadedItems, restore, visibleIds]);

  const setExpanded = (item: InventoryTreeItem, expanded: boolean) => {
    if (controlledExpanded === undefined) setLocalExpanded((previous) => expanded
      ? previous.includes(item.id) ? previous : [...previous, item.id]
      : previous.filter((value) => value !== item.id));
    onExpandedChange?.(item.id, expanded);
    if (expanded) ensureScope(item);
    else if (scopeStates.get(item.id) === "error") setScopeStates((previous) => {
      const next = new Map(previous); next.delete(item.id); return next;
    });
    const count = item.childCount === null ? "Contents" : `${item.childCount} contained ${item.childCount === 1 ? "item" : "items"}`;
    setAnnouncement(`${item.name} ${expanded ? "expanded" : "collapsed"}. ${count}.`);
  };
  const changeQuery = (value: string) => {
    const bounded = value.slice(0, 80);
    if (controlledQuery === undefined) setLocalQuery(bounded);
    onQueryChange?.(bounded);
  };

  return <section aria-label="Carried items" className="character-inventory">
    <div className="character-inventory__toolbar">
      <p><strong>{loadedItems.length}</strong> loaded {loadedItems.length === 1 ? "item" : "items"}</p>
      {(items.length > 8 || query) ? <label className="character-search"><Icon name="Search" size={17} />
        <span className="sr-only">Search loaded inventory</span>
        <input onChange={(event) => changeQuery(event.target.value)} placeholder="Search loaded inventory…"
          type="search" value={query} />
      </label> : null}
    </div>
    <p className="character-inventory__scope-note">Counts and search cover loaded containers. Open a row’s contents to load the next complete level.</p>
    {reasons.includes("unclassified-content") ? <div className="character-state character-state--stale" role="status">
      <Icon name="Clock3" size={18} /><div><strong>Some records are not classified as items</strong>
        <p>They remain visible as unknown records without invented definitions.</p></div>
    </div> : null}
    <p aria-live="polite" className="sr-only">{announcement}</p>
    {roots.length ? <ul className="character-inventory__tree">{roots.map((item) => <li key={item.id}>
      <InventoryBranch childrenByParent={childrenByParent} item={item} itemById={itemById} onDisclosure={setExpanded}
        onOpenItem={onOpenItem} expandedIds={expandedIds} queryActive={Boolean(normalizedQuery)}
        scopeStates={scopeStates} visibleIds={visibleIds} />
    </li>)}</ul> : normalizedQuery ? <div className="character-inventory__empty">
      <Icon name="Search" size={25} /><div><strong>No matching loaded items</strong><p>Try a broader search or open more containers.</p></div>
    </div> : <div className="character-inventory__empty"><Icon name="PackageOpen" size={25} />
      <div><strong>No carried items</strong><p>This inventory container is empty.</p></div>
    </div>}
  </section>;
}

function childrenLoaded(items: InventoryTreeItem[], parentId: string) {
  return items.some((item) => item.parentItemId === parentId);
}
