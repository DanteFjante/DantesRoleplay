import type { ComponentProps } from "react";
import { PartyView } from "../PartyView";
import { ItemView } from "./ItemView";
import { itemRouteHash, navigateItemRoute, readInventoryReturn, type ItemNavigationRoute } from "../../data/item-view-route";
import type { Perspective } from "../../data/hub-types";

export function ItemWorkspace({ route, campaignId, perspective, ...partyProps }: ComponentProps<typeof PartyView> & {
  route: ItemNavigationRoute; campaignId: string; perspective: Perspective;
}) {
  const selected = route.kind === "item" || route.kind === "inventory" ? route : null;
  const compatible = selected && selected.campaignId === campaignId && selected.perspective === perspective &&
    partyProps.party.some((member) => member.id === selected.characterId);
  const returnContext = selected ? readInventoryReturn(window.history.state, selected.characterId) : null;
  if (route.kind === "item" || route.kind === "invalid" || selected && !compatible) {
    return <ItemView tab={route.kind === "item" ? route.tab : "details"}
      onTab={(tab) => { if (route.kind === "item") navigateItemRoute({ ...route, tab }, true, returnContext); }}
      onBack={() => {
        const inventory = compatible ? { kind: "inventory" as const, characterId: selected.characterId, campaignId, perspective } : null;
        if (inventory && window.history.state?.itemInventoryOrigin === itemRouteHash(inventory)) window.history.back();
        else navigateItemRoute(inventory, true, returnContext);
      }} />;
  }
  return <PartyView {...partyProps} key={selected?.characterId ?? "party"} navigationCharacterId={selected?.characterId} inventoryReturn={returnContext}
    onOpenItem={(characterId, itemId, context) => {
      const inventory = { kind: "inventory" as const, characterId, campaignId, perspective };
      navigateItemRoute(inventory, true, context);
      navigateItemRoute({ ...inventory, kind: "item", itemId, tab: "details" }, false, context);
    }} />;
}
