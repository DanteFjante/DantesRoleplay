import type { MainTabId, Perspective } from "./hub-types";

export type ItemTab = "details" | "recipes" | "uses";
export type InventoryRoute = { kind: "inventory"; characterId: string; campaignId: string; perspective: Perspective };
export type ItemRoute = Omit<InventoryRoute, "kind"> & { kind: "item"; itemId: string; tab: ItemTab };
export type ItemNavigationRoute = ItemRoute | InventoryRoute | { kind: "invalid" } | { kind: "none" };
export type InventoryReturnContext = {
  characterId: string; expandedIds: string[]; focusItemId: string; scrollY: number;
};
export const ITEM_ROUTE_EVENT = "dnd2024-item-navigation";
const validId = (value: unknown): value is string => typeof value === "string" && /^[a-zA-Z0-9][a-zA-Z0-9._:-]{0,199}$/.test(value);

// The published application owns its pathname. A fragment adds a selection without
// changing the release URL or passing trusted bindings to the server.
export function parseItemRoute(hash: string): ItemNavigationRoute {
  if (!/^#(?:item|inventory)(?:\?|$)/.test(hash)) return { kind: "none" };
  if (hash.length > 1600 || /%(?![a-fA-F0-9]{2})/.test(hash)) return { kind: "invalid" };
  if ((hash.match(/\?/g) ?? []).length !== 1) return { kind: "invalid" };
  const [kind, query = ""] = hash.slice(1).split("?");
  const parameters = new URLSearchParams(query);
  const allowed = kind === "item" ? ["character", "campaign", "perspective", "item", "tab"] : ["character", "campaign", "perspective"];
  if ([...parameters.keys()].some((key) => !allowed.includes(key) || parameters.getAll(key).length !== 1)) return { kind: "invalid" };
  const characterId = parameters.get("character"), campaignId = parameters.get("campaign"), perspective = parameters.get("perspective");
  if (!validId(characterId) || !validId(campaignId) || (perspective !== "player" && perspective !== "dm")) return { kind: "invalid" };
  if (kind === "inventory") return { kind, characterId, campaignId, perspective };
  const itemId = parameters.get("item");
  if (!validId(itemId)) return { kind: "invalid" };
  const tab = parameters.get("tab");
  return { kind: "item", characterId, campaignId, perspective, itemId, tab: tab === "recipes" || tab === "uses" ? tab : "details" };
}

export function itemRouteHash(route: ItemRoute | InventoryRoute): string {
  const parameters = new URLSearchParams({ character: route.characterId, campaign: route.campaignId, perspective: route.perspective });
  if (route.kind === "item") { parameters.set("item", route.itemId); parameters.set("tab", route.tab); }
  return `#${route.kind}?${parameters}`;
}

export function readInventoryReturn(value: unknown, characterId: string): InventoryReturnContext | null {
  const context = (value as { itemInventoryReturn?: InventoryReturnContext } | null)?.itemInventoryReturn;
  if (!context || context.characterId !== characterId || !validId(context.characterId) || !validId(context.focusItemId) ||
      !Array.isArray(context.expandedIds) || context.expandedIds.length > 512 || !context.expandedIds.every(validId) ||
      !Number.isFinite(context.scrollY) || context.scrollY < 0 || context.scrollY > 10_000_000) return null;
  return context;
}

export function navigateItemRoute(route: ItemRoute | InventoryRoute | null, replace = false, returnContext?: InventoryReturnContext | null, mainTab: MainTabId = "party") {
  const url = `${window.location.pathname}${window.location.search}${route ? itemRouteHash(route) : ""}`;
  const origin = route?.kind === "item" ? replace ? window.history.state?.itemInventoryOrigin
    : window.location.hash === itemRouteHash({ ...route, kind: "inventory" }) ? window.location.hash : null : null;
  const state = { ...window.history.state, itemInventoryReturn: returnContext ?? null, itemInventoryOrigin: origin, itemMainTab: mainTab };
  window.history[replace ? "replaceState" : "pushState"](state, "", url);
  window.dispatchEvent(new Event(ITEM_ROUTE_EVENT));
}
