import assert from "node:assert/strict";
import test from "node:test";
import React, { act, useEffect, useState } from "react";
import { JSDOM } from "jsdom";
import { ItemWorkspace } from "../../src/components/items/ItemWorkspace";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { InventoryTree, mergeInventoryContainerItems } from "../../src/components/character/InventoryTree";
import { ITEM_ROUTE_EVENT, itemRouteHash, navigateItemRoute, parseItemRoute, readInventoryReturn, readItemReturn, type InventoryRoute } from "../../src/data/item-view-route";
import { hubRouteHash, parseHubRoute } from "../../src/data/hub-route";
import { hubSource } from "../support/hub-source.js";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { resolveAudience } from "../support/audience-policy.js";
import type { InventoryContainerItem, InventoryContainerPageItem, PartyMemberReadModel, ReadyHubEnvelope,
  RuleReadModel } from "../../src/data/hub-types";

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
      ...Array.from({ length: 8 }, (_, index) => ({
        id: `item.filler-${index}`, name: `Trail supply ${index + 1}`,
        definition: { id: `definition.filler-${index}`, label: `Trail supply ${index + 1}` },
        quantity: 1, slot: "carried", parentItemId: null, order: index + 1, depth: 1,
        childCount: 0, deeperContentsOmitted: false, equipmentSlots: [],
      })),
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
  const inventoryReturn = { kind: "inventory" as const, characterId: inventory.characterId,
    expandedIds: ["item.bag"], query: "private", focusItemId: "item.secret", scrollY: 487 };
  assert.deepEqual(readInventoryReturn({ itemReturnContext: inventoryReturn }, inventory.characterId), inventoryReturn);
  const registryReturn = { kind: "registry" as const, section: "items" as const,
    campaignId: "campaign.test", perspective: "dm" as const,
    query: "knife", pageCount: 2, focusEntryId: "definition.knife", scrollY: 218 };
  assert.deepEqual(readItemReturn({ itemReturnContext: registryReturn }), registryReturn);
  assert.equal(readItemReturn({ itemReturnContext: { ...registryReturn, perspective: "observer" } }), null);
  const registryItem = { kind: "registry-item" as const, campaignId: "campaign.test", perspective: "dm" as const,
    itemId: "dnd2024.item.knife", collection: "dnd2024", contentFingerprint: "A".repeat(64),
    tab: "recipes" as const };
  assert.deepEqual(parseItemRoute(itemRouteHash(registryItem)), registryItem);
  const registryRecipe = { kind: "registry-recipe" as const, campaignId: "campaign.test", perspective: "dm" as const,
    recipeId: "dnd2024.recipe.knife", collection: "dnd2024", contentFingerprint: "B".repeat(64) };
  assert.deepEqual(parseItemRoute(itemRouteHash(registryRecipe)), registryRecipe);
});

test("scoped inventory pages reach exactly 512 items and reject repeats or cycles", () => {
  const root: InventoryContainerItem = {
    id: "item.root", name: "Root", definition: { id: "definition.root", label: "Root" },
    quantity: 1, slot: "carried", order: 0, equipmentSlots: [], classification: "item", isContainer: true,
    parentItemId: null, depth: 1, childCount: null, deeperContentsOmitted: true,
  };
  const page = (prefix: string, count: number): InventoryContainerPageItem[] => Array.from({ length: count }, (_, index) => ({
    id: `item.${prefix}-${index}`, name: `${prefix} ${index}`, definition: index === count - 1 ? null
      : { id: `definition.${prefix}-${index}`, label: `${prefix} ${index}` },
    quantity: index, slot: "contents", order: index, equipmentSlots: [],
    classification: index === count - 1 ? "unclassified" : "item", isContainer: index === 0,
  }));
  let loaded = mergeInventoryContainerItems([root], root.id, page("a", 200));
  loaded = mergeInventoryContainerItems(loaded, "item.a-0", page("b", 200));
  loaded = mergeInventoryContainerItems(loaded, "item.b-0", page("c", 111));
  assert.equal(loaded.length, 512);
  assert.equal(loaded.at(-1)?.classification, "unclassified", "unknown definitions remain explicit");
  assert.throws(() => mergeInventoryContainerItems(loaded, "item.c-0", page("overflow", 1)), /512-item display bound/u);
  assert.throws(() => mergeInventoryContainerItems([root], root.id, [{ ...page("cycle", 1)[0]!, id: root.id }]), /repeats/u);
});

test("an empty scoped container loads once and unknown records remain honest clickable rows", async () => {
  const unknown: InventoryContainerItem = {
    id: "item.unknown", name: "Unknown record", definition: null, quantity: null, slot: "carried",
    order: 0, equipmentSlots: [], classification: "unclassified", isContainer: true, parentItemId: null, depth: 1,
    childCount: null, deeperContentsOmitted: true,
  };
  let reads = 0;
  const mounted = await mount("", <InventoryTree items={[unknown]} onOpenItem={() => {}}
    loadContainer={async (_id) => {
      reads += 1;
      return { status: "ready", failureCategory: null, diagnosticId: "empty-container", data: {
        version: 2, container: { id: unknown.id, label: unknown.name }, state: "ready", reasons: [], items: [],
        limits: { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false },
        projection: { stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
          resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64) },
      } };
    }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Unknown item record|View details/);
    const disclosure = mounted.container.querySelector<HTMLButtonElement>(".character-inventory__disclosure")!;
    await perform(() => disclosure.click());
    assert.match(mounted.container.textContent ?? "", /This container is empty\./);
    assert.equal(reads, 1);
    await perform(() => disclosure.click());
    await perform(() => disclosure.click());
    assert.equal(reads, 1, "an empty loaded page is cached when its disclosure is reopened");
  } finally { await mounted.cleanup(); }
});

test("ordinary items cannot disclose contents, including stale expanded state", async () => {
  const knife: InventoryContainerItem = {
    id: "item.knife", name: "Carving knife", definition: { id: "definition.knife", label: "Knife" },
    quantity: 1, slot: "carried", order: 0, equipmentSlots: [], classification: "item", isContainer: false,
    parentItemId: null, depth: 1, childCount: null, deeperContentsOmitted: true,
  };
  let reads = 0;
  let opened = "";
  const mounted = await mount("", <InventoryTree items={[knife]} expandedIds={[knife.id]}
    onOpenItem={id => { opened = id; }} loadContainer={async () => { reads++; throw new Error("Unexpected contents read"); }} />);
  try {
    assert.equal(mounted.container.querySelector(".character-inventory__disclosure"), null);
    assert.equal(mounted.container.querySelector(".character-inventory__contents"), null);
    assert.equal(reads, 0);
    await perform(() => mounted.container.querySelector<HTMLButtonElement>("[data-item-open]")!.click());
    assert.equal(opened, knife.id);
  } finally { await mounted.cleanup(); }
});

test("Campaign and Party routes are closed, addressable fragments", () => {
  assert.deepEqual(parseHubRoute(hubRouteHash("campaign", "log")), {
    kind: "hub", tab: "campaign", campaignSection: "log", partySection: "overview", characterId: null, worldSection: "overview",
    locationScopeId: null, locationScopePath: [], locationId: null,
  });
  assert.deepEqual(parseHubRoute(hubRouteHash("party")), {
    kind: "hub", tab: "party", campaignSection: "overview", partySection: "overview", characterId: null, worldSection: "overview",
    locationScopeId: null, locationScopePath: [], locationId: null,
  });
  assert.deepEqual(parseHubRoute(hubRouteHash("party", "overview", {
    partySection: "inventory",
    characterId: "actor.caldris.ganji",
  })), {
    kind: "hub", tab: "party", campaignSection: "overview", partySection: "inventory",
    characterId: "actor.caldris.ganji", worldSection: "overview",
    locationScopeId: null, locationScopePath: [], locationId: null,
  });
  assert.deepEqual(parseHubRoute(hubRouteHash("world", "overview", {
    worldSection: "locations",
    locationScopePath: ["atlas.renamed", "region.sol-1"],
    locationId: "region.sol-1",
  })), {
    kind: "hub", tab: "world", campaignSection: "overview", partySection: "overview", characterId: null, worldSection: "locations",
    locationScopeId: "region.sol-1", locationScopePath: ["atlas.renamed", "region.sol-1"],
    locationId: "region.sol-1",
  });
  for (const hash of ["#view?tab=campaign&section=unknown", "#view?tab=party&section=log",
    "#view?tab=campaign&character=actor.ganji", "#view?tab=party&character=actor%20ganji",
    "#view?tab=campaign&principal=gm", "#view?tab=campaign&tab=party", "#view?tab=%zz"]) {
    assert.equal(parseHubRoute(hash).kind, "invalid");
  }
});

test("Rules content links retain Rules as the parent through registry details", async () => {
  const projected = projectHubEnvelope(hubSource, "fixture", resolveAudience({
    authenticatedUserId: "dm.fixture", authenticatedUserEmail: "", requestedPerspective: "player",
    dmPrincipalIds: ["dm.fixture"],
  })) as ReadyHubEnvelope;
  const linkedRule: RuleReadModel = {
    id: "dnd2024.rule.equipment.inventory-equipment-and-carrying",
    resolutionKey: "rule.equipment.inventory-equipment-and-carrying",
    title: "Inventory, Equipment, and Carrying", summary: "Recorded inventory guidance.", order: 10,
    section: { id: "equipment", label: "Equipment", order: 60 },
    blocks: [{ kind: "paragraph", heading: null, body: "Containers preserve item identity.", items: [] }],
    examples: [], relatedRuleIds: [],
    relatedContent: [{ kind: "item", entityId: "dnd2024.item.backpack.v1", title: "Backpack",
      collection: "dnd2024", contentFingerprint: "A".repeat(64), available: true }],
    citations: [{ sourceId: "dnd2024.source.srd-5.2.1", locator: "Equipment > Backpack, PDF p. 95" }],
    authority: { mechanicIds: ["dnd2024.mechanic.inventory.read"], procedureIds: [] },
    visibility: "public", source: { ownerId: "base", label: "Core", classification: "core" },
  };
  const initial: ReadyHubEnvelope = { ...projected, party, rules: [linkedRule],
    contextSelection: { selectedWorldId: projected.world.id, selectedCampaignId: "campaign.test",
      worlds: [{ id: projected.world.id, name: projected.world.name,
        campaigns: [{ id: "campaign.test", name: "Fixture" }] }] } };
  const itemRecord = { id: "dnd2024.item.backpack.v1", collection: "dnd2024", name: "Backpack",
    status: "active", version: 1, contentFingerprint: "A".repeat(64), sourceId: "dnd2024-core",
    sourceLabel: "Core", classification: "core" as const };
  const mounted = await mount(hubRouteHash("rules"), <DndInformationHub initialEnvelope={initial}
    loadItemRegistryPage={async () => ({ resolutionFingerprint: "B".repeat(64), records: [],
      totalCount: 0, nextCursor: null })}
    loadItemDefinition={async () => ({ record: itemRecord, details: {
      version: 1, observerId: "shared-table", itemId: itemRecord.id, perspective: "player", state: "ready",
      name: itemRecord.name, description: "A carried container.", definitionId: itemRecord.id,
      quantity: null, container: null, equipmentSlots: [], properties: [], sources: [], media: [], reasons: [],
      observerKnowledge: null,
    } })} />);
  try {
    await perform(() => button(mounted.container, "Backpack").click());
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "Rules");
    assert.match(mounted.container.querySelector(".item-page__breadcrumbs")?.textContent ?? "", /RulesBackpack/);
    assert.match(mounted.container.textContent ?? "", /A carried container/);
    await perform(() => mounted.container.querySelector<HTMLButtonElement>(".item-page__breadcrumbs button")!.click());
    assert.equal(parseItemRoute(window.location.hash).kind, "none");
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "Rules");
    assert.match(mounted.container.textContent ?? "", /Inventory, Equipment, and Carrying/);
  } finally { await mounted.cleanup(); }
});

test("opening image/name is independent of disclosure; Back and Forward restore character, focus, scroll and nested contents", async () => {
  const before = structuredClone(party);
  const mounted = await mount(itemRouteHash(inventory));
  try {
    const { container } = mounted;
    assert.equal(container.querySelector('[aria-current="true"] strong')?.textContent, "actor.second");
    const bag = container.querySelector<HTMLButtonElement>('.character-inventory__disclosure[aria-controls="inventory-contents-item-bag"]')!;
    assert.ok(bag);
    await perform(() => bag.click());
    assert.equal(bag.getAttribute("aria-expanded"), "true");
    const search = container.querySelector<HTMLInputElement>('input[placeholder="Search loaded inventory…"]')!;
    await perform(() => {
      Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")!.set!.call(search, "private");
      search.dispatchEvent(new window.Event("input", { bubbles: true }));
    });
    window.scrollTo(0, 487);
    const trigger = container.querySelector<HTMLButtonElement>('[data-item-open="item.secret"]')!;
    trigger.focus();
    await perform(() => trigger.querySelector<HTMLElement>(".character-inventory__item-media")!.click());
    assert.equal(parseItemRoute(window.location.hash).kind, "item");
    assert.equal(document.activeElement?.id, "item-view-heading");
    assert.match(container.querySelector(".item-page__breadcrumbs")?.textContent ?? "", /Partyactor.secondInventory/);
    assert.doesNotMatch(container.querySelector(".item-page")?.textContent ?? "", /PRIVATE INVENTORY NAME|PRIVATE DEFINITION/);
    assert.equal(window.location.pathname, "/published/release"); assert.equal(window.location.search, "?keep=yes");
    await perform(() => button(container, "Known recipes").click());
    assert.match(container.textContent!, /Known recipes unavailable/);
    await perform(() => container.querySelector<HTMLButtonElement>(".item-page__breadcrumbs li:nth-child(3) button")!.click());
    assert.equal(parseItemRoute(window.location.hash).kind, "inventory");
    assert.equal(container.querySelector('[aria-controls="inventory-contents-item-bag"]')?.getAttribute("aria-expanded"), "true");
    assert.equal(container.querySelector<HTMLInputElement>('input[placeholder="Search loaded inventory…"]')?.value, "private");
    assert.equal((document.activeElement as HTMLElement).dataset.itemOpen, "item.secret");
    assert.equal(window.scrollY, 487);
    await perform(() => window.history.forward());
    assert.match(container.textContent!, /Known recipes unavailable/);
    const recipes = button(container, "Known recipes");
    await perform(() => recipes.dispatchEvent(new window.KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })));
    assert.equal(document.activeElement?.id, "item-tab-uses");
    assert.match(container.textContent!, /Known uses unavailable/);
    await perform(() => document.activeElement!.dispatchEvent(new window.KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    assert.equal(container.querySelector('[aria-controls="inventory-contents-item-bag"]')?.getAttribute("aria-expanded"), "true");
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
      assert.doesNotMatch(mounted.container.textContent!, /PRIVATE|No recipes known/);
      assert.equal(mounted.container.querySelector("img"), null);
      const back = mounted.container.querySelector<HTMLButtonElement>(".item-page__breadcrumbs li:nth-last-child(2) button")!;
      await perform(() => back.click());
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

test("shared table upgrades obsolete Player inventory links without offering a role switch", async () => {
  const projected = projectHubEnvelope(hubSource, "fixture", resolveAudience({
    authenticatedUserId: "dm.fixture", authenticatedUserEmail: "", requestedPerspective: "dm",
    dmPrincipalIds: ["dm.fixture"],
  })) as ReadyHubEnvelope;
  const initial: ReadyHubEnvelope = { ...projected, party,
    audience: { seat: "dm", perspective: "dm", allowedPerspectives: ["dm"] },
    contextSelection: { selectedWorldId: projected.world.id, selectedCampaignId: "campaign.test",
      worlds: [{ id: projected.world.id, name: projected.world.name,
        campaigns: [{ id: "campaign.test", name: "Fixture" }] }] } };
  const mounted = await mount(itemRouteHash(inventory), <DndInformationHub initialEnvelope={initial} />);
  try {
    await perform(() => {});
    const route = parseItemRoute(window.location.hash);
    assert.equal(route.kind, "inventory");
    assert.equal(route.kind === "inventory" && route.perspective, "dm");
    assert.match(mounted.container.textContent!, /Shared table/);
    assert.doesNotMatch(mounted.container.textContent!, /Actor binding required|not available for this seat/);
    assert.equal(mounted.container.querySelector('[aria-label="Table perspective"]'), null);
  } finally { await mounted.cleanup(); }
});

test("observer-preview item links fall back to the safe Party roster without Actor-binding notices or reads", async () => {
  const projected = projectHubEnvelope(hubSource, "fixture", resolveAudience({
    authenticatedUserId: "dm.fixture", authenticatedUserEmail: "", requestedPerspective: "player",
    dmPrincipalIds: ["dm.fixture"],
  })) as ReadyHubEnvelope;
  const initial: ReadyHubEnvelope = { ...projected, applicationId: "dnd2024-main", stateSpaceId: "state.fixture", party,
    contextSelection: { selectedWorldId: projected.world.id, selectedCampaignId: "campaign.test",
      worlds: [{ id: projected.world.id, name: projected.world.name,
        campaigns: [{ id: "campaign.test", name: "Fixture" }] }] } };
  let reads = 0;
  const deepLink = itemRouteHash({ ...inventory, kind: "item", itemId: "item.secret", tab: "details" });
  const mounted = await mount(deepLink, <DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({}) as never}
    loadCharacterSheet={async () => { reads += 1; throw new Error("not available"); }}
    loadDeferredSection={async () => { reads += 1; throw new Error("not available"); }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Observer preview shows the campaign roster/);
    assert.doesNotMatch(mounted.container.textContent ?? "", /Actor binding required|authorized Actor seat/);
    assert.equal(mounted.container.querySelector(".character-page") !== null, true);
    assert.equal(reads, 0);
  } finally { await mounted.cleanup(); }
});

test("Campaign section and Party navigation survive Back and Forward", async () => {
  const projected = projectHubEnvelope(hubSource, "fixture", resolveAudience({
    authenticatedUserId: "dm.fixture", authenticatedUserEmail: "", requestedPerspective: "dm",
    dmPrincipalIds: ["dm.fixture"],
  })) as ReadyHubEnvelope;
  const initial = { ...projected, applicationId: "dnd2024-main", stateSpaceId: "state.fixture", party,
    contextSelection: { selectedWorldId: projected.world.id, selectedCampaignId: "campaign.test",
      worlds: [{ id: projected.world.id, name: projected.world.name,
        campaigns: [{ id: "campaign.test", name: "Fixture" }] }] } };
  const mounted = await mount(hubRouteHash("campaign", "log"),
    <DndInformationHub initialEnvelope={initial} loadContent={async () => ({}) as never} />);
  try {
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "Campaign");
    assert.equal(mounted.container.querySelector('.section-tabs [aria-current="page"]')?.textContent?.trim(), "Adventure Log");
    await perform(() => button(mounted.container, "Party").click());
    let partyRoute = parseHubRoute(window.location.hash);
    assert.equal(partyRoute.kind, "hub");
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "Party");
    await perform(() => mounted.container.querySelector<HTMLButtonElement>('[data-character-member="actor.second"]')!.click());
    await perform(() => button(mounted.container, "Biography").click());
    partyRoute = parseHubRoute(window.location.hash);
    assert.equal(partyRoute.kind === "hub" && partyRoute.characterId, "actor.second");
    assert.equal(partyRoute.kind === "hub" && partyRoute.partySection, "backstory");
    assert.equal((document.activeElement as HTMLElement).dataset.characterSection, "backstory");
    await perform(() => window.history.back());
    partyRoute = parseHubRoute(window.location.hash);
    assert.equal(partyRoute.kind === "hub" && partyRoute.characterId, "actor.second");
    assert.equal(partyRoute.kind === "hub" && partyRoute.partySection, "overview");
    assert.equal((document.activeElement as HTMLElement).dataset.characterSection, "overview");
    await perform(() => window.history.back());
    assert.equal((parseHubRoute(window.location.hash) as { characterId?: string | null }).characterId, "actor.first");
    await perform(() => window.history.back());
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "Campaign");
    assert.equal(mounted.container.querySelector('.section-tabs [aria-current="page"]')?.textContent?.trim(), "Adventure Log");
    await perform(() => window.history.forward());
    assert.equal(mounted.container.querySelector('.main-nav [aria-current="page"]')?.textContent?.trim(), "Party");
    await perform(() => window.history.forward());
    assert.equal((parseHubRoute(window.location.hash) as { characterId?: string | null }).characterId, "actor.second");
    await perform(() => window.history.forward());
    partyRoute = parseHubRoute(window.location.hash);
    assert.equal(partyRoute.kind === "hub" && partyRoute.partySection, "backstory");
    assert.equal((document.activeElement as HTMLElement).dataset.characterSection, "backstory");
  } finally { await mounted.cleanup(); }
});
