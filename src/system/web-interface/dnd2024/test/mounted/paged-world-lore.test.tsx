import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { Provider } from "react-redux";
import { PagedWorldLore } from "../../src/components/PagedWorldLore";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { collectionActions } from "../../src/data/collection-state";
import { createHubStore, type HubStore } from "../../src/data/hub-store";
import { hubRouteHash } from "../../src/data/hub-route";
import type { ReadyHubEnvelope } from "../../src/data/hub-types";
import { ViewReadError } from "../../src/data/view-read-client";
import type { LorePageLoader, LorePageRequest, WorldLorePage } from "../../src/data/world-lore-page";
import { resolveAudience } from "../support/audience-policy.js";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { HUB_SOURCE_REVISION, hubSource } from "../support/hub-source.js";

const sourceRevision = "A".repeat(64), graphRevision = "B".repeat(64), selectionFingerprint = "C".repeat(64);
const firstRequest: LorePageRequest = { query: "", category: "", kind: "", cursor: null };
function page(title: string, nextCursor: string | null = null): WorldLorePage {
  return { entries: [{ id: title, title, category: "History", status: "Known", summary: `${title} summary`, body: "",
    linkedLocations: [], linkedPeople: [], linkedFactions: [], linkedHistory: [] }],
    totalCount: 2, nextCursor, sourceRevision, graphRevision, selectionFingerprint,
    facets: { category: { event: 1 }, kind: { fact: 1 } }, coverage: "complete", filterable: true };
}
const component = (loadPage: LorePageLoader, scopeKey = "scope.one") => <PagedWorldLore worldName="Caldris"
  scopeKey={scopeKey} loadPage={loadPage} onOpenLocation={() => {}} onOpenFaction={() => {}} onOpenHistory={() => {}} />;
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));
async function settle() { await act(async () => { await tick(); await tick(); }); }
function storeForScope() {
  const store = createHubStore();
  store.dispatch(collectionActions.scopeReplaced({ scope: "scope.one", generation: 1 }));
  return store;
}
async function mount(store: HubStore, element: React.ReactNode, hash = "") {
  const dom = new JSDOM('<!doctype html><html><body><div id="root"></div></body></html>', { url: `http://localhost/${hash}` });
  const keys = ["document", "window", "navigator", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : (dom.window as unknown as Record<string, unknown>)[key] });
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  dom.window.scrollTo = () => undefined;
  const render = (next: React.ReactNode, beforeRender?: () => void) => act(async () => {
    beforeRender?.(); root.render(<Provider store={store}>{next}</Provider>); await tick();
  });
  await render(element);
  return { render, async cleanup() {
    await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!);
      else Reflect.deleteProperty(globalThis, key); });
  } };
}
function button(label: string) {
  const value = [...document.querySelectorAll<HTMLButtonElement>("button")].find((entry) => entry.textContent === label);
  assert.ok(value, `Missing button ${label}`); return value;
}
async function click(label: string) { await act(async () => { button(label).click(); await tick(); }); }
async function setValue(input: HTMLInputElement | HTMLSelectElement, value: string) {
  await act(async () => {
    const prototype = input.tagName === "SELECT" ? window.HTMLSelectElement.prototype : window.HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new window.Event(input.tagName === "SELECT" ? "change" : "input", { bubbles: true }));
    await tick();
  });
}

test("Lore reads one page, forwards all three revision pins on Next, and reuses Previous from Redux", async () => {
  const store = storeForScope();
  const calls: LorePageRequest[] = [];
  const loadPage: LorePageLoader = async (request) => {
    calls.push(request); return request.cursor ? page("Second account") : page("First account", "cursor.two");
  };
  const view = await mount(store, component(loadPage));
  try {
    await settle();
    assert.deepEqual(calls, [firstRequest]);
    assert.match(document.body.textContent!, /First account/);
    assert.doesNotMatch(document.body.textContent!, /Second account/);
    await click("Next page");
    assert.deepEqual(calls[1], { ...firstRequest, cursor: "cursor.two", expectedSourceRevision: sourceRevision,
      expectedGraphRevision: graphRevision, expectedSelectionFingerprint: selectionFingerprint });
    assert.match(document.body.textContent!, /Second account/);
    assert.equal(button("Next page").disabled, true);
    assert.equal(Object.keys(store.getState().collections.entries).length, 2);
    await click("Previous page");
    assert.equal(calls.length, 2);
    assert.match(document.body.textContent!, /First account/);
    await click("Next page");
    assert.equal(calls.length, 2);
    assert.match(document.body.textContent!, /Second account/);
  } finally { await view.cleanup(); }
});

test("Lore submits search to its page owner and resets pagination for category and kind filters", async () => {
  const store = storeForScope();
  const calls: LorePageRequest[] = [];
  const loadPage: LorePageLoader = async (request) => { calls.push(request); return page("Recorded account", "cursor.two"); };
  const view = await mount(store, component(loadPage));
  try {
    await click("Next page");
    await setValue(document.querySelector<HTMLInputElement>('input[type="search"]')!, "  River history  ");
    assert.equal(calls.length, 2, "Typing does not read another page.");
    await act(async () => { document.querySelector("form")!.dispatchEvent(new window.Event("submit", { bubbles: true, cancelable: true })); await tick(); });
    assert.deepEqual(calls.at(-1), { ...firstRequest, query: "River history" });
    assert.equal(button("Previous page").disabled, true);
    await setValue(document.querySelectorAll<HTMLSelectElement>("select")[0]!, "event");
    assert.deepEqual(calls.at(-1), { ...firstRequest, query: "River history", category: "event" });
    await setValue(document.querySelectorAll<HTMLSelectElement>("select")[1]!, "fact");
    assert.deepEqual(calls.at(-1), { ...firstRequest, query: "River history", category: "event", kind: "fact" });
    await click("Clear filters");
    assert.equal(document.querySelector<HTMLInputElement>('input[type="search"]')!.value, "");
    assert.equal(calls.length, 5, "Clearing filters reuses the current scope's first-page cache.");
  } finally { await view.cleanup(); }
});

test("Lore generation replacement aborts a pending page and fences its late private response", async () => {
  const store = storeForScope();
  const calls: Array<{ request: LorePageRequest; signal: AbortSignal; preferCached?: boolean }> = [];
  let finishOld!: (value: WorldLorePage) => void;
  const loadPage: LorePageLoader = async (request, signal, preferCached) => {
    calls.push({ request, signal, preferCached });
    if (request.cursor) return new Promise((resolve) => { finishOld = resolve; });
    return page(calls.length === 1 ? "Old private account" : "Fresh account", "cursor.two");
  };
  const view = await mount(store, component(loadPage));
  try {
    await click("Next page");
    assert.equal(calls.length, 2);
    await act(async () => { store.dispatch(collectionActions.scopeReplaced({ scope: "scope.one", generation: 2 })); await tick(); });
    await settle();
    assert.equal(calls[1]!.signal.aborted, true);
    assert.equal(calls.length, 3);
    assert.deepEqual(calls[2]!.request, firstRequest);
    assert.equal(calls[2]!.preferCached, false);
    await act(async () => { finishOld(page("Late private account")); await tick(); });
    assert.match(document.body.textContent!, /Fresh account/);
    assert.doesNotMatch(document.body.textContent!, /Old private account|Late private account/);
    assert.equal(Object.keys(store.getState().collections.entries).length, 1);
    assert.doesNotMatch(JSON.stringify(store.getState().collections.entries), /Old private account|Late private account/);
  } finally { await view.cleanup(); }
});

test("Lore scope replacement clears cached pages and loads the new world's first page", async () => {
  const store = storeForScope();
  const calls: LorePageRequest[] = [];
  const loadPage: LorePageLoader = async (request) => { calls.push(request); return page(calls.length === 1 ? "Old private account" : "New world account"); };
  const view = await mount(store, component(loadPage));
  try {
    assert.match(document.body.textContent!, /Old private account/);
    await view.render(component(loadPage, "scope.two"), () => {
      store.dispatch(collectionActions.scopeReplaced({ scope: "scope.two", generation: 2 }));
    });
    await settle();
    assert.match(document.body.textContent!, /New world account/);
    assert.doesNotMatch(document.body.textContent!, /Old private account/);
    assert.deepEqual(calls.at(-1), firstRequest);
    assert.equal(calls.length, 2);
    const entries = store.getState().collections.entries;
    assert.equal(Object.keys(entries).length, 1);
    assert.ok(Object.keys(entries)[0]!.includes("scope.two"));
    assert.doesNotMatch(JSON.stringify(entries), /Old private account/);
  } finally { await view.cleanup(); }
});

test("a partial Lore page with no readable rows does not establish an empty search result", async () => {
  const loadPage: LorePageLoader = async () => ({ ...page("Unused", "cursor.more"), entries: [], coverage: "partial" });
  const view = await mount(storeForScope(), component(loadPage));
  try {
    assert.match(document.body.textContent!, /no visible entries/);
    assert.doesNotMatch(document.body.textContent!, /No lore matches/);
    assert.equal(button("Next page").disabled, false);
  } finally { await view.cleanup(); }
});

test("an empty Player Lore page with a continuation keeps Next available and loads its visible entries", async () => {
  const calls: LorePageRequest[] = [];
  let finishPage!: (value: WorldLorePage) => void;
  const loadPage: LorePageLoader = async (request) => {
    calls.push(request); return new Promise((resolve) => { finishPage = resolve; });
  };
  const view = await mount(storeForScope(), component(loadPage));
  try {
    assert.match(document.querySelector(".atlas-heading")!.textContent!, /Loading lore page/);
    await act(async () => { finishPage({ ...page("Unused", "cursor.40"), entries: [], totalCount: null, filterable: false }); await tick(); });
    assert.match(document.body.textContent!, /This page has no visible entries\. The next page may contain more lore\./);
    assert.doesNotMatch(document.body.textContent!, /No lore matches/);
    assert.equal(document.querySelector('form[aria-label="Search world lore"]'), null);
    assert.equal(button("Next page").disabled, false);
    assert.equal(calls.length, 1, "An empty authorized page does not automatically drain the continuation.");
    await click("Next page");
    assert.equal(calls[1]!.cursor, "cursor.40");
    assert.match(document.querySelector(".atlas-heading")!.textContent!, /Loading lore page/);
    assert.doesNotMatch(document.body.textContent!, /Loading first page|No lore matches/);
    await act(async () => { finishPage({ ...page("Visible bridge history"), totalCount: null, filterable: false }); await tick(); });
    assert.match(document.body.textContent!, /Visible bridge history/);
    assert.equal(button("Next page").disabled, true);
    assert.equal(calls.length, 2);
  } finally { await view.cleanup(); }
});

test("stale Lore continuation cannot revive cached pages before an explicit fresh first-page read", async () => {
  const store = storeForScope();
  const calls: Array<{ request: LorePageRequest; preferCached?: boolean }> = [];
  const loadPage: LorePageLoader = async (request, _signal, preferCached) => {
    calls.push({ request, preferCached });
    if (request.cursor) throw new ViewReadError("stale-data", "The graph revision changed");
    return page(calls.length === 1 ? "Outdated account" : "Revised account", "cursor.two");
  };
  const view = await mount(store, component(loadPage));
  try {
    await click("Next page");
    assert.match(document.querySelector('[role="alert"]')!.textContent!, /Lore changed while browsing/);
    await click("Previous page");
    assert.doesNotMatch(document.body.textContent!, /Outdated account/);
    assert.match(document.querySelector('[role="alert"]')!.textContent!, /Lore changed while browsing/);
    assert.equal(calls.length, 2);
    await click("Refresh lore");
    assert.deepEqual(calls[2], { request: firstRequest, preferCached: false });
    assert.match(document.body.textContent!, /Revised account/);
    assert.doesNotMatch(document.body.textContent!, /Outdated account|Lore changed while browsing/);
    assert.equal(button("Previous page").disabled, true);
  } finally { await view.cleanup(); }
});

test("the integrated Lore route reads only its requested page and never starts the legacy directory drain", async () => {
  const initial = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, resolveAudience({
    authenticatedUserId: "principal.dm.fixture", authenticatedUserEmail: "", requestedPerspective: "dm",
    dmPrincipalIds: ["principal.dm.fixture"],
  })) as ReadyHubEnvelope;
  let legacyReads = 0;
  const pageCalls: LorePageRequest[] = [];
  const store = createHubStore();
  const view = await mount(store, <DndInformationHub store={store} initialEnvelope={initial}
    loadLorePage={async (_envelope, request) => { pageCalls.push(request); return page("One selected page", "cursor.more"); }}
    loadDeferredSection={async () => { legacyReads++; throw new Error("The legacy directory must not load for paged Lore."); }} />,
  hubRouteHash("world", "overview", { worldSection: "lore" }));
  try {
    await settle();
    assert.match(document.body.textContent!, /One selected page/);
    assert.deepEqual(pageCalls, [firstRequest]);
    assert.equal(legacyReads, 0);
    await click("Next page");
    assert.equal(pageCalls.length, 2);
    assert.equal(legacyReads, 0);
  } finally { await view.cleanup(); }
});

test("Lore authorization failure clears every cached private page and stops automatic reads", async () => {
  const store = storeForScope();
  let calls = 0;
  const loadPage: LorePageLoader = async () => {
    calls++;
    if (calls > 1) throw new ViewReadError("authorization", "Permission removed");
    return page("Private royal secret", "cursor.two");
  };
  const view = await mount(store, component(loadPage));
  try {
    assert.match(document.body.textContent!, /Private royal secret/);
    await click("Next page");
    await settle();
    assert.equal(store.getState().collections.denied, true);
    assert.deepEqual(store.getState().collections.entries, {});
    assert.doesNotMatch(document.body.textContent!, /Private royal secret/);
    assert.match(document.querySelector('[role="alert"]')!.textContent!, /viewing permission/);
    await click("Retry lore");
    assert.equal(calls, 2);
    assert.deepEqual(store.getState().collections.entries, {});
  } finally { await view.cleanup(); }
});
