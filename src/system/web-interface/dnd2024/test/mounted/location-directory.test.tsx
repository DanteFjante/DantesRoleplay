import assert from "node:assert/strict";
import test from "node:test";
import { JSDOM } from "jsdom";
import React, { act } from "react";
import { LocationDirectory } from "../../src/components/LocationDirectory";
import type { WorldLocation } from "../../src/data/hub-types";

const locations: WorldLocation[] = Array.from({ length: 275 }, (_, index) => ({
  id: `place.${index}`, name: `Place ${String(index).padStart(3, "0")}`, parentId: `parent.${index % 7}`,
  region: index % 2 ? "East" : "West", kind: index % 3 ? "site" : "city", status: "Active",
  summary: "A known place.", description: "", atmosphere: "", landmarks: [], observations: [], routes: [],
  mapAnchor: null, people: [],
}));

async function mount() {
  const dom = new JSDOM('<!doctype html><html><body><div id="root"></div></body></html>', { url: "https://table.example.test/" });
  const previous = { document: globalThis.document, window: globalThis.window, Element: globalThis.Element,
    HTMLElement: globalThis.HTMLElement, Node: globalThis.Node, Event: globalThis.Event };
  Object.assign(globalThis, { document: dom.window.document, window: dom.window, Element: dom.window.Element,
    HTMLElement: dom.window.HTMLElement, Node: dom.window.Node, Event: dom.window.Event });
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.querySelector("#root")!;
  const root = createRoot(container);
  const selected: string[] = [];
  await act(async () => root.render(<LocationDirectory locations={locations} currentLocationId="place.274" selectedLocationId=""
    onSelect={(id) => selected.push(id)} />));
  return { container, selected, cleanup: async () => {
    await act(async () => root.unmount());
    dom.window.close();
    Object.assign(globalThis, previous);
    delete globalThis.IS_REACT_ACT_ENVIRONMENT;
  } };
}

async function setValue(input: HTMLInputElement | HTMLSelectElement, value: string) {
  await act(async () => {
    const prototype = input.tagName === "SELECT" ? window.HTMLSelectElement.prototype : window.HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new window.Event(input.tagName === "SELECT" ? "change" : "input", { bubbles: true }));
  });
}

test("flat Locations paginates all 275 places and searches unopened nested locations without more reads", async () => {
  const view = await mount();
  try {
    assert.match(view.container.textContent!, /All locations275/);
    assert.equal(view.container.querySelectorAll(".location-row").length, 25);
    assert.equal(view.container.querySelector('[role="status"]')?.textContent, "1–25 of 275");
    assert.equal(view.container.querySelector(".location-row__browse"), null);
    assert.equal(view.container.querySelector(".location-browser__back"), null);
    const next = [...view.container.querySelectorAll<HTMLButtonElement>("button")].find((button) => button.textContent === "Next")!;
    for (let index = 0; index < 10; index++) await act(async () => next.click());
    assert.equal(view.container.querySelector('[role="status"]')?.textContent, "251–275 of 275");
    assert.equal(next.disabled, true);
    await setValue(view.container.querySelector<HTMLInputElement>('input[type="search"]')!, "Place 274");
    assert.equal(view.container.querySelectorAll(".location-row").length, 1);
    assert.equal(view.container.querySelector('[role="status"]')?.textContent, "1–1 of 1");
    const result = view.container.querySelector<HTMLButtonElement>('.location-row[aria-label="View details for Place 274"]')!;
    assert.equal(result.querySelector("em")?.textContent, "Current");
    await act(async () => result.click());
    assert.deepEqual(view.selected, ["place.274"]);
  } finally { await view.cleanup(); }
});

test("region and type filters apply across the complete flat dataset and reset pagination", async () => {
  const view = await mount();
  try {
    const next = [...view.container.querySelectorAll<HTMLButtonElement>("button")].find((button) => button.textContent === "Next")!;
    await act(async () => next.click());
    const filters = view.container.querySelectorAll<HTMLSelectElement>("select");
    await setValue(filters[0]!, "West");
    await setValue(filters[1]!, "city");
    assert.equal(view.container.querySelector('[role="status"]')?.textContent, "1–25 of 46");
    for (const row of view.container.querySelectorAll(".location-row")) assert.match(row.textContent!, /West · city/);
    await act(async () => next.click());
    assert.equal(view.container.querySelector('[role="status"]')?.textContent, "26–46 of 46");
    await setValue(view.container.querySelector<HTMLInputElement>('input[type="search"]')!, "No such place");
    assert.equal(view.container.querySelectorAll(".location-row").length, 0);
    assert.match(view.container.textContent!, /No matching locations/);
    assert.equal(view.container.querySelector('[role="status"]')?.textContent, "0 of 0");
  } finally { await view.cleanup(); }
});
