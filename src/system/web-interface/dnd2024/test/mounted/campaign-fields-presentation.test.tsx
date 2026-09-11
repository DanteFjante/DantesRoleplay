import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act } from "react";
import { createRoot } from "react-dom/client";

import { CampaignView } from "../../src/components/CampaignView";
import { WorldCampaignSelector } from "../../src/components/WorldCampaignSelector";
import type { CampaignReadModel } from "../../src/data/hub-types";

function campaign(overrides: Partial<CampaignReadModel> = {}): CampaignReadModel {
  return {
    title: "The Fixture Campaign", subtitle: "Connected live campaign", status: "Active",
    chapter: "Chapter title retained", question: "Active chapter question unavailable.",
    premise: "A bounded premise.", progress: "Campaign structure unavailable.",
    objective: "Objective unavailable.", stakes: "Stakes unavailable.", nextMilestone: "Milestone unavailable.",
    facts: [], adventureLog: [], placesVisited: [], outcomes: [], mapOverlays: [], quests: [], threads: [], clues: [],
    ...overrides,
  };
}

async function mount(element: React.ReactElement) {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", { url: "https://table.example.test/" });
  const previous = { document: globalThis.document, window: globalThis.window, HTMLElement: globalThis.HTMLElement,
    Element: globalThis.Element, Node: globalThis.Node, Event: globalThis.Event };
  Object.assign(globalThis, { document: dom.window.document, window: dom.window, HTMLElement: dom.window.HTMLElement,
    Element: dom.window.Element, Node: dom.window.Node, Event: dom.window.Event });
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const root = createRoot(dom.window.document.querySelector("#root")!);
  await act(async () => { root.render(element); });
  return { dom, root, container: dom.window.document.querySelector("#root")!, cleanup: async () => {
    await act(async () => root.unmount());
    Object.assign(globalThis, previous);
    delete globalThis.IS_REACT_ACT_ENVIRONMENT;
    dom.window.close();
  } };
}

const callbacks = {
  onOpenFaction: () => {}, onOpenLocation: () => {}, onOpenPerson: () => {}, onSectionChange: () => {},
};

test("Campaign distinguishes unloaded and partial clues from confirmed empty clues", async () => {
  for (const coverage of ["unavailable", "partial", "complete"] as const) {
    for (const section of ["overview", "clues"] as const) {
      const view = await mount(<CampaignView campaign={campaign({ cluesCoverage: coverage })}
        detailsError="" detailsStatus="ready" hasValidatedDetails section={section}
        worldName="Fixture World" {...callbacks} />);
      try {
        const text = view.container.textContent ?? "";
        if (coverage === "complete") assert.match(text, section === "clues"
          ? /No clues available yet/ : /No campaign-owned clues are recorded/);
        else {
          assert.doesNotMatch(text, /No campaign-owned clues are recorded|No campaign clues recorded yet/);
          assert.match(text, /not (?:a )?confirmed (?:absent|empty)|not been loaded/);
        }
      } finally { await view.cleanup(); }
    }
  }
});

test("partial context directory keeps confirmed choices and explains missing records", async () => {
  const view = await mount(<WorldCampaignSelector busy={false} onCampaignChange={() => {}}
    selection={{ selectedWorldId: "world.fixture", selectedCampaignId: "campaign.fixture", coverage: "partial",
      worlds: [{ id: "world.fixture", name: "Fixture World", campaigns: [
        { id: "campaign.fixture", name: "Useful campaign name" },
      ] }] }} />);
  try {
    await act(async () => { view.container.querySelector<HTMLButtonElement>(".world-context__trigger")!.click(); });
    assert.match(view.container.querySelector("[role=dialog]")?.textContent ?? "", /Useful campaign name/);
    assert.match(view.container.querySelector("[role=status]")?.textContent ?? "", /Some directory records or names are unavailable/);
    assert.equal(view.container.querySelectorAll(".context-picker__campaign").length, 1);
  } finally { await view.cleanup(); }
});

test("Campaign overview retains a chapter title when its question facet is unavailable", async () => {
  const view = await mount(<CampaignView campaign={campaign({
    detailFields: { chapters: "partial", arcs: "empty", sessions: "empty", visits: "empty" },
  })} detailsError="" detailsStatus="ready" hasValidatedDetails section="overview" worldName="Fixture World" {...callbacks} />);
  try {
    assert.match(view.container.textContent ?? "", /Chapter title retained/);
    assert.match(view.container.textContent ?? "", /Some campaign details are unavailable/);
  } finally { await view.cleanup(); }
});

test("partial Campaign collections display coverage without claiming an empty record set", async () => {
  const view = await mount(<CampaignView campaign={campaign({
    detailFields: { chapters: "partial", arcs: "partial", sessions: "partial", visits: "partial" },
  })} detailsError="" detailsStatus="ready" hasValidatedDetails section="places" worldName="Fixture World" {...callbacks} />);
  try {
    assert.match(view.container.textContent ?? "", /Some campaign details are unavailable/);
    assert.match(view.container.textContent ?? "", /Campaign visits unavailable/);
    assert.doesNotMatch(view.container.textContent ?? "", /No campaign visits recorded yet/);
  } finally { await view.cleanup(); }
});

test("initial deferred Campaign absence remains loading rather than becoming an empty claim", async () => {
  const view = await mount(<CampaignView campaign={campaign()} detailsError="" detailsStatus="unloaded" hasValidatedDetails={false}
    section="places" worldName="Fixture World" {...callbacks} />);
  try {
    assert.match(view.container.textContent ?? "", /Loading campaign details/);
    assert.doesNotMatch(view.container.textContent ?? "", /No campaign visits recorded yet|No completed campaign memories yet/);
  } finally { await view.cleanup(); }
});
