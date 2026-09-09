import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import { JSDOM } from "jsdom";
import React, { act, useState } from "react";

import { WorldPeopleDirectory } from "../../src/components/WorldPeopleDirectory";
import type { WorldPersonDirectoryEntry, WorldReadModel } from "../../src/data/hub-types";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

async function mount(element: React.ReactNode) {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", {
    url: "https://table.example.test/",
  });
  const previous = {
    document: globalThis.document, Element: globalThis.Element, Event: globalThis.Event,
    HTMLElement: globalThis.HTMLElement, MouseEvent: globalThis.MouseEvent,
    Node: globalThis.Node, window: globalThis.window,
  };
  Object.assign(globalThis, {
    document: dom.window.document, Element: dom.window.Element, Event: dom.window.Event,
    HTMLElement: dom.window.HTMLElement, MouseEvent: dom.window.MouseEvent,
    Node: dom.window.Node, window: dom.window,
  });
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const { createRoot } = await import("react-dom/client");
  const container = dom.window.document.querySelector("#root")!;
  const reactRoot = createRoot(container);
  await act(async () => reactRoot.render(element));
  return {
    container,
    async cleanup() {
      await act(async () => reactRoot.unmount());
      dom.window.close();
      Object.assign(globalThis, previous);
      delete globalThis.IS_REACT_ACT_ENVIRONMENT;
    },
  };
}

function person(id: string, name: string, locationId: string): WorldPersonDirectoryEntry {
  return {
    id, name, initials: name.slice(0, 2), kind: "NPC", role: "Recorded person",
    summary: `${name} summary.`, background: "Recorded background.", disposition: "Active",
    location: { id: locationId, name: `${name} Place`, region: "Deep Region" },
  };
}

const people = [person("person.one", "First Person", "location.one"),
  person("person.two", "Second Person", "location.two")];
const world = {
  id: "world.fixture", name: "Fixture World", people,
  peopleDirectory: { totalCount: 2, hierarchyComplete: false, sourceRevisionFingerprint: "A".repeat(64) },
} as WorldReadModel;

test("People is an explicit list/detail workspace with selectable records and location navigation", async () => {
  let opened = "";
  function Harness() {
    const [selected, setSelected] = useState(people[0]!.id);
    return <WorldPeopleDirectory world={world} selectedPersonId={selected}
      onPersonSelect={setSelected} onOpenLocation={(id) => { opened = id; }} />;
  }
  const mounted = await mount(<Harness />);
  try {
    assert.equal(mounted.container.querySelectorAll(".world-person-list__item").length, 2);
    assert.match(mounted.container.textContent!, /deeper places may contain more people/u);
    const second = [...mounted.container.querySelectorAll<HTMLButtonElement>(".world-person-list__item")][1]!;
    await act(async () => second.click());
    assert.equal(mounted.container.querySelector(".world-person-detail h2")?.textContent, "Second Person");
    const location = mounted.container.querySelector<HTMLButtonElement>(".directory-link-button")!;
    await act(async () => location.click());
    assert.equal(opened, "location.two");
  } finally { await mounted.cleanup(); }
});

test("People list/detail stacks and releases sticky detail on narrow screens", async () => {
  const css = await readFile(path.join(root, "src/styles.css"), "utf8");
  assert.match(css, /@media \(max-width: 620px\)[\s\S]*?\.world-person-workspace\s*\{\s*grid-template-columns:\s*1fr/u);
  assert.match(css, /@media \(max-width: 620px\)[\s\S]*?\.world-person-detail\s*\{\s*position:\s*static/u);
});
