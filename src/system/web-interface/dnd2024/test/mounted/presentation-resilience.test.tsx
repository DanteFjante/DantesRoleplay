import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, useState } from "react";
import { createRoot } from "react-dom/client";

import { MainNavigation } from "../../src/components/MainNavigation";
import { PanelErrorBoundary } from "../../src/components/PanelState";
import { SystemControls } from "../../src/components/SystemControls";
import { TopBar } from "../../src/components/TopBar";
import { WorldFactions } from "../../src/components/WorldFactions";
import { WorldLore } from "../../src/components/WorldLore";
import { WorldOverview } from "../../src/components/WorldOverview";
import type { CampaignReadModel, MainTabId, WorldReadModel } from "../../src/data/hub-types";

async function mount(element: React.ReactElement) {
  const dom = new JSDOM("<!doctype html><html><body><div id='root'></div></body></html>", {
    url: "https://table.example.test/ui/dnd2024-play",
    pretendToBeVisual: true,
  });
  const previous = {
    document: globalThis.document, window: globalThis.window, HTMLElement: globalThis.HTMLElement,
    Element: globalThis.Element, Node: globalThis.Node, Event: globalThis.Event,
    KeyboardEvent: globalThis.KeyboardEvent, MouseEvent: globalThis.MouseEvent,
  };
  Object.assign(globalThis, {
    document: dom.window.document, window: dom.window, HTMLElement: dom.window.HTMLElement,
    Element: dom.window.Element, Node: dom.window.Node, Event: dom.window.Event,
    KeyboardEvent: dom.window.KeyboardEvent, MouseEvent: dom.window.MouseEvent,
  });
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const root = createRoot(dom.window.document.querySelector("#root")!);
  await act(async () => root.render(element));
  return {
    container: dom.window.document.querySelector("#root")!,
    async render(next: React.ReactElement) { await act(async () => root.render(next)); },
    async cleanup() {
      await act(async () => root.unmount());
      Object.assign(globalThis, previous);
      delete globalThis.IS_REACT_ACT_ENVIRONMENT;
      dom.window.close();
    },
  };
}

const worldBase = {
  id: "world.fixture", name: "Fixture World", factions: [], lore: [],
  factionDirectory: { totalCount: 0, complete: true, nextCursor: null, sourceRevisionFingerprint: null },
} as unknown as WorldReadModel;

test("Lore and Factions accept extra JSON and isolate malformed records", async () => {
  const lore = [{
    id: "lore.good", title: "The bronze bell", summary: "A useful fact.",
    futureDisplayField: { nested: true },
  }, { title: "Missing identity must stay local" }];
  const factions = [{
    id: "faction.good", name: "Lantern Watch", summary: "Keeps the quay.",
    futureDisplayField: ["ignored"],
  }, { name: "Missing identity must stay local" }];
  const view = await mount(<div>
    <WorldLore world={{ ...worldBase, lore } as unknown as WorldReadModel}
      onOpenFaction={() => {}} onOpenHistory={() => {}} onOpenLocation={() => {}} />
    <WorldFactions world={{ ...worldBase, factions, factionDirectory: {
      totalCount: 2, complete: true, nextCursor: null, sourceRevisionFingerprint: null,
    } } as unknown as WorldReadModel} selectedFactionId="" onFactionSelect={() => {}} onOpenLocation={() => {}} />
  </div>);
  try {
    assert.match(view.container.textContent ?? "", /The bronze bell/);
    assert.match(view.container.textContent ?? "", /Lantern Watch/);
    assert.match(view.container.textContent ?? "", /Details unavailable/);
    assert.match(view.container.textContent ?? "", /Some faction records or details are unavailable/);
    assert.doesNotMatch(view.container.textContent ?? "", /Missing identity must stay local/);
  } finally { await view.cleanup(); }
});

test("a missing overview collection leaves its sibling panels usable", async () => {
  const world = {
    ...worldBase,
    era: "The present age", summary: "A persistent world.", premise: "Choices leave marks.",
    facts: [{ label: "Known", value: "Present", unexpected: true }, { detail: "missing label" }],
    regions: undefined,
  } as unknown as WorldReadModel;
  const campaign = { title: "Fixture Campaign" } as CampaignReadModel;
  const view = await mount(<WorldOverview world={world} campaign={campaign} currentLocation={null}
    onBrowseLocations={() => {}} />);
  try {
    assert.match(view.container.textContent ?? "", /A persistent world/);
    assert.match(view.container.textContent ?? "", /World premise/);
    assert.match(view.container.textContent ?? "", /Where the party is now/);
    assert.match(view.container.textContent ?? "", /Known/);
  } finally { await view.cleanup(); }
});

test("panel failure stays local and a new scope clears the failed state", async () => {
  function Broken({ fail }: { fail: boolean }) {
    if (fail) throw new Error("fixture panel failure");
    return <p>Replacement scope is readable</p>;
  }
  const originalConsoleError = console.error;
  console.error = () => {};
  const view = await mount(<div><p>Sibling survives</p><PanelErrorBoundary label="Lore" resetKey="scope-a">
    <Broken fail /></PanelErrorBoundary></div>);
  try {
    assert.match(view.container.textContent ?? "", /Sibling survives/);
    assert.match(view.container.textContent ?? "", /Lore could not be displayed/);
    await view.render(<div><p>Sibling survives</p><PanelErrorBoundary label="Lore" resetKey="scope-b">
      <Broken fail={false} /></PanelErrorBoundary></div>);
    assert.match(view.container.textContent ?? "", /Replacement scope is readable/);
    assert.doesNotMatch(view.container.textContent ?? "", /could not be displayed/);
  } finally { console.error = originalConsoleError; await view.cleanup(); }
});

test("DND navigation supports roving keyboard focus and a mobile select", async () => {
  function Harness() {
    const [active, setActive] = useState<MainTabId>("world");
    return <MainNavigation activeTab={active} chapter="Fixture chapter" onSelect={setActive} />;
  }
  const view = await mount(<Harness />);
  try {
    const world = [...view.container.querySelectorAll("button")].find((item) => item.textContent?.includes("World"))!;
    const campaign = [...view.container.querySelectorAll("button")].find((item) => item.textContent?.includes("Campaign"))!;
    (world as HTMLElement).focus();
    await act(async () => world.dispatchEvent(new window.KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })));
    assert.equal(document.activeElement, campaign);
    await act(async () => campaign.dispatchEvent(new window.MouseEvent("click", { bubbles: true })));
    assert.equal(campaign.getAttribute("aria-current"), "page");
    const mobileSelect = view.container.querySelector(".main-nav__mobile-label select") as HTMLSelectElement;
    assert.equal(mobileSelect.getAttribute("aria-label"), "D&D table view");
    assert.equal(mobileSelect.value, "campaign");
  } finally { await view.cleanup(); }
});

test("the DND shell consumes one shared navigation and theme owner", async () => {
  const view = await mount(<SystemControls />);
  try {
    assert.equal(view.container.querySelector("system-navigation")?.getAttribute("application-id"), "dnd2024");
    assert.equal(view.container.querySelectorAll("system-theme-toggle").length, 0,
      "system-navigation owns the single shared theme control");
  } finally { await view.cleanup(); }
});

test("the shared owner header exposes only the server-authorized perspective controls", async () => {
  const view = await mount(<TopBar
    allowedPerspectives={["dm", "player"]}
    busy={false}
    contextSelection={{
      selectedWorldId: "world.fixture",
      selectedCampaignId: "campaign.fixture",
      worlds: [{ id: "world.fixture", name: "Fixture world", campaigns: [
        { id: "campaign.fixture", name: "Fixture campaign" },
      ] }],
    }}
    onCampaignChange={() => undefined}
    onPerspectiveChange={() => undefined}
    perspective="dm"
    sharedAccess
  />);
  try {
    assert.ok(view.container.querySelector(".perspective-switch"));
    assert.deepEqual([...view.container.querySelectorAll(".perspective-switch button")]
      .map((button) => button.textContent), ["DM", "Player"]);
    assert.equal(view.container.querySelectorAll("system-navigation").length, 1);
    assert.equal(view.container.querySelectorAll("system-theme-toggle").length, 0);
  } finally { await view.cleanup(); }
});

test("a player-only shared entry cannot render a DM selector", async () => {
  const view = await mount(<TopBar
    allowedPerspectives={["player"]}
    busy={false}
    contextSelection={{
      selectedWorldId: "world.fixture",
      selectedCampaignId: "campaign.fixture",
      worlds: [{ id: "world.fixture", name: "Fixture world", campaigns: [
        { id: "campaign.fixture", name: "Fixture campaign" },
      ] }],
    }}
    onCampaignChange={() => undefined}
    onPerspectiveChange={() => undefined}
    perspective="player"
    sharedAccess
  />);
  try {
    assert.equal(view.container.querySelector(".perspective-switch"), null);
    assert.match(view.container.textContent ?? "", /Player view/u);
    assert.equal(view.container.querySelector("system-navigation")?.getAttribute("access-mode"), "player");
  } finally { await view.cleanup(); }
});
