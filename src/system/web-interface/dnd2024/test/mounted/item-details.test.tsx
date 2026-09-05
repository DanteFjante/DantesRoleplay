import assert from "node:assert/strict";
import test from "node:test";
import { execFileSync } from "node:child_process";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { ConnectedItemView } from "../../src/components/items/ConnectedItemView";
import { ItemViewClient, readItemDetails, type ItemDetailsRequest } from "../../src/server/item-view-client";
import { itemRequest, itemData, itemEnvelope } from "../fixtures/item-details";

const response = (value: unknown) => new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
const tick = () => new Promise((resolve) => setTimeout(resolve, 10));
async function mounted(client: ItemViewClient, initial = itemRequest) {
  const dom = new JSDOM("<html><body><div id='root'></div></body></html>", { url: "https://table.test/", pretendToBeVisual: true });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true, value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.getElementById("root")!; const root = createRoot(container);
  const render = async (request = initial, tab: "details" | "recipes" | "uses" = "details") => {
    await act(async () => { root.render(<ConnectedItemView request={request} client={client} tab={tab} onTab={() => {}} onBack={() => {}} />); await tick(); });
    await act(tick);
  };
  await render();
  return { container, render, async cleanup() { await act(async () => root.unmount()); dom.window.close(); keys.forEach((key, index) => {
    if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!); else Reflect.deleteProperty(globalThis, key);
  }); } };
}

test("the standalone item validator matches the current authored catalog", () => {
  execFileSync(process.execPath, ["scripts/generate-item-validator.mjs", "--check"], { cwd: new URL("../..", import.meta.url) });
});

test("Details makes one actor-scoped, read-only request with closed input", async () => {
  const calls: { url: string; init?: RequestInit }[] = [];
  const result = await readItemDetails(itemRequest, new AbortController().signal, (async (url, init) => {
    calls.push({ url: String(url), init }); return response(itemEnvelope());
  }) as typeof fetch);
  assert.equal(result.status, "ready"); assert.equal(calls.length, 1);
  const url = new URL(calls[0].url, "https://table.test");
  assert.match(url.pathname, /entities\/actor.fixture\/read-models\/dnd2024.query.inventory-item-details$/);
  assert.deepEqual(JSON.parse(url.searchParams.get("input")!), { itemId: "item.staff" });
  assert.deepEqual([...url.searchParams.keys()].sort(), ["campaignId", "input", "perspective"]);
  assert.equal(calls[0].init?.cache, "no-store"); assert.equal(calls[0].init?.credentials, "same-origin");
  assert.equal(calls[0].init?.method, undefined);
});

test("forged scope, schema, hidden fields, oversized data and unsafe media fail before display", async () => {
  const mutations = [
    (e: any) => { e.applicationId = "other"; }, (e: any) => { e.stateSpaceId = "other"; },
    (e: any) => { e.data.itemId = "other"; }, (e: any) => { e.data.observerId = "other"; },
    (e: any) => { e.data.perspective = "dm"; }, (e: any) => { e.outputSchemaHash = "0".repeat(64); },
    (e: any) => { delete e.sourceRevisionFingerprint; }, (e: any) => { e.data.secret = "PRIVATE"; },
    (e: any) => { e.data.media = [{ contentUrl: "/api/entities/private/media", alt: "PRIVATE", caption: null }]; },
    (e: any) => { e.data.properties[0].observerKnowledge = "known"; },
    (e: any) => { e.data.state = "partial"; }, (e: any) => { e.data.name = "x".repeat(80_000); },
  ];
  for (const mutate of mutations) {
    const envelope = itemEnvelope(); mutate(envelope);
    await assert.rejects(() => readItemDetails(itemRequest, new AbortController().signal, (async () => response(envelope)) as typeof fetch), /response|limit/);
  }
  for (const [status, expected] of [[403, "forbidden"], [404, "unavailable"], [409, "stale"], [503, "unavailable"]] as const) {
    assert.deepEqual(await readItemDetails(itemRequest, new AbortController().signal, (async () => new Response("PRIVATE SERVER ERROR", { status })) as typeof fetch), { status: expected, data: null });
  }
});

test("Details renders exact zero/false, units, partial reasons and uncertain sources; tabs reuse one read", async () => {
  const request = { ...itemRequest, itemId: "item.special", perspective: "dm" as const };
  const data = itemData(request); data.state = "partial"; data.reasons = ["dependency-unavailable"];
  let calls = 0;
  const client = new ItemViewClient((async () => { calls++; return response(itemEnvelope(request, data)); }) as typeof fetch);
  const view = await mounted(client, request);
  try {
    assert.equal(view.container.querySelector("h1")?.textContent, data.name);
    assert.match(view.container.textContent!, /4 lb/); assert.match(view.container.textContent!, /Recorded durability0/);
    assert.match(view.container.textContent!, /AttunedNo/); assert.match(view.container.textContent!, /suspected/);
    assert.match(view.container.textContent!, /Some supporting information is unavailable/);
    await view.render(request, "recipes"); await view.render(request, "details"); assert.equal(calls, 1);
    const axe = (await import("axe-core")).default;
    const audit = await axe.run(view.container, { rules: { "color-contrast": { enabled: false } } });
    assert.deepEqual(audit.violations.filter((v) => ["serious", "critical"].includes(v.impact!)).map((v) => v.id), []);
  } finally { await view.cleanup(); }
});

test("slow scope switches, invalidation and transfer never resurrect previous headers or images", async () => {
  const pending: { request: ItemDetailsRequest; resolve: (r: Response) => void }[] = [];
  const client = new ItemViewClient(((url) => new Promise<Response>((resolve) => {
    const u = new URL(String(url), "https://table.test");
    pending.push({ request: { ...itemRequest, perspective: u.searchParams.get("perspective") as "dm" | "player", itemId: JSON.parse(u.searchParams.get("input")!).itemId }, resolve });
  })) as typeof fetch);
  const dm = { ...itemRequest, perspective: "dm" as const };
  const view = await mounted(client, dm);
  try {
    const data = itemData(dm); data.name = "PRIVATE DM ITEM";
    data.media = [{ contentUrl: `/api/read-model-media/${"a".repeat(64)}/content`, alt: "PRIVATE IMAGE", caption: "PRIVATE CAPTION" }];
    await act(async () => { pending[0].resolve(response(itemEnvelope(dm, data))); await tick(); });
    assert.match(view.container.textContent!, /PRIVATE DM ITEM/); assert.equal(view.container.querySelectorAll("img").length, 1);
    await view.render(itemRequest);
    assert.equal(view.container.querySelector("h1")?.textContent, "Item"); assert.equal(view.container.querySelector("img"), null);
    const second = { ...itemRequest, itemId: "item.pack" };
    await view.render(second);
    await act(async () => { pending[1].resolve(response(itemEnvelope(itemRequest))); pending[2].resolve(response(itemEnvelope(second))); await tick(); });
    assert.match(view.container.textContent!, /Weathered backpack/); assert.doesNotMatch(view.container.textContent!, /Travel staff|PRIVATE/);
    await act(async () => { client.invalidate(); await tick(); });
    assert.equal(view.container.querySelector("h1")?.textContent, "Item");
    await act(async () => { pending[3].resolve(new Response(null, { status: 404 })); await tick(); });
    assert.match(view.container.textContent!, /Item details unavailable/); assert.doesNotMatch(view.container.textContent!, /Weathered backpack|PRIVATE/);
  } finally { await view.cleanup(); }
});

test("fresh return uses cache; expiry clears content without a polling loop", async () => {
  let calls = 0;
  const client = new ItemViewClient((async () => { calls++; return response(itemEnvelope()); }) as typeof fetch, 250);
  let view = await mounted(client);
  await view.cleanup(); view = await mounted(client);
  try {
    assert.equal(calls, 1); assert.match(view.container.textContent!, /Travel staff/);
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 270)); });
    assert.equal(view.container.querySelector("h1")?.textContent, "Item"); assert.match(view.container.textContent!, /Item details need a refresh/);
    assert.equal(calls, 1);
    await act(async () => { view.container.querySelector<HTMLButtonElement>('[role="status"] button')!.click(); await tick(); });
    assert.equal(calls, 2); assert.match(view.container.textContent!, /Travel staff/);
  } finally { await view.cleanup(); }
});

test("cached Details cannot cross observer, binding lifetime, campaign, state or perspective", async () => {
  const client = new ItemViewClient((async () => response(itemEnvelope())) as typeof fetch);
  await client.reads.load(itemRequest);
  assert.ok(client.reads.peek(itemRequest));
  for (const request of [{ ...itemRequest, observerId: "actor.other" }, { ...itemRequest, campaignId: "campaign.other" },
    { ...itemRequest, applicationId: "other" }, { ...itemRequest, stateSpaceId: "state.other" },
    { ...itemRequest, perspective: "dm" as const }, { ...itemRequest, contextRevision: "new-binding" }]) {
    assert.equal(client.reads.peek(request), null);
  }
  const rebound = new ItemViewClient((async () => response(itemEnvelope())) as typeof fetch);
  assert.equal(rebound.reads.peek(itemRequest), null);
  client.invalidate(); assert.equal(client.reads.peek(itemRequest), null);
});
