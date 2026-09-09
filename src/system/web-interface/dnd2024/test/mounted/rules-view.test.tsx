import assert from "node:assert/strict";
import { createRequire } from "node:module";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";

import { RulesView } from "../../src/components/RulesView";
import type { RuleReadModel, RulesReferencePublication } from "../../src/data/hub-types";
import { parseItemRoute } from "../../src/data/item-view-route";
import { ViewReadError } from "../../src/data/view-read-client";

const fingerprint = "A".repeat(64);

function rule(): RuleReadModel {
  return {
    id: "dnd2024.rule.equipment.inventory-equipment-and-carrying",
    resolutionKey: "rule.equipment.inventory-equipment-and-carrying",
    title: "Inventory, Equipment, and Carrying",
    summary: "Inventory records ownership and containment.",
    order: 10,
    section: { id: "equipment", label: "Equipment", order: 60 },
    blocks: [{ kind: "paragraph", heading: "Containers", body: "Backpacks contain item instances.", items: [] }],
    examples: [], relatedRuleIds: [],
    relatedContent: [{ kind: "item", entityId: "dnd2024.item.backpack.v1", title: "Backpack",
      collection: "dnd2024", contentFingerprint: fingerprint, available: true }],
    citations: [{ sourceId: "dnd2024.source.srd-5.2.1", locator: "Equipment > Backpack, PDF p. 95" }],
    authority: { mechanicIds: ["dnd2024.mechanic.inventory.read"],
      procedureIds: ["dnd2024.procedure.mechanic.inventory-read"] },
    visibility: "public", source: { ownerId: "base", label: "Core", classification: "core" },
  };
}

function publication(rules: RuleReadModel[]): RulesReferencePublication {
  return { applicationId: "dnd2024", resolutionFingerprint: fingerprint,
    rulesFingerprint: "B".repeat(64), audience: "public", articleCount: rules.length, rules };
}

async function mount(initial: RuleReadModel[], loadRules?: (preferCached?: boolean,
  signal?: AbortSignal) => Promise<RulesReferencePublication>) {
  const dom = new JSDOM("<div id='root'></div>", { url: "https://table.test/ui/dnd2024-play#view?tab=rules" });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "MouseEvent",
    "HTMLInputElement", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  dom.window.requestAnimationFrame = (callback) => dom.window.setTimeout(() => callback(0), 0);
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  await act(async () => { root.render(<RulesView rules={initial} loadRules={loadRules}
    campaignId="campaign.test" perspective="dm" />); await new Promise((resolve) => setTimeout(resolve, 30)); });
  return { dom, async cleanup() { await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!);
      else Reflect.deleteProperty(globalThis, key); }); } };
}

async function perform(action: () => void) {
  await act(async () => { action(); await new Promise((resolve) => setTimeout(resolve, 25)); });
}

function button(text: string) {
  const found = [...document.querySelectorAll<HTMLButtonElement>("button")]
    .find((candidate) => candidate.textContent?.includes(text));
  assert.ok(found, `Missing button: ${text}`);
  return found;
}

test("Rules view loads an endpoint-counted publication, searches it, and opens declared content", async () => {
  const useCache: boolean[] = [];
  const mounted = await mount([], async (preferCached) => { useCache.push(preferCached ?? true); return publication([rule()]); });
  try {
    assert.match(document.body.textContent!, /1 published rule/);
    assert.deepEqual(useCache, [true]);
    const input = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    await perform(() => {
      Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")!.set!.call(input, "backpack");
      input.dispatchEvent(new window.Event("input", { bubbles: true }));
      input.dispatchEvent(new window.Event("change", { bubbles: true }));
    });
    assert.match(document.body.textContent!, /Inventory, Equipment, and Carrying/);
    await perform(() => button("Backpack").click());
    assert.equal(parseItemRoute(window.location.hash).kind, "registry-item");
    assert.equal(window.history.state.itemMainTab, "rules");

    const load = createRequire(import.meta.url);
    delete load.cache[load.resolve("axe-core")];
    const axe = load("axe-core") as typeof import("axe-core").default;
    const report = await axe.run(document.body, { rules: { region: { enabled: false } } });
    assert.deepEqual(report.violations, []);
  } finally { await mounted.cleanup(); }
});

test("Rules view distinguishes an empty publication from a hard read failure", async () => {
  const empty = await mount([], async () => publication([]));
  try {
    assert.match(document.body.textContent!, /No published readable rules/);
    assert.doesNotMatch(document.body.textContent!, /Rules could not be loaded/);
  } finally { await empty.cleanup(); }

  const failed = await mount([], async () => {
    throw new ViewReadError("transport", "unavailable");
  });
  try {
    assert.match(document.body.textContent!, /Rules could not be loaded/);
    assert.match(document.body.textContent!, /Check the connection and try again/);
  } finally { await failed.cleanup(); }
});

test("Rules view preserves the last valid publication when refresh fails", async () => {
  const mounted = await mount([rule()], async () => {
    throw new ViewReadError("incompatible-data", "changed");
  });
  try {
    assert.match(document.body.textContent!, /Inventory, Equipment, and Carrying/);
    assert.match(document.body.textContent!, /last valid publication is still available/);
    assert.doesNotMatch(document.body.textContent!, /Rules could not be loaded/);
  } finally { await mounted.cleanup(); }
});
