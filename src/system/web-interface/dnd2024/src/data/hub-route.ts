import type { CampaignSectionId, MainTabId } from "./hub-types";
import { normalizeCampaignSection, normalizeMainTab } from "../state.js";

export type HubRoute = {
  kind: "hub";
  tab: MainTabId;
  campaignSection: CampaignSectionId;
} | { kind: "none" } | { kind: "invalid" };

export const HUB_ROUTE_EVENT = "dnd2024-hub-navigation";

export function parseHubRoute(hash: string): HubRoute {
  if (!/^#view(?:\?|$)/u.test(hash)) return { kind: "none" };
  if (hash.length > 500 || /%(?![a-fA-F0-9]{2})/u.test(hash) ||
      (hash.match(/\?/gu) ?? []).length !== 1) return { kind: "invalid" };
  const parameters = new URLSearchParams(hash.slice(hash.indexOf("?") + 1));
  if ([...parameters.keys()].some((key) => !["tab", "section"].includes(key) ||
      parameters.getAll(key).length !== 1)) return { kind: "invalid" };
  const requestedTab = parameters.get("tab");
  const tab = normalizeMainTab(requestedTab) as MainTabId;
  if (tab !== requestedTab) return { kind: "invalid" };
  const requestedSection = parameters.get("section");
  if (tab !== "campaign" && requestedSection !== null) return { kind: "invalid" };
  const campaignSection = normalizeCampaignSection(requestedSection ?? "overview") as CampaignSectionId;
  if (tab === "campaign" && requestedSection !== null && campaignSection !== requestedSection)
    return { kind: "invalid" };
  return { kind: "hub", tab, campaignSection };
}

export function hubRouteHash(tab: MainTabId, campaignSection: CampaignSectionId = "overview") {
  const parameters = new URLSearchParams({ tab });
  if (tab === "campaign") parameters.set("section", campaignSection);
  return `#view?${parameters}`;
}

export function navigateHubRoute(
  tab: MainTabId,
  campaignSection: CampaignSectionId = "overview",
  replace = false,
) {
  const route = hubRouteHash(tab, campaignSection);
  if (window.location.hash === route) return;
  const target = `${window.location.pathname}${window.location.search}${route}`;
  window.history[replace ? "replaceState" : "pushState"](
    { ...window.history.state, itemInventoryReturn: null, itemInventoryOrigin: null, itemMainTab: tab },
    "",
    target,
  );
  window.dispatchEvent(new Event(HUB_ROUTE_EVENT));
}
