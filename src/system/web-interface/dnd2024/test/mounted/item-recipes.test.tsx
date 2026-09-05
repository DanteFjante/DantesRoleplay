import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { ItemViewClient } from "../../src/server/item-view-client";
import { readItemRecipes, type ItemRecipesRequest } from "../../src/server/item-recipes-client";
import { ConnectedItemView } from "../../src/components/items/ConnectedItemView";
import { itemEnvelope, itemRequest } from "../fixtures/item-details";
import { recipeData, recipeEnvelope, recipeRequest } from "../fixtures/item-recipes";
const response = (data: unknown) => new Response(JSON.stringify(data));
const tick = () => new Promise(resolve => setTimeout(resolve, 10));
async function mount(client: ItemViewClient) {
  const dom = new JSDOM("<html><body><div id='root'></div></body></html>", { url: "https://table.test", pretendToBeVisual: true });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const prior = keys.map(k => Object.getOwnPropertyDescriptor(globalThis, k));
  for(const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true, value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  const { createRoot } = await import("react-dom/client"); const container = document.getElementById("root")!; const root = createRoot(container);
  const render = async (tab: "recipes" | "details" = "recipes", request = itemRequest) => { await act(async () => { root.render(<ConnectedItemView tab={tab} request={request} client={client} onTab={() => {}} onBack={() => {}} />); await tick(); }); await act(tick); };
  const click = async (text: string) => { const button = [...container.querySelectorAll("button")].find(b => b.textContent?.includes(text)); assert.ok(button); await act(async () => { button.click(); await tick(); }); await act(tick); };
  return { container, render, click, async cleanup() { await act(async () => root.unmount()); dom.window.close(); keys.forEach((k, i) => { if(prior[i]) Object.defineProperty(globalThis, k, prior[i]!); else Reflect.deleteProperty(globalThis, k); }); } };
}
test("recipe transport validates closed scope, output, advancing offsets and source revision", async () => {
  let url = "";
  const result = await readItemRecipes(recipeRequest, new AbortController().signal, (async (u) => { url = String(u); return response(recipeEnvelope()); }) as typeof fetch);
  assert.equal(result.status, "ready"); assert.match(url, /entities\/actor.fixture\/read-models\/dnd2024.query.inventory-item-recipes/);
  assert.deepEqual(JSON.parse(new URL(url, "https://table.test").searchParams.get("input")!), { itemId: itemRequest.itemId, makesOffset: 0, usesOffset: 0, expectedSourceRevision: null });
  for(const mutate of [(e: any) => { e.data.observerId = "other"; }, (e: any) => { e.data.makes.entries[0].secret = "private"; },
    (e: any) => { e.data.makes.nextOffset = 0; }, (e: any) => { e.data.makes.entries.push(e.data.makes.entries[0]); },
    (e: any) => { e.outputSchemaHash = "0".repeat(64); }, (e: any) => { e.data.makes.entries[0].observerKnowledge = "known"; }]) {
    const envelope = recipeEnvelope(); mutate(envelope);
    await assert.rejects(readItemRecipes(recipeRequest, new AbortController().signal, (async () => response(envelope)) as typeof fetch));
  }
  const next = { ...recipeRequest, makesOffset: 1, expectedSourceRevision: "A".repeat(64) };
  const mismatch = recipeEnvelope(next); mismatch.sourceRevisionFingerprint = "B".repeat(64);
  assert.equal((await readItemRecipes(next, new AbortController().signal, (async () => response(mismatch)) as typeof fetch)).status, "stale");
});
test("first visit fetches Recipes once; groups, sources and independent pages remain accessible", async () => {
  const calls: ItemRecipesRequest[] = []; let details = 0;
  const client = new ItemViewClient((async (url) => {
    if(String(url).includes("inventory-item-details")) { details++; return response(itemEnvelope()); }
    const request = { ...recipeRequest, ...JSON.parse(new URL(String(url), "https://table.test").searchParams.get("input")!) }; calls.push(request);
    return response(recipeEnvelope(request));
  }) as typeof fetch);
  const view = await mount(client);
  try {
    await view.render("details"); assert.equal(calls.length, 0);
    await view.render(); assert.equal(calls.length, 1); assert.equal(details, 1);
    assert.equal(view.container.querySelectorAll("article").length, 2); assert.match(view.container.textContent!, /Makes this item/); assert.match(view.container.textContent!, /Uses this item/);
    assert.match(view.container.textContent!, /suspected/); assert.match(view.container.textContent!, /Availability not evaluated/); assert.match(view.container.textContent!, /2 × Resin/);
    assert.equal([...view.container.querySelectorAll("button")].some(button => /^(craft|start crafting)$/i.test(button.textContent ?? "")), false);
    await view.render("details"); await view.render(); assert.equal(calls.length, 1);
    await view.click("Next page: makes"); assert.equal(calls.length, 2); assert.equal(calls[1].makesOffset, 1); assert.equal(calls[1].usesOffset, 0); assert.ok(calls[1].expectedSourceRevision);
    assert.equal(view.container.querySelectorAll("article").length, 2);
    const axe = (await import("axe-core")).default;
    assert.deepEqual((await axe.run(view.container, { rules: { "color-contrast": { enabled: false } } })).violations.filter(v => ["serious", "critical"].includes(v.impact!)).map(v => v.id), []);
  } finally { await view.cleanup(); }
});
test("stale continuation clears every recipe and refresh restarts both groups", async () => {
  let stale = false; let reads = 0;
  const client = new ItemViewClient((async (url) => {
    if(String(url).includes("inventory-item-details")) return response(itemEnvelope());
    reads++; if(stale) return new Response(null, { status: 409 });
    return response(recipeEnvelope());
  }) as typeof fetch);
  const view = await mount(client);
  try { await view.render(); stale = true; await view.click("Next page: makes"); assert.equal(view.container.querySelectorAll("article").length, 0);
    assert.match(view.container.textContent!, /Recipes need a refresh/); stale = false; await view.click("Refresh recipes"); assert.equal(reads, 3); assert.equal(view.container.querySelectorAll("article").length, 2);
  } finally { await view.cleanup(); }
});
test("empty and incomplete groups have different messages and missing requirements remain visible", async () => {
  const data = recipeData(); data.makes = { state: "empty", entries: [], reasons: [], nextOffset: null };
  data.uses.state = "partial"; data.uses.reasons = ["source-incomplete"]; data.uses.entries[0].availability = "requirements-not-met";
  const client = new ItemViewClient((async (url) => response(String(url).includes("inventory-item-details") ? itemEnvelope() : recipeEnvelope(recipeRequest, data))) as typeof fetch);
  const view = await mount(client);
  try { await view.render(); assert.match(view.container.textContent!, /No known recipes in this group/); assert.match(view.container.textContent!, /Recipe list is partial/); assert.match(view.container.textContent!, /Requirements not met/); assert.equal(view.container.querySelectorAll("article").length, 1); }
  finally { await view.cleanup(); }
});
test("invalidation and slow item changes cannot restore retired recipe text", async () => {
  const pending: { request: ItemRecipesRequest; resolve: (r: Response) => void }[] = [];
  const client = new ItemViewClient((async (url) => {
    const request = { ...recipeRequest, ...JSON.parse(new URL(String(url), "https://table.test").searchParams.get("input")!) };
    if(String(url).includes("inventory-item-details")) return response(itemEnvelope(request));
    return new Promise<Response>(resolve => pending.push({ request, resolve }));
  }) as typeof fetch);
  const view = await mount(client);
  try { await view.render(); await view.render("recipes", { ...itemRequest, itemId: "item.pack" });
    await act(async () => { const old = recipeData(); old.makes.entries[0].name = "RETIRED PRIVATE RECIPE"; pending[0].resolve(response(recipeEnvelope(recipeRequest, old))); pending[1].resolve(response(recipeEnvelope(pending[1].request))); await tick(); });
    assert.doesNotMatch(view.container.textContent!, /RETIRED PRIVATE/);
    await act(async () => { client.invalidate(); await tick(); }); assert.equal(view.container.querySelectorAll("article").length, 0);
  } finally { await view.cleanup(); }
});
