import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { ConnectedItemView } from "../../src/components/items/ConnectedItemView";
import { ItemViewClient, readItemDetails, type ItemDetailsRequest } from "../../src/server/item-view-client";
import { commitItemFacet, createHubStore, itemActions, itemFacetKey, itemScope, MAX_CONFIRMED_ITEM_BYTES, MAX_CONFIRMED_ITEM_ENTRIES, selectItemFacet } from "../../src/data/hub-store";
import { HubStoreProvider } from "../../src/data/hub-store-provider";
import { ItemResourceOwner } from "../../src/data/item-resource-owner";
import type { ReadyHubEnvelope } from "../../src/data/hub-types";
import { itemRequest, itemData, itemEnvelope } from "../fixtures/item-details";

const response = (value: unknown) => new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json" } });
const tick = () => new Promise((resolve) => setTimeout(resolve, 10));
function itemHubEnvelope(request: ItemDetailsRequest): ReadyHubEnvelope {
  return {
    applicationId: request.applicationId, stateSpaceId: request.stateSpaceId, revision: request.contextRevision,
    world: { id: "world.fixture", name: "Fixture world" }, audience: { seat: request.observerId, perspective: request.perspective },
    party: [], contextSelection: { selectedCampaignId: request.campaignId, selectedWorldId: "world.fixture" }, objectQueries: {},
  } as ReadyHubEnvelope;
}
async function mounted(client: ItemViewClient, initial = itemRequest, maximumAgeMs?: number) {
  const dom = new JSDOM("<html><body><div id='root'></div></body></html>", { url: "https://table.test/", pretendToBeVisual: true });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true, value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  const { createRoot } = await import("react-dom/client");
  const store = createHubStore();
  const owner = new ItemResourceOwner({ store, maximumAgeMs,
    readDetails: (request, signal) => client.loadDetails(request, signal),
    readUses: (request, signal) => client.loadUses(request, signal),
    readRecipes: (request, signal) => client.loadRecipes(request, signal),
  });
  const container = dom.window.document.getElementById("root")!; const root = createRoot(container);
  const render = async (request = initial, tab: "details" | "recipes" | "uses" = "details") => {
    const envelope = itemHubEnvelope(request);
    await act(async () => { root.render(<HubStoreProvider store={store}><ConnectedItemView request={request} scope={itemScope(envelope)}
      loadDetails={(value, signal, preferCached) => owner.loadDetails(envelope, value, signal, preferCached)}
      loadUses={(value, signal, preferCached) => owner.loadUses(envelope, value, signal, preferCached)}
      loadRecipes={(value, signal, preferCached) => owner.loadRecipes(envelope, value, signal, preferCached)}
      tab={tab} onTab={() => {}} onBack={() => {}} /></HubStoreProvider>); await tick(); });
    await act(tick);
  };
  await render();
  return { container, render, store, owner, async cleanup() { await act(async () => root.unmount()); dom.window.close(); keys.forEach((key, index) => {
    if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!); else Reflect.deleteProperty(globalThis, key);
  }); } };
}

test("Details makes one actor-scoped, read-only request with closed input", async () => {
  const calls: { url: string; init?: RequestInit }[] = [];
  const result = await readItemDetails(itemRequest, new AbortController().signal, (async (url, init) => {
    calls.push({ url: String(url), init }); return response(itemEnvelope());
  }) as typeof fetch);
  assert.equal(result.status, "ready"); assert.equal(calls.length, 1);
  const url = new URL(calls[0].url, "https://table.test");
  assert.match(url.pathname, /entities\/actor.fixture\/read-models\/dnd2024.query.inventory-item-details$/);
  assert.deepEqual(JSON.parse(url.searchParams.get("input")!), { itemId: "item.staff", selectionId: itemRequest.campaignId });
  assert.deepEqual([...url.searchParams.keys()].sort(), ["input", "perspective"]);
  assert.equal(calls[0].init?.cache, "no-store"); assert.equal(calls[0].init?.credentials, "same-origin");
  assert.equal(calls[0].init?.method, undefined);
});

test("forged scope and transport evidence fail, while malformed display fields degrade locally", async () => {
  const rejected = [
    (e: any) => { e.applicationId = "other"; }, (e: any) => { e.stateSpaceId = "other"; },
    (e: any) => { e.data.itemId = "other"; }, (e: any) => { e.data.observerId = "other"; },
    (e: any) => { e.data.perspective = "dm"; }, (e: any) => { e.outputSchemaHash = "not-a-fingerprint"; },
    (e: any) => { delete e.sourceRevisionFingerprint; }, (e: any) => { e.data.name = "x".repeat(80_000); },
  ];
  for (const mutate of rejected) {
    const envelope = itemEnvelope(); mutate(envelope);
    await assert.rejects(() => readItemDetails(itemRequest, new AbortController().signal, (async () => response(envelope)) as typeof fetch), /response|limit/);
  }
  for (const mutate of [
    (e: any) => { e.data.secret = "PRIVATE"; },
    (e: any) => { e.data.media = [{ contentUrl: "/api/entities/private/media", alt: "PRIVATE", caption: null }]; },
    (e: any) => { e.data.properties[0].observerKnowledge = "known"; },
    (e: any) => { e.data.state = "partial"; },
  ]) {
    const envelope = itemEnvelope(); mutate(envelope);
    const result = await readItemDetails(itemRequest, new AbortController().signal, (async () => response(envelope)) as typeof fetch);
    assert.equal(result.status, "ready");
    assert.equal(result.data.itemId, itemRequest.itemId);
  }
  const playerKnowledge = itemEnvelope(); playerKnowledge.data.observerKnowledge = "known";
  const playerResult = await readItemDetails(itemRequest, new AbortController().signal,
    (async () => response(playerKnowledge)) as typeof fetch);
  assert.equal(playerResult.status, "ready");
  assert.equal(playerResult.data.observerKnowledge, null, "DM-only top-level knowledge is not projected to players");
  for (const [status, expected] of [[403, "forbidden"], [404, "unavailable"], [409, "stale"], [503, "unavailable"]] as const) {
    assert.deepEqual(await readItemDetails(itemRequest, new AbortController().signal, (async () => new Response("PRIVATE SERVER ERROR", { status })) as typeof fetch), { status: expected, data: null });
  }
});

test("Details renders exact zero/false, units, partial reasons and uncertain sources; tabs reuse one read", async () => {
  const request = { ...itemRequest, itemId: "item.special", perspective: "dm" as const };
  const data = itemData(request); data.state = "partial"; data.reasons = ["dependency-unavailable"];
  let calls = 0;
  const client = new ItemViewClient((async (url) => {
    if (!String(url).includes("inventory-item-details")) return new Response(null, { status: 404 });
    calls++; return response(itemEnvelope(request, data));
  }) as typeof fetch);
  const view = await mounted(client, request);
  try {
    assert.equal(view.container.querySelector("h2")?.textContent, data.name);
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
    assert.equal(view.container.querySelector("h2")?.textContent, "Item"); assert.equal(view.container.querySelector("img"), null);
    const second = { ...itemRequest, itemId: "item.pack" };
    await view.render(second);
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });
    assert.equal(pending.length, 3);
    await act(async () => { pending[1].resolve(response(itemEnvelope(itemRequest))); pending[2].resolve(response(itemEnvelope(second))); await tick(); });
    assert.match(view.container.textContent!, /Weathered backpack/); assert.doesNotMatch(view.container.textContent!, /Travel staff|PRIVATE/);
    await act(async () => { view.owner.invalidateAll(); await tick(); });
    assert.equal(view.container.querySelector("h2")?.textContent, "Item");
    await act(async () => { pending[3].resolve(new Response(null, { status: 404 })); await tick(); });
    assert.match(view.container.textContent!, /Item details unavailable/); assert.doesNotMatch(view.container.textContent!, /Weathered backpack|PRIVATE/);
  } finally { await view.cleanup(); }
});

test("fresh return uses cache and expiry revalidates without hiding last-good content", async () => {
  let calls = 0;
  const client = new ItemViewClient((async (url) => {
    if (String(url).includes("inventory-item-details")) calls += 1;
    return response(itemEnvelope());
  }) as typeof fetch);
  const view = await mounted(client, itemRequest, 250);
  try {
    await view.render(itemRequest, "recipes"); await view.render(itemRequest, "details");
    assert.equal(calls, 1); assert.match(view.container.textContent!, /Travel staff/);
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 270)); });
    assert.equal(view.container.querySelector("h2")?.textContent, "Travel staff");
    assert.equal(calls, 2); assert.match(view.container.textContent!, /Travel staff/);
  } finally { await view.cleanup(); }
});

test("Details accepts additive metadata and a different valid output schema hash", async () => {
  const changed = itemEnvelope(); changed.outputSchemaHash = "0".repeat(64);
  Object.assign(changed, { newMetadata: { producerVersion: 2 } });
  const result = await readItemDetails(itemRequest, new AbortController().signal,
    (async () => response(changed)) as typeof fetch);
  assert.equal(result.status, "ready");
  assert.equal(result.data.name, itemData(itemRequest).name);
});

test("same-key Item consumers share one read and different Item keys remain concurrent", async () => {
  const pending: { request: ItemDetailsRequest; resolve: (response: Response) => void }[] = [];
  const client = new ItemViewClient(((url) => new Promise<Response>((resolve) => {
    const parsed = new URL(String(url), "https://table.test");
    pending.push({ request: { ...itemRequest,
      perspective: parsed.searchParams.get("perspective") as "player" | "dm",
      itemId: JSON.parse(parsed.searchParams.get("input")!).itemId }, resolve });
  })) as typeof fetch);
  const other = { ...itemRequest, itemId: "item.pack" };
  const store = createHubStore();
  const owner = new ItemResourceOwner({ store, readDetails: (request, signal) => client.loadDetails(request, signal),
    readUses: (request, signal) => client.loadUses(request, signal), readRecipes: (request, signal) => client.loadRecipes(request, signal) });
  const first = owner.loadDetails(itemHubEnvelope(itemRequest), itemRequest);
  const shared = owner.loadDetails(itemHubEnvelope(itemRequest), itemRequest);
  const separate = owner.loadDetails(itemHubEnvelope(other), other);
  assert.equal(pending.length, 2);
  for (const read of pending) read.resolve(response(itemEnvelope(read.request)));
  await Promise.all([first, shared, separate]);
  const scope = itemScope(itemHubEnvelope(other));
  assert.equal(selectItemFacet(scope, itemFacetKey("details", other))(store.getState())?.status, "ready");
  assert.equal(Object.keys(store.getState().items.entries).length, 2);
});

test("failed background refresh keeps the last valid Item details visible", async () => {
  let calls = 0;
  let fail = false;
  const client = new ItemViewClient((async () => {
    calls += 1;
    if (fail) throw new TypeError("offline");
    return response(itemEnvelope());
  }) as typeof fetch, 40);
  const view = await mounted(client, itemRequest, 40);
  try {
    fail = true;
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 90)); });
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 40)); });
    assert.ok(calls >= 2);
    assert.equal(view.container.querySelector("h2")?.textContent, "Travel staff");
    assert.match(view.container.textContent!, /last available details remain visible/i);
  } finally { await view.cleanup(); }
});

test("confirmed Details cannot cross observer, binding lifetime, campaign, state or perspective", async () => {
  const client = new ItemViewClient((async () => response(itemEnvelope())) as typeof fetch);
  const store = createHubStore();
  const owner = new ItemResourceOwner({ store, readDetails: (request, signal) => client.loadDetails(request, signal),
    readUses: (request, signal) => client.loadUses(request, signal), readRecipes: (request, signal) => client.loadRecipes(request, signal) });
  const scope = itemScope(itemHubEnvelope(itemRequest));
  await owner.loadDetails(itemHubEnvelope(itemRequest), itemRequest);
  assert.ok(selectItemFacet(scope, itemFacetKey("details", itemRequest))(store.getState()));
  for (const request of [{ ...itemRequest, observerId: "actor.other" }, { ...itemRequest, campaignId: "campaign.other" },
    { ...itemRequest, applicationId: "other" }, { ...itemRequest, stateSpaceId: "state.other" },
    { ...itemRequest, perspective: "dm" as const }, { ...itemRequest, contextRevision: "new-binding" }]) {
    assert.equal(selectItemFacet(itemScope(itemHubEnvelope(request)), itemFacetKey("details", request))(store.getState()), null);
  }
  owner.invalidateAll(); assert.equal(selectItemFacet(scope, itemFacetKey("details", itemRequest))(store.getState()), null);
});

test("Redux item facets retain only an exact transient fallback, clear on denial, and stay bounded", async () => {
  let status: "ready" | "unavailable" | "forbidden" = "ready";
  const store = createHubStore();
  const owner = new ItemResourceOwner({ store,
    readDetails: async (request) => status === "ready"
      ? { status, data: { itemId: request.itemId, marker: request.itemId }, sourceRevision: "r", expiresAt: 0 } as any
      : { status, data: null } as any,
    readUses: async () => ({ status: "unavailable", data: null }) as any,
    readRecipes: async () => ({ status: "unavailable", data: null }) as any,
  });
  const envelope = itemHubEnvelope(itemRequest), scope = itemScope(envelope), key = itemFacetKey("details", itemRequest);
  await owner.loadDetails(envelope, itemRequest, undefined, false);
  status = "unavailable";
  await owner.loadDetails(envelope, itemRequest, undefined, false);
  assert.equal(selectItemFacet(scope, key)(store.getState())?.status, "ready", "same-key transient results retain only their exact confirmed view");
  status = "forbidden";
  await owner.loadDetails(envelope, itemRequest, undefined, false);
  assert.equal(selectItemFacet(scope, key)(store.getState())?.status, "forbidden", "an authorization denial clears the fallback");
  status = "ready";
  for (let index = 0; index < MAX_CONFIRMED_ITEM_ENTRIES + 3; index += 1) {
    const request = { ...itemRequest, itemId: `item.bound-${index}` };
    await owner.loadDetails(envelope, request, undefined, false);
  }
  assert.ok(Object.keys(store.getState().items.entries).length <= MAX_CONFIRMED_ITEM_ENTRIES);
  assert.ok(store.getState().items.retainedBytes <= MAX_CONFIRMED_ITEM_BYTES);
  const changedRequest = { ...itemRequest, perspective: "dm" as const };
  const changedEnvelope = itemHubEnvelope(changedRequest);
  await owner.loadDetails(changedEnvelope, changedRequest, undefined, false);
  assert.equal(selectItemFacet(scope, key)(store.getState()), null, "a changed audience scope cannot retain the prior observer's item");
});

test("forced same-scope replacement retires item data and a transient fallback keeps its original age", () => {
  const store = createHubStore();
  const scope = "scope.fixture", key = itemFacetKey("details", itemRequest);
  store.dispatch(itemActions.scopeReplaced({ scope }));
  assert.equal(store.getState().items.refreshEpoch, 0, "initial scope admission must not restart an active cold read");
  store.dispatch(itemActions.requestStarted({ scope, key, requestToken: 1 }));
  const ready = { status: "ready", data: { itemId: itemRequest.itemId }, sourceRevision: "r", expiresAt: 25 } as any;
  store.dispatch(commitItemFacet({ scope, key, value: ready }, { generation: 1, requestToken: 1, bytes: 40, confirmedAt: 10 }));
  store.dispatch(itemActions.requestStarted({ scope, key, requestToken: 2 }));
  store.dispatch(commitItemFacet({ scope, key, value: { status: "unavailable", data: null } },
    { generation: 1, requestToken: 2, bytes: 20, confirmedAt: 100 }));
  const fallback = store.getState().items.entries[key]!;
  assert.equal(fallback.value.status, "ready");
  assert.equal(fallback.confirmedAt, 10, "a failed refresh never makes old data fresh");
  assert.equal((fallback.value as any).expiresAt, 25);
  const generation = store.getState().items.generation;
  store.dispatch(itemActions.scopeReplaced({ scope, force: true }));
  assert.equal(store.getState().items.generation, generation + 1);
  assert.equal(store.getState().items.refreshEpoch, 1, "a forced same-scope replacement restarts active facets");
  assert.equal(Object.keys(store.getState().items.entries).length, 0);
  assert.equal(Object.keys(store.getState().items.requests).length, 0);
});
