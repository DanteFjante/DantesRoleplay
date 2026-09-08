import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, type ReactNode } from "react";

import { DndInformationHub } from "../../src/components/DndInformationHub";
import { hubRouteHash } from "../../src/data/hub-route";
import type {
  DeferredHubUpdate,
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

function byLabel(container: Element, label: string) {
  const match = container.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
  assert.ok(match, `Missing ${label}`);
  return match;
}

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
    if (scopeId === atlas.id) return locationUpdate(source, [atlas, eredane, solasca], [
      source.world.locationScopes[0]!, scope(atlas.id, atlas.name, "world.caldris", [eredane.id, solasca.id]),
    ]);
    assert.equal(scopeId, solasca.id);
    const shown = cursor === null ? places.slice(0, 100) : places;
    const retained = source.world.locations.filter((entry) => !entry.id.startsWith("site.solasca."));
    return locationUpdate(source, [...retained, ...shown], [
      ...source.world.locationScopes.filter((entry) => entry.id !== solasca.id),
      scope(solasca.id, solasca.name, atlas.id, shown.map((entry) => entry.id), cursor === null ? "100" : null),
    ]);
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
    assert.match(mounted.container.textContent!, /Search is limited to the locations shown in this level/);
  } finally { await mounted.cleanup(); }
});
