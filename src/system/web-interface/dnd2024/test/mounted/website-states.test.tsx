import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, type ReactNode, useState } from "react";

import type {
  PartyMemberReadModel,
  MapDocument,
  Perspective,
  ReadyHubEnvelope,
  SectionState,
  TacticalEncounterBoard,
} from "../../src/data/hub-types";
import { resolveAudience } from "../../src/server/audience-policy.js";
import { projectHubEnvelope } from "../../src/server/hub-envelope.js";
import {
  DM_ONLY_BASE_CANARIES,
  DM_ONLY_LAYER_CANARIES,
  HIDDEN_MAP_CANARIES,
  HUB_SOURCE_REVISION,
  SECRET_CANARIES,
  hubSource,
} from "../../src/server/hub-source.js";
import {
  DEFAULT_MAP_VIEWPORT,
  MapCanvas,
  type MapViewportState,
} from "../../src/components/MapCanvas";
import { TacticalBoard } from "../../src/components/TacticalBoard";
import { PERFORMANCE_MARKS, resetPerformanceMarksForTests } from "../../src/observability/performance.js";

const dmPrincipal = "principal.dm.fixture";

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
  Object.defineProperty(globalThis, "navigator", {
    configurable: true,
    value: dom.window.navigator,
  });
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

function button(container: Element, label: string) {
  const match = [...container.querySelectorAll("button")]
    .find((candidate) => candidate.textContent?.trim() === label);
  assert.ok(match, `Expected a ${label} button`);
  return match as HTMLButtonElement;
}

async function click(control: HTMLButtonElement) {
  await act(async () => {
    control.dispatchEvent(new window.MouseEvent("click", { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

function pointerEvent(type: string, {
  clientX,
  clientY,
  pointerId,
  pointerType = "mouse",
}: {
  clientX: number;
  clientY: number;
  pointerId: number;
  pointerType?: string;
}) {
  const event = new window.Event(type, { bubbles: true, cancelable: true });
  Object.defineProperties(event, {
    clientX: { value: clientX },
    clientY: { value: clientY },
    pointerId: { value: pointerId },
    pointerType: { value: pointerType },
  });
  return event;
}

const mountedMap: MapDocument = {
  id: "map.mounted",
  scope: "region",
  parentMapId: null,
  subject: { kind: "region", id: "region.mounted", name: "Mounted Vale" },
  coordinateSpace: { id: "space.mounted", unit: "illustrative", width: 100, height: 100 },
  base: null,
  layers: [{ id: "layer.places", kind: "markers", order: 1, label: "Places" }],
  features: [{
    id: "feature.keep",
    kind: "point",
    layerId: "layer.places",
    coordinateSpaceId: "space.mounted",
    geometry: { x: 50, y: 50 },
    name: "Test Keep",
    detail: "A selected test place.",
    locationId: "location.keep",
  }],
  scopeLinks: [],
};

function MapHarness() {
  const [viewport, setViewport] = useState<MapViewportState>(DEFAULT_MAP_VIEWPORT);
  const [selectedFeatureId, setSelectedFeatureId] = useState("feature.keep");
  const [openedScope, setOpenedScope] = useState("");

  return (
    <div data-testid="long-page" style={{ height: "2400px", overflow: "auto" }}>
      <div style={{ marginTop: "700px" }}>
        <MapCanvas
          annotatedFeatureIds={new Set()}
          currentLocationId="location.keep"
          influencedFeatureIds={new Set()}
          map={mountedMap}
          onFeatureSelect={setSelectedFeatureId}
          onOpenScope={setOpenedScope}
          onViewportChange={setViewport}
          scopeLinkFeatureIds={new Map()}
          selectedFeatureId={selectedFeatureId}
          viewport={viewport}
        />
      </div>
      <output data-testid="selection">{selectedFeatureId || "none"}</output>
      <output data-testid="opened-scope">{openedScope || "none"}</output>
    </div>
  );
}

function setMapGeometry(container: Element) {
  const canvas = container.querySelector(".world-map-canvas") as HTMLDivElement;
  const stage = container.querySelector(".world-map-stage") as HTMLDivElement;
  assert.ok(canvas);
  assert.ok(stage);
  Object.defineProperties(canvas, {
    clientWidth: { configurable: true, value: 400 },
    clientHeight: { configurable: true, value: 300 },
    getBoundingClientRect: {
      configurable: true,
      value: () => ({
        left: 0,
        top: 0,
        right: 400,
        bottom: 300,
        width: 400,
        height: 300,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      }),
    },
  });
  Object.defineProperties(stage, {
    offsetWidth: { configurable: true, value: 800 },
    offsetHeight: { configurable: true, value: 600 },
  });
  const captured = new Set<number>();
  canvas.setPointerCapture = (pointerId) => captured.add(pointerId);
  canvas.hasPointerCapture = (pointerId) => captured.has(pointerId);
  canvas.releasePointerCapture = (pointerId) => captured.delete(pointerId);
  return { canvas, stage };
}

async function dispatch(target: Element, event: Event) {
  let allowed = false;
  await act(async () => {
    allowed = target.dispatchEvent(event);
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
  return allowed;
}

test("mounted map leaves wheel events uncancelled without changing zoom", async () => {
  const mounted = await mount(<MapHarness />);
  try {
    const { canvas } = setMapGeometry(mounted.container);
    const wheel = new window.WheelEvent("wheel", {
      bubbles: true,
      cancelable: true,
      deltaY: 280,
    });
    const browserDefaultAllowed = await dispatch(canvas, wheel);

    assert.equal(browserDefaultAllowed, true);
    assert.equal(wheel.defaultPrevented, false);
    assert.equal(mounted.container.querySelector("[aria-label='Current map zoom']")?.textContent, "100%");
  } finally {
    await mounted.cleanup();
  }
});

const mountedBoard: TacticalEncounterBoard = {
  revision: 4,
  columns: 12,
  rows: 8,
  feetPerSquare: 5,
  terrain: [{ id: "terrain.rubble", label: "Rubble", area: { x: 4, y: 1, width: 2, height: 2 }, movementCost: 2 }],
  obstacles: [{ id: "obstacle.wall", label: "Stone wall", area: { x: 8, y: 0, width: 1, height: 4 } }],
  participants: [{
    id: "actor.hero", name: "Hero", initiative: 17, active: true,
    position: { x: 2, y: 3, width: 1, height: 1, elevationFeet: 0, revision: 2 },
  }],
  turn: { id: "turn.1", participationId: "participation.hero", actorId: "actor.hero", actorName: "Hero", ordinal: 0 },
};

test("mounted tactical board leaves wheel events uncancelled and zooms only through buttons", async () => {
  const mounted = await mount(<div data-testid="board-page" style={{ height: "2400px", overflow: "auto" }}><TacticalBoard board={mountedBoard} /></div>);
  try {
    const viewport = mounted.container.querySelector(".tactical-board-viewport") as HTMLDivElement;
    const stage = mounted.container.querySelector(".tactical-board-stage") as HTMLDivElement;
    assert.ok(viewport);
    assert.ok(stage);
    Object.defineProperties(viewport, {
      clientWidth: { configurable: true, value: 400 },
      clientHeight: { configurable: true, value: 300 },
    });
    Object.defineProperties(stage, {
      offsetWidth: { configurable: true, value: 800 },
      offsetHeight: { configurable: true, value: 500 },
    });
    const wheel = new window.WheelEvent("wheel", { bubbles: true, cancelable: true, deltaY: 220 });
    const browserDefaultAllowed = await dispatch(viewport, wheel);
    assert.equal(browserDefaultAllowed, true);
    assert.equal(wheel.defaultPrevented, false);
    assert.equal(mounted.container.querySelector("[aria-label='Current tactical board zoom']")?.textContent, "100%");

    const zoomIn = mounted.container.querySelector("[aria-label='Zoom tactical board in']") as HTMLButtonElement;
    await click(zoomIn);
    assert.equal(mounted.container.querySelector("[aria-label='Current tactical board zoom']")?.textContent, "125%");
    for (const key of ["+", "-", "0", "f", "F"]) {
      const shortcut = new window.KeyboardEvent("keydown", { bubbles: true, cancelable: true, key });
      assert.equal(await dispatch(viewport, shortcut), true);
      assert.equal(shortcut.defaultPrevented, false);
      assert.equal(mounted.container.querySelector("[aria-label='Current tactical board zoom']")?.textContent, "125%");
    }
    assert.match(mounted.container.querySelector("[aria-label^='Hero. Current turn']")?.getAttribute("aria-label") ?? "", /Grid 3, 4.*Footprint 1 by 1.*Elevation 0 feet/u);
  } finally {
    await mounted.cleanup();
  }
});

test("mounted map changes zoom only through buttons and retains its non-zoom interactions", async () => {
  const mounted = await mount(<MapHarness />);
  try {
    const { canvas, stage } = setMapGeometry(mounted.container);
    const zoom = () => mounted.container.querySelector("[aria-label='Current map zoom']")?.textContent;
    const zoomIn = mounted.container.querySelector("[aria-label='Zoom in']") as HTMLButtonElement;
    const zoomOut = mounted.container.querySelector("[aria-label='Zoom out']") as HTMLButtonElement;
    assert.ok(zoomIn);
    assert.ok(zoomOut);

    const plus = new window.KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "+" });
    assert.equal(await dispatch(canvas, plus), true);
    assert.equal(plus.defaultPrevented, false);
    assert.equal(zoom(), "100%");
    const minus = new window.KeyboardEvent("keydown", { bubbles: true, cancelable: true, key: "-" });
    assert.equal(await dispatch(canvas, minus), true);
    assert.equal(minus.defaultPrevented, false);
    assert.equal(zoom(), "100%");
    assert.equal(canvas.getAttribute("aria-keyshortcuts"), "ArrowLeft ArrowRight ArrowUp ArrowDown");
    await click(zoomIn);
    for (const key of ["0", "f", "F"]) {
      const shortcut = new window.KeyboardEvent("keydown", { bubbles: true, cancelable: true, key });
      assert.equal(await dispatch(canvas, shortcut), true);
      assert.equal(shortcut.defaultPrevented, false);
      assert.equal(zoom(), "125%");
    }
    await click(button(mounted.container, "Reset view"));

    const firstTouch = pointerEvent("pointerdown", {
      clientX: 120,
      clientY: 120,
      pointerId: 10,
      pointerType: "touch",
    });
    const secondTouch = pointerEvent("pointerdown", {
      clientX: 220,
      clientY: 120,
      pointerId: 11,
      pointerType: "touch",
    });
    assert.equal(await dispatch(canvas, firstTouch), true);
    assert.equal(await dispatch(canvas, secondTouch), true);
    assert.equal(await dispatch(canvas, pointerEvent("pointermove", {
      clientX: 80,
      clientY: 120,
      pointerId: 10,
      pointerType: "touch",
    })), true);
    assert.equal(await dispatch(canvas, pointerEvent("pointermove", {
      clientX: 260,
      clientY: 120,
      pointerId: 11,
      pointerType: "touch",
    })), true);
    assert.equal(zoom(), "100%");

    for (let index = 0; index < 20; index += 1) await click(zoomIn);
    assert.equal(zoom(), "400%");
    assert.equal(zoomIn.disabled, true);
    await click(zoomIn);
    assert.equal(zoom(), "400%");

    for (let index = 0; index < 20; index += 1) await click(zoomOut);
    assert.equal(zoom(), "50%");
    assert.equal(zoomOut.disabled, true);
    await click(zoomOut);
    assert.equal(zoom(), "50%");

    await click(button(mounted.container, "Reset view"));
    assert.equal(zoom(), "100%");
    await click(button(mounted.container, "Fit map"));
    assert.equal(zoom(), "50%");
    await click(button(mounted.container, "Reset view"));

    const arrow = new window.KeyboardEvent("keydown", {
      bubbles: true,
      cancelable: true,
      key: "ArrowRight",
    });
    assert.equal(await dispatch(canvas, arrow), false);
    assert.equal(arrow.defaultPrevented, true);
    assert.match(stage.style.transform, /translate3d\(-64px, 0px, 0\) scale\(1\)/);

    await click(button(mounted.container, "Reset view"));
    await dispatch(canvas, pointerEvent("pointerdown", {
      clientX: 200,
      clientY: 180,
      pointerId: 1,
    }));
    await dispatch(canvas, pointerEvent("pointermove", {
      clientX: 120,
      clientY: 120,
      pointerId: 1,
    }));
    await dispatch(canvas, pointerEvent("pointerup", {
      clientX: 120,
      clientY: 120,
      pointerId: 1,
    }));
    assert.match(stage.style.transform, /translate3d\(-80px, -60px, 0\) scale\(1\)/);

    await dispatch(canvas, new window.MouseEvent("click", { bubbles: true }));
    assert.equal(mounted.container.querySelector("[data-testid=selection]")?.textContent, "feature.keep");
    await dispatch(canvas, new window.MouseEvent("click", { bubbles: true }));
    assert.equal(mounted.container.querySelector("[data-testid=selection]")?.textContent, "none");

    const marker = mounted.container.querySelector("[data-feature-id='feature.keep']") as HTMLButtonElement;
    await click(marker);
    assert.equal(mounted.container.querySelector("[data-testid=selection]")?.textContent, "feature.keep");
    await click(button(mounted.container, "Focus selected"));
    assert.equal(zoom(), "200%");
  } finally {
    await mounted.cleanup();
  }
});

function partyMember(sheetState: SectionState<PartyMemberReadModel["sheet"]>): PartyMemberReadModel {
  const member = structuredClone(hubSource.party[0]) as PartyMemberReadModel;
  member.sheetState = sheetState;
  member.sheet = "data" in sheetState && Array.isArray(sheetState.data) ? sheetState.data : [];
  member.sheetStatus = sheetState.status === "error" || sheetState.status === "forbidden"
    ? "unavailable"
    : "source" in sheetState && sheetState.source === "canonical" ? "canonical" : "provisional";
  member.recordStatus = member.sheetStatus === "canonical"
    ? "Canonical character state"
    : member.sheetStatus === "unavailable" ? "Canonical character unavailable" : member.recordStatus;
  return member;
}

function envelope(perspective: Perspective): ReadyHubEnvelope {
  const projected = projectHubEnvelope(
    hubSource,
    HUB_SOURCE_REVISION,
    resolveAudience({
      authenticatedUserId: dmPrincipal,
      authenticatedUserEmail: "",
      requestedPerspective: perspective,
      dmPrincipalIds: [dmPrincipal],
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

test("mounted Party view distinguishes loading, ready, empty, stale, forbidden, and malformed data", async (t) => {
  const { PartyView } = await import("../../src/components/PartyView");
  const confirmedEntry = [{
    id: "sheet.confirmed",
    kind: "class",
    title: "Confirmed Ranger",
    detail: "Authoritative projected value",
  }];
  const cases: Array<{
    name: string;
    loading?: boolean;
    state: SectionState<PartyMemberReadModel["sheet"]>;
    expected: string;
    excluded?: string;
  }> = [
    {
      name: "idle is not empty",
      state: { status: "idle", data: null },
      expected: "section has not been requested yet",
      excluded: "No sheet recorded|No canonical character sheet is recorded|0 recorded entries",
    },
    {
      name: "first load is not empty",
      state: { status: "loading", data: null },
      expected: "Refreshing character sheet",
      excluded: "No sheet recorded|No canonical character sheet is recorded|0 recorded entries",
    },
    {
      name: "first-read HTTP 500 is not empty or provisional",
      state: { status: "error", data: null, failureCategory: "http", httpStatus: 500, diagnosticId: "first-read-500" },
      expected: "Character service unavailable",
      excluded: "No sheet recorded|No canonical character sheet is recorded|Confirmed Ranger",
    },
    {
      name: "ready",
      state: { status: "ready", source: "canonical", data: confirmedEntry },
      expected: "Confirmed Ranger",
    },
    {
      name: "provisional is not canonical readiness",
      state: { status: "ready", source: "provisional", data: confirmedEntry },
      expected: "Confirmed Ranger",
    },
    {
      name: "empty",
      state: { status: "empty", source: "canonical", data: [] },
      expected: "No canonical character sheet is recorded",
    },
    {
      name: "loading",
      loading: true,
      state: { status: "ready", source: "canonical", data: confirmedEntry },
      expected: "Refreshing character sheet",
    },
    {
      name: "stale",
      state: {
        status: "stale",
        source: "canonical",
        data: confirmedEntry,
        failureCategory: "http",
        diagnosticId: "diag-stale",
      },
      expected: "existing canonical information remains visible",
    },
    {
      name: "forbidden",
      state: {
        status: "forbidden",
        data: null,
        failureCategory: "authorization",
        diagnosticId: "diag-forbidden",
      },
      expected: "not authorized to read character sheet",
      excluded: "No sheet recorded",
    },
    {
      name: "malformed",
      state: {
        status: "error",
        data: null,
        failureCategory: "incompatible-data",
        diagnosticId: "diag-malformed",
      },
      expected: "incompatible character sheet; no values were displayed",
      excluded: "Confirmed Ranger",
    },
  ];

  for (const scenario of cases) {
    await t.test(scenario.name, async () => {
      resetPerformanceMarksForTests();
      performance.clearMarks(PERFORMANCE_MARKS.characterReady);
      const member = partyMember(scenario.state);
      member.inventoryState = scenario.state;
      const mounted = await mount(
        <PartyView loading={scenario.loading} party={[member]} />,
      );
      try {
        assert.equal(performance.getEntriesByName(PERFORMANCE_MARKS.characterReady).length,
          scenario.state.status === "ready" && scenario.state.source === "canonical" && !scenario.loading ? 1 : 0,
          "Only a ready canonical character may emit the ready timing");
        await click(button(mounted.container, "Character"));
        const text = mounted.container.textContent ?? "";
        assert.match(text, new RegExp(scenario.expected, "i"));
        if (scenario.excluded) assert.doesNotMatch(text, new RegExp(scenario.excluded, "i"));
        if (scenario.state.status === "error" || scenario.state.status === "forbidden" ||
            scenario.state.status === "idle" || scenario.state.status === "loading") {
          assert.match(text, /Record count unavailable/);
          assert.doesNotMatch(text, /0 recorded entries/);
          await click(button(mounted.container, "Inventory"));
          const inventoryText = mounted.container.textContent ?? "";
          assert.match(inventoryText, /Record count unavailable/);
          assert.doesNotMatch(inventoryText, /0 recorded entries|No coins are recorded|No inventory recorded/);
        }
      } finally {
        await mounted.cleanup();
      }
    });
  }
});

test("mounted character sheet renders species senses without crashing the party view", async () => {
  const { PartyView } = await import("../../src/components/PartyView");
  const member = partyMember({ status: "ready", source: "canonical", data: [] });
  const definition = { id: "species.fixture", label: "Fixture species", canonicalName: "Fixture species",
    kind: "species", status: "identity-only" as const, summary: null, source: null };
  member.characterSheet = {
    version: 2, subject: { id: member.id, label: member.name },
    hitPoints: { current: 9, maximum: 9, maximumReduction: 0 }, armorClass: { value: 15 },
    senses: [{ sense: { id: "dnd2024.vocabulary.sense.darkvision", label: "Darkvision" },
      numerator: 60, denominator: 1, unit: { id: "dnd2024.vocabulary.distance-unit.foot", label: "Foot" } }],
    inventory: { items: [], contentsDepth: 4, mayOmitDeeperContents: true },
    wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
    dossier: {
      origin: { species: definition, background: { ...definition, id: "background.fixture", kind: "background" }, traits: [] },
      classes: [], features: [], inventory: { definitions: [], contentsDepth: 4, mayOmitDeeperContents: true },
      levelOneRules: { test: "character-level-one-rules-project", subjectId: member.id,
        armorClass: {}, attacks: [], senses: [], savingThrowCircumstances: [], spellAccess: {}, equipment: {}, entitlements: [] },
      definitions: [], provenance: {
        sheetQueryId: "dnd2024.query.character-sheet-v2", sheetProjectionId: "dnd2024.mechanic.character-sheet-v2.project",
        dossierProjectionId: "dnd2024.mechanic.character-dossier-v1.project", definitionCount: 0, inventoryDepth: 4, ruleTextPolicy: "canonical-only",
      },
    },
  };
  const mounted = await mount(<PartyView party={[member]} />);
  try {
    await click(button(mounted.container, "Character"));
    const text = mounted.container.textContent ?? "";
    assert.match(text, /Hit points9 \/ 9/);
    assert.match(text, /Armor class15/);
    assert.match(text, /Darkvision60 Foot/);
  } finally {
    await mounted.cleanup();
  }
});

test("mounted character inventory exposes nested disclosures, wallet totals, and image-free fallbacks", async () => {
  const { PartyView } = await import("../../src/components/PartyView");
  const member = structuredClone(hubSource.party[0]) as PartyMemberReadModel;
  member.recordStatus = "Canonical character state";
  member.sheetStatus = "canonical";
  member.inventoryStatus = "canonical";
  member.sheetState = { status: "ready", source: "canonical", data: [] };
  member.inventoryState = { status: "ready", source: "canonical", data: [] };
  member.characterSheet = {
    version: 2,
    subject: { id: member.id, label: member.name },
    classes: [{
      id: "membership.ranger",
      name: "Ranger membership",
      class: { id: "dnd2024.class.ranger", label: "Ranger" },
      level: 5,
      subclass: null,
    }],
    inventory: {
      contentsDepth: 4,
      mayOmitDeeperContents: true,
      items: [
        {
          id: "item.backpack",
          name: "Backpack",
          definition: { id: "dnd2024.item.backpack", label: "Backpack" },
          quantity: 1,
          slot: "carried",
          parentItemId: null,
          order: 0,
          depth: 1,
          childCount: 1,
          deeperContentsOmitted: false,
          equipmentSlots: [],
        },
        {
          id: "item.pouch",
          name: "Belt Pouch",
          definition: { id: "dnd2024.item.pouch", label: "Pouch" },
          quantity: 1,
          slot: "contents",
          parentItemId: "item.backpack",
          order: 0,
          depth: 2,
          childCount: 1,
          deeperContentsOmitted: false,
          equipmentSlots: [],
        },
        {
          id: "item.coins",
          name: "Gold Coins",
          definition: { id: "dnd2024.currency.gp", label: "Gold Piece" },
          quantity: 25,
          slot: "contents",
          parentItemId: "item.pouch",
          order: 0,
          depth: 3,
          childCount: 0,
          deeperContentsOmitted: false,
          equipmentSlots: [],
        },
      ],
    },
    wallet: {
      coinCount: 25,
      copperValue: 2500,
      gpCount: 25,
      denominations: [{
        denomination: { id: "dnd2024.currency.gp", label: "Gold Piece" },
        code: "gp",
        count: 25,
        copperValuePerCoin: 100,
        totalCopperValue: 2500,
      }],
    },
  };

  const mounted = await mount(<PartyView party={[member]} />);
  try {
    await click(button(mounted.container, "Inventory"));
    const text = mounted.container.textContent ?? "";
    assert.match(text, /Backpack/);
    assert.match(text, /Belt Pouch/);
    assert.match(text, /Gold Coins/);
    assert.match(text, /Gold pieces25/);
    assert.match(text, /Copper value2(?:,|\u00a0)500/);
    assert.equal(mounted.container.querySelectorAll(".character-inventory__tree details").length, 2);
    assert.ok(mounted.container.querySelector(".character-inventory__item-media svg"));
    const backpack = mounted.container.querySelector(".character-inventory__tree details") as HTMLDetailsElement;
    await act(async () => {
      backpack.open = true;
      backpack.dispatchEvent(new window.Event("toggle"));
    });
    assert.match(mounted.container.querySelector("[aria-live=polite]")?.textContent ?? "", /Backpack expanded\. 1 contained item\./);
  } finally {
    await mounted.cleanup();
  }
});

test("mounted character page has no critical or serious automated accessibility violations", async () => {
  const { PartyView } = await import("../../src/components/PartyView");
  const mounted = await mount(<PartyView party={[partyMember({ status: "ready", source: "canonical", data: [] })]} />);
  try {
    const axe = (await import("axe-core")).default;
    const result = await axe.run(mounted.container, {
      rules: {
        "color-contrast": { enabled: false },
      },
    });
    const severe = result.violations
      .filter((violation) => violation.impact === "critical" || violation.impact === "serious")
      .map((violation) => ({ id: violation.id, impact: violation.impact, targets: violation.nodes.map((node) => node.target) }));
    assert.deepEqual(severe, []);
  } finally {
    await mounted.cleanup();
  }
});

test("mounted hub retry replaces a stale character warning with Ready canonical data", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  const confirmedEntry = [{
    id: "sheet.last-good",
    kind: "class",
    title: "Last confirmed sheet",
    detail: "Preserved while refresh is unavailable",
  }];
  initial.party[0] = partyMember({
    status: "stale",
    source: "canonical",
    data: confirmedEntry,
    failureCategory: "transport",
    diagnosticId: "diag-refresh",
  });
  const recovered = structuredClone(initial);
  recovered.party[0] = partyMember({ status: "ready", source: "canonical", data: confirmedEntry });
  const failedRefresh = structuredClone(initial);
  failedRefresh.party[0] = partyMember({
    status: "error", data: null, failureCategory: "stale-data",
    diagnosticId: "diag-revision-changed", errorCode: "READ_MODEL_STATE_SPACE_STALE", httpStatus: 409,
  });
  let calls = 0;

  const mounted = await mount(
    <DndInformationHub
      initialEnvelope={initial}
      loadContent={async () => { throw new Error("not used"); }}
      loadEnvelope={async () => {
        calls += 1;
        return calls === 1 ? failedRefresh : recovered;
      }}
    />,
  );
  try {
    await click(button(mounted.container, "Party"));
    await click(button(mounted.container, "Character"));
    assert.match(mounted.container.textContent ?? "", /existing canonical information remains visible/i);
    await click(button(mounted.container, "Retry character sheet"));
    assert.equal(calls, 1);
    assert.match(mounted.container.textContent ?? "", /source revision changed/i);
    assert.match(mounted.container.textContent ?? "", /Last confirmed sheet/i);
    assert.match(mounted.container.textContent ?? "", /diag-revision-changed/);
    await click(button(mounted.container, "Retry character sheet"));
    assert.equal(calls, 2);
    assert.doesNotMatch(mounted.container.textContent ?? "", /existing canonical information remains visible/i);
    assert.equal(mounted.container.querySelector(".character-state--stale"), null);
    assert.match(mounted.container.textContent ?? "", /Last confirmed sheet/i);
  } finally {
    await mounted.cleanup();
  }
});

test("mounted hub first-read failures remain explicit until a successful retry", async (t) => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const failures: Array<SectionState<PartyMemberReadModel["sheet"]>> = [
    { status: "error", data: null, failureCategory: "http", httpStatus: 500, diagnosticId: "first-read-500" },
    { status: "error", data: null, failureCategory: "incompatible-data", diagnosticId: "first-read-malformed" },
    { status: "forbidden", data: null, failureCategory: "authorization", diagnosticId: "first-read-forbidden" },
  ];
  for (const failure of failures) await t.test(failure.status === "error" ? failure.failureCategory : failure.status, async () => {
    const initial = envelope("player");
    initial.party = [partyMember(failure)];
    initial.party[0].inventoryState = failure;
    const recovered = structuredClone(initial);
    const confirmed = [{ id: "sheet.retry", kind: "class", title: "Recovered canonical sheet", detail: "Confirmed after retry" }];
    recovered.party = [partyMember({ status: "ready", source: "canonical", data: confirmed })];
    let completeRead: (value: ReadyHubEnvelope) => void = () => { throw new Error("Retry not started"); };
    const mounted = await mount(<DndInformationHub
      initialEnvelope={initial}
      loadContent={async () => { throw new Error("not used"); }}
      loadEnvelope={() => new Promise(resolve => { completeRead = resolve; })}
    />);
    try {
      await click(button(mounted.container, "Party"));
      await click(button(mounted.container, "Character"));
      assert.ok(mounted.container.querySelector(".character-state[role=alert]"));
      assert.doesNotMatch(mounted.container.textContent ?? "", /No sheet recorded|0 recorded entries|Recovered canonical sheet/);
      await click(button(mounted.container, "Retry character sheet"));
      assert.ok(mounted.container.querySelector(".character-skeleton"));
      assert.doesNotMatch(mounted.container.textContent ?? "", /No sheet recorded|Recovered canonical sheet/);
      await act(async () => { completeRead(recovered); });
      assert.equal(mounted.container.querySelector(".character-state[role=alert], .character-skeleton"), null);
      assert.match(mounted.container.textContent ?? "", /Recovered canonical sheet/);
      for (const canary of [...SECRET_CANARIES, ...DM_ONLY_BASE_CANARIES, ...DM_ONLY_LAYER_CANARIES, ...HIDDEN_MAP_CANARIES]) {
        assert.equal(mounted.container.innerHTML.includes(canary), false, `Player retry leaked ${canary}`);
      }
    } finally { await mounted.cleanup(); }
  });
});

test("mounted hub switches Player to DM to Player without retaining DM-only data", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("player");
  const dm = envelope("dm");
  const player = envelope("player");
  const requests: Array<[Perspective, string, boolean]> = [];
  const mounted = await mount(
    <DndInformationHub
      initialEnvelope={initial}
      loadContent={async () => { throw new Error("not used"); }}
      loadEnvelope={async (perspective, campaignId, preferCached) => {
        requests.push([perspective, campaignId, preferCached]);
        return perspective === "dm" ? dm : player;
      }}
    />,
  );
  try {
    await click(button(mounted.container, "DM"));
    assert.equal(mounted.container.querySelector(".information-hub")?.getAttribute("data-perspective"), "dm");
    await click(button(mounted.container, "Player"));
    assert.deepEqual(requests, [
      ["dm", "campaign.fixture.eldervale", true],
      ["player", "campaign.fixture.eldervale", true],
    ]);
    assert.equal(mounted.container.querySelector(".information-hub")?.getAttribute("data-perspective"), "player");
    const rendered = mounted.container.innerHTML;
    for (const canary of [
      ...SECRET_CANARIES,
      ...DM_ONLY_BASE_CANARIES,
      ...DM_ONLY_LAYER_CANARIES,
      ...HIDDEN_MAP_CANARIES,
    ]) {
      assert.equal(rendered.includes(canary), false, `Player DOM leaked ${canary}`);
    }
  } finally {
    await mounted.cleanup();
  }
});

test("a mounted view error boundary keeps a rendering failure local", async () => {
  const { ViewErrorBoundary } = await import("../../src/components/ViewErrorBoundary");
  function BrokenView(): ReactNode {
    throw new Error("mounted render failure");
  }
  const originalConsoleError = console.error;
  console.error = () => {};
  try {
    const mounted = await mount(
      <div>
        <p>Navigation remains available</p>
        <ViewErrorBoundary viewLabel="Party"><BrokenView /></ViewErrorBoundary>
      </div>,
    );
    try {
      assert.match(mounted.container.textContent ?? "", /Party could not be displayed/);
      assert.match(mounted.container.textContent ?? "", /Navigation remains available/);
      assert.equal(mounted.container.querySelector("[role=alert]") !== null, true);
    } finally {
      await mounted.cleanup();
    }
  } finally {
    console.error = originalConsoleError;
  }
});
