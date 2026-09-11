import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { Provider } from "react-redux";
import { collectionActions, collectionReducer, MAX_COLLECTION_BYTES, MAX_COLLECTION_ENTRIES,
  MAX_COLLECTION_ENTRY_BYTES } from "../../src/data/collection-state";
import { createHubStore, type HubStore } from "../../src/data/hub-store";
import { useCollectionValue } from "../../src/data/use-collection-value";
import { InventoryTree } from "../../src/components/character/InventoryTree";
import type { InventoryContainerItem, InventoryContainerPageResult } from "../../src/data/hub-types";

const bytes = (value: unknown) => new TextEncoder().encode(JSON.stringify(value)).byteLength;
const commit = (key: string, value: unknown, overrides = {}) => collectionActions.committed({
  scope: "scope.one", generation: 1, key, value, bytes: bytes(value), confirmedAt: 100, ...overrides,
});

test("collection ownership measures UTF-8 bytes and evicts by entry count and retained bytes", () => {
  let state = collectionReducer(undefined, collectionActions.scopeReplaced({ scope: "scope.one", generation: 1 }));
  state = collectionReducer(state, commit("unicode", "測試🐉"));
  assert.equal(state.retainedBytes, bytes("測試🐉"));
  const before = state;
  for (const action of [commit("lie", "測試🐉", { bytes: 1 }), commit("__proto__", {}),
    commit("huge", "x".repeat(MAX_COLLECTION_ENTRY_BYTES)), commit("stale", {}, { generation: 0 }),
    commit("other", {}, { scope: "scope.two" })]) {
    state = collectionReducer(state, action); assert.equal(state, before);
  }
  for (let index = 0; index < MAX_COLLECTION_ENTRIES; index++) state = collectionReducer(state, commit(`row${index}`, index));
  assert.equal(Object.keys(state.entries).length, MAX_COLLECTION_ENTRIES);
  assert.equal(state.entries.unicode, undefined);
  const big = "x".repeat(9 * 1024 * 1024);
  state = collectionReducer(state, commit("large.one", big));
  state = collectionReducer(state, commit("large.two", big));
  assert.deepEqual(Object.keys(state.entries), ["large.two"]);
  assert.ok(state.retainedBytes <= MAX_COLLECTION_BYTES);
  state = collectionReducer(state, collectionActions.released({ scope: "scope.one", generation: 0, key: "large.two" }));
  assert.ok(state.entries["large.two"]);
  state = collectionReducer(state, collectionActions.denied({ scope: "scope.one", generation: 1 }));
  assert.equal(state.retainedBytes, 0); assert.equal(state.denied, true);
  assert.equal(collectionReducer(state, commit("late", "secret")), state);
  state = collectionReducer(state, collectionActions.scopeReplaced({ scope: "scope.two", generation: 3 }));
  assert.equal(state.denied, false); assert.equal(state.retainedBytes, 0);
});

async function mount(store: HubStore, element: React.ReactNode) {
  const dom = new JSDOM('<!doctype html><html><body><div id="root"></div></body></html>', { url: "http://localhost/" });
  const keys = ["document", "window", "navigator", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : (dom.window as unknown as Record<string, unknown>)[key] });
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  const render = (next: React.ReactNode) => act(async () => { root.render(<Provider store={store}>{next}</Provider>); });
  await render(element);
  return { render, async cleanup() {
    await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!);
      else Reflect.deleteProperty(globalThis, key); });
  } };
}

test("collection selectors ignore unrelated updates, report eviction, and fence late former-scope setters", async () => {
  const store = createHubStore();
  store.dispatch(collectionActions.scopeReplaced({ scope: "scope.one", generation: 1 }));
  let captured!: ReturnType<typeof useCollectionValue<string[]>>;
  let renders = 0;
  function Probe() { captured = useCollectionValue<string[]>("selected", []); renders++;
    return <p>{captured[6] ? "released" : captured[0].join(",")}</p>; }
  const view = await mount(store, <Probe />);
  try {
    await act(async () => captured[1](["first"]));
    const oldSetter = captured[1];
    const rendered = renders, entry = store.getState().collections.entries.selected;
    await act(async () => store.dispatch(commit("unrelated", ["other"])));
    assert.equal(renders, rendered); assert.equal(store.getState().collections.entries.selected, entry);
    await act(async () => store.dispatch(collectionActions.released({ scope: "scope.one", generation: 1, key: "selected" })));
    assert.equal(document.querySelector("p")?.textContent, "released");
    await act(async () => captured[1](["reloaded"]));
    assert.equal(captured[6], false);
    await act(async () => store.dispatch(collectionActions.scopeReplaced({ scope: "scope.two", generation: 2 })));
    assert.deepEqual(captured[0], []); assert.equal(captured[6], false);
    await act(async () => oldSetter(["late private body"]));
    assert.deepEqual(store.getState().collections.entries, {});
  } finally { await view.cleanup(); }
});

const bag = { id: "item.bag", name: "Travel bag", quantity: 1, equipmentSlots: [], slot: "carried",
  order: 0, parentItemId: null, depth: 1, childCount: null, isContainer: true,
  deeperContentsOmitted: true } as InventoryContainerItem;
const page = { status: "ready", failureCategory: null, diagnosticId: "fixture", data: {
  container: { id: bag.id, label: bag.name }, state: "ready", reasons: [], notices: [],
  items: [{ id: "item.compass", name: "Copper compass", quantity: 1, equipmentSlots: [], slot: "contents", order: 0,
    isContainer: false, definition: null }], limits: { directComplete: true },
} } as InventoryContainerPageResult;

test("expanded inventory has one Redux body, reports eviction, and retires child pages after source replacement", async () => {
  const store = createHubStore();
  store.dispatch(collectionActions.scopeReplaced({ scope: "scope.one", generation: 1 }));
  let calls = 0;
  const loadContainer = async () => { calls++; return page; };
  const items = [bag];
  const tree = (sourceRevision: string, nextItems = items) => <InventoryTree items={nextItems} subjectId="actor.fixture"
    sourceRevision={sourceRevision} loadContainer={loadContainer} />;
  const view = await mount(store, tree("revision.one"));
  const toggle = () => act(async () => document.querySelector<HTMLButtonElement>(".character-inventory__disclosure")!.click());
  try {
    await toggle();
    assert.equal(calls, 1); assert.match(document.body.textContent!, /Copper compass/);
    const key = Object.keys(store.getState().collections.entries)[0];
    const stored = store.getState().collections.entries[key].value as Record<string, unknown>;
    assert.deepEqual(Object.keys(stored), [bag.id]);
    await act(async () => store.dispatch(collectionActions.released({ scope: "scope.one", generation: 1, key })));
    assert.doesNotMatch(document.body.textContent!, /Copper compass|This container is empty/);
    assert.match(document.body.textContent!, /Contents could not be loaded/);
    assert.equal(calls, 1, "Eviction does not cause an automatic refetch loop.");
    await toggle(); await toggle();
    assert.equal(calls, 2); assert.match(document.body.textContent!, /Copper compass/);
    await view.render(tree("revision.two", [{ ...bag, isContainer: false, deeperContentsOmitted: false, childCount: 0 }]));
    assert.doesNotMatch(document.body.textContent!, /Copper compass/);
    assert.equal(calls, 2);
  } finally { await view.cleanup(); }
});
