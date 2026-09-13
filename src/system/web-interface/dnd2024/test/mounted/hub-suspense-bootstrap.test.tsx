import assert from "node:assert/strict";
import test from "node:test";
import React, { act, StrictMode, Suspense } from "react";
import { JSDOM } from "jsdom";
import { DndInformationHub } from "../../src/components/DndInformationHub";
import { characterScope, createHubStore, referenceActions, tableScope } from "../../src/data/hub-store";
import type { ReadyHubEnvelope } from "../../src/data/hub-types";
import { resolveAudience } from "../support/audience-policy.js";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { HUB_SOURCE_REVISION, hubSource } from "../support/hub-source.js";

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));
function envelope(perspective: "dm" | "player") {
  const projected = projectHubEnvelope(hubSource, HUB_SOURCE_REVISION, resolveAudience({
    authenticatedUserId: "principal.dm.fixture", authenticatedUserEmail: "", requestedPerspective: perspective,
    dmPrincipalIds: ["principal.dm.fixture"],
  })) as ReadyHubEnvelope;
  return { ...projected, applicationId: "dnd2024", stateSpaceId: "state.fixture" };
}

test("revealing the suspended hub never replays its initial bootstrap over a newer perspective", async () => {
  const dom = new JSDOM('<!doctype html><html><body><div id="root"></div></body></html>', { url: "http://localhost/#view?tab=world&section=overview" });
  const keys = ["document", "window", "navigator", "HTMLElement", "Element", "Node", "Event", "MouseEvent", "IS_REACT_ACT_ENVIRONMENT"] as const;
  const previous = keys.map((key) => Object.getOwnPropertyDescriptor(globalThis, key));
  for (const key of keys) Object.defineProperty(globalThis, key, { configurable: true, writable: true,
    value: key === "IS_REACT_ACT_ENVIRONMENT" ? true : (dom.window as unknown as Record<string, unknown>)[key] });
  dom.window.scrollTo = () => undefined;
  const { createRoot } = await import("react-dom/client");
  const root = createRoot(document.getElementById("root")!);
  const store = createHubStore();
  const dispatch = store.dispatch;
  let referenceInitializations = 0;
  store.dispatch = ((action) => {
    if (referenceActions.scopeReplaced.match(action)) referenceInitializations++;
    return dispatch(action);
  }) as typeof store.dispatch;
  let initial = envelope("dm");
  const player = envelope("player");
  const loadEnvelope = async () => player;
  let pending: Promise<void> | null = null;
  let reveal!: () => void;
  function PendingPanel() { if (pending) throw pending; return <p>Selected panel ready</p>; }
  const render = () => root.render(<StrictMode><Suspense fallback={<p>Selected panel loading</p>}>
    <DndInformationHub initialEnvelope={initial} store={store} currentBootstrapManaged loadEnvelope={loadEnvelope} />
    <PendingPanel />
  </Suspense></StrictMode>);
  try {
    await act(async () => { render(); await tick(); });
    await act(async () => {
      document.querySelector<HTMLButtonElement>('[title="Use the Player perspective"]')!.click();
      await tick();
    });
    const beforeReveal = store.getState();
    const beforeReferenceInitializations = referenceInitializations;
    assert.equal(beforeReveal.confirmed.scope, characterScope(player));
    pending = new Promise<void>((resolve) => { reveal = resolve; });
    await act(async () => { render(); await tick(); });
    assert.match(document.body.textContent!, /Selected panel loading/);
    await act(async () => { pending = null; reveal(); await tick(); });
    assert.match(document.body.textContent!, /Selected panel ready/);
    assert.equal(store.getState().confirmed.scope, characterScope(player), "Suspense must retain the user's newer viewing perspective.");
    assert.equal(store.getState().table.scope, beforeReveal.table.scope);
    assert.equal(store.getState().table.generation, beforeReveal.table.generation, "Reveal is not a new bootstrap.");
    assert.equal(store.getState().confirmed.generation, beforeReveal.confirmed.generation);
    assert.equal(referenceInitializations, beforeReferenceInitializations,
      "A panel reveal must not force reference scope initialization again.");

    initial = { ...envelope("dm"), revision: "replacement-bootstrap" };
    await act(async () => { render(); await tick(); });
    assert.equal(store.getState().confirmed.scope, characterScope(initial), "A genuinely replaced bootstrap prop is still admitted.");
    assert.equal(store.getState().table.scope, tableScope(initial));
  } finally {
    await act(async () => root.unmount()); dom.window.close();
    keys.forEach((key, index) => { if (previous[index]) Object.defineProperty(globalThis, key, previous[index]!);
      else Reflect.deleteProperty(globalThis, key); });
  }
});
