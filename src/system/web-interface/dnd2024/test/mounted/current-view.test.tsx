import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, type ReactNode } from "react";

import { CurrentViewPreview } from "../../src/components/PreviewViews";
import {
  allocateCurrentRequestToken,
  commitCurrentBootstrap,
  createHubStore,
  currentActions,
  currentScope,
  selectCurrentDisplay,
  selectCurrentFresh,
} from "../../src/data/hub-store";
import { ViewReadError } from "../../src/data/view-read-client";
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
      if (scenario.situation.status === "ready" && scenario.situation.kind === "exploration") {
        assert.equal([...mounted.container.querySelectorAll("h1, h2")].filter(node => node.textContent === location.name).length, 1);
        assert.equal([...mounted.container.querySelectorAll("p")].filter(node => node.textContent === location.description).length, 1);
        assert.equal(mounted.container.querySelector(".current-scene-card h1")?.textContent, location.name);
        assert.equal(mounted.container.querySelector(".current-scene-card__copy > p")?.textContent, location.description);
      }
      if (scenario.absent) assert.doesNotMatch(mounted.container.textContent ?? "", scenario.absent);
      assert.equal(mounted.container.querySelector("application-conversation"), null);
      assert.equal(mounted.container.querySelector(".current-scene-card__visual img"), null,
        "a missing illustration does not reserve an empty image region");
    } finally { await mounted.cleanup(); }
  });
});

test("Current tactical board warns when omitted geometry is not confirmed clear and preserves valid participants", async () => {
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const partialBoard: TacticalEncounterBoard = { ...board, coverage: "partial",
    notices: ["Terrain data is incomplete; uninspected areas may contain terrain."] };
  const mounted = await mount(<CurrentViewPreview image={null} location={location} perspective="dm"
    situation={{ status: "ready", kind: "combat", locationId: location.id, combat: {
      id: "encounter.partial", name: "Partial clash", board: partialBoard,
      participants: [{ id: "participant.guard", name: "Gate Guard", initiative: 14, active: true }],
      turn: { id: "turn.one", participationId: "participant.guard", actorName: "Gate Guard", ordinal: 1,
        budget: { actions: 1, bonusActions: 1, reactions: 1 } },
    } }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Board information is partial[\s\S]*uninspected areas may contain terrain/u);
    assert.match(mounted.container.textContent ?? "", /Gate Guard/u);
    assert.match(mounted.container.textContent ?? "", /Terrain information is incomplete; omitted areas are not confirmed clear/u);
  } finally { await mounted.cleanup(); }
});

test("Current keeps the location illustration, heading and description together inside one card", async () => {
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const image = { imageUrl: "/api/applications/fixture/location/media/setting/content",
    alt: "The current settlement", width: 800, height: 600 };
  const mounted = await mount(<CurrentViewPreview image={image} location={location}
    situation={{ status: "ready", kind: "exploration", locationId: location.id }} />);
  try {
    const card = mounted.container.querySelector(".current-scene-card");
    assert.equal(card?.querySelector("img")?.getAttribute("src"), image.imageUrl);
    assert.equal(card?.querySelector("h1")?.textContent, location.name);
    assert.equal(card?.querySelector(".current-scene-card__copy > p")?.textContent, location.description);
    assert.equal(mounted.container.querySelectorAll("h1").length, 1);
    assert.equal([...mounted.container.querySelectorAll("p")]
      .filter((node) => node.textContent === location.description).length, 1);
  } finally { await mounted.cleanup(); }
});

test("Current keeps conversation identity when its location is unavailable", async () => {
  const mounted = await mount(<CurrentViewPreview image={null} location={null} perspective="dm"
    situation={{ status: "ready", kind: "conversation", locationId: "location.hidden", affordances: [], conversation: {
      id: "conversation.known", name: "Known conversation", participants: [],
    } }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Known conversation/u);
    assert.match(mounted.container.textContent ?? "", /Current location unavailable/u);
    assert.doesNotMatch(mounted.container.textContent ?? "", /Where you are/u);
  } finally { await mounted.cleanup(); }
});

test("Current renders sparse location fields and distinguishes unavailable routes from empty routes", async () => {
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const sparseLocation = { ...location } as typeof location;
  delete (sparseLocation as Partial<typeof location>).description;
  delete (sparseLocation as Partial<typeof location>).observations;
  delete (sparseLocation as Partial<typeof location>).people;
  delete (sparseLocation as Partial<typeof location>).routes;
  const mounted = await mount(<CurrentViewPreview image={null} location={sparseLocation} perspective="dm"
    situation={{ status: "ready", kind: "exploration", locationId: location.id, affordances: [],
      unavailableFields: ["affordances"], routesCoverage: "unavailable" }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Location description is unavailable/);
    assert.match(mounted.container.textContent ?? "", /Known ways onward are unavailable for this read/);
    assert.match(mounted.container.textContent ?? "", /Some scene affordances are unavailable for this read/);
    assert.match(mounted.container.textContent ?? "", /0 ways shown/);
    assert.match(mounted.container.textContent ?? "", /0 people shown/);
    assert.doesNotMatch(mounted.container.textContent ?? "", /0 known ways/);
    assert.doesNotMatch(mounted.container.textContent ?? "", /No known exits have been projected/);
  } finally { await mounted.cleanup(); }
});

test("Current retains known routes alongside partial route coverage", async () => {
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const mounted = await mount(<CurrentViewPreview image={null} location={location} perspective="dm"
    situation={{ status: "ready", kind: "exploration", locationId: location.id, affordances: [], routesCoverage: "partial" }} />);
  try {
    assert.match(mounted.container.textContent ?? "", new RegExp(location.routes[0].destination, "u"));
    assert.match(mounted.container.textContent ?? "", /Known ways onward are partially available/u);
  } finally { await mounted.cleanup(); }
});

test("Current preserves affordance identity when its descriptive summary is unavailable", async () => {
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const mounted = await mount(<CurrentViewPreview image={null} location={location} perspective="dm"
    situation={{ status: "ready", kind: "exploration", locationId: location.id,
      affordances: [{ key: "inspect", label: "Inspect" }], unavailableFields: ["affordances"] }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Inspect/u);
    assert.match(mounted.container.textContent ?? "", /Affordance details unavailable/u);
    assert.match(mounted.container.textContent ?? "", /Some scene affordances are unavailable/u);
  } finally { await mounted.cleanup(); }
});

test("Current recorded history distinguishes unavailable participants and dialogue from confirmed empty fields", async () => {
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const mounted = await mount(<CurrentViewPreview image={null} location={location} perspective="dm"
    situation={{ status: "ready", kind: "recorded", locationId: location.id, coverage: "partial",
      unavailableFields: ["participants", "interactions"], recorded: {
        id: "play.partial", kind: "conversation", summary: "Summary remains readable.",
        participants: [], interactions: [], location: { id: location.id, name: location.name },
      } }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Summary remains readable/u);
    assert.match(mounted.container.textContent ?? "", /Participant identities are unavailable for this read/u);
    assert.match(mounted.container.textContent ?? "", /Recorded dialogue is unavailable for this read/u);
    assert.doesNotMatch(mounted.container.textContent ?? "", /No participant identities were recorded/u);
    assert.doesNotMatch(mounted.container.textContent ?? "", /No recorded dialogue is available yet/u);
  } finally { await mounted.cleanup(); }
});

test("Current keeps useful rows while visibly marking withheld siblings", async () => {
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const mounted = await mount(<CurrentViewPreview image={null} location={location} perspective="dm"
    situation={{ status: "ready", kind: "recorded", locationId: location.id, coverage: "partial",
      unavailableFields: ["participants", "interactions"], recorded: {
        id: "play.partial-rows", kind: "conversation", participants: [{ id: "actor.keep", name: "Kept" }],
        interactions: [{ id: "message.keep", role: "player", text: "Still visible." }],
      } }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /Kept/u);
    assert.match(mounted.container.textContent ?? "", /Still visible/u);
    assert.match(mounted.container.textContent ?? "", /Some participant identities are unavailable for this read/u);
    assert.match(mounted.container.textContent ?? "", /Some recorded dialogue is unavailable for this read/u);
  } finally { await mounted.cleanup(); }
});

test("Current bootstrap and deferred fixtures project bad optional rows without crashing the Dnd hub", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const rawSituation = {
    status: "ready", kind: "recorded", recorded: {
      id: "play.sparse", kind: "conversation",
      participants: [{ id: "actor.keep", name: "Kept" }, { id: "actor.keep", name: "Duplicate" }, { id: "actor.unnamed" }],
      interactions: [{ id: "message.keep", role: "player", text: "Useful", ordinal: 1 }, { id: "message.bad", role: "other" }],
    },
  };
  initial.currentSituation = rawSituation as unknown as CurrentSituationReadModel;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async (_source, section) => {
      assert.equal(section, "current");
      return { section: "current", currentSituation: rawSituation as unknown as CurrentSituationReadModel,
        world: { currentLocationId: initial.world.currentLocationId, locations: initial.world.locations },
        campaign: { mapOverlays: initial.campaign.mapOverlays } };
    }} />);
  try {
    await click(button(mounted.container, "Current View"));
    await flush();
    assert.match(mounted.container.textContent ?? "", /Recorded situation summary unavailable/u);
    assert.match(mounted.container.textContent ?? "", /Unnamed participant/u);
    assert.match(mounted.container.textContent ?? "", /Useful/u);
    assert.doesNotMatch(mounted.container.textContent ?? "", /Duplicate/u);
    assert.doesNotMatch(mounted.container.textContent ?? "", /message.bad/u);
  } finally { await mounted.cleanup(); }
});

test("an unusable optional Current bootstrap stays local to the Current workspace", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const unusable = { status: "ready", kind: "future-scene", locationId: initial.world.currentLocationId };
  initial.currentSituation = unusable as unknown as CurrentSituationReadModel;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async (_source, section) => {
      assert.equal(section, "current");
      return { section: "current", currentSituation: unusable as unknown as CurrentSituationReadModel,
        world: { currentLocationId: initial.world.currentLocationId, locations: initial.world.locations },
        campaign: { mapOverlays: initial.campaign.mapOverlays } };
    }} />);
  try {
    assert.match(mounted.container.textContent ?? "", /World view ready/u);
    await click(button(mounted.container, "Current View"));
    await flush();
    assert.match(mounted.container.textContent ?? "", /Current view unavailable/u);
  } finally { await mounted.cleanup(); }
});

test("Current admits a scene with no campaign wrapper or World fallback location", async () => {
  const { normalizeCurrentViewUpdate } = await import("../../src/data/current-deferred-projection");
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const update = await normalizeCurrentViewUpdate({
    section: "current",
    currentSituation: { status: "ready", kind: "exploration", locationId: location.id, affordances: [] },
    // The World wrapper intentionally has no currentLocationId and there is no
    // unrelated campaign/map overlay payload to admit before rendering Current.
    world: { locations: [location] },
  });
  assert.equal(update?.currentSituation.status, "ready");
  assert.deepEqual(update?.world.locations.map((candidate) => candidate.id), [location.id]);
  assert.equal("campaign" in (update ?? {}), false);
});

test("the standalone Current bootstrap adapter preserves stale data without reseeding it", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  initial.currentSituation = { status: "ready", kind: "exploration", locationId: location.id, affordances: [] };
  const store = createHubStore();
  const scope = currentScope(initial);
  const mounted = await mount(<DndInformationHub store={store} initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }} />);
  try {
    await flush();
    assert.equal(selectCurrentDisplay(scope)(store.getState())?.situation.status, "ready");
    await act(async () => store.dispatch(currentActions.invalidated()));
    await flush();
    assert.equal(selectCurrentDisplay(scope)(store.getState())?.situation.status, "ready",
      "recoverable invalidation keeps the last authorized scene visible");
    assert.equal(selectCurrentFresh(scope)(store.getState()), false,
      "the retained scene cannot satisfy a fresh cache read");

    const requestToken = allocateCurrentRequestToken();
    await act(async () => {
      store.dispatch(currentActions.currentRequestStarted({ scope, requestToken }));
      store.dispatch(currentActions.currentDenied({ scope, requestToken }));
    });
    await flush();
    assert.equal(selectCurrentDisplay(scope)(store.getState()), null,
      "a denial remains authoritative until a new bootstrap/read commits");

    const newerToken = allocateCurrentRequestToken();
    await act(async () => store.dispatch(commitCurrentBootstrap({ scope, value: {
      situation: { status: "ready", kind: "exploration", locationId: location.id, affordances: [] },
      location,
    } }, { generation: store.getState().current.generation, requestToken: newerToken,
      bytes: 512, confirmedAt: 1 })));
    assert.equal(selectCurrentDisplay(scope)(store.getState())?.situation.status, "ready",
      "a distinct authoritative bootstrap may seed Current after a denial");
  } finally { await mounted.cleanup(); }
});

test("Current invalidation retries once and exposes a bounded retry when its source is not ready", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const first = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
  const second = initial.world.locations.find((candidate) => candidate.id === "hollow-beacon")!;
  const store = createHubStore();
  let reads = 0;
  const read = async (): Promise<DeferredHubUpdate> => {
    reads += 1;
    if (reads === 2) throw new ViewReadError("transport", "Current source is reconnecting.");
    const location = reads === 1 ? first : second;
    return { section: "current", currentSituation: {
      status: "ready", kind: "exploration", locationId: location.id, affordances: [],
    }, world: { locations: initial.world.locations } };
  };
  const mounted = await mount(<DndInformationHub store={store} currentBootstrapManaged initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async (_source, section) => {
      assert.equal(section, "current");
      return read();
    }} />);
  try {
    await click(button(mounted.container, "Current View"));
    await flush();
    assert.equal(reads, 1);
    assert.match(mounted.container.textContent ?? "", new RegExp(first.name, "u"));

    await act(async () => {
      store.dispatch(currentActions.invalidated());
      window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", {
        detail: { reason: "object-change" },
      }));
    });
    await flush();
    assert.equal(reads, 2, "a stale canonical Current generation gets one revalidation");
    assert.match(mounted.container.textContent ?? "", /Current scene could not be refreshed/u);
    assert.match(mounted.container.textContent ?? "", new RegExp(first.name, "u"));
    assert.ok(button(mounted.container, "Retry current scene"));
    await flush();
    assert.equal(reads, 2, "a failed revalidation waits for the explicit retry");

    await click(button(mounted.container, "Retry current scene"));
    await flush();
    assert.equal(reads, 3);
    assert.match(mounted.container.textContent ?? "", new RegExp(second.name, "u"));
  } finally { await mounted.cleanup(); }
});

test("an authoritative unavailable Current completion does not retry itself", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope();
  const store = createHubStore();
  let reads = 0;
  const mounted = await mount(<DndInformationHub store={store} currentBootstrapManaged initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async () => {
      reads += 1;
      return { section: "current", currentSituation: {
        status: "unavailable", message: "No play has been authorized.",
      }, world: { locations: [] } } as DeferredHubUpdate;
    }} />);
  try {
    await click(button(mounted.container, "Current View"));
    await flush();
    await flush();
    assert.equal(reads, 1, "a confirmed unavailable scene is a completed read, not a retry trigger");
    assert.doesNotMatch(mounted.container.textContent ?? "", /Retry view/u);
  } finally { await mounted.cleanup(); }
});

test("the DM game hub shows combat read-only without map upload or acceptance controls", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  for (const acceptedBoard of [board, undefined]) {
    const initial = envelope();
    const location = initial.world.locations.find((candidate) => candidate.id === initial.world.currentLocationId)!;
    initial.currentSituation = { status: "ready", kind: "combat", locationId: location.id, combat: {
      id: "encounter.readonly", name: "Read-only clash", board: acceptedBoard,
      participants: [{ id: "participant.guard", name: "Gate Guard", initiative: 14, active: true }],
    } };
    let directRequests = 0;
    const previousFetch = globalThis.fetch;
    const mounted = await mount(<DndInformationHub initialEnvelope={initial}
      loadContent={async () => { throw new Error("not used"); }}
      loadDeferredSection={async (_source, section) => {
        assert.equal(section, "current");
        return { section: "current", currentSituation: initial.currentSituation!, world: {
          currentLocationId: location.id, locations: initial.world.locations,
        }, campaign: { mapOverlays: initial.campaign.mapOverlays } };
      }} />);
    try {
      globalThis.fetch = async () => { directRequests += 1; throw new Error("Unexpected direct request"); };
      await click(button(mounted.container, "Current View"));
      await flush();
      assert.match(mounted.container.textContent ?? "", /Read-only clash[\s\S]*Initiative[\s\S]*Gate Guard/u);
      assert.ok(mounted.container.querySelector(".tactical-board-svg"), "the existing board or honest placeholder remains visible");
      assert.equal(mounted.container.querySelector('.board-draft-workshop, input[type="file"], textarea'), null);
      assert.doesNotMatch(mounted.container.textContent ?? "", /Review acceptance|Accept reviewed board|GM combat-map workshop/u);
      for (const control of mounted.container.querySelectorAll<HTMLButtonElement>("button")) {
        if (control.textContent === "Generate combat map") assert.equal(control.disabled, true);
      }
      await click(button(mounted.container, "World"));
      await click(button(mounted.container, "Current View"));
      assert.equal(mounted.container.querySelector(".board-draft-workshop"), null);
      assert.equal(directRequests, 0, "Current cannot upload, prepare or execute a board mutation");
    } finally {
      globalThis.fetch = previousFetch;
      await mounted.cleanup();
    }
  }
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
  const cachePreferences: Array<boolean | undefined> = [];
  let finishRetry: (update: DeferredHubUpdate) => void = () => { throw new Error("Retry not started"); };
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => { throw new Error("not used"); }}
    loadDeferredSection={async (_source, section, _signal, preferCached) => {
      assert.equal(section, "current");
      cachePreferences.push(preferCached);
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
    assert.deepEqual(cachePreferences, [true, false], "explicit retry must bypass the Current owner's completed value");
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
