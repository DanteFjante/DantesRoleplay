import assert from "node:assert/strict";
import test from "node:test";
import React, { act } from "react";
import { JSDOM } from "jsdom";
import { ItemMediaGallery } from "../../src/components/EntityMediaGallery";

const url = (letter: string) => `/api/read-model-media/${letter.repeat(64)}/content`;
test("item gallery removes old images and captions immediately across perspective, observer and item switches", async () => {
  const dom = new JSDOM('<div id="root"></div>', { url: "https://table.example.test/" });
  const previousWindow = globalThis.window, previousDocument = globalThis.document;
  Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true });
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  try {
    const secret = { scopeKey: "actor-a:item-a:dm", media: [{ contentUrl: url("a"), alt: "DM image", caption: "Private caption" }] };
    await act(async () => root.render(<ItemMediaGallery scopeKey={secret.scopeKey} view={secret} />));
    const oldImage = document.querySelector("img")!;
    assert.equal(oldImage.alt, "DM image");
    for (const scope of ["actor-a:item-a:player", "actor-b:item-a:player", "actor-b:item-b:player"]) {
      await act(async () => root.render(<ItemMediaGallery scopeKey={scope} view={secret} />));
      assert.equal(document.querySelector("img"), null);
      assert.doesNotMatch(document.body.textContent!, /Private|DM image/);
      assert.match(document.body.textContent!, /No image available/);
    }
    const player = { scopeKey: "actor-b:item-b:player", media: [{ contentUrl: url("b"), alt: "Permitted illustration", caption: "Known caption" }] };
    await act(async () => root.render(<ItemMediaGallery scopeKey={player.scopeKey} view={player} />));
    await act(async () => oldImage.dispatchEvent(new dom.window.Event("error")));
    assert.equal(document.querySelector("img")!.alt, "Permitted illustration");
    await act(async () => document.querySelector("img")!.dispatchEvent(new dom.window.Event("error")));
    assert.equal(document.querySelector("figcaption"), null);
    assert.match(document.body.textContent!, /No image available/);
    await act(async () => root.render(<ItemMediaGallery scopeKey="fresh" view={{ ...player, scopeKey: "fresh" }} />));
    assert.equal(document.querySelector("img")!.alt, "Permitted illustration");
  } finally {
    await act(async () => root.unmount());
    dom.window.close();
    Object.assign(globalThis, { window: previousWindow, document: previousDocument });
  }
});

test("item gallery admits only view-bound URLs and uses a neutral empty fallback", async () => {
  const dom = new JSDOM('<div id="root"></div>');
  const previousWindow = globalThis.window, previousDocument = globalThis.document;
  Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true });
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  try {
    const media = ["https://private.example/image.png", "/api/applications/fixture/entities/secret/media/visual-0/content", "javascript:alert(1)"]
      .map(contentUrl => ({ contentUrl, alt: "Rejected image", caption: "Rejected caption" }));
    await act(async () => root.render(<ItemMediaGallery scopeKey="player" view={{ scopeKey: "player", media }} />));
    assert.equal(document.querySelector("img"), null);
    assert.equal(document.querySelector("figcaption"), null);
    assert.doesNotMatch(document.body.textContent!, /Rejected/);
  } finally {
    await act(async () => root.unmount());
    dom.window.close();
    Object.assign(globalThis, { window: previousWindow, document: previousDocument });
  }
});
