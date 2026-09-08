import type { CampaignSectionId, MainTabId, WorldSectionId } from "./hub-types";
import { normalizeCampaignSection, normalizeMainTab, normalizeWorldSection } from "../state.js";

export type HubRoute = {
  kind: "hub";
  tab: MainTabId;
  campaignSection: CampaignSectionId;
  worldSection: WorldSectionId;
  locationScopeId: string | null;
  locationScopePath: string[];
  locationId: string | null;
} | { kind: "none" } | { kind: "invalid" };

export const HUB_ROUTE_EVENT = "dnd2024-hub-navigation";

export function parseHubRoute(hash: string): HubRoute {
  if (!/^#view(?:\?|$)/u.test(hash)) return { kind: "none" };
  if (hash.length > 500 || /%(?![a-fA-F0-9]{2})/u.test(hash) ||
      (hash.match(/\?/gu) ?? []).length !== 1) return { kind: "invalid" };
  const parameters = new URLSearchParams(hash.slice(hash.indexOf("?") + 1));
  if ([...parameters.keys()].some((key) => !["tab", "section", "scope", "location"].includes(key) ||
      parameters.getAll(key).length !== 1)) return { kind: "invalid" };
  const requestedTab = parameters.get("tab");
  const tab = normalizeMainTab(requestedTab) as MainTabId;
  if (tab !== requestedTab) return { kind: "invalid" };
  const requestedSection = parameters.get("section");
  if (tab !== "campaign" && tab !== "world" && requestedSection !== null) return { kind: "invalid" };
  const campaignSection = normalizeCampaignSection(requestedSection ?? "overview") as CampaignSectionId;
  if (tab === "campaign" && requestedSection !== null && campaignSection !== requestedSection)
    return { kind: "invalid" };
  const worldSection = normalizeWorldSection(requestedSection ?? "overview") as WorldSectionId;
  if (tab === "world" && requestedSection !== null && worldSection !== requestedSection)
    return { kind: "invalid" };
  const encodedScopePath = parameters.get("scope");
  const locationScopePath = encodedScopePath === null ? [] : encodedScopePath.split(",");
  const locationScopeId = locationScopePath.at(-1) ?? null;
  const locationId = parameters.get("location");
  const validId = (value: string | null) => value === null || value.length > 0 && value.length <= 200 &&
    value === value.trim() && !/[\s,]/u.test(value);
  if (((locationScopeId !== null || locationId !== null) && (tab !== "world" || worldSection !== "locations")) ||
      locationScopePath.length > 20 || locationScopePath.some((id) => !validId(id)) ||
      !validId(locationId)) return { kind: "invalid" };
  return {
    kind: "hub", tab,
    campaignSection: tab === "campaign" ? campaignSection : "overview",
    worldSection: tab === "world" ? worldSection : "overview",
    locationScopeId,
    locationScopePath,
    locationId,
  };
}

export function hubRouteHash(
  tab: MainTabId,
  campaignSection: CampaignSectionId = "overview",
  options: { worldSection?: WorldSectionId; locationScopePath?: string[]; locationId?: string | null } = {},
) {
  const parameters = new URLSearchParams({ tab });
  if (tab === "campaign") parameters.set("section", campaignSection);
  if (tab === "world" && options.worldSection && options.worldSection !== "overview") {
    parameters.set("section", options.worldSection);
    if (options.worldSection === "locations") {
      if (options.locationScopePath?.length) parameters.set("scope", options.locationScopePath.join(","));
      if (options.locationId) parameters.set("location", options.locationId);
    }
  }
  return `#view?${parameters}`;
}

export function navigateHubRoute(
  tab: MainTabId,
  campaignSection: CampaignSectionId = "overview",
  replace = false,
  options: { worldSection?: WorldSectionId; locationScopePath?: string[]; locationId?: string | null } = {},
) {
  const route = hubRouteHash(tab, campaignSection, options);
  if (window.location.hash === route) return;
  const target = `${window.location.pathname}${window.location.search}${route}`;
  window.history[replace ? "replaceState" : "pushState"](
    { ...window.history.state, itemInventoryReturn: null, itemInventoryOrigin: null, itemMainTab: tab },
    "",
    target,
  );
  window.dispatchEvent(new Event(HUB_ROUTE_EVENT));
}
