import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import axe from "axe-core";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { itemRouteHash } from "../../src/data/item-view-route";
import { integrationEnvelope, integrationInventory, integrationRead, integrationResponse, type ItemRead } from "../fixtures/item-integration";
import "../../src/styles.css";
import "../../src/character-page.css";
import "../../src/item-page.css";

// The complete application uses its actual item client against disposable data.
// Any unexpected request fails instead of reaching a running game.
const reads: ItemRead[] = [], hubReads: string[] = [];
let updateCounts = () => {};
window.fetch = (async url => {
  const read = integrationRead(String(url)); reads.push(read); updateCounts();
  return integrationResponse(read);
}) as typeof fetch;
history.replaceState(null, "", itemRouteHash(integrationInventory));
const initialEnvelope = integrationEnvelope();
function Fixture() {
  const [, update] = useState(0), [audit, setAudit] = useState("Not checked");
  updateCounts = () => update(n => n + 1);
  async function checkAccessibility() {
    const root = document.getElementById("integration-app")!;
    const result = await axe.run(root);
    setAudit(JSON.stringify({ violations: result.violations.map(v => ({ id: v.id, impact: v.impact, nodes: v.nodes.map(n => ({ target: n.target, summary: n.failureSummary })) })), passes: result.passes.length }));
  }
  return <>
    <aside aria-label="Disposable fixture controls" style={{ padding: 8, overflowWrap: "anywhere" }}>
      <p>Disposable integration preview · no live data</p>
      <output id="read-counts">{JSON.stringify({ details: reads.filter(r => r.tab === "details").length, recipes: reads.filter(r => r.tab === "recipes").length, uses: reads.filter(r => r.tab === "uses").length, hub: hubReads.length })}</output>
      <button type="button" style={{ minHeight: 44, margin: 8 }} onClick={() => void checkAccessibility()}>Check accessibility</button>
      <output id="accessibility-result" style={{ display: "block" }}>{audit}</output>
    </aside>
    <div id="integration-app"><DndInformationHub initialEnvelope={initialEnvelope} loadContent={async () => ({}) as never} loadEnvelope={async perspective => {
      hubReads.push(perspective); updateCounts(); return integrationEnvelope(perspective);
    }} /></div>
  </>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
