import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { CHARACTER_SECTIONS } from "../../src/components/character/CharacterShell";
import { hubRouteHash, parseHubRoute } from "../../src/data/hub-route";
import { itemRouteHash, navigateItemRoute } from "../../src/data/item-view-route";
import { integrationEnvelope, integrationParty } from "../fixtures/item-integration";
import { itemData } from "../fixtures/item-details";
import { recipeData } from "../fixtures/item-recipes";
import type { ItemRegistryRecord } from "../../src/server/item-registry";
import { createHubStore, hubActions, referenceActions } from "../../src/data/hub-store";
import { collectionActions } from "../../src/data/collection-state";
import { ViewReadError } from "../../src/data/view-read-client";

const fingerprint = "A".repeat(64);
const record: ItemRegistryRecord = { id: "definition.staff", collection: "dnd2024", name: "Travel staff",
  status: "active", version: 1, contentFingerprint: fingerprint, sourceId: "core", sourceLabel: "Core", classification: "core" };
const recipe = { ...record, id: "recipe.0", name: "Restoring the travel staff" };
const registryRoute = { kind: "registry-item" as const, campaignId: "campaign.fixture", perspective: "dm" as const,
  itemId: record.id, collection: record.collection, contentFingerprint: fingerprint, tab: "details" as const };
const listHash = hubRouteHash("party", "overview", { partySection: "registry", characterId: "actor.second" });
const tick = () => new Promise((resolve) => setTimeout(resolve, 25));
async function perform(action: () => void) { await act(async () => { action(); await tick(); }); await act(tick); }

async function mount(options: { hash?: string; emptyParty?: boolean; sparseHeader?: boolean; unavailable?: boolean } = {}) {
  const dom = new JSDOM("<!doctype html><html lang='en'><head><title>Registry</title></head><body><div id='root'></div></body></html>", {
    url: "https://table.test/ui/dnd2024-play" + (options.hash ?? listHash), pretendToBeVisual: true,
  });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const prior = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  dom.window.requestAnimationFrame = (callback) => dom.window.setTimeout(() => callback(0), 0);
  dom.window.scrollTo = () => {};
  const envelope = integrationEnvelope("dm");
  if (options.emptyParty) envelope.party = [];
  if (options.sparseHeader) for (const member of envelope.party) {
    delete member.characterSheet;
    delete member.portrait;
    member.sheet = [];
    member.sheetState = { status: "loading", data: null };
  }
  const calls = { page: 0, definition: 0, recipe: 0, header: [] as string[] };
  const control = { itemName: record.name, denied: false };
  const store = createHubStore();
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  await act(async () => {
    root.render(<DndInformationHub store={store} initialEnvelope={envelope} loadContent={async () => ({}) as never}
      loadCharacterSheet={options.sparseHeader ? async (_envelope, id) => {
        calls.header.push(id);
        assert.ok(calls.header.length < 10, "Header admission must not trigger itself.");
        return integrationParty().find((member) => member.id === id)!;
      } : undefined}
      loadItemRegistryPage={async () => {
        calls.page++;
        if (control.denied) throw new ViewReadError("authorization", "Access denied");
        if (options.unavailable) throw new Error("Offline");
        return { records: [{ ...record, name: control.itemName }], resolutionFingerprint: fingerprint, totalCount: 1, nextCursor: null };
      }}
      loadItemDefinition={async () => {
        calls.definition++;
        return { record, details: { ...itemData(), quantity: null, container: null } };
      }}
      loadRecipeRegistryPage={async () => ({ records: [recipe], resolutionFingerprint: fingerprint, totalCount: 1, nextCursor: null })}
      loadRecipeDefinition={async () => {
        calls.recipe++;
        return { record: recipe, entry: { ...recipeData().makes.entries[0], name: recipe.name }, linkedItems: [record] };
      }} />);
    await tick();
  });
  await act(tick); await act(tick);
  return { calls, control, store, async cleanup() {
    await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (prior[index]) Object.defineProperty(globalThis, key, prior[index]!);
      else Reflect.deleteProperty(globalThis, key); });
  } };
}
function click(selector: string) {
  const button = document.querySelector<HTMLButtonElement>(selector);
  assert.ok(button, `Missing ${selector}`);
  return perform(() => button.click());
}
function assertShell(character = "actor.second", section = "registry") {
  assert.equal(document.querySelector(".character-page")?.getAttribute("data-record-id"), character);
  assert.equal(document.querySelectorAll("#main-view-heading").length, 1);
  assert.deepEqual([...document.querySelectorAll("[data-character-section]")].map((button) => button.getAttribute("data-character-section")),
    CHARACTER_SECTIONS.map((section) => section.id));
  assert.equal(document.querySelector(`[data-character-section="${section}"]`)?.getAttribute("aria-current"), "page");
}

test("Registry keeps the selected character and all Party tabs through lists, definitions and Back", async () => {
  const view = await mount();
  try {
    assertShell();
    assert.equal(document.querySelector("#registry-view-heading")?.tagName, "H2");
    await click('[data-registry-entry="definition.staff"]');
    assertShell();
    assert.ok(document.querySelector("#item-view-heading"));
    await click('#item-tab-recipes');
    assertShell();
    await click('[data-recipe-entry="recipe.0"]');
    assertShell();
    assert.match(document.querySelector("#recipe-view-heading")?.textContent ?? "", /Restoring/);
    await click('.item-page__breadcrumbs li:nth-child(2) button');
    assert.equal(window.location.hash, listHash, "Registry breadcrumb must retain the selected character.");
    assertShell();
    for (const { id } of CHARACTER_SECTIONS.filter(({ id }) => id !== "registry")) {
      await click(`[data-character-section="${id}"]`);
      assert.equal(parseHubRoute(window.location.hash).kind, "hub");
      assertShell("actor.second", id);
      await click('[data-character-section="registry"]');
      assertShell();
    }
    await click('[data-registry-entry="definition.staff"]');
    await perform(() => window.history.back());
    assertShell();
    assert.ok(document.querySelector(".party-registry"));
    await perform(() => window.history.forward());
    assertShell();
    assert.ok(document.querySelector("#item-view-heading"));
    await click('.item-page__breadcrumbs li:nth-child(2) button');
    assert.equal(window.location.hash, listHash);
    await click('[data-character-member="actor.fixture"]');
    assertShell("actor.fixture");
  } finally { await view.cleanup(); }
});

test("Registry retains completed facets only in scoped Redux and refreshes its open list after invalidation", async () => {
  const view = await mount();
  try {
    assert.equal(view.calls.page, 1);
    const state = view.store.getState().collections;
    assert.equal(Object.keys(state.entries).length, 1);
    const entry = Object.values(state.entries)[0];
    assert.ok(entry.bytes > 0);
    await click('[data-character-section="overview"]');
    await click('[data-character-section="registry"]');
    assert.equal(view.calls.page, 1, "A fresh Redux result is reused without a completed transport cache.");
    assert.equal(Object.values(view.store.getState().collections.entries)[0].confirmedAt, entry.confirmedAt,
      "A cache hit does not renew freshness.");
    view.control.itemName = "Updated staff";
    await perform(() => view.store.dispatch(referenceActions.invalidated()));
    assert.equal(view.calls.page, 2);
    assert.match(document.querySelector('[data-registry-entry]')?.textContent ?? "", /Updated staff/);
    const old = view.store.getState().collections;
    await perform(() => view.store.dispatch(hubActions.scopeCleared()));
    assert.equal(view.store.getState().collections.retainedBytes, 0);
    assert.notEqual(view.store.getState().collections.generation, old.generation);
    assert.equal(document.querySelector('[data-registry-entry]'), null);
  } finally { await view.cleanup(); }
});

test("a denied Registry refresh clears all confirmed collection bodies without an automatic retry loop", async () => {
  const view = await mount();
  try {
    view.control.denied = true;
    await perform(() => view.store.dispatch(referenceActions.invalidated()));
    assert.equal(view.calls.page, 2);
    assert.equal(view.store.getState().collections.denied, true);
    assert.equal(view.store.getState().collections.retainedBytes, 0);
    assert.equal(document.querySelector('[data-registry-entry]'), null);
    assert.match(document.body.textContent ?? "", /not available for this audience/);
    await act(tick);
    assert.equal(view.calls.page, 2);
  } finally { await view.cleanup(); }
});

test("Registry cache eviction offers a bounded explicit reload instead of an empty list or retry loop", async () => {
  const view = await mount();
  try {
    const state = view.store.getState().collections;
    await perform(() => view.store.dispatch(collectionActions.released({ scope: state.scope,
      generation: state.generation, key: Object.keys(state.entries)[0] })));
    assert.equal(document.querySelector('[data-registry-entry]'), null);
    assert.match(document.body.textContent ?? "", /saved list was released/);
    assert.equal(view.calls.page, 1);
    await click('.party-registry__notice--error button');
    assert.equal(view.calls.page, 2);
    assert.ok(document.querySelector('[data-registry-entry]'));
    assert.doesNotMatch(document.body.textContent ?? "", /saved list was released/);
  } finally { await view.cleanup(); }
});

test("Registry failures retain usable Party tabs and an empty party can still browse definitions", async () => {
  const failed = await mount({ unavailable: true });
  try {
    assertShell();
    assert.match(document.querySelector('[role="alert"]')?.textContent ?? "", /could not be loaded/);
    await click('[data-character-section="overview"]');
    assertShell("actor.second", "overview");
  } finally { await failed.cleanup(); }
  const empty = await mount({ emptyParty: true });
  try {
    assert.equal(document.querySelector(".character-page"), null);
    assert.equal(document.querySelectorAll("#main-view-heading").length, 1);
    assert.ok(document.querySelector('[data-registry-entry="definition.staff"]'));
  } finally { await empty.cleanup(); }
});

test("cold Registry and definition links hydrate their header once without repeating catalog reads", async () => {
  for (const hash of [listHash, itemRouteHash(registryRoute)]) {
    const view = await mount({ hash, sparseHeader: true });
    try {
      const character = hash === listHash ? "actor.second" : "actor.fixture";
      assertShell(character);
      assert.deepEqual(view.calls.header, [character]);
      assert.equal(view.calls.page, hash === listHash ? 1 : 0);
      assert.equal(view.calls.definition, hash === listHash ? 0 : 1);
      await act(tick);
      assert.deepEqual(view.calls.header, [character]);
    } finally { await view.cleanup(); }
  }
});

test("Rules-owned definitions stay in Rules without acquiring a Party shell or character reads", async () => {
  const view = await mount({ hash: "#view?tab=rules", sparseHeader: true });
  try {
    await perform(() => navigateItemRoute(registryRoute, false, null, "rules"));
    assert.equal(document.querySelector(".character-page"), null);
    assert.match(document.querySelector(".item-page__breadcrumbs")?.textContent ?? "", /Rules/);
    assert.equal(view.calls.definition, 1);
    assert.deepEqual(view.calls.header, []);
  } finally { await view.cleanup(); }
});
