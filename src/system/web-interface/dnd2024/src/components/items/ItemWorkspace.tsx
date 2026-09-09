import { useEffect, useMemo, type ComponentProps, type ReactNode } from "react";
import { PartyView } from "../PartyView";
import { ItemView } from "./ItemView";
import { itemRouteHash, navigateItemRoute, readInventoryReturn, type ItemNavigationRoute } from "../../data/item-view-route";
import type { Perspective, ReadyHubEnvelope } from "../../data/hub-types";
import { ItemViewClient } from "../../server/item-view-client";
import { ConnectedItemView } from "./ConnectedItemView";
import { objectConsumers } from "../../data/scoped-change-stream";
import { CharacterShell } from "../character/CharacterShell";

export function ItemWorkspace({ route, campaignId, perspective, context, itemClient, retainItemClient, ...partyProps }: ComponentProps<typeof PartyView> & {
  route: ItemNavigationRoute; campaignId: string; perspective: Perspective;
  context?: Pick<ReadyHubEnvelope, "applicationId" | "stateSpaceId" | "revision">;
  itemClient?: ItemViewClient;
  retainItemClient?: (client: ItemViewClient) => void;
}) {
  const client = useMemo(() => itemClient ?? new ItemViewClient(), [context, itemClient]);
  useEffect(() => retainItemClient?.(client), [client, retainItemClient]);
  useEffect(() => {
    const changed = (event: Event) => {
      if (objectConsumers((event as CustomEvent).detail?.object?.qualifiedId).item)
        client.invalidate("object-change");
    };
    const invalidated = (event: Event) => {
      const reason = (event as CustomEvent).detail?.reason;
      client.invalidate(["stream-recovery", "stream-error", "pagehide", "unknown-object",
        "release-replaced", "workspace-replaced"].includes(reason) ? reason : "stream-recovery");
    };
    window.addEventListener("dnd2024-object-changed", changed);
    window.addEventListener("dnd2024-view-invalidated", invalidated);
    return () => {
      window.removeEventListener("dnd2024-object-changed", changed);
      window.removeEventListener("dnd2024-view-invalidated", invalidated);
      if (!retainItemClient) client.invalidate("scope-replaced");
    };
  }, [client, retainItemClient]);
  const selected = route.kind === "item" || route.kind === "inventory" ? route : null;
  const compatible = selected && selected.campaignId === campaignId && selected.perspective === perspective &&
    partyProps.party.some((member) => member.id === selected.characterId);
  const returnContext = selected ? readInventoryReturn(window.history.state, selected.characterId) : null;
  if (route.kind === "item" || route.kind === "invalid" || selected && !compatible) {
    const selectedMember = compatible && selected
      ? partyProps.party.find((member) => member.id === selected.characterId) ?? null : null;
    const navigation: ComponentProps<typeof ItemView> = { tab: route.kind === "item" ? route.tab : "details",
      onTab: (tab) => { if (route.kind === "item") navigateItemRoute({ ...route, tab }, true, returnContext); },
      onBack: () => {
        const inventory = compatible ? { kind: "inventory" as const, characterId: selected.characterId, campaignId, perspective } : null;
        if (inventory && window.history.state?.itemInventoryOrigin === itemRouteHash(inventory)) window.history.back();
        else navigateItemRoute(inventory, true, returnContext);
      },
      characterName: selectedMember?.name,
      onParty: selectedMember && partyProps.onNavigationChange
        ? () => partyProps.onNavigationChange!(selectedMember.id, "overview") : undefined,
    };
    const wrap = (content: ReactNode) => selectedMember ? <CharacterShell
      onSelectMember={(id) => partyProps.onNavigationChange?.(id, "overview")}
      onSelectSection={(section) => section === "inventory" ? navigation.onBack()
        : partyProps.onNavigationChange?.(selectedMember.id, section)}
      party={partyProps.party}
      section="inventory"
      selectedMember={selectedMember}
    >{content}</CharacterShell> : content;
    if (!compatible || !context || route.kind !== "item") return wrap(<ItemView {...navigation} />);
    const request = { applicationId: context.applicationId, stateSpaceId: context.stateSpaceId, contextRevision: context.revision,
      campaignId, perspective, observerId: route.characterId, itemId: route.itemId };
    return wrap(<ConnectedItemView key={client.key(request)} {...navigation} client={client} request={request} />);
  }
  return <PartyView {...partyProps} key={selected?.characterId ?? "party"} navigationCharacterId={selected?.characterId}
    navigationSection={selected?.kind === "inventory" ? "inventory" : partyProps.navigationSection} inventoryReturn={returnContext}
    onOpenItem={(characterId, itemId, context) => {
      const inventory = { kind: "inventory" as const, characterId, campaignId, perspective };
      navigateItemRoute(inventory, true, context);
      navigateItemRoute({ ...inventory, kind: "item", itemId, tab: "details" }, false, context);
    }} />;
}
