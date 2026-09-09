import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, type ReactNode } from "react";

import { CurrentViewPreview } from "../../src/components/PreviewViews";
import type {
  CurrentSituationReadModel,
  DeferredHubUpdate,
  Perspective,
  ReadyHubEnvelope,
  TacticalEncounterBoard,
} from "../../src/data/hub-types";
import { resolveAudience } from "../support/audience-policy.js";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { HUB_SOURCE_REVISION, hubSource } from "../support/hub-source.js";

async function mount(element: ReactNode) {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", {
    url: "https://table.example.test/",
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
    dom,
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

async function flush() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
}

async function click(control: HTMLButtonElement) {
  await act(async () => {
    control.dispatchEvent(new window.MouseEvent("click", { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function button(container: Element, label: string) {
  const match = [...container.querySelectorAll<HTMLButtonElement>("button")]
    .find((candidate) => candidate.textContent?.trim() === label);
  assert.ok(match, `Expected a ${label} button`);
  return match;
}

function envelope(perspective: Perspective = "dm"): ReadyHubEnvelope {
  const projected = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    resolveAudience({
      authenticatedUserId: "principal.dm.fixture",
      authenticatedUserEmail: "",
      requestedPerspective: perspective,
      dmPrincipalIds: ["principal.dm.fixture"],
    }),
  ) as ReadyHubEnvelope;
  return {
    ...projected,
    applicationId: "dnd2024-main",
    stateSpaceId: "campaign.fixture.eldervale",
    contextSelection: {
      selectedCampaignId: "campaign.fixture.eldervale",
      selectedWorldId: projected.world.id,
      worlds: [{
        id: projected.world.id,
        name: projected.world.name,
        campaigns: [{ id: "campaign.fixture.eldervale", name: projected.campaign.title }],
      }],
    },
  };
}

const board: TacticalEncounterBoard = {
  revision: 2,
  columns: 8,
  rows: 6,
  feetPerSquare: 5,
  terrain: [],
  obstacles: [],
  participants: [{
    id: "participant.guard",
    name: "Gate Guard",
    initiative: 14,
    active: true,
    position: { x: 2, y: 2, width: 1, height: 1, elevationFeet: 0, revision: 1 },
  }],
};

test("Current scene presentation covers exploration, recorded play, conversation, combat, and no-scene states", async (t) => {
  const source = envelope();
  const location = source.world.locations.find((candidate) => candidate.id === source.world.currentLocationId)!;
  const cases: Array<{
    name: string;
    situation: CurrentSituationReadModel;
    expected: RegExp;
    absent?: RegExp;
  }> = [
    {
      name: "exploration without an illustration or empty action panel",
      situation: { status: "ready", kind: "exploration", locationId: location.id, affordances: [] },
      expected: new RegExp(location.name, "u"),
      absent: /Available now|No scene actions/u,
    },
    {
      name: "recorded play remains readable history",
      situation: { status: "ready", kind: "recorded", locationId: location.id, recorded: {
        id: "play.recorded", kind: "conversation", summary: "The gatekeeper agreed to wait.",
        participants: [{ id: "actor.one", name: "Ganji" }],
        interactions: [{ id: "message.one", ordinal: 1, role: "assistant", text: "The gate opens at dawn." }],
        location: { id: location.id, name: location.name },
      } },
      expected: /The gatekeeper agreed to wait[\s\S]*Recent interactions[\s\S]*The gate opens at dawn/u,
    },
    {
      name: "existing conversation is a read-only scene",
      situation: { status: "ready", kind: "conversation", locationId: location.id,
        conversation: { id: "conversation.one", name: "Gatehouse parley", summary: "Terms are being weighed.",
          participants: [{ id: "actor.one", name: "Ganji" }] }, affordances: [] },
      expected: /Gatehouse parley[\s\S]*Visible participants[\s\S]*Ganji/u,
      absent: /Continue the story/u,
    },
    {
      name: "combat retains its board and active turn",
      situation: { status: "ready", kind: "combat", locationId: location.id, combat: {
        id: "encounter.one", name: "Gatehouse clash", board,
        participants: [{ id: "participant.guard", name: "Gate Guard", initiative: 14, active: true }],
        turn: { id: "turn.one", participationId: "participant.guard", actorName: "Gate Guard", ordinal: 1,
          budget: { actions: 1, bonusActions: 1, reactions: 1 } },
      } },
      expected: /Gatehouse clash[\s\S]*Initiative[\s\S]*Active turn[\s\S]*Gate Guard/u,
    },
    {
      name: "no scene remains explicit",
      situation: { status: "unavailable", message: "No scene has been recorded." },
      expected: /No current scene[\s\S]*No scene has been recorded[\s\S]*Where you are/u,
    },
  ];

  for (const scenario of cases) await t.test(scenario.name, async () => {
    const mounted = await mount(<CurrentViewPreview image={null}
      location={location}
      situation={scenario.situation} perspective="dm" />);
    try {
      assert.match(mounted.container.textContent ?? "", scenario.expected);
      if (scenario.absent) assert.doesNotMatch(mounted.container.textContent ?? "", scenario.absent);
      assert.equal(mounted.container.querySelector("application-conversation"), null);
      assert.equal(mounted.container.querySelector(".current-scene-card__visual img"), null,
        "a missing illustration does not reserve an empty image region");
    } finally { await mounted.cleanup(); }
  });
});

test("opening and revisiting Current mounts no conversation element and issues no implicit write", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  initial.currentSituation = { status: "ready", kind: "exploration", locationId: location.id, affordances: [] };
  let customElementConnections = 0;
  let currentReads = 0;
  let directRequests = 0;
  const previousFetch = globalThis.fetch;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async (_source, section) => {
      assert.equal(section, "current");
      currentReads += 1;
      return { section: "current", currentSituation: initial.currentSituation!, world: {
        currentLocationId: location.id, locations: initial.world.locations,
      }, campaign: { mapOverlays: initial.campaign.mapOverlays } };
    }} />);
  try {
    globalThis.fetch = async () => {
      directRequests += 1;
      return new Response(null, { status: 204 });
    };
    mounted.dom.window.customElements.define("application-conversation", class extends mounted.dom.window.HTMLElement {
      connectedCallback() { customElementConnections += 1; }
    });
    await click(button(mounted.container, "Current View"));
    await flush();
    assert.equal(currentReads, 1);
    assert.equal(customElementConnections, 0);
    assert.equal(mounted.container.querySelector("application-conversation, textarea"), null);
    await click(button(mounted.container, "World"));
    await click(button(mounted.container, "Current View"));
    assert.equal(currentReads, 1, "the fresh validated Current resource is reused on revisit");
    assert.equal(customElementConnections, 0);
    assert.equal(directRequests, 0, "Current uses only its injected read owner and starts no write-capable client");
  } finally {
    globalThis.fetch = previousFetch;
    await mounted.cleanup();
  }
});

test("a failed Current refresh preserves the confirmed scene and retry installs the changed scene", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const first = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const changed = initial.world.locations.find((candidate) => candidate.id === "hollow-beacon")!;
  initial.currentSituation = { status: "ready", kind: "exploration", locationId: first.id, affordances: [] };
  let attempt = 0;
  let finishRetry: (update: DeferredHubUpdate) => void = () => { throw new Error("Retry not started"); };
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async (_source, section) => {
      assert.equal(section, "current");
      attempt += 1;
      if (attempt === 1) throw new Error("Current projection is temporarily unavailable.");
      return new Promise<DeferredHubUpdate>((resolve) => { finishRetry = resolve; });
    }} />);
  try {
    await click(button(mounted.container, "Current View"));
    await flush();
    assert.match(mounted.container.textContent ?? "", new RegExp(first.name, "u"));
    assert.match(mounted.container.textContent ?? "", /Current scene could not be refreshed[\s\S]*Showing the last confirmed scene/u);
    await click(button(mounted.container, "Retry current scene"));
    assert.match(mounted.container.textContent ?? "", new RegExp(first.name, "u"));
    await act(async () => finishRetry({
      section: "current",
      currentSituation: { status: "ready", kind: "exploration", locationId: changed.id, affordances: [] },
      world: { currentLocationId: changed.id, locations: initial.world.locations },
      campaign: { mapOverlays: initial.campaign.mapOverlays },
    }));
    assert.match(mounted.container.textContent ?? "", new RegExp(changed.name, "u"));
    assert.doesNotMatch(mounted.container.textContent ?? "", /Current scene could not be refreshed/u);
  } finally { await mounted.cleanup(); }
});

test("a late Current response cannot replace the scene after its view was abandoned", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const first = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const late = initial.world.locations.find((candidate) => candidate.id === "hollow-beacon")!;
  initial.currentSituation = { status: "ready", kind: "exploration", locationId: first.id, affordances: [] };
  const pending: Array<(update: DeferredHubUpdate) => void> = [];
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async (_source, section) => {
      assert.equal(section, "current");
      return new Promise<DeferredHubUpdate>((resolve) => pending.push(resolve));
    }} />);
  try {
    await click(button(mounted.container, "Current View"));
    assert.equal(pending.length, 1);
    await click(button(mounted.container, "Campaign"));
    await act(async () => pending[0]({
      section: "current",
      currentSituation: { status: "ready", kind: "exploration", locationId: late.id, affordances: [] },
      world: { currentLocationId: late.id, locations: initial.world.locations },
      campaign: { mapOverlays: initial.campaign.mapOverlays },
    }));
    await click(button(mounted.container, "Current View"));
    assert.equal(pending.length, 2);
    assert.match(mounted.container.textContent ?? "", new RegExp(first.name, "u"));
    assert.doesNotMatch(mounted.container.textContent ?? "", new RegExp(late.name, "u"));
    await act(async () => pending[1]({
      section: "current",
      currentSituation: initial.currentSituation!,
      world: { currentLocationId: first.id, locations: initial.world.locations },
      campaign: { mapOverlays: initial.campaign.mapOverlays },
    }));
  } finally { await mounted.cleanup(); }
});
