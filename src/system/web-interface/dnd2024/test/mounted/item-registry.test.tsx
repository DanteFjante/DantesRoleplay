import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";
import React, { act, useEffect, useState } from "react";
import { JSDOM } from "jsdom";

import { ItemRegistryWorkspace, type ItemDefinitionLoader,
  type ItemRegistryPageLoader } from "../../src/components/registry/ItemRegistryWorkspace";
import type { RecipeDefinitionLoader, RecipeRegistryPageLoader } from "../../src/components/registry/RecipeRegistryWorkspace";
import { ITEM_ROUTE_EVENT, parseItemRoute } from "../../src/data/item-view-route";
import type { ItemDetailsData } from "../../src/server/item-view-client";
import type { ItemRegistryRecord } from "../../src/server/item-registry";
import type { RecipeEntry } from "../../src/server/item-recipes-client";
import type { RecipeRegistryRecord } from "../../src/server/recipe-registry";

const fingerprint = "A".repeat(64);
function freshAxe() {
  const load = createRequire(import.meta.url);
  delete load.cache[load.resolve("axe-core")];
  return load("axe-core") as typeof import("axe-core").default;
}
function record(id: string, name: string): ItemRegistryRecord {
  return { id, collection: "dnd2024", name, status: "active", version: 1,
    contentFingerprint: fingerprint, sourceId: "core", sourceLabel: "D&D 2024 core", classification: "core" };
}
const first = record("dnd2024.item.lantern.v1", "Lantern (D&D 2024, definition v1)");
const second = record("dnd2024.item.rope.v1", "Rope (D&D 2024, definition v1)");

function details(selected: ItemRegistryRecord): ItemDetailsData {
  return { version: 1, observerId: "shared-table", itemId: selected.id, perspective: "dm", state: "ready",
    name: selected.name, description: "A recorded item definition.", definitionId: selected.id,
    quantity: null, container: null, equipmentSlots: [], properties: [{ label: "Kind", value: "Adventuring gear",
      unit: null, sources: [{ label: selected.sourceLabel, knowledgeState: "known" }], observerKnowledge: null }],
    sources: [{ label: selected.sourceLabel, knowledgeState: "known" }], media: [], reasons: [], observerKnowledge: null };
}

async function mountedWorkspace(loadPage: ItemRegistryPageLoader, loadDefinition: ItemDefinitionLoader,
  loadRecipePage?: RecipeRegistryPageLoader, loadRecipeDefinition?: RecipeDefinitionLoader) {
  const dom = new JSDOM("<div id='root'></div>", { url: "https://table.test/ui/dnd2024-play#view?tab=party&section=registry" });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  dom.window.requestAnimationFrame = (callback) => dom.window.setTimeout(() => callback(0), 0);
  dom.window.scrollTo = (options) => Object.defineProperty(dom.window, "scrollY", { configurable: true,
    value: typeof options === "object" ? options.top ?? 0 : 0 });
  function Workspace() {
    const [route, setRoute] = useState(() => parseItemRoute(window.location.hash));
    useEffect(() => {
      const changed = () => setRoute(parseItemRoute(window.location.hash));
      for (const event of [ITEM_ROUTE_EVENT, "popstate", "hashchange"]) window.addEventListener(event, changed);
      return () => { for (const event of [ITEM_ROUTE_EVENT, "popstate", "hashchange"]) window.removeEventListener(event, changed); };
    }, []);
    return <ItemRegistryWorkspace route={route} campaignId="campaign.test" perspective="dm"
      loadPage={loadPage} loadDefinition={loadDefinition} loadRecipePage={loadRecipePage}
      loadRecipeDefinition={loadRecipeDefinition} />;
  }
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  await act(async () => { root.render(<Workspace />); await new Promise((resolve) => setTimeout(resolve, 20)); });
  return { dom, async cleanup() { await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!);
      else Reflect.deleteProperty(globalThis, key); }); } };
}

async function perform(action: () => void) {
  await act(async () => { action(); await new Promise((resolve) => setTimeout(resolve, 35)); });
}

function button(text: string) {
  const result = [...document.querySelectorAll<HTMLButtonElement>("button")]
    .find((candidate) => candidate.textContent?.trim().includes(text));
  assert.ok(result, `Missing button ${text}`);
  return result;
}

test("Party registry pages definitions, opens definition-only details, and restores page, focus, and query", async () => {
  const pageCalls: Array<{ query: string; cursor: string | null }> = [];
  const loadPage: ItemRegistryPageLoader = async (request) => {
    pageCalls.push({ query: request.query, cursor: request.cursor });
    return request.cursor ? { resolutionFingerprint: fingerprint, records: [second], totalCount: 2, nextCursor: null }
      : { resolutionFingerprint: fingerprint, records: [first], totalCount: 2, nextCursor: "second-page" };
  };
  const mounted = await mountedWorkspace(loadPage, async (request) => ({ record: second, details: details(second) }));
  try {
    assert.match(document.body.textContent!, /All recorded item definitions/);
    assert.match(document.body.textContent!, /Possession does not imply discovery or knowledge/);
    await perform(() => button("Load more").click());
    assert.deepEqual(pageCalls.map((call) => call.cursor), [null, "second-page"]);
    const input = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    await perform(() => {
      Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")!.set!.call(input, "rope");
      input.dispatchEvent(new window.Event("input", { bubbles: true }));
      input.dispatchEvent(new window.Event("change", { bubbles: true }));
    });
    await perform(() => document.querySelector<HTMLFormElement>('form[role="search"]')!
      .dispatchEvent(new window.Event("submit", { bubbles: true, cancelable: true })));
    await perform(() => button("Load more").click());
    window.scrollTo({ top: 318 });
    await perform(() => button("Rope").click());
    assert.equal(parseItemRoute(window.location.hash).kind, "registry-item");
    assert.match(document.querySelector(".item-page__breadcrumbs")?.textContent ?? "", /PartyRegistryItems/);
    assert.equal(document.querySelectorAll('[role="tab"]').length, 2);
    assert.doesNotMatch(document.body.textContent!, /Quantity|Carried in/);
    assert.match(document.body.textContent!, /Adventuring gear/);
    await perform(() => button("Items").click());
    await perform(() => {});
    assert.equal(parseItemRoute(window.location.hash).kind, "none");
    assert.equal(document.querySelectorAll(".party-registry-card").length, 2);
    assert.equal((document.activeElement as HTMLElement).dataset.registryEntry, second.id);
    assert.equal(window.scrollY, 318);
    assert.equal(document.querySelector<HTMLInputElement>('input[type="search"]')?.value, "rope");
  } finally { await mounted.cleanup(); }
});

test("Party registry exposes an honest empty state and a separate recipe destination", async () => {
  const mounted = await mountedWorkspace(async () => ({ resolutionFingerprint: fingerprint,
    records: [], totalCount: 0, nextCursor: null }), async () => { throw new Error("unused"); });
  try {
    assert.match(document.body.textContent!, /No matching item definitions/);
    await perform(() => button("Recipes").click());
    assert.match(document.body.textContent!, /Recipe registry unavailable/);
    assert.doesNotMatch(document.body.textContent!, /No recipes known/);
  } finally { await mounted.cleanup(); }
});

test("Party registry has no serious or critical accessibility violations", async () => {
  const mounted = await mountedWorkspace(async () => ({ resolutionFingerprint: fingerprint,
    records: [first], totalCount: 1, nextCursor: null }), async () => ({ record: first, details: details(first) }));
  try {
    const axe = freshAxe();
    const result = await axe.run(document.body, { rules: { "color-contrast": { enabled: false } } });
    assert.deepEqual(result.violations.filter((value) => value.impact === "serious" || value.impact === "critical")
      .map((value) => value.id), []);
  } finally { await mounted.cleanup(); }
});

const recipeFirst: RecipeRegistryRecord = { ...first, id: "dnd2024.recipe.tonic", name: "Brew Tonic" };
const recipeSecond: RecipeRegistryRecord = { ...second, id: "dnd2024.recipe.salve", name: "Prepare Healing Salve" };
function recipeEntry(record: RecipeRegistryRecord): RecipeEntry {
  return { id: record.id, name: record.name, description: "A readable recipe definition.", knowledgeState: "known",
    sources: [{ label: record.sourceLabel, knowledgeState: "known" }], availability: "not-evaluated",
    outputs: [{ name: "Rope", definitionId: second.id, quantity: 1 }], materials: [],
    tools: ["Herbalism Kit"], duration: "4 Hour", observerKnowledge: null,
    requirements: [{ label: "Crafter requirement", value: "Unsupported requirement: Minimum Level",
      unit: null, sources: [{ label: record.sourceLabel, knowledgeState: "known" }], observerKnowledge: null }] };
}

test("Recipe Registry pages, cross-links to items, and restores recipe search, page, and focus", async () => {
  const recipeCalls: Array<{ query: string; cursor: string | null; relatedItemId: string | null }> = [];
  const loadRecipes: RecipeRegistryPageLoader = async (request) => {
    recipeCalls.push({ query: request.query, cursor: request.cursor, relatedItemId: request.relatedItemId });
    return request.cursor ? { resolutionFingerprint: fingerprint, records: [recipeSecond], totalCount: 2, nextCursor: null }
      : { resolutionFingerprint: fingerprint, records: [recipeFirst], totalCount: 2, nextCursor: "recipe-page-two" };
  };
  const mounted = await mountedWorkspace(async () => ({ resolutionFingerprint: fingerprint,
    records: [second], totalCount: 1, nextCursor: null }), async () => ({ record: second, details: details(second) }),
  loadRecipes, async () => ({ record: recipeSecond, entry: recipeEntry(recipeSecond), linkedItems: [second] }));
  try {
    await perform(() => button("Recipes").click());
    const input = document.querySelector<HTMLInputElement>('input[placeholder*="input"]')!;
    await perform(() => {
      Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")!.set!.call(input, "salve");
      input.dispatchEvent(new window.Event("input", { bubbles: true }));
      input.dispatchEvent(new window.Event("change", { bubbles: true }));
    });
    await perform(() => input.closest("form")!.dispatchEvent(new window.Event("submit", { bubbles: true, cancelable: true })));
    await perform(() => button("Load more").click());
    window.scrollTo({ top: 426 });
    await perform(() => button("Prepare Healing Salve").click());
    assert.equal(parseItemRoute(window.location.hash).kind, "registry-recipe");
    assert.match(document.querySelector(".item-page__breadcrumbs")?.textContent ?? "", /PartyRegistryRecipes/);
    assert.match(document.body.textContent!, /Unsupported requirement: Minimum Level/);
    assert.doesNotMatch(document.body.textContent!, /Craft now|Start crafting|Requirements met/);
    const axe = freshAxe();
    const accessibility = await axe.run(mounted.dom.window.document.body,
      { rules: { "color-contrast": { enabled: false } } });
    assert.deepEqual(accessibility.violations.filter((value) => value.impact === "serious" || value.impact === "critical")
      .map((value) => value.id), []);
    await perform(() => button("1 × Rope").click());
    const linkedRoute = parseItemRoute(window.location.hash);
    assert.equal(linkedRoute.kind, "registry-item");
    assert.equal(linkedRoute.kind === "registry-item" ? linkedRoute.itemId : null, second.id);
    await perform(() => window.history.back());
    await perform(() => button("Recipes").click());
    await perform(() => {});
    assert.equal(parseItemRoute(window.location.hash).kind, "none");
    assert.deepEqual(recipeCalls.filter((call) => call.relatedItemId === null).map((call) => call.cursor),
      [null, null, "recipe-page-two", null, "recipe-page-two"]);
    assert.equal(document.querySelectorAll(".party-registry-card--recipe").length, 2);
    assert.equal((document.activeElement as HTMLElement).dataset.recipeEntry, recipeSecond.id);
    assert.equal(window.scrollY, 426);
    assert.equal(document.querySelector<HTMLInputElement>('input[placeholder*="input"]')?.value, "salve");
  } finally { await mounted.cleanup(); }
});

test("Item Registry Recipes tab uses the bounded related-item filter and opens the recipe", async () => {
  const related: Array<string | null> = [];
  const mounted = await mountedWorkspace(async () => ({ resolutionFingerprint: fingerprint,
    records: [second], totalCount: 1, nextCursor: null }), async () => ({ record: second, details: details(second) }),
  async (request) => { related.push(request.relatedItemId); return { resolutionFingerprint: fingerprint,
    records: [recipeSecond], totalCount: 1, nextCursor: null }; },
  async () => ({ record: recipeSecond, entry: recipeEntry(recipeSecond), linkedItems: [second] }));
  try {
    await perform(() => button("Rope").click());
    await perform(() => button("Recipes").click());
    assert.deepEqual(related, [second.id]);
    assert.match(document.body.textContent!, /Recipes involving this item/);
    await perform(() => button("Prepare Healing Salve").click());
    assert.equal(parseItemRoute(window.location.hash).kind, "registry-recipe");
  } finally { await mounted.cleanup(); }
});
