import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act } from "react";

import { InstalledContentView, type InstalledContentLoader } from "../../src/components/InstalledContentView";
import type { InstalledContentPage, InstalledContentRequest } from "../../src/server/effective-content";

const fingerprint = "A".repeat(64);
const extension = {
  extensionId: "caldris-homebrew",
  displayName: "Caldris Homebrew",
  description: "Reviewed Caldris additions.",
  classification: "homebrew" as const,
};

function record(id: string, name: string) {
  return {
    id,
    name,
    description: `${name} contribution.`,
    kind: "entity",
    path: "entities/items",
    ownerId: extension.extensionId,
    sourceLabel: extension.displayName,
    classification: extension.classification,
    presentationRoles: ["entity", "items"],
    isAdditive: true,
  };
}

function page(records: ReturnType<typeof record>[], nextCursor: string | null, totalCount = 3): InstalledContentPage {
  return {
    resolutionFingerprint: fingerprint,
    extensions: [extension],
    records,
    availableKinds: ["entity", "procedure"],
    totalCount,
    nextCursor,
  };
}

async function tick() {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

test("installed content pages compact contributions and sends source, type, and search filters", async () => {
  const dom = new JSDOM("<div id='root'></div>", { url: "https://table.example.test/" });
  const previous = {
    window: globalThis.window,
    document: globalThis.document,
    Event: globalThis.Event,
    Element: globalThis.Element,
    HTMLElement: globalThis.HTMLElement,
    Node: globalThis.Node,
  };
  Object.assign(globalThis, {
    window: dom.window,
    document: dom.window.document,
    Event: dom.window.Event,
    Element: dom.window.Element,
    HTMLElement: dom.window.HTMLElement,
    Node: dom.window.Node,
    IS_REACT_ACT_ENVIRONMENT: true,
  });
  const calls: InstalledContentRequest[] = [];
  const loader: InstalledContentLoader = async (request) => {
    calls.push(structuredClone(request));
    if (request.query === "needle") return page([record("extension.needle", "Needle")], null, 1);
    if (request.cursor) return page([record("extension.third", "Third")], null);
    return page([record("extension.first", "First"), record("extension.second", "Second")], "next-page");
  };
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  try {
    await act(async () => {
      root.render(<InstalledContentView loadContent={loader} resolutionFingerprint={fingerprint} />);
      await tick();
    });
    assert.match(document.body.textContent!, /Showing 2 of 3/);
    assert.equal(document.querySelectorAll(".installed-content-record").length, 2);
    const more = [...document.querySelectorAll<HTMLButtonElement>("button")]
      .find((button) => button.textContent?.includes("Load more"));
    assert.ok(more);
    await act(async () => { more.click(); await tick(); });
    assert.equal(calls[1]!.cursor, "next-page");
    assert.equal(calls[1]!.expectedResolutionFingerprint, fingerprint);
    assert.match(document.body.textContent!, /Showing 3 of 3/);

    const source = document.querySelectorAll<HTMLSelectElement>("select")[0]!;
    await act(async () => {
      source.value = extension.extensionId;
      source.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      await tick();
    });
    assert.equal(calls.at(-1)!.ownerId, extension.extensionId);
    assert.equal(calls.at(-1)!.cursor, null);

    const input = document.querySelector<HTMLInputElement>('input[type="search"]')!;
    const form = document.querySelector<HTMLFormElement>('form[role="search"]')!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, "value")!
        .set!.call(input, "needle");
      input.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
      input.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      await tick();
    });
    await act(async () => {
      form.dispatchEvent(new dom.window.Event("submit", { bubbles: true, cancelable: true }));
      await tick();
    });
    assert.equal(calls.at(-1)!.query, "needle");
    assert.match(document.body.textContent!, /Showing 1 of 1/);
    assert.equal(document.querySelectorAll(".installed-content-record").length, 1);
  } finally {
    await act(async () => root.unmount());
    Object.assign(globalThis, previous);
    delete globalThis.IS_REACT_ACT_ENVIRONMENT;
    dom.window.close();
  }
});
