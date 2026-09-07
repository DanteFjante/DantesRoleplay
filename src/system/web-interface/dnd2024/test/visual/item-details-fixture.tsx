import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { ItemWorkspace } from "../../src/components/items/ItemWorkspace";
import { ItemViewClient } from "../../src/server/item-view-client";
import { ITEM_ROUTE_EVENT, navigateItemRoute, parseItemRoute } from "../../src/data/item-view-route";
import { itemRequest, itemEnvelope } from "../fixtures/item-details";
import { hubSource } from "../support/hub-source.js";
import type { PartyMemberReadModel } from "../../src/data/hub-types";
import "../../src/styles.css";
import "../../src/character-page.css";

const context = { applicationId: itemRequest.applicationId, stateSpaceId: itemRequest.stateSpaceId, revision: "fixture" };
const party = [{ ...hubSource.party[0], id: itemRequest.observerId }] as PartyMemberReadModel[];
function Fixture() {
  const [route, setRoute] = useState(() => parseItemRoute(window.location.hash));
  const [reads, setReads] = useState(0);
  const select = (itemId: string) => navigateItemRoute({ kind: "item", characterId: itemRequest.observerId,
    campaignId: itemRequest.campaignId, perspective: itemId === "item.special" ? "dm" : "player", itemId, tab: "details" });
  const client = useMemo(() => new ItemViewClient((async (url, init) => {
    const parameters = new URL(String(url), window.location.origin).searchParams;
    setReads((value) => value + 1);
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(resolve, 150);
      init?.signal?.addEventListener("abort", () => { clearTimeout(timer); reject(new DOMException("Cancelled", "AbortError")); }, { once: true });
    });
    return new Response(JSON.stringify(itemEnvelope({ ...itemRequest, itemId: JSON.parse(parameters.get("input")!).itemId,
      perspective: parameters.get("perspective") as "player" | "dm" })), { headers: { "Content-Type": "application/json" } });
  }) as typeof fetch), []);
  useEffect(() => {
    const changed = () => setRoute(parseItemRoute(window.location.hash));
    for (const event of [ITEM_ROUTE_EVENT, "popstate", "hashchange"]) window.addEventListener(event, changed);
    if (route.kind === "none") select("item.staff");
    return () => { for (const event of [ITEM_ROUTE_EVENT, "popstate", "hashchange"]) window.removeEventListener(event, changed); };
  }, []);
  return <main style={{ maxWidth: 1220, margin: "0 auto", padding: "24px 12px" }}>
    <aside aria-label="Disposable fixture controls" style={{ marginBottom: 24 }}>
      <p>Disposable item preview · {reads} Details reads · no live game data</p>
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>{[["item.staff", "Mundane item"], ["item.special", "Special instance"], ["item.pack", "Container"], ["item.unknown", "Unidentified item"]].map(([id, label]) =>
        <button style={{ minHeight: 44, padding: 10 }} type="button" key={id} onClick={() => select(id)}>{label}</button>)}</div>
    </aside>
    <ItemWorkspace route={route} context={context} itemClient={client} party={party} campaignId={itemRequest.campaignId}
      perspective={route.kind === "item" || route.kind === "inventory" ? route.perspective : "player"} />
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
