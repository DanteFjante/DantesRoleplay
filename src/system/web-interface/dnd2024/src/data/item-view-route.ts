import type { MainTabId, Perspective } from "./hub-types";

export type ItemTab = "details" | "recipes" | "uses";
export type InventoryRoute = { kind: "inventory"; characterId: string; campaignId: string; perspective: Perspective };
export type ItemRoute = Omit<InventoryRoute, "kind"> & { kind: "item"; itemId: string; tab: ItemTab };
export type RegistryItemRoute = { kind: "registry-item"; campaignId: string; perspective: Perspective;
  itemId: string; collection: string; contentFingerprint: string };
export type ItemNavigationRoute = ItemRoute | InventoryRoute | RegistryItemRoute | { kind: "invalid" } | { kind: "none" };
export type InventoryReturnContext = {
  kind: "inventory"; characterId: string; expandedIds: string[]; query: string; focusItemId: string; scrollY: number;
};
export type RegistryReturnContext = {
  kind: "registry"; campaignId: string; perspective: Perspective;
  section: "items" | "recipes"; query: string; pageCount: number;
  focusEntryId: string; scrollY: number;
};
export type ItemReturnContext = InventoryReturnContext | RegistryReturnContext;
export const ITEM_ROUTE_EVENT = "dnd2024-item-navigation";
const validId = (value: unknown): value is string => typeof value === "string" && /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(value);

// The published application owns its pathname. A fragment adds a selection without
// changing the release URL or passing trusted bindings to the server.
export function parseItemRoute(hash: string): ItemNavigationRoute {
  if (!/^#(?:item|inventory|registry-item)(?:\?|$)/.test(hash)) return { kind: "none" };
  if (hash.length > 1600 || /%(?![a-fA-F0-9]{2})/.test(hash)) return { kind: "invalid" };
  if ((hash.match(/\?/g) ?? []).length !== 1) return { kind: "invalid" };
  const [kind, query = ""] = hash.slice(1).split("?");
  const parameters = new URLSearchParams(query);
  const allowed = kind === "item" ? ["character", "campaign", "perspective", "item", "tab"]
    : kind === "registry-item" ? ["campaign", "perspective", "item", "collection", "fingerprint"]
      : ["character", "campaign", "perspective"];
  if ([...parameters.keys()].some((key) => !allowed.includes(key) || parameters.getAll(key).length !== 1)) return { kind: "invalid" };
  const campaignId = parameters.get("campaign"), perspective = parameters.get("perspective");
  if (!validId(campaignId) || (perspective !== "player" && perspective !== "dm")) return { kind: "invalid" };
  if (kind === "registry-item") {
    const itemId = parameters.get("item"), collection = parameters.get("collection"), contentFingerprint = parameters.get("fingerprint");
    return validId(itemId) && validId(collection) && typeof contentFingerprint === "string" && /^[A-F0-9]{64}$/iu.test(contentFingerprint)
      ? { kind, campaignId, perspective, itemId, collection, contentFingerprint: contentFingerprint.toUpperCase() }
      : { kind: "invalid" };
  }
  const characterId = parameters.get("character");
  if (!validId(characterId)) return { kind: "invalid" };
  if (kind === "inventory") return { kind, characterId, campaignId, perspective };
  const itemId = parameters.get("item");
  if (!validId(itemId)) return { kind: "invalid" };
  const tab = parameters.get("tab");
  return { kind: "item", characterId, campaignId, perspective, itemId, tab: tab === "recipes" || tab === "uses" ? tab : "details" };
}

export function itemRouteHash(route: ItemRoute | InventoryRoute | RegistryItemRoute): string {
  const parameters = new URLSearchParams({ campaign: route.campaignId, perspective: route.perspective });
  if (route.kind === "registry-item") {
    parameters.set("item", route.itemId); parameters.set("collection", route.collection);
    parameters.set("fingerprint", route.contentFingerprint);
    return `#${route.kind}?${parameters}`;
  }
  parameters.set("character", route.characterId);
  if (route.kind === "item") { parameters.set("item", route.itemId); parameters.set("tab", route.tab); }
  return `#${route.kind}?${parameters}`;
}

export function readInventoryReturn(value: unknown, characterId: string): InventoryReturnContext | null {
  const raw = (value as { itemReturnContext?: ItemReturnContext; itemInventoryReturn?: Partial<InventoryReturnContext> } | null);
  const context = raw?.itemReturnContext ?? raw?.itemInventoryReturn;
  if (!context || (context.kind !== undefined && context.kind !== "inventory") ||
      context.characterId !== characterId || !validId(context.characterId) || !validId(context.focusItemId) ||
      !Array.isArray(context.expandedIds) || context.expandedIds.length > 512 || !context.expandedIds.every(validId) ||
      !(context.query === undefined || typeof context.query === "string" && context.query.length <= 80) ||
      !Number.isFinite(context.scrollY) || context.scrollY! < 0 || context.scrollY! > 10_000_000) return null;
  return { kind: "inventory", characterId: context.characterId, expandedIds: context.expandedIds,
    query: context.query ?? "", focusItemId: context.focusItemId, scrollY: context.scrollY! };
}

export function readItemReturn(value: unknown): ItemReturnContext | null {
  const context = (value as { itemReturnContext?: ItemReturnContext } | null)?.itemReturnContext;
  if (!context) return null;
  if (context.kind === "inventory") return readInventoryReturn(value, context.characterId);
  if (context.kind !== "registry" || !validId(context.campaignId) ||
      (context.perspective !== "player" && context.perspective !== "dm") ||
      !["items", "recipes"].includes(context.section) ||
      typeof context.query !== "string" || context.query.length > 256 || !Number.isSafeInteger(context.pageCount) ||
      context.pageCount < 1 || context.pageCount > 500 || !validId(context.focusEntryId) ||
      !Number.isFinite(context.scrollY) || context.scrollY < 0 || context.scrollY > 10_000_000) return null;
  return context;
}

export function navigateItemRoute(route: ItemRoute | InventoryRoute | RegistryItemRoute | null, replace = false, returnContext?: ItemReturnContext | null, mainTab: MainTabId = "party") {
  const url = `${window.location.pathname}${window.location.search}${route ? itemRouteHash(route) : ""}`;
  const origin = route?.kind === "item" ? replace ? window.history.state?.itemInventoryOrigin
    : window.location.hash === itemRouteHash({ ...route, kind: "inventory" }) ? window.location.hash : null : null;
  const state = { ...window.history.state, itemReturnContext: returnContext ?? null,
    itemInventoryReturn: returnContext?.kind === "inventory" ? returnContext : null,
    itemInventoryOrigin: origin, itemMainTab: mainTab };
  window.history[replace ? "replaceState" : "pushState"](state, "", url);
  window.dispatchEvent(new Event(ITEM_ROUTE_EVENT));
}
