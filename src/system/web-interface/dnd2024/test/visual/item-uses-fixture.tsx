import React, { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { ConnectedItemView } from "../../src/components/items/ConnectedItemView";
import { ItemViewClient } from "../../src/server/item-view-client";
import { itemRequest, itemEnvelope } from "../fixtures/item-details";
import { usesRequest, usesData, usesEnvelope } from "../fixtures/item-uses";
import { recipeEnvelope } from "../fixtures/item-recipes";
import type { ItemTab } from "../../src/data/item-view-route";
import "../../src/styles.css";
import "../../src/item-page.css";
function Fixture() {
  const [tab, setTab] = useState<ItemTab>("details"), [scenario, setScenario] = useState("Populated uses"), [reads, setReads] = useState(0);
  const client = useMemo(() => new ItemViewClient((async url => {
    await new Promise(resolve => setTimeout(resolve, 150));
    if(String(url).includes("inventory-item-details")) return new Response(JSON.stringify(itemEnvelope()));
    if(String(url).includes("inventory-item-recipes")) return new Response(JSON.stringify(recipeEnvelope()));
    setReads(n => n + 1);
    const request = { ...usesRequest, ...JSON.parse(new URL(String(url), window.location.origin).searchParams.get("input")!) };
    if(scenario === "Changed source") return new Response(null, { status: 409 });
    const data = usesData(request);
    if(scenario === "No known uses") data.uses = { state: "empty", entries: [], reasons: [], nextOffset: null };
    if(scenario === "Incomplete use") { data.uses.state = "partial"; data.uses.reasons = ["source-incomplete"]; data.uses.entries[0].availability = "definition-incomplete"; }
    return new Response(JSON.stringify(usesEnvelope(request, data)));
  }) as typeof fetch), [scenario]);
  return <main style={{ maxWidth: 1100, margin: "0 auto", padding: "24px 12px" }}>
    <aside aria-label="Disposable fixture controls"><p>Disposable preview · {reads} use reads · no live data</p>
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginBottom: 20 }}>{["Populated uses", "No known uses", "Incomplete use", "Changed source"].map(name =>
        <button style={{ minHeight: 44 }} key={name} type="button" onClick={() => setScenario(name)}>{name}</button>)}</div></aside>
    <ConnectedItemView request={itemRequest} client={client} tab={tab} onTab={setTab} onBack={() => setTab("details")} />
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
