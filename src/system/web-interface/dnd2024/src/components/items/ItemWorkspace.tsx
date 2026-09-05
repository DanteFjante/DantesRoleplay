import { useEffect, useMemo, type ComponentProps } from "react";
import { PartyView } from "../PartyView";
import { ItemView } from "./ItemView";
import { itemRouteHash, navigateItemRoute, readInventoryReturn, type ItemNavigationRoute } from "../../data/item-view-route";
import type { Perspective, ReadyHubEnvelope } from "../../data/hub-types";
import { ItemViewClient } from "../../server/item-view-client";
import { ConnectedItemView } from "./ConnectedItemView";

export function ItemWorkspace({ route, campaignId, perspective, context, itemClient, ...partyProps }: ComponentProps<typeof PartyView> & {
  route: ItemNavigationRoute; campaignId: string; perspective: Perspective;
  context?: Pick<ReadyHubEnvelope, "applicationId" | "stateSpaceId" | "revision">;
  itemClient?: ItemViewClient;
}) {
  const client = useMemo(() => itemClient ?? new ItemViewClient(), [context, itemClient]);
  useEffect(() => {
    for (const event of ["dnd2024-view-invalidated", "focus", "pagehide"]) window.addEventListener(event, client.invalidate);
    document.addEventListener("visibilitychange", client.invalidate);
    return () => {
      for (const event of ["dnd2024-view-invalidated", "focus", "pagehide"]) window.removeEventListener(event, client.invalidate);
      document.removeEventListener("visibilitychange", client.invalidate);
      client.invalidate();
    };
  }, [client]);
  const selected = route.kind === "item" || route.kind === "inventory" ? route : null;
  const compatible = selected && selected.campaignId === campaignId && selected.perspective === perspective &&
    partyProps.party.some((member) => member.id === selected.characterId);
  const returnContext = selected ? readInventoryReturn(window.history.state, selected.characterId) : null;
  if (route.kind === "item" || route.kind === "invalid" || selected && !compatible) {
    const navigation: ComponentProps<typeof ItemView> = { tab: route.kind === "item" ? route.tab : "details",
      onTab: (tab) => { if (route.kind === "item") navigateItemRoute({ ...route, tab }, true, returnContext); },
      onBack: () => {
        const inventory = compatible ? { kind: "inventory" as const, characterId: selected.characterId, campaignId, perspective } : null;
        if (inventory && window.history.state?.itemInventoryOrigin === itemRouteHash(inventory)) window.history.back();
        else navigateItemRoute(inventory, true, returnContext);
      } };
    if (!compatible || !context || route.kind !== "item") return <ItemView {...navigation} />;
    const request = { applicationId: context.applicationId, stateSpaceId: context.stateSpaceId, contextRevision: context.revision,
      campaignId, perspective, observerId: route.characterId, itemId: route.itemId };
    return <ConnectedItemView key={client.key(request)} {...navigation} client={client} request={request} />;
  }
  return <PartyView {...partyProps} key={selected?.characterId ?? "party"} navigationCharacterId={selected?.characterId} inventoryReturn={returnContext}
    onOpenItem={(characterId, itemId, context) => {
      const inventory = { kind: "inventory" as const, characterId, campaignId, perspective };
      navigateItemRoute(inventory, true, context);
      navigateItemRoute({ ...inventory, kind: "item", itemId, tab: "details" }, false, context);
    }} />;
}
