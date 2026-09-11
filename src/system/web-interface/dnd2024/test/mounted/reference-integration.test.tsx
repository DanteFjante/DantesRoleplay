import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { createHubStore } from "../../src/data/hub-store";
import type { RuleReadModel, RulesReferencePublication } from "../../src/data/hub-types";
import type { InstalledContentPage } from "../../src/server/effective-content";
import { ViewReadError } from "../../src/data/view-read-client";
import { integrationEnvelope } from "../fixtures/item-integration";

const fingerprint = "A".repeat(64);
function rule(title: string): RuleReadModel {
  return { id: "dnd2024.rule.fixture.reference", resolutionKey: "rule.fixture.reference", title,
    summary: "A useful read-only explanation.", order: 1,
    section: { id: "equipment", label: "Equipment", order: 1 }, blocks: [], examples: [],
    relatedRuleIds: [], relatedContent: [], citations: [], authority: { mechanicIds: [], procedureIds: [] },
    visibility: "public", source: { ownerId: "base", label: "Core", classification: "core" } };
}
function publication(title: string): RulesReferencePublication {
  return { applicationId: "dnd2024", resolutionFingerprint: fingerprint, rulesFingerprint: fingerprint,
    audience: "public", articleCount: 1, rules: [rule(title)], coverage: "ready", notices: [] };
}
function content(name: string, nextCursor: string | null = null): InstalledContentPage {
  return { resolutionFingerprint: fingerprint, totalCount: 1, nextCursor, availableKinds: ["entity"],
    coverage: "ready", notices: [], extensions: [{ extensionId: "fixture-extension", displayName: "Fixture extension",
      description: "Reviewed test content.", classification: "homebrew" }], records: [{ id: "fixture.item.one", name,
      description: "A useful item description.", kind: "entity", path: "entities/items", ownerId: "fixture-extension",
      sourceLabel: "Fixture extension", classification: "homebrew", presentationRoles: [], isAdditive: true }] };
}
async function settle(predicate: () => boolean) {
  for (let count = 0; count < 40; count++) {
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
    if (predicate()) return;
  }
  assert.ok(predicate(), document.body.textContent ?? "The expected view did not settle.");
}
async function mount(tab: "rules" | "content", options: {
  holdRules?: boolean;
  initialDenied?: boolean;
  nextContentCursor?: string | null;
} = {}) {
  const dom = new JSDOM("<!doctype html><html><body><div id='root'></div></body></html>", {
    url: `https://table.test/ui/dnd2024-play#view?tab=${tab}`, pretendToBeVisual: true,
  });
  const keys = ["window", "document", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : dom.window[key as keyof Window] });
  dom.window.requestAnimationFrame = (callback) => dom.window.setTimeout(() => callback(0), 0);
  dom.window.scrollTo = () => {};
  const store = createHubStore();
  const control = { rulesCalls: 0, contentCalls: 0, title: "First reference", name: "First contribution", denied: options.initialDenied === true,
    stale: false,
    holdRules: options.holdRules === true, nextContentCursor: options.nextContentCursor ?? null };
  const envelope = integrationEnvelope("dm");
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  await act(async () => root.render(<DndInformationHub store={store} initialEnvelope={envelope}
    loadRules={async (_preferCached, signal) => {
      assert.ok(++control.rulesCalls < 10, "Confirmed publications must not refetch themselves indefinitely.");
      if (control.denied) throw new ViewReadError("authorization", "Not available to this audience.");
      if (control.holdRules) await new Promise<never>((_, reject) => signal?.addEventListener("abort",
        () => reject(new DOMException("Cancelled", "AbortError")), { once: true }));
      return publication(control.title);
    }}
    loadContent={async (request) => {
      assert.ok(++control.contentCalls < 10, "Confirmed content must not refetch itself indefinitely.");
      if (control.denied) throw new ViewReadError("authorization", "Not available to this audience.");
      if (control.stale && request.cursor) throw new ViewReadError("stale-data", "Page changed.");
      return content(control.name, request.cursor ? null : control.nextContentCursor);
    }} />));
  const click = async (label: string) => {
    const button = [...document.querySelectorAll<HTMLButtonElement>("button")]
      .find((value) => value.textContent?.trim() === label);
    assert.ok(button, `Missing button: ${label}`);
    await act(async () => button.click());
  };
  return { store, control, click, async cleanup() {
    await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!);
      else Reflect.deleteProperty(globalThis, key); });
  } };
}

test("connected Rules uses Redux across navigation and rereads after same-scope invalidation", async () => {
  const view = await mount("rules");
  try {
    await settle(() => document.body.textContent?.includes("First reference") === true);
    assert.equal(view.control.rulesCalls, 1);
    assert.equal(view.store.getState().references.rules?.value.rules[0]?.title, "First reference");
    await view.click("World"); await view.click("Rules");
    await settle(() => document.body.textContent?.includes("First reference") === true);
    assert.equal(view.control.rulesCalls, 1, "Navigation reads the same confirmed publication.");
    view.control.title = "Updated reference";
    await act(async () => window.dispatchEvent(new Event("dnd2024-view-invalidated")));
    await settle(() => document.body.textContent?.includes("Updated reference") === true);
    assert.equal(view.control.rulesCalls, 2);
    assert.doesNotMatch(document.body.textContent!, /First reference/);
  } finally { await view.cleanup(); }
});

test("connected Installed Content rereads an invalidated first page without a second completed cache", async () => {
  const view = await mount("content");
  try {
    await settle(() => document.body.textContent?.includes("First contribution") === true);
    assert.equal(view.control.contentCalls, 1);
    assert.equal(Object.keys(view.store.getState().references.content).length, 1);
    await view.click("World"); await view.click("Installed Content");
    await settle(() => document.body.textContent?.includes("First contribution") === true);
    assert.equal(view.control.contentCalls, 1);
    view.control.name = "Updated contribution";
    await act(async () => window.dispatchEvent(new Event("dnd2024-view-invalidated")));
    await settle(() => document.body.textContent?.includes("Updated contribution") === true);
    assert.equal(view.control.contentCalls, 2);
    assert.doesNotMatch(document.body.textContent!, /First contribution/);
  } finally { await view.cleanup(); }
});

test("connected Rules clears previously confirmed text after a denied refresh", async () => {
  const view = await mount("rules");
  try {
    await settle(() => document.body.textContent?.includes("First reference") === true);
    assert.equal(view.control.rulesCalls, 1);
    view.control.denied = true;
    await view.click("Refresh rules");
    await settle(() => document.body.textContent?.includes("not available for this audience") === true);
    assert.equal(view.store.getState().references.rules, null, "Denied results must clear the confirmed owner, not just hide text.");
    assert.doesNotMatch(document.body.textContent!, /First reference/);
    assert.equal(view.control.rulesCalls, 2, "Authorization failures are not retried as connection failures.");
    assert.match(document.body.textContent!, /audience|access|permission|not available/i);
  } finally { await view.cleanup(); }
});

test("connected Rules queues a current-scope reread when invalidation cancels its first flight", async () => {
  const view = await mount("rules", { holdRules: true });
  try {
    await settle(() => view.control.rulesCalls === 1);
    view.control.holdRules = false;
    view.control.title = "Recovered reference";
    await act(async () => window.dispatchEvent(new Event("dnd2024-view-invalidated")));
    await settle(() => document.body.textContent?.includes("Recovered reference") === true);
    assert.equal(view.control.rulesCalls, 2);
  } finally { await view.cleanup(); }
});

test("denied content continuation clears its local page chain without automatic cursor retry", async () => {
  const view = await mount("content", { nextContentCursor: "next-page" });
  try {
    await settle(() => document.body.textContent?.includes("First contribution") === true);
    view.control.denied = true;
    await view.click("Load more (1 of 1)");
    await settle(() => document.body.textContent?.includes("not available for this audience") === true);
    assert.equal(view.control.contentCalls, 2);
    assert.equal(Object.keys(view.store.getState().references.content).length, 0);
    assert.doesNotMatch(document.body.textContent!, /First contribution/);
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 50)); });
    assert.equal(view.control.contentCalls, 2, "A cleared continuation must not retry itself as a new base page.");
  } finally { await view.cleanup(); }
});

test("denied first content page retries once when the user reloads it", async () => {
  const view = await mount("content", { initialDenied: true });
  try {
    await settle(() => document.body.textContent?.includes("not available for this audience") === true);
    assert.equal(view.control.contentCalls, 1);
    view.control.denied = false;
    await view.click("Reload list");
    await settle(() => document.body.textContent?.includes("First contribution") === true);
    assert.equal(view.control.contentCalls, 2);
  } finally { await view.cleanup(); }
});

test("a stale continuation returns to the first page instead of retrying its old cursor", async () => {
  const view = await mount("content", { nextContentCursor: "next-page" });
  try {
    await settle(() => document.body.textContent?.includes("First contribution") === true);
    view.control.stale = true;
    await view.click("Load more (1 of 1)");
    await settle(() => document.body.textContent?.includes("changed while this page was loading") === true);
    assert.equal(view.control.contentCalls, 2);
    view.control.stale = false;
    await view.click("Reload list");
    await settle(() => document.body.textContent?.includes("First contribution") === true && view.control.contentCalls === 3);
  } finally { await view.cleanup(); }
});
