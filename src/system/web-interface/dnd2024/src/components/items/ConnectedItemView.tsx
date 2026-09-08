import { ItemUses } from "./ItemUses";
import { useEffect, useState, useSyncExternalStore, type ComponentProps } from "react";
import { ViewReadError } from "../../data/view-read-client";
import type { ItemDetailsRequest, ItemDetailsResult, ItemViewClient } from "../../server/item-view-client";
import { ItemDetails } from "./ItemDetails";
import { ItemView, focusItemPanel } from "./ItemView";
import { ItemRecipes } from "./ItemRecipes";

export function ConnectedItemView({ client, request, ...navigation }: ComponentProps<typeof ItemView> & {
  client: ItemViewClient; request: ItemDetailsRequest;
}) {
  const revision = useSyncExternalStore(client.subscribe, client.snapshot);
  const key = `${client.key(request)}:${revision}`;
  const [retry, setRetry] = useState(0);
  const [loaded, setLoaded] = useState<{ key: string; result: ItemDetailsResult } | null>(null);
  const [refreshFailed, setRefreshFailed] = useState(false);
  useEffect(() => {
    let active = true; let timer: ReturnType<typeof setTimeout> | undefined;
    const controller = new AbortController();
    const accept = (result: ItemDetailsResult, schedule = true) => {
      if (!active) return;
      setLoaded({ key, result });
      setRefreshFailed(false);
      if (schedule && result.status === "ready") timer = setTimeout(() => {
        if (active) setRetry((value) => value + 1);
      }, Math.max(0, result.expiresAt - Date.now()));
    };
    const cached = client.reads.peek(request);
    if (cached) accept(cached.value);
    else {
      const previous = client.reads.state(request);
      if ((previous.status === "ready" || previous.status === "stale") && previous.result.value.status === "ready")
        setLoaded({ key, result: previous.result.value });
      else setLoaded(null);
      void client.reads.load(request, { signal: controller.signal }).then((result) => accept(result.value)).catch((error) => {
        if (!active || error instanceof ViewReadError && error.category === "cancelled") return;
        const retained = client.reads.state(request);
        if (retained.status === "stale" && retained.result.value.status === "ready") {
          setLoaded({ key, result: retained.result.value });
          setRefreshFailed(true);
        } else accept({ status: "unavailable", data: null }, false);
      });
    }
    return () => { active = false; clearTimeout(timer); controller.abort(); };
    // Request identity is encoded in key, excluding the selected tab.
  }, [client, key, retry]);
  const result = loaded?.key === key ? loaded.result : null;
  const data = result?.status === "ready" ? result.data : null;
  const state = result?.status ?? "loading";
  const refresh = () => { focusItemPanel(); client.reads.invalidate(request, "manual"); setLoaded(null); setRetry((value) => value + 1); };
  return <ItemView {...navigation} name={data?.name} uses={<ItemUses key={key} client={client} request={request} active={navigation.tab === "uses"} />} recipes={<ItemRecipes key={key} client={client} request={request} active={navigation.tab === "recipes"} />} details={data ? <><ItemDetails data={data} scopeKey={key} />
    {refreshFailed ? <div className="item-details__notice" role="status"><strong>Could not refresh item details</strong><p>The last available details remain visible.</p><button type="button" onClick={refresh}>Try again</button></div> : null}</> :
    <div role="status" aria-busy={state === "loading"}>
      <h2>{state === "loading" ? "Loading item details" : state === "stale" ? "Item details need a refresh" : state === "forbidden" ? "Item details unavailable in this perspective" : "Item details unavailable"}</h2>
      <p>{state === "loading" ? "Reading the selected character’s authorized information…" : state === "stale" ? "Refresh to see the current information." : "This item could not be read. Its previous details are no longer shown."}</p>
      {state !== "loading" ? <button type="button" onClick={refresh}>Refresh details</button> : null}
    </div>} />;
}
