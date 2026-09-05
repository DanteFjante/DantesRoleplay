import React, { useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { ConnectedItemView } from "../../src/components/items/ConnectedItemView";
import { ItemViewClient } from "../../src/server/item-view-client";
import { itemRequest, itemEnvelope } from "../fixtures/item-details";
import { recipeRequest, recipeData, recipeEnvelope } from "../fixtures/item-recipes";
import type { ItemTab } from "../../src/data/item-view-route";
import "../../src/styles.css";
import "../../src/item-page.css";
function Fixture() {
  const [tab, setTab] = useState<ItemTab>("details"), [scenario, setScenario] = useState("Known recipes"), [reads, setReads] = useState(0);
  const client = useMemo(() => new ItemViewClient((async (url) => {
    await new Promise(resolve => setTimeout(resolve, 150));
    if(String(url).includes("inventory-item-details")) return new Response(JSON.stringify(itemEnvelope()));
    setReads(n => n + 1);
    const request = { ...recipeRequest, ...JSON.parse(new URL(String(url), window.location.origin).searchParams.get("input")!) };
    if(scenario === "Changed source") return new Response(null, { status: 409 });
    const data = recipeData(request);
    if(scenario === "No known recipes") data.makes = data.uses = { state: "empty", entries: [], reasons: [], nextOffset: null };
    if(scenario === "Incomplete recipe") { data.makes = { state: "partial", entries: [], nextOffset: null, reasons: ["source-incomplete"] };
      data.uses.state = "partial"; data.uses.reasons = ["source-incomplete"]; data.uses.entries[0].availability = "definition-incomplete"; }
    return new Response(JSON.stringify(recipeEnvelope(request, data)));
  }) as typeof fetch), [scenario]);
  return <main style={{ maxWidth: 1100, margin: "0 auto", padding: "24px 12px" }}>
    <aside aria-label="Disposable fixture controls"><p>Disposable preview · {reads} recipe reads · no live data</p>
      <div style={{ display: "flex", gap: 8, flexWrap: "wrap", marginBottom: 20 }}>{["Known recipes", "No known recipes", "Incomplete recipe", "Changed source"].map(name =>
        <button style={{ minHeight: 44 }} key={name} type="button" onClick={() => setScenario(name)}>{name}</button>)}</div></aside>
    <ConnectedItemView request={itemRequest} client={client} tab={tab} onTab={setTab} onBack={() => setTab("details")} />
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
