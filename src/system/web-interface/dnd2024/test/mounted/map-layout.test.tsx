import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, type ReactNode, useState } from "react";

import type { MapDocument } from "../../src/data/hub-types";
import { DEFAULT_MAP_VIEWPORT, MapCanvas, type MapViewportState } from "../../src/components/MapCanvas";

type Geometry = { width: number; height: number; stageWidth: number; stageHeight: number };

const layoutMap = (id = "map.layout") => ({
  id,
  kind: "map",
  subject: { id: "location.layout", name: "Layout Vale", kind: "location" },
  coordinateSpace: { id: "space.layout", unit: "percent", width: 100, height: 100 },
  baseState: "ready",
  base: { imageUrl: `/${id}.png`, alt: "Layout Vale map", width: 1000, height: 500 },
  layers: [{ id: "layer.places", kind: "markers", order: 1, label: "Places" }],
  features: [{
    id: "feature.anchor",
    kind: "point",
    name: "Anchor",
    detail: "A useful test marker",
    locationId: null,
    layerId: "layer.places",
    geometry: { x: 25, y: 50 },
  }],
}) as unknown as MapDocument;

class ControlledResizeObserver {
  static instances: ControlledResizeObserver[] = [];
  readonly callback: ResizeObserverCallback;
  disconnected = false;
  observed: Element[] = [];

  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
    ControlledResizeObserver.instances.push(this);
  }

  observe(element: Element) { this.observed.push(element); }
  disconnect() { this.disconnected = true; }
  unobserve() {}
  notify() { this.callback([], this as unknown as ResizeObserver); }
}

function geometry(container: Element, value: Geometry) {
  const canvas = container.querySelector(".world-map-canvas") as HTMLDivElement;
  const stage = container.querySelector(".world-map-stage") as HTMLDivElement;
  assert.ok(canvas);
  assert.ok(stage);
  Object.defineProperties(canvas, {
    clientWidth: { configurable: true, value: value.width },
    clientHeight: { configurable: true, value: value.height },
  });
  Object.defineProperties(stage, {
    offsetWidth: { configurable: true, value: value.stageWidth },
    offsetHeight: { configurable: true, value: value.stageHeight },
  });
  return { canvas, stage };
}

async function mount(element: ReactNode) {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", {
    url: "https://table.example.test/",
  });
  const previous = {
    document: globalThis.document,
    Element: globalThis.Element,
    Event: globalThis.Event,
    HTMLElement: globalThis.HTMLElement,
    Node: globalThis.Node,
    ResizeObserver: globalThis.ResizeObserver,
    window: globalThis.window,
  };
  Object.assign(globalThis, {
    document: dom.window.document,
    Element: dom.window.Element,
    Event: dom.window.Event,
    HTMLElement: dom.window.HTMLElement,
    Node: dom.window.Node,
    ResizeObserver: ControlledResizeObserver,
    window: dom.window,
  });
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
      delete globalThis.IS_REACT_ACT_ENVIRONMENT;
      ControlledResizeObserver.instances = [];
    },
  };
}

function mapProps(map: MapDocument, viewport: MapViewportState, onViewportChange: (next: MapViewportState) => void) {
  return <MapCanvas
    annotatedFeatureIds={new Set()}
    currentLocationId=""
    influencedFeatureIds={new Set()}
    map={map}
    onFeatureSelect={() => {}}
    onOpenScope={() => {}}
    onViewportChange={onViewportChange}
    scopeLinkFeatureIds={new Map()}
    selectedFeatureId=""
    viewport={viewport}
  />;
}

function Harness({ map = layoutMap(), initialViewport = DEFAULT_MAP_VIEWPORT }: {
  map?: MapDocument;
  initialViewport?: MapViewportState;
}) {
  const [viewport, setViewport] = useState(initialViewport);
  const [currentMap, setCurrentMap] = useState(map);
  const [emissions, setEmissions] = useState(0);
  const updateViewport = (next: MapViewportState) => {
    setEmissions((count) => count + 1);
    setViewport(next);
  };
  return <>
    {mapProps(currentMap, viewport, updateViewport)}
    <button type="button" onClick={() => setViewport({ zoom: 0.5, x: 0, y: 0 })}>Set small view</button>
    <button type="button" onClick={() => setCurrentMap(layoutMap("map.replacement"))}>Replace map</button>
    <output data-testid="viewport">{JSON.stringify(viewport)}</output>
    <output data-testid="emissions">{emissions}</output>
  </>;
}

async function loadImage(container: Element) {
  const image = container.querySelector(".world-map-stage > img") as HTMLImageElement;
  assert.ok(image);
  await act(async () => image.dispatchEvent(new window.Event("load")));
}

async function failImage(container: Element) {
  const image = container.querySelector(".world-map-stage > img") as HTMLImageElement;
  assert.ok(image);
  await act(async () => image.dispatchEvent(new window.Event("error")));
}

function viewportOutput(container: Element): MapViewportState {
  return JSON.parse(container.querySelector("[data-testid=viewport]")?.textContent ?? "{}") as MapViewportState;
}

test("unselected ready image centers a short stage after it loads while markers share that stage", async () => {
  const mounted = await mount(<Harness />);
  try {
    const { canvas, stage } = geometry(mounted.container, { width: 400, height: 300, stageWidth: 400, stageHeight: 200 });
    assert.equal(stage.parentElement, canvas);
    assert.equal(stage.querySelector(".world-map-markers")?.parentElement, stage);
    await loadImage(mounted.container);
    assert.deepEqual(viewportOutput(mounted.container), { zoom: 1, x: 0, y: 50 });
    assert.match(stage.style.transform, /translate3d\(0px, 50px, 0\) scale\(1\)/u);
    assert.equal(mounted.container.querySelector("[data-testid=emissions]")?.textContent, "1");
    await act(async () => ControlledResizeObserver.instances[0].notify());
    assert.equal(mounted.container.querySelector("[data-testid=emissions]")?.textContent, "1");
  } finally { await mounted.cleanup(); }
});

test("a stale restored pan is reclamped after image load without changing zoom", async () => {
  const mounted = await mount(<Harness initialViewport={{ zoom: 1, x: 80, y: -500 }} />);
  try {
    geometry(mounted.container, { width: 400, height: 300, stageWidth: 400, stageHeight: 200 });
    await loadImage(mounted.container);
    assert.deepEqual(viewportOutput(mounted.container), { zoom: 1, x: 0, y: 50 });
  } finally { await mounted.cleanup(); }
});

test("resize observer constrains the latest zoom and pan rather than its mount-time view", async () => {
  const mounted = await mount(<Harness initialViewport={{ zoom: 2, x: -600, y: -400 }} />);
  try {
    geometry(mounted.container, { width: 400, height: 300, stageWidth: 500, stageHeight: 400 });
    await loadImage(mounted.container);
    assert.equal(ControlledResizeObserver.instances.length, 1);
    const setSmallView = [...mounted.container.querySelectorAll("button")]
      .find((candidate) => candidate.textContent === "Set small view") as HTMLButtonElement;
    await act(async () => setSmallView.click());
    assert.deepEqual(viewportOutput(mounted.container), { zoom: 0.5, x: 0, y: 0 });
    geometry(mounted.container, { width: 400, height: 300, stageWidth: 500, stageHeight: 400 });
    await act(async () => ControlledResizeObserver.instances[0].notify());
    assert.deepEqual(viewportOutput(mounted.container), { zoom: 0.5, x: 75, y: 50 });
    await act(async () => ControlledResizeObserver.instances[0].notify());
    assert.equal(mounted.container.querySelector("[data-testid=emissions]")?.textContent, "1");
  } finally { await mounted.cleanup(); }
});

test("map replacement with no selection reruns bounds normalization", async () => {
  const mounted = await mount(<Harness initialViewport={{ zoom: 1, x: 40, y: -100 }} />);
  try {
    geometry(mounted.container, { width: 400, height: 300, stageWidth: 400, stageHeight: 200 });
    await loadImage(mounted.container);
    assert.deepEqual(viewportOutput(mounted.container), { zoom: 1, x: 0, y: 50 });
    const setSmallView = [...mounted.container.querySelectorAll("button")]
      .find((candidate) => candidate.textContent === "Set small view") as HTMLButtonElement;
    await act(async () => setSmallView.click());
    const replace = [...mounted.container.querySelectorAll("button")].find((candidate) => candidate.textContent === "Replace map") as HTMLButtonElement;
    await act(async () => replace.click());
    geometry(mounted.container, { width: 400, height: 300, stageWidth: 400, stageHeight: 200 });
    await loadImage(mounted.container);
    assert.deepEqual(viewportOutput(mounted.container), { zoom: 0.5, x: 100, y: 100 });
  } finally { await mounted.cleanup(); }
});

test("late observer notifications after disposal cannot emit viewport changes", async () => {
  const mounted = await mount(<Harness initialViewport={{ zoom: 1, x: 0, y: 20 }} />);
  const observer = () => ControlledResizeObserver.instances[0];
  let cleaned = false;
  try {
    geometry(mounted.container, { width: 400, height: 300, stageWidth: 400, stageHeight: 200 });
    await loadImage(mounted.container);
    const current = observer();
    await mounted.cleanup();
    cleaned = true;
    assert.equal(current.disconnected, true);
    assert.doesNotThrow(() => current.notify());
  } finally {
    if (!cleaned) await mounted.cleanup().catch(() => {});
  }
});

test("image failure disconnects its observer before late resize notifications", async () => {
  const mounted = await mount(<Harness />);
  try {
    geometry(mounted.container, { width: 400, height: 300, stageWidth: 400, stageHeight: 200 });
    await loadImage(mounted.container);
    const current = ControlledResizeObserver.instances[0];
    const before = mounted.container.querySelector("[data-testid=emissions]")?.textContent;
    await failImage(mounted.container);
    assert.equal(current.disconnected, true);
    assert.doesNotThrow(() => current.notify());
    assert.equal(mounted.container.querySelector("[data-testid=emissions]")?.textContent, before);
  } finally { await mounted.cleanup(); }
});
