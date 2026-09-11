import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act } from "react";
import { createRoot } from "react-dom/client";

import { WorldFactions } from "../../src/components/WorldFactions";
import type { WorldFaction, WorldReadModel } from "../../src/data/hub-types";

async function mount(element: React.ReactElement) {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", {
    url: "https://table.example.test/",
  });
  const previous = {
    document: globalThis.document, window: globalThis.window, HTMLElement: globalThis.HTMLElement,
    Element: globalThis.Element, Node: globalThis.Node, Event: globalThis.Event,
  };
  Object.assign(globalThis, {
    document: dom.window.document, window: dom.window, HTMLElement: dom.window.HTMLElement,
    Element: dom.window.Element, Node: dom.window.Node, Event: dom.window.Event,
  });
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const root = createRoot(dom.window.document.querySelector("#root")!);
  await act(async () => root.render(element));
  return {
    container: dom.window.document.querySelector("#root")!,
    async cleanup() {
      await act(async () => root.unmount());
      Object.assign(globalThis, previous);
      delete globalThis.IS_REACT_ACT_ENVIRONMENT;
      dom.window.close();
    },
  };
}

const faction: WorldFaction = {
  id: "faction.fixture", monogram: "F", name: "Fixture Faction", influence: "Regional",
  status: "Active", summary: "A recorded power.", goals: [], methods: [], assets: [],
  members: [], territories: [], relationships: [], unavailableFields: ["summary"],
};
const world = {
  id: "world.fixture", name: "Fixture World", factions: [faction],
  factionDirectory: { totalCount: 2, complete: true, nextCursor: null, sourceRevisionFingerprint: null },
} as WorldReadModel;

test("Factions keeps usable rows and explains field or record gaps", async () => {
  const view = await mount(<WorldFactions world={world} selectedFactionId=""
    onFactionSelect={() => {}} onOpenLocation={() => {}} />);
  try {
    assert.match(view.container.textContent ?? "", /1 displayed from 2 source records/);
    assert.match(view.container.textContent ?? "", /Some faction records or details are unavailable/);
    assert.match(view.container.textContent ?? "", /Some details are unavailable: summary/);
    assert.doesNotMatch(view.container.textContent ?? "", /No factions match/);
  } finally { await view.cleanup(); }
});

test("Factions does not turn a partially admitted empty page into a confirmed empty claim", async () => {
  const view = await mount(<WorldFactions world={{ ...world, factions: [] }} selectedFactionId=""
    onFactionSelect={() => {}} onOpenLocation={() => {}} />);
  try {
    assert.match(view.container.textContent ?? "", /Faction records unavailable/);
    assert.doesNotMatch(view.container.textContent ?? "", /No factions match/);
  } finally { await view.cleanup(); }
});

test("Factions reports a malformed partial page before its continuation is loaded", async () => {
  const view = await mount(<WorldFactions world={{ ...world, factions: [], factionDirectory: {
    totalCount: 40, complete: false, coverage: "partial", nextCursor: "next", sourceRevisionFingerprint: null,
  } }} selectedFactionId="" onFactionSelect={() => {}} onOpenLocation={() => {}} />);
  try {
    assert.match(view.container.textContent ?? "", /Faction records unavailable/);
    assert.match(view.container.textContent ?? "", /Load more factions/);
    assert.doesNotMatch(view.container.textContent ?? "", /No factions match/);
  } finally { await view.cleanup(); }
});
