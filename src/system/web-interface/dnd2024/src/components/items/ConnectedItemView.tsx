import { useEffect, useState, type ComponentProps } from "react";
import { itemFacetKey, selectItemFacet, selectItemRefreshEpoch, useHubSelector } from "../../data/hub-store";
import { ViewReadError } from "../../data/view-read-client";
import type { ItemDetailsRequest, ItemDetailsResult, ItemViewClient } from "../../server/item-view-client";
import { ItemDetails } from "./ItemDetails";
import { ItemView, focusItemPanel } from "./ItemView";
import { ItemRecipes } from "./ItemRecipes";
import { ItemUses } from "./ItemUses";

export type ItemDetailsLoader = (request: ItemDetailsRequest, signal: AbortSignal,
  preferCached?: boolean) => Promise<ItemDetailsResult>;
export type ItemUsesLoader = (request: import("../../server/item-uses-client").ItemUsesRequest,
  signal: AbortSignal, preferCached?: boolean) => Promise<import("../../server/item-uses-client").ItemUsesResult>;
export type ItemRecipesLoader = (request: import("../../server/item-recipes-client").ItemRecipesRequest,
  signal: AbortSignal, preferCached?: boolean) => Promise<import("../../server/item-recipes-client").ItemRecipesResult>;

/** Renders the Redux-confirmed exact item query; component state is request UI only. */
export function ConnectedItemView({ request, scope = "", loadDetails, loadUses, loadRecipes, client, ...navigation }:
  ComponentProps<typeof ItemView> & {
    request: ItemDetailsRequest; scope?: string; loadDetails?: ItemDetailsLoader;
    loadUses?: ItemUsesLoader; loadRecipes?: ItemRecipesLoader; client?: ItemViewClient;
  }) {
  const key = itemFacetKey("details", request);
  const result = useHubSelector(selectItemFacet(scope, key)) as ItemDetailsResult | null;
  const refreshEpoch = useHubSelector(selectItemRefreshEpoch);
  const hasConfirmedResult = result !== null;
  const [retry, setRetry] = useState(0);
  const [loading, setLoading] = useState(true);
  const [refreshFailed, setRefreshFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    const read = loadDetails ?? (client ? (value: ItemDetailsRequest, signal: AbortSignal) => client.loadDetails(value, signal) : null);
    if (!read) { setLoading(false); return () => controller.abort(); }
    void read(request, controller.signal, retry === 0).then((value) => {
      if (!controller.signal.aborted) setRefreshFailed(value.status !== "ready");
    }).catch((error) => {
      if (!controller.signal.aborted && !(error instanceof ViewReadError && error.category === "cancelled"))
        setRefreshFailed(true);
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [scope, key, loadDetails, request, retry, client, hasConfirmedResult, refreshEpoch]);
  const expiresAt = result?.status === "ready" ? result.expiresAt : null;
  useEffect(() => {
    if (expiresAt === null) return;
    const timer = window.setTimeout(() => setRetry((value) => value + 1), Math.max(0, expiresAt - Date.now()));
    return () => window.clearTimeout(timer);
  }, [key, expiresAt]);
  const data = result?.status === "ready" ? result.data : null;
  const refresh = () => { focusItemPanel(); setRefreshFailed(false); setRetry((value) => value + 1); };
  return <ItemView {...navigation} name={data?.name}
    uses={<ItemUses request={request} scope={scope} loadUses={loadUses ?? (client ? (value, signal) => client.loadUses(value, signal) : undefined)} active={navigation.tab === "uses"} />}
    recipes={<ItemRecipes request={request} scope={scope} loadRecipes={loadRecipes ?? (client ? (value, signal) => client.loadRecipes(value, signal) : undefined)} active={navigation.tab === "recipes"} />}
    details={data ? <><ItemDetails data={data} scopeKey={key} />
      {refreshFailed ? <div className="item-details__notice" role="status"><strong>Could not refresh item details</strong><p>The last available details remain visible.</p><button type="button" onClick={refresh}>Try again</button></div> : null}</> :
      <div role="status" aria-busy={loading}>
        <h2>{loading ? "Loading item details" : result?.status === "forbidden" ? "Item details unavailable in this perspective" : "Item details unavailable"}</h2>
        <p>{loading ? "Reading the selected character’s authorized information…" : "This item could not be read. Its previous details are no longer shown."}</p>
        {!loading ? <button type="button" onClick={refresh}>Refresh details</button> : null}
      </div>} />;
}
