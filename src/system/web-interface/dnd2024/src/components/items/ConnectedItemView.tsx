import { useEffect, useState, useSyncExternalStore, type ComponentProps } from "react";
import { ViewReadError } from "../../data/view-read-client";
import type { ItemDetailsRequest, ItemDetailsResult, ItemViewClient } from "../../server/item-view-client";
import { ItemDetails } from "./ItemDetails";
import { ItemView } from "./ItemView";

export function ConnectedItemView({ client, request, ...navigation }: ComponentProps<typeof ItemView> & {
  client: ItemViewClient; request: ItemDetailsRequest;
}) {
  const revision = useSyncExternalStore(client.subscribe, client.snapshot);
  const key = `${client.key(request)}:${revision}`;
  const [retry, setRetry] = useState(0);
  const [loaded, setLoaded] = useState<{ key: string; result: ItemDetailsResult } | null>(null);
  useEffect(() => {
    let active = true; let timer: ReturnType<typeof setTimeout> | undefined;
    const accept = (result: ItemDetailsResult) => {
      if (!active) return;
      setLoaded({ key, result });
      if (result.status === "ready") timer = setTimeout(() => {
        client.reads.invalidate(request);
        if (active) setLoaded({ key, result: { status: "stale", data: null } });
      }, Math.max(0, result.expiresAt - Date.now()));
    };
    setLoaded(null);
    if (document.visibilityState === "hidden") accept({ status: "stale", data: null });
    else {
      const cached = client.reads.peek(request);
      if (cached?.value.status === "ready" && cached.value.expiresAt > Date.now()) accept(cached.value);
      else void client.reads.load(request).then((result) => accept(result.value)).catch((error) => {
        if (error instanceof ViewReadError && error.category === "cancelled") return;
        accept({ status: "unavailable", data: null });
      });
    }
    return () => { active = false; clearTimeout(timer); client.reads.cancel(); };
    // Request identity is encoded in key, excluding the selected tab.
  }, [client, key, retry]);
  const result = loaded?.key === key ? loaded.result : null;
  const data = result?.status === "ready" && result.expiresAt > Date.now() ? result.data : null;
  const state = result?.status ?? "loading";
  const refresh = () => { client.reads.invalidate(request); setLoaded(null); setRetry((value) => value + 1); };
  return <ItemView {...navigation} name={data?.name} details={data ? <ItemDetails data={data} scopeKey={key} /> :
    <div role="status" aria-busy={state === "loading"}>
      <h2>{state === "loading" ? "Loading item details" : state === "stale" ? "Item details need a refresh" : state === "forbidden" ? "Item details unavailable in this perspective" : "Item details unavailable"}</h2>
      <p>{state === "loading" ? "Reading the selected character’s authorized information…" : state === "stale" ? "Refresh to see the current information." : "This item could not be read. Its previous details are no longer shown."}</p>
      {state !== "loading" ? <button type="button" onClick={refresh}>Refresh details</button> : null}
    </div>} />;
}
