import { useCallback, useMemo, useRef, type ComponentProps, type ReactNode } from "react";
import { PartyView } from "../PartyView";
import { ItemView } from "./ItemView";
import { itemRouteHash, navigateItemRoute, readInventoryReturn, type ItemNavigationRoute } from "../../data/item-view-route";
import type { Perspective, ReadyHubEnvelope } from "../../data/hub-types";
import type { ItemViewClient } from "../../server/item-view-client";
import { ConnectedItemView, type ItemDetailsLoader, type ItemRecipesLoader, type ItemUsesLoader } from "./ConnectedItemView";
import { CharacterShell } from "../character/CharacterShell";

export function ItemWorkspace({ route, campaignId, perspective, context, itemScope = "", loadItemDetails, loadItemUses, loadItemRecipes, itemClient, ...partyProps }: ComponentProps<typeof PartyView> & {
  route: ItemNavigationRoute; campaignId: string; perspective: Perspective;
  context?: Pick<ReadyHubEnvelope, "applicationId" | "stateSpaceId" | "revision">;
  itemScope?: string;
  loadItemDetails?: ItemDetailsLoader;
  loadItemUses?: ItemUsesLoader;
  loadItemRecipes?: ItemRecipesLoader;
  /** Fixture-only no-cache adapter; production receives Redux-backed loaders. */
  itemClient?: ItemViewClient;
}) {
  // The hub rematerializes its envelope when the optional direct-link header
  // read commits. Item effects must keep their route request and loader identity
  // through that unrelated character-facet update, or the details read restarts
  // itself before the header can settle.
  const loaders = useRef({ details: loadItemDetails, uses: loadItemUses, recipes: loadItemRecipes });
  loaders.current = { details: loadItemDetails, uses: loadItemUses, recipes: loadItemRecipes };
  const readDetails = useCallback<ItemDetailsLoader>((request, signal, preferCached) => {
    const loader = loaders.current.details;
    if (!loader) return Promise.reject(new Error("Item details loading is unavailable."));
    return loader(request, signal, preferCached);
  }, []);
  const readUses = useCallback<ItemUsesLoader>((request, signal, preferCached) => {
    const loader = loaders.current.uses;
    if (!loader) return Promise.reject(new Error("Item uses loading is unavailable."));
    return loader(request, signal, preferCached);
  }, []);
  const readRecipes = useCallback<ItemRecipesLoader>((request, signal, preferCached) => {
    const loader = loaders.current.recipes;
    if (!loader) return Promise.reject(new Error("Item recipes loading is unavailable."));
    return loader(request, signal, preferCached);
  }, []);
  const selected = route.kind === "item" || route.kind === "inventory" ? route : null;
  const compatible = selected && selected.campaignId === campaignId && selected.perspective === perspective &&
    partyProps.party.some((member) => member.id === selected.characterId);
  const itemRequest = useMemo(() => route.kind === "item" && context ? {
    applicationId: context.applicationId, stateSpaceId: context.stateSpaceId, contextRevision: context.revision,
    campaignId, perspective, observerId: route.characterId, itemId: route.itemId,
  } : null, [campaignId, context?.applicationId, context?.revision, context?.stateSpaceId, perspective,
    route.kind, route.kind === "item" ? route.characterId : null, route.kind === "item" ? route.itemId : null]);
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
    if (!compatible || !context || route.kind !== "item" ||
        ((!loadItemDetails || !loadItemUses || !loadItemRecipes) && !itemClient))
      return wrap(<ItemView {...navigation} />);
    if (!itemRequest) return wrap(<ItemView {...navigation} />);
    return wrap(<ConnectedItemView key={`${itemScope}:${route.itemId}`} {...navigation} request={itemRequest} scope={itemScope}
      loadDetails={loadItemDetails ? readDetails : undefined} loadUses={loadItemUses ? readUses : undefined}
      loadRecipes={loadItemRecipes ? readRecipes : undefined} client={itemClient} />);
  }
  return <PartyView {...partyProps} key={selected?.characterId ?? "party"} navigationCharacterId={selected?.characterId}
    navigationSection={selected?.kind === "inventory" ? "inventory" : partyProps.navigationSection} inventoryReturn={returnContext}
    onOpenItem={(characterId, itemId, context) => {
      const inventory = { kind: "inventory" as const, characterId, campaignId, perspective };
      navigateItemRoute(inventory, true, context);
      navigateItemRoute({ ...inventory, kind: "item", itemId, tab: "details" }, false, context);
    }} />;
}
