import assert from "node:assert/strict";
import { JSDOM } from "jsdom";
import test from "node:test";
import React, { act } from "react";

import type {
  InventoryContainerPageResult,
  InventoryContainerResult,
  PartyMemberReadModel,
} from "../../src/data/hub-types";
import { PartyView } from "../../src/components/PartyView";
import { readCanonicalInventory } from "../../src/server/game-server-context.js";
import { contract as inventoryContainerContract } from "../../src/server/inventory-container-contract.js";
import { contract as inventoryWalletContract } from "../../src/server/inventory-wallet-contract.js";

async function mount(element: React.ReactNode) {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", {
    url: "https://table.synthetic.test/",
  });
  const previous = {
    document: globalThis.document,
    Element: globalThis.Element,
    Event: globalThis.Event,
    HTMLElement: globalThis.HTMLElement,
    MouseEvent: globalThis.MouseEvent,
    Node: globalThis.Node,
    window: globalThis.window,
  };
  const previousNavigator = Object.getOwnPropertyDescriptor(globalThis, "navigator");
  Object.assign(globalThis, {
    document: dom.window.document,
    Element: dom.window.Element,
    Event: dom.window.Event,
    HTMLElement: dom.window.HTMLElement,
    MouseEvent: dom.window.MouseEvent,
    Node: dom.window.Node,
    window: dom.window,
  });
  Object.defineProperty(globalThis, "navigator", { configurable: true, value: dom.window.navigator });
  dom.window.requestAnimationFrame = (callback) => dom.window.setTimeout(() => callback(Date.now()), 0);
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.querySelector("#root") as HTMLDivElement;
  const root = createRoot(container);
  await act(async () => root.render(element));
  return {
    container,
    async cleanup() {
      await act(async () => root.unmount());
      dom.window.close();
      Object.assign(globalThis, previous);
      if (previousNavigator) Object.defineProperty(globalThis, "navigator", previousNavigator);
      else delete (globalThis as { navigator?: Navigator }).navigator;
      delete globalThis.IS_REACT_ACT_ENVIRONMENT;
    },
  };
}

function syntheticMember(): PartyMemberReadModel {
  return {
    id: "actor.synthetic.inventory",
    initials: "SI",
    name: "Synthetic Investigator",
    detail: "Synthetic character",
    status: "Active",
    isCurrent: true,
    portrait: { imageUrl: "/synthetic-portrait.png", alt: "Synthetic portrait", width: 64, height: 64 },
    recordStatus: "Canonical character state",
    sheetStatus: "canonical",
    inventoryStatus: "canonical",
    sheetState: { status: "ready", source: "canonical", data: [{
      id: "sheet.synthetic", kind: "vital", title: "Synthetic sheet", detail: "Independent sheet data",
    }] },
    inventoryState: { status: "ready", source: "canonical", data: [] },
    sheet: [{ id: "sheet.synthetic", kind: "vital", title: "Synthetic sheet", detail: "Independent sheet data" }],
    knowledge: [], backstory: [], origin: [], inventory: [],
    characterSheet: {
      version: 2,
      subject: { id: "actor.synthetic.inventory", label: "Synthetic Investigator" },
      hitPoints: { current: 11, maximum: 11, maximumReduction: 0 },
      inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
      wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
    },
  } satisfies PartyMemberReadModel;
}

function malformedInventory(): InventoryContainerResult {
  return {
    status: "ready",
    failureCategory: null,
    diagnosticId: "synthetic-inventory-partial",
    data: {
      container: { id: "actor.synthetic.inventory", label: "Synthetic Investigator" },
      state: "partial",
      reasons: [],
      notices: ["duplicate-item-identity", "invalid-item-identity"],
      items: [{
        id: "item.synthetic.good",
        name: "Reliable compass",
        definition: { id: "definition.compass", label: "Compass" },
        quantity: 0,
        quantityState: "value",
        slot: "carried",
        order: 0,
        equipmentSlots: [],
        equipmentSlotsKnown: true,
        classification: "item",
        isContainer: false,
        containerState: "value",
        unavailableFields: [],
        parentItemId: null,
        depth: 1,
        childCount: 0,
        deeperContentsOmitted: false,
      }],
      limits: { contentsDepth: 1, directComplete: true, recursiveComplete: false },
      wallet: null,
      walletState: { status: "unavailable", reason: "read-failed" },
      projection: {
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
      },
    },
  };
}

function transportResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

async function readPartialInventoryThroughAdapter(): Promise<InventoryContainerResult> {
  const actorId = "actor.synthetic.inventory";
  return readCanonicalInventory({
    origin: "http://localhost:6217",
    applicationId: "dnd2024",
    stateSpaceId: "state.synthetic",
    actorId,
    perspective: "dm",
    fetchImpl: async (input) => {
      const request = new URL(input);
      if (request.pathname.endsWith(inventoryWalletContract.id)) return transportResponse(200, {
        applicationId: "dnd2024", stateSpaceId: "state.synthetic",
        qualifiedQueryId: inventoryWalletContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: "a".repeat(64), resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 1, owner: { id: actorId, label: "Synthetic Investigator" }, state: "ready", reasons: [],
          wallet: {
            coinCount: 2, copperValue: 200, gpCount: 2,
            denominations: [{ denomination: { id: "currency.gp", label: "Gold" }, code: "gp", count: "unknown",
              copperValuePerCoin: 100, totalCopperValue: 200 }],
          },
          limits: { contentsDepth: 4, complete: true },
        },
      });
      return transportResponse(200, {
        applicationId: "dnd2024", stateSpaceId: "state.synthetic",
        qualifiedQueryId: inventoryContainerContract.id,
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        outputSchemaHash: "b".repeat(64), resultFingerprint: "3".repeat(64),
        sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 3, container: { id: actorId, label: "Synthetic Investigator" }, state: "ready", reasons: [],
          items: [
            {
              id: "item.synthetic.good", name: "Reliable compass",
              definition: { id: "definition.compass", label: "Compass" }, quantity: 0,
              slot: "carried", order: 0, equipmentSlots: [], classification: "item", isContainer: false,
            },
            {
              id: "item.synthetic.duplicate", name: "Duplicate one",
              definition: { id: "definition.duplicate", label: "Duplicate" }, quantity: 1,
              slot: "carried", order: 1, equipmentSlots: [], classification: "item", isContainer: false,
            },
            {
              id: "item.synthetic.duplicate", name: "Duplicate two",
              definition: { id: "definition.duplicate", label: "Duplicate" }, quantity: 1,
              slot: "carried", order: 2, equipmentSlots: [], classification: "item", isContainer: false,
            },
            { id: "", name: "Malformed identity", quantity: 1, slot: "carried", order: 3, equipmentSlots: [] },
          ],
          limits: { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false },
          additiveMetadata: { producerVersion: 3 },
        },
      });
    },
  });
}

test("partial field-based inventory leaves independent sheet and portrait usable and exposes no unsafe row action", async () => {
  const opened: string[] = [];
  const member = syntheticMember();
  const mounted = await mount(
    <PartyView
      party={[member]}
      loadCharacterInventory={readPartialInventoryThroughAdapter}
      onOpenItem={(_characterId, itemId) => opened.push(itemId)}
    />,
  );
  try {
    const portrait = mounted.container.querySelector<HTMLImageElement>(".character-hero__portrait img, .character-hero img");
    assert.ok(portrait, "the independent portrait remains mounted");
    assert.equal(portrait?.getAttribute("src"), "/synthetic-portrait.png");

    const inventoryTab = mounted.container.querySelector<HTMLButtonElement>("[data-character-section='inventory']");
    assert.ok(inventoryTab);
    await act(async () => {
      inventoryTab.click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    assert.match(mounted.container.textContent ?? "", /Reliable compass/);
    assert.match(mounted.container.textContent ?? "", /Some wallet fields are unavailable/);
    assert.doesNotMatch(mounted.container.textContent ?? "", /No coins are recorded/);
    assert.doesNotMatch(mounted.container.textContent ?? "", /malformed|duplicate/i);
    const openButton = mounted.container.querySelector<HTMLButtonElement>("[data-item-open='item.synthetic.good']");
    assert.ok(openButton);
    await act(async () => openButton.click());
    assert.deepEqual(opened, ["item.synthetic.good"]);

    const sheetTab = mounted.container.querySelector<HTMLButtonElement>("[data-character-section='sheet']");
    assert.ok(sheetTab);
    await act(async () => sheetTab.click());
    assert.match(mounted.container.textContent ?? "", /11 \/ 11/);
    assert.match(mounted.container.textContent ?? "", /Synthetic Investigator/);
    assert.equal(mounted.container.querySelector<HTMLImageElement>(".character-hero__portrait img, .character-hero img")?.src,
      "https://table.synthetic.test/synthetic-portrait.png");
  } finally {
    await mounted.cleanup();
  }
});

test("nested partial inventory never presents an empty container or discards its safety notice", async () => {
  const root = malformedInventory();
  root.data.items = [{
    id: "item.synthetic.bag",
    name: "Uncertain satchel",
    definition: { id: "definition.satchel", label: "Satchel" },
    quantity: 1,
    quantityState: "value",
    slot: "carried",
    order: 0,
    equipmentSlots: [],
    equipmentSlotsKnown: true,
    classification: "item",
    isContainer: true,
    containerState: "value",
    unavailableFields: [],
    parentItemId: null,
    depth: 1,
    childCount: null,
    deeperContentsOmitted: true,
  }];
  root.data.state = "ready";
  root.data.notices = [];
  const nested: InventoryContainerPageResult = {
    status: "ready",
    failureCategory: null,
    diagnosticId: "synthetic-nested-partial",
    data: {
      container: { id: "item.synthetic.bag", label: "Uncertain satchel" },
      state: "partial",
      reasons: [],
      notices: ["duplicate-item-identity"],
      items: [{
        id: "item.synthetic.child",
        name: "Recovered child",
        definition: { id: "definition.child", label: "Recovered child" },
        quantity: 1,
        quantityState: "value",
        slot: "contents",
        order: 0,
        equipmentSlots: [],
        equipmentSlotsKnown: true,
        classification: "item",
        isContainer: false,
        containerState: "value",
        unavailableFields: [],
      }],
      limits: { contentsDepth: 1, directComplete: null, recursiveComplete: false },
      projection: {
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
      },
    },
  };
  const mounted = await mount(
    <PartyView
      party={[syntheticMember()]}
      loadCharacterInventory={async () => root}
      loadInventoryContainer={async () => nested}
    />,
  );
  try {
    const inventoryTab = mounted.container.querySelector<HTMLButtonElement>("[data-character-section='inventory']");
    assert.ok(inventoryTab);
    await act(async () => {
      inventoryTab.click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    const disclosure = mounted.container.querySelector<HTMLButtonElement>(".character-inventory__disclosure");
    assert.ok(disclosure);
    await act(async () => {
      disclosure.click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    const text = mounted.container.textContent ?? "";
    assert.match(text, /Some contents could not be safely displayed/);
    assert.match(text, /Recovered child/);
    assert.doesNotMatch(text, /This container is empty/);
  } finally {
    await mounted.cleanup();
  }
});

test("a nested page repeating an existing item becomes a local contents error without losing the root or portrait", async () => {
  const root = malformedInventory();
  root.data.state = "ready";
  root.data.notices = [];
  root.data.items = [{
    id: "item.synthetic.bag",
    name: "Uncertain satchel",
    definition: { id: "definition.satchel", label: "Satchel" },
    quantity: 1,
    quantityState: "value",
    slot: "carried",
    order: 0,
    equipmentSlots: [],
    equipmentSlotsKnown: true,
    classification: "item",
    isContainer: true,
    containerState: "value",
    unavailableFields: [],
    parentItemId: null,
    depth: 1,
    childCount: null,
    deeperContentsOmitted: true,
  }];
  const repeated: InventoryContainerPageResult = {
    status: "ready",
    failureCategory: null,
    diagnosticId: "synthetic-nested-repeat",
    data: {
      container: { id: "item.synthetic.bag", label: "Uncertain satchel" },
      state: "ready",
      reasons: [],
      notices: [],
      items: [{
        id: "item.synthetic.bag",
        name: "Repeated satchel",
        definition: { id: "definition.satchel", label: "Satchel" },
        quantity: 1,
        quantityState: "value",
        slot: "contents",
        order: 0,
        equipmentSlots: [],
        equipmentSlotsKnown: true,
        classification: "item",
        isContainer: true,
        containerState: "value",
        unavailableFields: [],
      }],
      limits: { contentsDepth: 1, directComplete: true, recursiveComplete: true },
      projection: {
        stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
      },
    },
  };
  const mounted = await mount(
    <PartyView
      party={[syntheticMember()]}
      loadCharacterInventory={async () => root}
      loadInventoryContainer={async () => repeated}
    />,
  );
  try {
    const inventoryTab = mounted.container.querySelector<HTMLButtonElement>("[data-character-section='inventory']");
    assert.ok(inventoryTab);
    await act(async () => {
      inventoryTab.click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    const disclosure = mounted.container.querySelector<HTMLButtonElement>(".character-inventory__disclosure");
    assert.ok(disclosure);
    await act(async () => {
      disclosure.click();
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    const text = mounted.container.textContent ?? "";
    assert.match(text, /Uncertain satchel/);
    assert.match(text, /Contents could not be loaded/);
    assert.equal(mounted.container.querySelector<HTMLImageElement>(".character-hero__portrait img, .character-hero img")?.src,
      "https://table.synthetic.test/synthetic-portrait.png");
  } finally {
    await mounted.cleanup();
  }
});

test("an empty root stays distinct from an unavailable wallet and an explicit zero wallet", async () => {
  for (const [label, walletState, wallet] of [
    ["unavailable", { status: "unavailable", reason: "read-failed" }, null],
    ["zero", { status: "complete" }, {
      coinCount: 0, copperValue: 0, gpCount: 0, denominations: [],
    }],
  ] as const) {
    const root = malformedInventory();
    root.data.state = "ready";
    root.data.notices = [];
    root.data.items = [];
    root.data.walletState = walletState;
    root.data.wallet = wallet;
    const mounted = await mount(
      <PartyView party={[syntheticMember()]} loadCharacterInventory={async () => root} />,
    );
    try {
      const inventoryTab = mounted.container.querySelector<HTMLButtonElement>("[data-character-section='inventory']");
      assert.ok(inventoryTab, label);
      await act(async () => {
        inventoryTab.click();
        await new Promise((resolve) => setTimeout(resolve, 0));
      });
      const text = mounted.container.textContent ?? "";
      assert.match(text, /This inventory container is empty\./, label);
      if (label === "unavailable") {
        assert.match(text, /Wallet totals could not be calculated/);
        assert.doesNotMatch(text, /No coins are recorded/);
      } else {
        assert.match(text, /All coins0/);
        assert.match(text, /No coins are recorded/);
      }
    } finally {
      await mounted.cleanup();
    }
  }
});
