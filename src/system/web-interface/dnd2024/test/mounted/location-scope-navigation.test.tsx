import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, type ReactNode } from "react";

import { DndInformationHub } from "../../src/components/DndInformationHub";
import { ScopedMapWorkspace } from "../../src/components/ScopedMapWorkspace";
import { LocationBrowser } from "../../src/components/LocationBrowser";
import { hubRouteHash } from "../../src/data/hub-route";
import type {
  DeferredHubUpdate,
  MapDocument,
  ReadyHubEnvelope,
  WorldLocation,
  WorldLocationScope,
} from "../../src/data/hub-types";
import { resolveAudience } from "../support/audience-policy.js";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { HUB_SOURCE_REVISION, hubSource } from "../support/hub-source.js";

const revision = "A".repeat(64);

async function tick() {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

async function mount(hash: string, element: ReactNode) {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", {
    url: `https://table.example.test/${hash}`,
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
  dom.window.scrollTo = () => undefined;
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;

  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.querySelector("#root") as HTMLDivElement;
  const root = createRoot(container);
  await act(async () => { root.render(element); await tick(); });
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

function initialEnvelope() {
  const base = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, resolveAudience({
    authenticatedUserId: "principal.dm.fixture",
    authenticatedUserEmail: "",
    requestedPerspective: "dm",
    dmPrincipalIds: ["principal.dm.fixture"],
  })) as ReadyHubEnvelope;
  const atlas = location(base.world.locations[0]!, "atlas.renamed", "The Caldris Atlas", "Caldris", "region");
  return {
    ...base,
    applicationId: "dnd2024",
    stateSpaceId: "campaign.fixture.caldris",
    contextSelection: {
      selectedCampaignId: "campaign.fixture.caldris",
      selectedWorldId: "world.caldris",
      worlds: [{ id: "world.caldris", name: "Caldris", campaigns: [{
        id: "campaign.fixture.caldris", name: base.campaign.title,
      }] }],
    },
    world: {
      ...base.world,
      id: "world.caldris",
      name: "Caldris",
      currentLocationId: "location.current.not-loaded",
      locations: [atlas],
      locationScopes: [scope("world.caldris", "Caldris", null, [atlas.id])],
    },
  } satisfies ReadyHubEnvelope;
}

test("location browser distinguishes partial or unloaded coverage from confirmed empty", async () => {
  for (const coverage of ["partial", "unloaded", "complete"] as const) {
    const locationScope = coverage === "unloaded" ? null : {
      ...scope("scope", "A location", null, []), coverage,
    };
    const view = await mount("", <LocationBrowser allLocations={[]} locations={[]} locationScopes={[]}
      locationScope={locationScope} busy={false} error="" query="" selectedLocationId="" currentLocationId=""
      onBack={() => {}} onLoadMore={() => {}} onQueryChange={() => {}} onRetry={() => {}}
      onSelect={() => {}} onBrowse={() => {}} />);
    try {
      const content = view.container.textContent ?? "";
      if (coverage === "complete") assert.match(content, /No child locations/);
      else {
        assert.match(content, /Locations unavailable/);
        assert.doesNotMatch(content, /No child locations|has no recorded locations/);
      }
      if (coverage === "partial") assert.match(content, /Some location information is unavailable/);
    } finally { await view.cleanup(); }
  }
});

function location(template: WorldLocation, id: string, name: string, region: string, kind: string): WorldLocation {
  return {
    ...structuredClone(template),
    id, name, region, kind,
    status: "Active",
    summary: `${name} summary.`,
    description: `${name} details.`,
    media: undefined,
  };
}

function scope(id: string, name: string, parentId: string | null, childIds: string[],
  nextCursor: string | null = null): WorldLocationScope {
  return {
    id, name, parentId, childIds,
    totalCount: childIds.length + (nextCursor ? 1 : 0),
    complete: nextCursor === null,
    nextCursor,
    sourceRevisionFingerprint: revision,
  };
}

function locationUpdate(source: ReadyHubEnvelope, locations: WorldLocation[], scopes: WorldLocationScope[]):
  Extract<DeferredHubUpdate, { section: "locations" }> {
  return {
    section: "locations",
    world: {
      currentLocationId: source.world.currentLocationId,
      map: source.world.map,
      mapOwnerId: source.world.mapOwnerId,
      rootMapId: source.world.rootMapId,
      maps: source.world.maps,
      regions: source.world.regions,
      facts: source.world.facts,
      locations,
      locationScopes: scopes,
    },
    campaign: { mapOverlays: source.campaign.mapOverlays },
  };
}

test("Map waits for its declared atlas scope, renders exact markers, and returns from a child map", async () => {
  const initial = initialEnvelope();
  const atlas = initial.world.locations[0]!;
  const eredane = location(atlas, "region.eredane", "Eredane", "Eredane", "region");
  const lanternSea = location(atlas, "region.lantern-sea", "Lantern Sea", "Lantern Sea", "region");
  const solasca = location(atlas, "region.solasca", "Solasca", "Solasca", "region");
  const childMaps = [eredane, lanternSea, solasca].map((entry): MapDocument => ({
    id: `map.live.${entry.id}`,
    scope: "region",
    parentMapId: `map.live.${atlas.id}`,
    subject: { kind: "region", id: entry.id, name: entry.name },
    coordinateSpace: { id: `space.live.${entry.id}`, unit: "normalized", width: 1000, height: 1000 },
    baseState: "ready",
    base: { imageUrl: `/maps/${entry.id}.png`, alt: `${entry.name} map` },
    layers: [],
    features: [],
    scopeLinks: [],
  }));
  const atlasMap: MapDocument = {
    id: `map.live.${atlas.id}`,
    scope: "world",
    parentMapId: null,
    subject: { kind: "region", id: atlas.id, name: atlas.name },
    coordinateSpace: { id: `space.live.${atlas.id}`, unit: "normalized", width: 1000, height: 1000 },
    baseState: "ready",
    base: { imageUrl: "/api/applications/dnd2024/state-spaces/test/media/atlas/content",
      alt: "Caldris atlas", width: 2000, height: 1500 },
    layers: [{ id: "layer.live.world.regions", kind: "markers", order: 1, label: "Regions" }],
    features: [
      { id: "feature.eredane", kind: "point", layerId: "layer.live.world.regions",
        coordinateSpaceId: `space.live.${atlas.id}`, geometry: { x: 255, y: 380 },
        name: "Eredane", detail: "Western continent.", locationId: eredane.id },
      { id: "feature.lantern", kind: "point", layerId: "layer.live.world.regions",
        coordinateSpaceId: `space.live.${atlas.id}`, geometry: { x: 510, y: 570 },
        name: "Lantern Sea", detail: "The sea between continents.", locationId: lanternSea.id },
      { id: "feature.solasca", kind: "point", layerId: "layer.live.world.regions",
        coordinateSpaceId: `space.live.${atlas.id}`, geometry: { x: 765, y: 490 },
        name: "Solasca", detail: "Eastern continent.", locationId: solasca.id },
    ],
    scopeLinks: childMaps.map((map, index) => ({
      id: `scope.${map.subject.id}`,
      childMapId: map.id,
      childScope: "region",
      childName: map.subject.name,
      viaFeatureId: atlasMapFeatureId(index),
    })),
  };
  function atlasMapFeatureId(index: number) {
    return ["feature.eredane", "feature.lantern", "feature.solasca"][index]!;
  }
  initial.world = {
    ...initial.world,
    mapOwnerId: atlas.id,
    rootMapId: atlasMap.id,
    maps: [{ ...atlasMap, features: [], layers: [], scopeLinks: [] }],
  };
  let finishAtlas!: (update: Extract<DeferredHubUpdate, { section: "locations" }>) => void;
  const calls: string[] = [];
  let deferredReads = 0;
  const loader = async (source: ReadyHubEnvelope, scopeId: string) => {
    calls.push(scopeId);
    if (scopeId === atlas.id) return new Promise<Extract<DeferredHubUpdate, { section: "locations" }>>(
      (resolve) => { finishAtlas = resolve; });
    assert.equal(scopeId, eredane.id);
    return locationUpdate(source, source.world.locations, [
      ...source.world.locationScopes,
      scope(eredane.id, eredane.name, atlas.id, []),
    ]);
  };
  const mounted = await mount(hubRouteHash("world", "overview", { worldSection: "map" }),
    <DndInformationHub initialEnvelope={initial} loadWorldScope={loader}
      loadDeferredSection={async () => { deferredReads += 1; throw new Error("unexpected directory read"); }} />);
  try {
    assert.deepEqual(calls, [atlas.id]);
    assert.equal(deferredReads, 0, "the map reads its immediate scope instead of the complete directory");
    assert.match(mounted.container.textContent!, /Loading the places on this map/);
    await act(async () => {
      finishAtlas({
        ...locationUpdate(initial, [atlas, eredane, lanternSea, solasca], [
          initial.world.locationScopes[0]!,
          scope(atlas.id, atlas.name, "world.caldris", [eredane.id, lanternSea.id, solasca.id]),
        ]),
        world: {
          ...locationUpdate(initial, [], []).world,
          locations: [atlas, eredane, lanternSea, solasca],
          locationScopes: [initial.world.locationScopes[0]!,
            scope(atlas.id, atlas.name, "world.caldris", [eredane.id, lanternSea.id, solasca.id])],
          maps: [atlasMap, ...childMaps],
        },
      });
      await tick();
    });
    assert.equal(mounted.container.querySelector(".map-scope-status"), null);
    assert.deepEqual([...mounted.container.querySelectorAll(".world-map-marker")]
      .map((marker) => marker.getAttribute("data-feature-id")),
    ["feature.eredane", "feature.lantern", "feature.solasca"]);
    const image = mounted.container.querySelector<HTMLImageElement>(".world-map-stage > img");
    assert.equal(image?.getAttribute("width"), "2000");
    assert.equal(image?.getAttribute("height"), "1500");
    const eredaneScope = [...mounted.container.querySelectorAll<HTMLButtonElement>(".map-scope-links button")]
      .find((button) => button.textContent?.includes("Eredane"));
    assert.ok(eredaneScope);
    await click(eredaneScope);
    assert.match(mounted.container.textContent!, /Eredane map/);
    const parent = [...mounted.container.querySelectorAll<HTMLButtonElement>(".map-breadcrumbs button")]
      .find((button) => button.textContent?.includes(atlas.name));
    assert.ok(parent);
    await click(parent);
    assert.match(mounted.container.textContent!, new RegExp(`${atlas.name} map`));
    assert.deepEqual(calls, [atlas.id, eredane.id]);
  } finally { await mounted.cleanup(); }
});

test("Map loads the root scope when bootstrap has only a placeholder map", async () => {
  const initial = initialEnvelope();
  initial.world = {
    ...initial.world,
    locations: [],
    locationScopes: [],
    mapOwnerId: null,
  };
  const calls: string[] = [];
  const mounted = await mount(hubRouteHash("world", "overview", { worldSection: "map" }),
    <DndInformationHub initialEnvelope={initial} loadWorldScope={async (source, scopeId) => {
      calls.push(scopeId);
      return locationUpdate(source, [], [scope(scopeId, source.world.name, null, [])]);
    }} />);
  try {
    await act(tick);
    assert.deepEqual(calls, ["world.caldris"]);
  } finally { await mounted.cleanup(); }
});

function byLabel(container: Element, label: string) {
  const match = container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  assert.ok(match, `Missing ${label}`);
  return match;
}

test("opening a marker-only location redirects to its parent and selects its existing details", async () => {
  const initial = initialEnvelope();
  const template = initial.world.maps[0]!;
  const parent: MapDocument = {
    ...template, id: "map.parent", parentMapId: null,
    subject: { kind: "region", id: "region.parent", name: "Parent Vale" },
    baseState: "ready", base: { imageUrl: "/parent.png", alt: "Parent map" },
    features: [{ id: "feature.village", kind: "point", layerId: "places",
      coordinateSpaceId: template.coordinateSpace.id, geometry: { x: 440, y: 295 },
      name: "Goat Village", detail: "A high pasture village.", locationId: "location.village" }],
    layers: [{ id: "places", label: "Places", kind: "markers", order: 0 }],
    scopeLinks: [{ id: "scope.village", childMapId: "map.village", childName: "Goat Village",
      childScope: "location", viaFeatureId: "feature.village" }],
  };
  const village: MapDocument = { ...parent, id: "map.village", parentMapId: parent.id,
    subject: { kind: "location", id: "location.village", name: "Goat Village" },
    baseState: "absent", base: null, features: [], layers: [], scopeLinks: [] };
  const navigations: string[][] = [];
  const mounted = await mount("", <ScopedMapWorkspace world={{ ...initial.world, maps: [parent, village] }}
    activeMapId={village.id} selectedFeatureId="" currentLocationId="" campaignTitle="Test"
    overlays={[]} scopeState="ready" scopeError="" onMapChange={() => {}}
    onNavigateToFeature={(mapId, featureId) => navigations.push([mapId, featureId])}
    onFeatureSelect={() => {}} onOpenLocation={() => {}} onRetryScope={() => {}} />);
  try {
    assert.deepEqual(navigations, [[parent.id, "feature.village"]]);
    assert.equal(mounted.container.querySelector('[aria-label="Map view controls"]'), null);
  } finally { await mounted.cleanup(); }
});

async function click(control: HTMLButtonElement) {
  await act(async () => { control.click(); await tick(); });
}

test("Locations reaches atlas, both regions, and a deeper no-media place with parent navigation", async () => {
  const initial = initialEnvelope();
  const atlas = initial.world.locations[0]!;
  const eredane = location(atlas, "region.eredane", "Eredane", "Eredane", "region");
  const solasca = location(atlas, "region.solasca", "Solasca", "Solasca", "region");
  const keep = location(atlas, "site.eredane.lower-keep", "Lower Keep", "Eredane", "site");
  const calls: Array<[string, string | null]> = [];
  const loader = async (source: ReadyHubEnvelope, scopeId: string, cursor: string | null) => {
    calls.push([scopeId, cursor]);
    if (scopeId === atlas.id) return locationUpdate(source, [atlas, eredane, solasca], [
      source.world.locationScopes[0]!, scope(atlas.id, atlas.name, "world.caldris", [eredane.id, solasca.id]),
    ]);
    assert.equal(scopeId, eredane.id);
    return locationUpdate(source, [...source.world.locations, keep], [
      ...source.world.locationScopes, scope(eredane.id, eredane.name, atlas.id, [keep.id]),
    ]);
  };
  const mounted = await mount(hubRouteHash("world", "overview", { worldSection: "locations" }),
    <DndInformationHub initialEnvelope={initial} loadWorldScope={loader} />);
  try {
    assert.equal(mounted.container.querySelector(".location-row__action em"), null,
      "an unloaded current location must not make a fallback row current");
    const atlasButton = byLabel(mounted.container, `Open ${atlas.name} and browse its locations`);
    assert.equal(atlasButton.tagName, "BUTTON");
    atlasButton.focus();
    assert.equal(document.activeElement, atlasButton, "location disclosure remains keyboard focusable");
    await click(atlasButton);
    assert.match(mounted.container.textContent!, /Eredane/);
    assert.match(mounted.container.textContent!, /Solasca/);
    await click(byLabel(mounted.container, "Open Eredane and browse its locations"));
    assert.match(mounted.container.textContent!, /Lower Keep/);
    assert.deepEqual(calls, [[atlas.id, null], [eredane.id, null]]);
    await click([...mounted.container.querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent?.includes("Parent location"))!);
    assert.match(mounted.container.textContent!, /Solasca/);
    assert.match(window.location.hash, /scope=atlas\.renamed/u);
  } finally { await mounted.cleanup(); }
});

test("deep links authorize each scope in order and page 101 siblings without a false world-wide search", async () => {
  const initial = initialEnvelope();
  const atlas = initial.world.locations[0]!;
  const eredane = location(atlas, "region.eredane", "Eredane", "Eredane", "region");
  const solasca = location(atlas, "region.solasca", "Solasca", "Solasca", "region");
  const places = Array.from({ length: 101 }, (_, index) => location(atlas,
    `site.solasca.${String(index).padStart(3, "0")}`, `Solasca Place ${String(index).padStart(3, "0")}`,
    "Solasca", "site"));
  const calls: Array<[string, string | null]> = [];
  const loader = async (source: ReadyHubEnvelope, scopeId: string, cursor: string | null) => {
    calls.push([scopeId, cursor]);
    if (scopeId === atlas.id) return {
      ...locationUpdate(source, [atlas, eredane, solasca], [
        source.world.locationScopes[0]!, scope(atlas.id, atlas.name, "world.caldris", [eredane.id, solasca.id]),
      ]),
      scopePage: { id: atlas.id },
    };
    assert.equal(scopeId, solasca.id);
    const shown = cursor === null ? places.slice(0, 100) : places;
    const retained = source.world.locations.filter((entry) => !entry.id.startsWith("site.solasca."));
    return {
      ...locationUpdate(source, [...retained, ...shown], [
        ...source.world.locationScopes.filter((entry) => entry.id !== solasca.id),
        scope(solasca.id, solasca.name, atlas.id, shown.map((entry) => entry.id), cursor === null ? "100" : null),
      ]),
      scopePage: { id: solasca.id },
    };
  };
  const hash = hubRouteHash("world", "overview", {
    worldSection: "locations", locationScopePath: [atlas.id, solasca.id], locationId: solasca.id,
  });
  const mounted = await mount(hash, <DndInformationHub initialEnvelope={initial} loadWorldScope={loader} />);
  try {
    await act(async () => { await tick(); await tick(); await tick(); });
    assert.deepEqual(calls.slice(0, 2), [[atlas.id, null], [solasca.id, null]]);
    assert.match(mounted.container.textContent!, /100 of 101/);
    const more = [...mounted.container.querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent?.includes("Load more locations"));
    assert.ok(more);
    await click(more);
    assert.deepEqual(calls.at(-1), [solasca.id, "100"]);
    assert.match(mounted.container.textContent!, /Solasca Place 100/);
    assert.match(mounted.container.textContent!, /101 of 101/);
    assert.match(mounted.container.textContent!, /Only the direct locations inside this area are shown/);
  } finally { await mounted.cleanup(); }
});

test("Locations opens one root page, presents hierarchy truthfully, and keeps its continuation reachable", async () => {
  const initial = initialEnvelope();
  const template = initial.world.locations[0]!;
  const places = Array.from({ length: 101 }, (_, index) => location(template,
    `site.root.${String(index).padStart(3, "0")}`, `Root Place ${String(index).padStart(3, "0")}`,
    "Caldris", "site"));
  initial.world = { ...initial.world, locations: [], locationScopes: [] };
  let rootReads = 0;
  const continuations: Array<[string, string | null]> = [];
  const page = (shown: WorldLocation[], cursor: string | null): Extract<DeferredHubUpdate, { section: "locations" }> => ({
    ...locationUpdate(initial, shown, [scope("world.caldris", "Caldris", null,
      shown.map((entry) => entry.id), cursor)]),
    scopePage: { id: "world.caldris" },
  });
  const mounted = await mount(hubRouteHash("world", "overview", { worldSection: "locations" }),
    <DndInformationHub initialEnvelope={initial}
      loadDeferredSection={async (_source, section) => {
        assert.equal(section, "locations");
        rootReads += 1;
        return page(places.slice(0, 100), "100");
      }}
      loadWorldScope={async (_source, scopeId, cursor) => {
        continuations.push([scopeId, cursor]);
        return page(places, null);
      }} />);
  try {
    await act(async () => { await tick(); await tick(); });
    assert.equal(rootReads, 1, "opening Locations reads only the first root page");
    assert.match(mounted.container.textContent!, /This location level/);
    assert.match(mounted.container.textContent!, /100 of 101/);
    assert.doesNotMatch(mounted.container.textContent!, /Complete directory|All places|All known locations/);
    const more = [...mounted.container.querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent?.includes("Load more locations"));
    assert.ok(more);
    await click(more);
    assert.deepEqual(continuations, [["world.caldris", "100"]]);
    assert.match(mounted.container.textContent!, /Root Place 100/);
    assert.match(mounted.container.textContent!, /101 of 101/);
  } finally { await mounted.cleanup(); }
});
