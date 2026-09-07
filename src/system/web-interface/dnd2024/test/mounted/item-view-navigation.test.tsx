import assert from "node:assert/strict";
import test from "node:test";
import React, { act, useEffect, useState } from "react";
import { JSDOM } from "jsdom";
import { ItemWorkspace } from "../../src/components/items/ItemWorkspace";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { ITEM_ROUTE_EVENT, itemRouteHash, navigateItemRoute, parseItemRoute, readInventoryReturn, type InventoryRoute } from "../../src/data/item-view-route";
import { hubSource } from "../support/hub-source.js";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { resolveAudience } from "../support/audience-policy.js";
import type { PartyMemberReadModel, ReadyHubEnvelope } from "../../src/data/hub-types";

const inventory: InventoryRoute = { kind: "inventory", characterId: "actor.second", campaignId: "campaign.test", perspective: "player" };
function member(id: string): PartyMemberReadModel {
  const value = structuredClone(hubSource.party[0]) as PartyMemberReadModel;
  value.id = id; value.name = id; value.inventoryStatus = "canonical";
  value.inventoryState = { status: "ready", source: "canonical", data: [] };
  value.characterSheet = {
    version: 2, subject: { id, label: id }, classes: [],
    inventory: { contentsDepth: 4, mayOmitDeeperContents: true, items: [
      { id: "item.bag", name: "Bag", definition: { id: "definition.bag", label: "Bag" }, quantity: 1, slot: "carried", parentItemId: null, order: 0, depth: 1, childCount: 1, deeperContentsOmitted: false, equipmentSlots: [] },
      { id: "item.secret", name: "PRIVATE INVENTORY NAME", definition: { id: "definition.item", label: "PRIVATE DEFINITION" }, quantity: 2, slot: "contents", parentItemId: "item.bag", order: 0, depth: 2, childCount: 0, deeperContentsOmitted: false, equipmentSlots: [] },
    ] },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
  } as PartyMemberReadModel["characterSheet"];
  return value;
}
const party = [member("actor.first"), member("actor.second")];
function Workspace() {
  const [route, setRoute] = useState(() => parseItemRoute(window.location.hash));
  useEffect(() => {
    const changed = () => setRoute(parseItemRoute(window.location.hash));
    for (const event of [ITEM_ROUTE_EVENT, "popstate", "hashchange"]) window.addEventListener(event, changed);
    return () => { for (const event of [ITEM_ROUTE_EVENT, "popstate", "hashchange"]) window.removeEventListener(event, changed); };
  }, []);
  return <ItemWorkspace route={route} campaignId="campaign.test" perspective="player" party={party} />;
}
async function mount(hash: string, element = <Workspace />) {
  const dom = new JSDOM("<html><body><div id='root'></div></body></html>", { url: `https://table.test/published/release?keep=yes${hash}` });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true, value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  dom.window.requestAnimationFrame = (callback) => dom.window.setTimeout(() => callback(0), 0);
  dom.window.scrollTo = (_x, y) => Object.defineProperty(dom.window, "scrollY", { configurable: true, value: y });
  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.getElementById("root")!;
  const root = createRoot(container);
  await act(async () => { root.render(element); await new Promise((resolve) => setTimeout(resolve, 20)); });
  return { container, dom, async cleanup() {
    await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!); else Reflect.deleteProperty(globalThis, key); });
  } };
}
async function perform(action: () => void) { await act(async () => { action(); await new Promise((resolve) => setTimeout(resolve, 30)); }); }
function button(container: Element, text: string) {
  const found = [...container.querySelectorAll<HTMLButtonElement>("button")].find((value) => value.textContent?.trim() === text);
  assert.ok(found, `Missing ${text}`); return found;
}

test("item fragment bounds, tab fallback and return context never accept bindings or data", () => {
  const route = { ...inventory, kind: "item" as const, itemId: "item:a.b-1", tab: "uses" as const };
  assert.deepEqual(parseItemRoute(itemRouteHash(route)), route);
  assert.equal((parseItemRoute(itemRouteHash(route).replace("tab=uses", "tab=unknown")) as typeof route).tab, "details");
  for (const suffix of ["&item=duplicate", "&principal=gm", "&character=actor.other", "?extra", "&bad=%zz"]) {
    assert.equal(parseItemRoute(itemRouteHash(route) + suffix).kind, "invalid");
  }
  assert.equal(parseItemRoute("#item").kind, "invalid");
  assert.equal(parseItemRoute(itemRouteHash(route).replace("item%3Aa.b-1", "%3Cscript%3E")).kind, "invalid");
  assert.equal(parseItemRoute("#information-content").kind, "none");
  assert.equal(readInventoryReturn({ itemInventoryReturn: { characterId: "actor.other" } }, inventory.characterId), null);
});

test("opening image/name is independent of disclosure; Back and Forward restore character, focus, scroll and nested contents", async () => {
  const before = structuredClone(party);
  const mounted = await mount(itemRouteHash(inventory));
  try {
    const { container } = mounted;
    assert.equal(container.querySelector('[aria-current="true"] strong')?.textContent, "actor.second");
    const bag = container.querySelector("details")!;
    await perform(() => { bag.open = true; bag.dispatchEvent(new window.Event("toggle")); });
    assert.equal(bag.querySelector("summary button, summary a"), null);
    window.scrollTo(0, 487);
    const trigger = container.querySelector<HTMLButtonElement>('[data-item-open="item.secret"]')!;
    trigger.focus();
    await perform(() => trigger.querySelector<HTMLElement>(".character-inventory__item-media")!.click());
    assert.equal(parseItemRoute(window.location.hash).kind, "item");
    assert.equal(document.activeElement?.id, "main-view-heading");
    assert.doesNotMatch(container.textContent!, /PRIVATE INVENTORY NAME|PRIVATE DEFINITION|actor.second/);
    assert.equal(window.location.pathname, "/published/release"); assert.equal(window.location.search, "?keep=yes");
    await perform(() => button(container, "Known recipes").click());
    assert.match(container.textContent!, /Known recipes unavailable/);
    await perform(() => button(container, "Back to inventory").click());
    assert.equal(parseItemRoute(window.location.hash).kind, "inventory");
    assert.equal(container.querySelector("details")?.open, true);
    assert.equal((document.activeElement as HTMLElement).dataset.itemOpen, "item.secret");
    assert.equal(window.scrollY, 487);
    await perform(() => window.history.forward());
    assert.match(container.textContent!, /Known recipes unavailable/);
    const recipes = button(container, "Known recipes");
    await perform(() => recipes.dispatchEvent(new window.KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })));
    assert.equal(document.activeElement?.id, "item-tab-uses");
    assert.match(container.textContent!, /Known uses unavailable/);
    await perform(() => document.activeElement!.dispatchEvent(new window.KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    assert.equal(container.querySelector("details")?.open, true);
    assert.deepEqual(party, before);
  } finally { await mounted.cleanup(); }
});

test("reload and unauthorized or malformed deep links expose no item header or cached identity", async () => {
  for (const hash of [itemRouteHash({ ...inventory, kind: "item", itemId: "item.secret", tab: "details" }),
    itemRouteHash({ ...inventory, kind: "item", itemId: "item.missing", tab: "uses", perspective: "dm" }),
    itemRouteHash({ ...inventory, characterId: "actor.unknown" }), "#item?character=forged"]) {
    const mounted = await mount(hash);
    try {
      assert.equal(mounted.container.querySelectorAll('[role="tab"]').length, 3);
      assert.doesNotMatch(mounted.container.textContent!, /PRIVATE|actor.first|actor.second|No recipes known/);
      assert.equal(mounted.container.querySelector("img"), null);
      await perform(() => button(mounted.container, "Back to inventory").click());
      assert.equal(parseItemRoute(window.location.hash).kind === "item", false);
    } finally { await mounted.cleanup(); }
  }
});

test("item shell has no serious or critical accessibility violations", async () => {
  const mounted = await mount(itemRouteHash({ ...inventory, kind: "item", itemId: "item.secret", tab: "details" }));
  try {
    const axe = (await import("axe-core")).default;
    const result = await axe.run(mounted.container, { rules: { "color-contrast": { enabled: false } } });
    assert.deepEqual(result.violations.filter((value) => value.impact === "serious" || value.impact === "critical").map((value) => value.id), []);
  } finally { await mounted.cleanup(); }
});

test("hub deep links request the authorized perspective once across tabs and discard late responses after scope reversal", async () => {
  const projected = projectHubEnvelope(hubSource, "fixture", resolveAudience({ authenticatedUserId: "dm.fixture", authenticatedUserEmail: "", requestedPerspective: "dm", dmPrincipalIds: ["dm.fixture"] })) as ReadyHubEnvelope;
  const initial = { ...projected, applicationId: "dnd2024-main", stateSpaceId: "state.fixture", party,
    contextSelection: { selectedWorldId: projected.world.id, selectedCampaignId: "campaign.test", worlds: [{ id: projected.world.id, name: projected.world.name, campaigns: [{ id: "campaign.test", name: "Fixture" }] }] } };
  let resolvePlayer!: (value: ReadyHubEnvelope) => void;
  const requests: string[] = [];
  const mounted = await mount(itemRouteHash({ ...inventory, kind: "item", itemId: "item.secret", tab: "details" }),
    <DndInformationHub initialEnvelope={initial} loadContent={async () => ({}) as never}
      loadEnvelope={(perspective) => { requests.push(perspective); return new Promise((resolve) => { resolvePlayer = resolve; }); }} />);
  try {
    await perform(() => {}); // Resolve the lazy workspace module.
    assert.deepEqual(requests, ["player"]);
    assert.doesNotMatch(mounted.container.textContent!, /PRIVATE INVENTORY NAME/);
    await perform(() => button(mounted.container, "Known recipes").click());
    assert.deepEqual(requests, ["player"]);
    await perform(() => navigateItemRoute({ ...inventory, kind: "item", itemId: "item.secret", tab: "details", perspective: "dm" }));
    await perform(() => resolvePlayer({ ...initial, audience: { ...initial.audience, perspective: "player" } }));
    assert.equal(mounted.container.querySelector(".information-hub")?.getAttribute("data-perspective"), "dm");
    assert.doesNotMatch(mounted.container.textContent!, /PRIVATE INVENTORY NAME/);
    assert.equal(mounted.container.querySelectorAll('[role="tab"]').length, 3);
    await perform(() => button(mounted.container, "World").click());
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "World");
    await perform(() => window.history.back());
    assert.equal(mounted.container.querySelectorAll('[role="tab"]').length, 3);
    await perform(() => window.history.forward());
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "World");
  } finally { await mounted.cleanup(); }
});
