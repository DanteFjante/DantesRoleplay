import assert from "node:assert/strict";
import test from "node:test";

import { JSDOM } from "jsdom";
import React, { act, type ReactNode } from "react";

import {
  CharacterResourceOwner,
  TableResourceOwner,
} from "../../src/data/object-resources";
import { characterScope, createHubStore, hubActions, peekCharacterFacet } from "../../src/data/hub-store";
import { subscribeScopedChanges } from "../../src/data/scoped-change-stream";
import { ViewReadError } from "../../src/data/view-read-client";
import type {
  InventoryContainerResult,
  PartyMemberReadModel,
  ReadyHubEnvelope,
  Perspective,
} from "../../src/data/hub-types";
import { projectHubEnvelope } from "../support/hub-envelope.js";
import { resolveAudience } from "../support/audience-policy.js";
import { HUB_SOURCE_REVISION, hubSource } from "../support/hub-source.js";

const dmPrincipal = "principal.dm.fixture";

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
      worlds: [{ id: projected.world.id, name: projected.world.name,
        campaigns: [{ id: "campaign.fixture.eldervale", name: projected.campaign.title }] }],
    },
    objectQueries: { campaignSummary: {
      qualifiedQueryId: "dnd2024.query.campaign-summary",
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      outputSchemaHash: "3".repeat(64), resultFingerprint: "4".repeat(64),
      sourceRevisionFingerprint: "A".repeat(64),
    } },
  };
}

async function mount(element: ReactNode, url = "https://table.example.test/") {
  const dom = new JSDOM("<!doctype html><html><body><div id=\"root\"></div></body></html>", {
    url,
  });
  const previous = {
    document: globalThis.document,
    Element: globalThis.Element,
    Event: globalThis.Event,
    HTMLElement: globalThis.HTMLElement,
    MessageEvent: globalThis.MessageEvent,
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
    MessageEvent: dom.window.MessageEvent,
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

async function settle(milliseconds = 0) {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, milliseconds)); });
}

async function settleUntil(condition: () => boolean, timeoutMs = 250) {
  const deadline = Date.now() + timeoutMs;
  while (!condition() && Date.now() < deadline) await settle(10);
}

// Drive only the scheduler delay under test. Rendering/React timers keep their
// real clock, so a slow test worker cannot accidentally consume a retry slot
// while the fixture is still clicking through the setup workflow.
function controlledRecoveryDelay(delay: number) {
  const originalSet = globalThis.setTimeout;
  const originalClear = globalThis.clearTimeout;
  const queued = new Map<ReturnType<typeof setTimeout>, () => void>();
  globalThis.setTimeout = ((callback: (...args: unknown[]) => void, milliseconds?: number, ...args: unknown[]) => {
    if (milliseconds !== delay) return originalSet(callback, milliseconds, ...args);
    const timer = {} as ReturnType<typeof setTimeout>;
    queued.set(timer, () => callback(...args));
    return timer;
  }) as typeof setTimeout;
  globalThis.clearTimeout = ((timer: ReturnType<typeof setTimeout>) => {
    if (!queued.delete(timer)) originalClear(timer);
  }) as typeof clearTimeout;
  return {
    fire() {
      assert.equal(queued.size, 1, "exactly one recovery timer should be pending");
      const [timer, callback] = queued.entries().next().value!;
      queued.delete(timer); callback();
    },
    restore() { globalThis.setTimeout = originalSet; globalThis.clearTimeout = originalClear; queued.clear(); },
  };
}

function button(container: Element, label: string) {
  const found = [...container.querySelectorAll("button")]
    .find((candidate) => candidate.textContent?.trim() === label || candidate.getAttribute("aria-label") === label);
  assert.ok(found, `Expected ${label} button`);
  return found as HTMLButtonElement;
}

async function click(control: HTMLButtonElement) {
  await act(async () => {
    control.dispatchEvent(new window.MouseEvent("click", { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

async function enterTextarea(control: HTMLTextAreaElement, value: string) {
  await act(async () => {
    const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, "value")?.set;
    setter?.call(control, value);
    control.dispatchEvent(new window.Event("input", { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 0));
  });
}

class FakeEventSource {
  readyState = 1;
  closed = false;
  private readonly listeners = new Map<string, Set<(event: { type: string; data?: string }) => void>>();

  addEventListener(type: string, listener: EventListenerOrEventListenerObject | null) {
    if (!listener) return;
    const callback = typeof listener === "function" ? listener : (event: Event) => listener.handleEvent(event);
    const entries = this.listeners.get(type) ?? new Set();
    entries.add(callback as (event: { type: string; data?: string }) => void);
    this.listeners.set(type, entries);
  }

  removeEventListener(type: string, listener: EventListenerOrEventListenerObject | null) {
    if (!listener) return;
    const entries = this.listeners.get(type);
    if (!entries) return;
    entries.delete(listener as unknown as (event: { type: string; data?: string }) => void);
  }

  private dispatch(type: string, data?: string) {
    for (const listener of this.listeners.get(type) ?? []) listener({ type, data });
  }

  close() {
    this.closed = true;
    this.readyState = 2;
  }

  frame(name: string, value: unknown) {
    this.dispatch(name, JSON.stringify(value));
  }

  fail() {
    this.dispatch("error");
  }
}

function inventory(actor: PartyMemberReadModel): InventoryContainerResult {
  return {
    status: "ready", failureCategory: null, diagnosticId: "inventory-stream-recovered",
    data: {
      container: { id: actor.id, label: actor.name }, state: "ready", reasons: [], notices: [],
      items: [
        { id: "item.stream.recovered-one", name: "Recovered compass", definition: { id: "definition.compass", label: "Compass" },
          quantity: 1, quantityState: "value", slot: "carried", order: 0, equipmentSlots: [], equipmentSlotsKnown: true,
          classification: "item", isContainer: false, containerState: "value", unavailableFields: [], parentItemId: null,
          depth: 1, childCount: 0, deeperContentsOmitted: false },
        { id: "item.stream.recovered-two", name: "Recovered lantern", definition: { id: "definition.lantern", label: "Lantern" },
          quantity: 2, quantityState: "value", slot: "carried", order: 1, equipmentSlots: [], equipmentSlotsKnown: true,
          classification: "item", isContainer: false, containerState: "value", unavailableFields: [], parentItemId: null,
          depth: 1, childCount: 0, deeperContentsOmitted: false },
      ],
      limits: { contentsDepth: 1, directComplete: true, recursiveComplete: false },
      wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
      walletState: { status: "complete", reason: null },
      projection: { stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64) },
    },
  };
}

test("real stream recovery rehydrates Party Inventory and fences old sources", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  const actor = initial.party[0]!;
  actor.portrait = { imageUrl: "/portrait/stream-stale", alt: "Stale portrait", width: 100, height: 150 };
  actor.portraitCoverage = "confirmed";
  const recovered = structuredClone(initial);
  const recoveredActor = recovered.party.find((member) => member.id === actor.id)!;
  delete recoveredActor.portrait;
  delete recoveredActor.characterSheet;
  recoveredActor.sheet = [];
  recoveredActor.sheetState = { status: "empty", source: "canonical", data: [] };
  const hydrated: PartyMemberReadModel = {
    ...actor,
    portrait: { imageUrl: "/portrait/stream-recovered", alt: "Recovered portrait", width: 100, height: 150 },
    portraitCoverage: "confirmed",
    sheetState: { status: "ready", source: "canonical", data: actor.sheet },
  };
  const recoveredInventory = inventory(actor);
  const store = createHubStore();
  const characterOwner = new CharacterResourceOwner({
    readSheet: async () => {
      sheetReads += 1;
      sheetReadStates.push(online ? "online" : "offline");
      if (!online) throw new Error("The host is restarting.");
      return hydrated;
    },
    readDetails: async () => hydrated,
    readInventory: async () => {
      inventoryReads += 1;
      inventoryReadStates.push(online ? "online" : "offline");
      if (!online) throw new Error("The host is restarting.");
      return recoveredInventory;
    },
    readConfirmed: (facet, request, maximumAgeMs) => peekCharacterFacet(
      store.getState(), characterScope(request.envelope), request.actorId, facet, maximumAgeMs),
    clearConfirmed: () => store.dispatch(hubActions.characterFacetsCleared(undefined)),
    clearScope: () => store.dispatch(hubActions.scopeCleared(undefined)),
  });
  let online = true;
  let sheetReads = 0;
  let inventoryReads = 0;
  let bootstrapReads = 0;
  let forcedBootstrapReads = 0;
  let finishBootstrap: (() => void) | null = null;
  const sheetReadStates: string[] = [];
  const inventoryReadStates: string[] = [];
  const sources: FakeEventSource[] = [];
  const invalidations: string[] = [];
  const observedChanges: number[] = [];
  const sourceSubscriber = (scope: ReadyHubEnvelope) => subscribeScopedChanges(scope, {
    createSource: () => {
      const source = new FakeEventSource();
      sources.push(source);
      return source as unknown as EventSource;
    },
    invalidate: (reason) => {
      invalidations.push(reason);
      characterOwner.invalidateAll(reason);
      window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", { detail: { reason } }));
    },
    changed: (notice) => {
      observedChanges.push(notice.cursor);
      characterOwner.invalidateObject(notice.object.qualifiedId);
      window.dispatchEvent(new window.CustomEvent("dnd2024-object-changed", { detail: notice }));
    },
    reconnected: () => window.dispatchEvent(new window.Event("dnd2024-stream-reconnected")),
  });
  const route = `https://table.example.test/#view?tab=party&section=inventory&character=${encodeURIComponent(actor.id)}`;
  const mounted = await mount(<DndInformationHub
    initialEnvelope={initial}
    store={store}
    loadContent={async () => ({ records: [], extensions: [] })}
    loadRules={async () => ({ applicationId: "dnd2024", resolutionFingerprint: "2".repeat(64), rulesFingerprint: "3".repeat(64), audience: "dm", articleCount: 0, rules: [] })}
    loadEnvelope={async (perspective, _campaignId, preferCached) => {
      bootstrapReads += 1;
      if (!preferCached) {
        forcedBootstrapReads += 1;
        return new Promise<ReadyHubEnvelope>((resolve) => {
          finishBootstrap = () => {
            online = true;
            characterOwner.replaceScope(recovered, true);
            resolve(recovered);
          };
        });
      }
      return perspective === "player" ? envelope("player") : initial;
    }}
    loadCharacterSheet={(view, actorId, signal) => characterOwner.loadSheet({ envelope: view, actorId }, signal)}
    loadCharacterDetails={(view, actorId, signal) => characterOwner.loadDetails({ envelope: view, actorId }, signal)}
    loadCharacterInventory={(view, actorId, signal) => characterOwner.loadInventory({ envelope: view, actorId }, signal)}
    subscribeChanges={sourceSubscriber}
  />, route);
  try {
    await settle(40);
    assert.match(mounted.container.textContent ?? "", /Recovered compass/);
    assert.match(mounted.container.textContent ?? "", /Recovered lantern/);
    assert.equal(mounted.container.querySelector(".character-roster__portrait img")?.getAttribute("src"), "/portrait/stream-recovered");
    const readsBeforeOutage = { inventory: inventoryReads, sheet: sheetReads };

    online = false;
    await act(async () => sources[0]!.fail());
    await settle(10);
    assert.deepEqual(invalidations, ["stream-error"]);
    assert.equal(mounted.container.querySelector(".character-roster__portrait img"), null,
      "the real owner clears confirmed portrait authority during stream invalidation");

    await act(async () => sources[0]!.frame("invalidate", { reason: "connected" }));
    await settle(10);
    assert.equal(forcedBootstrapReads, 1);
    assert.equal(finishBootstrap !== null, true);

    await act(async () => sources[0]!.frame("object-change", {
      contractVersion: 1, cursor: 1, applicationId: initial.applicationId, stateSpaceId: initial.stateSpaceId,
      object: { qualifiedId: "dnd2024.object.campaign-summary", version: 3 },
    }));
    assert.deepEqual(observedChanges, [1]);
    await act(async () => finishBootstrap!());
    await settle(50);
    assert.equal(bootstrapReads, 2,
      "a notice received during recovery queues one newer bootstrap");
    assert.match(mounted.container.textContent ?? "", /Recovered compass/);
    assert.match(mounted.container.textContent ?? "", /Recovered lantern/);
    assert.equal(mounted.container.querySelector(".character-roster__portrait img")?.getAttribute("src"), "/portrait/stream-recovered");
    assert.deepEqual(inventoryReadStates.slice(readsBeforeOutage.inventory), ["offline", "offline", "online"],
      "inventory performs bounded invalidation failures then one recovery read");
    assert.deepEqual(sheetReadStates.slice(readsBeforeOutage.sheet), ["offline", "offline", "online"],
      "sheet performs bounded invalidation failures then one recovery read");
    await settle(50);
    assert.equal(inventoryReads - readsBeforeOutage.inventory, 3, "a settled recovery does not loop inventory reads");
    assert.equal(sheetReads - readsBeforeOutage.sheet, 3, "a settled recovery does not loop sheet reads");
    assert.match(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u,
      "the older recovery result cannot acknowledge a notice received during its read");
    await act(async () => finishBootstrap!());
    await settle(50);
    assert.doesNotMatch(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u,
      "a fresh read started after the replay acknowledges it without navigation or reload");
    assert.equal(bootstrapReads, 2, "settled historical replay does not loop bootstrap reads");

    await click(button(mounted.container, "Player"));
    await settle(20);
    assert.equal(sources[0]!.closed, true, "perspective change must dispose the old source");
    assert.equal(sources.length, 2, "recovery uses one source before the perspective change creates the next source");
    const readsAfterScope = bootstrapReads;
    await act(async () => sources[0]!.frame("object-change", {
      contractVersion: 1, cursor: 2, applicationId: initial.applicationId, stateSpaceId: initial.stateSpaceId,
      object: { qualifiedId: "dnd2024.object.campaign-summary", version: 4 },
    }));
    assert.equal(bootstrapReads, readsAfterScope, "old-source frames cannot trigger a new read");
  } finally {
    const readsBeforeCleanup = bootstrapReads;
    const lastSource = sources.at(-1);
    await mounted.cleanup();
    assert.equal(lastSource?.closed, true);
    const stateAfterCleanup = store.getState();
    await act(async () => {
      lastSource?.frame("invalidate", { reason: "connected" });
      lastSource?.frame("object-change", {
        contractVersion: 1, cursor: 3, applicationId: initial.applicationId, stateSpaceId: initial.stateSpaceId,
        object: { qualifiedId: "dnd2024.object.campaign-summary", version: 5 },
      });
      lastSource?.fail();
    });
    assert.equal(bootstrapReads, readsBeforeCleanup, "disposed sources cannot schedule recovery");
    assert.strictEqual(store.getState(), stateAfterCleanup, "disposed sources cannot mutate Redux");
  }
});

test("a real scope invalidation frame rereads party knowledge admissions", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  const actor = initial.party[0]!;
  actor.knowledge = [{ id: "knowledge.stale", kind: "rumor", stance: "doubted", text: "An old admission" }];
  const recovered = structuredClone(initial);
  const recoveredActor = recovered.party.find((member) => member.id === actor.id)!;
  recoveredActor.knowledge = [{ id: "knowledge.current", kind: "testimony", stance: "known", text: "A refreshed admission" }];
  let reads = 0;
  const sources: FakeEventSource[] = [];
  const mounted = await mount(<DndInformationHub
    initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    loadEnvelope={async () => {
      reads += 1;
      return structuredClone(recovered);
    }}
    subscribeChanges={(scope) => subscribeScopedChanges(scope, {
      createSource: () => {
        const source = new FakeEventSource();
        sources.push(source);
        return source as unknown as EventSource;
      },
      invalidate: (reason) => window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", {
        detail: { reason },
      })),
      changed: () => {},
    })}
  />, `https://table.example.test/#view?tab=party&section=knowledge&character=${encodeURIComponent(actor.id)}`);
  try {
    await settle(40);
    assert.match(mounted.container.textContent ?? "", /An old admission/);
    const readsBeforeInvalidation = reads;
    await act(async () => sources[0]!.frame("invalidate", { reason: "dependency-fallback" }));
    await settleUntil(() => reads === readsBeforeInvalidation + 1);
    assert.match(mounted.container.textContent ?? "", /A refreshed admission/);
    assert.doesNotMatch(mounted.container.textContent ?? "", /An old admission/);
    assert.doesNotMatch(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u,
      "the production stream invalidation is acknowledged by the automatic reread");
  } finally {
    await mounted.cleanup();
  }
});

test("continuous notices through the delayed trailing read remain bounded and pending", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  const completions: Array<(fail?: boolean) => void> = [];
  let reads = 0;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    loadRules={async () => ({ applicationId: "dnd2024", resolutionFingerprint: "2".repeat(64), rulesFingerprint: "3".repeat(64), audience: "dm", articleCount: 0, rules: [] })}
    recoveryRetryDelaysMs={[5, 5, 5]}
    recoveryTrailingDelayMs={10}
    loadEnvelope={async () => {
      reads += 1;
      return new Promise<ReadyHubEnvelope>((resolve, reject) => completions.push((fail = false) => fail
        ? reject(new ViewReadError("transport", "Trailing refresh unavailable."))
        : resolve(structuredClone(initial))));
    }} />);
  let cursor = 0;
  const notice = () => window.dispatchEvent(new window.CustomEvent("dnd2024-object-changed", {
    detail: { cursor: ++cursor, object: { qualifiedId: "dnd2024.object.campaign-summary", version: 4 } },
  }));
  try {
    await act(async () => { notice(); });
    for (let index = 0; index < 4; index += 1) {
      await settle(10);
      assert.equal(reads, index + 1);
      await act(async () => { notice(); });
      await act(async () => completions[index]!());
    }
    await settle(20);
    assert.equal(reads, 5, "four immediate reads permit exactly one delayed trailing catch-up");
    await act(async () => { notice(); });
    await act(async () => completions[4]!(true));
    await settle(30);
    assert.equal(reads, 5, "a failed trailing read cannot retry or start a sixth request");
    assert.match(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u,
      "the trailing read still predates a notice and must not hide it when the budget ends");
  } finally { await mounted.cleanup(); }
});

test("the one trailing read preserves a notice newer than a manual acknowledgement", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  const completions: Array<() => void> = [];
  let reads = 0;
  const clock = controlledRecoveryDelay(12_345);
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryTrailingDelayMs={12_345}
    loadEnvelope={async () => {
      reads += 1;
      return new Promise<ReadyHubEnvelope>((resolve) => completions.push(() => resolve(structuredClone(initial))));
    }} />);
  const notice = () => window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", {
    detail: { reason: "stream-recovery" },
  }));
  try {
    await act(async () => notice());
    for (let index = 0; index < 4; index += 1) {
      await settleUntil(() => reads === index + 1);
      await act(async () => notice());
      await act(async () => completions[index]!());
    }
    await click(button(mounted.container, "Refresh view"));
    assert.equal(reads, 5);
    await act(async () => completions[4]!());
    await act(async () => notice());
    await act(async () => clock.fire());
    await settleUntil(() => reads === 6);
    assert.equal(reads, 6, "the single automatic trailing slot covers the notice newer than the manual read");
    await act(async () => notice());
    await act(async () => completions[5]!());
    await settle(80);
    assert.equal(reads, 6, "a notice during the trailing read cannot start another automatic request in the cycle");
    assert.match(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u,
      "the final uncovered notice remains truthful after the bounded cycle");
  } finally { await mounted.cleanup(); clock.restore(); }
});

test("the production fallback and item frame cadence retries its cancelled Table owner read", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  let reads = 0;
  const tableOwner = new TableResourceOwner({
    readCampaign: async (_request, signal) => {
      reads += 1;
      if (reads > 1) return structuredClone(initial);
      return new Promise((_, reject) => signal.addEventListener("abort", () =>
        reject(new DOMException("Table resource invalidated", "AbortError")), { once: true }));
    },
    validateCampaign: (value): value is ReadyHubEnvelope => Boolean(value && typeof value === "object" &&
      (value as { status?: unknown }).status === "ready"),
    readFactionPage: async () => ({}) as never,
    readCampaignDetails: async () => ({}) as never,
    readCampaignContext: async () => ({}) as never,
  });
  let source: FakeEventSource | null = null;
  const subscribeChanges = (scope: ReadyHubEnvelope) => subscribeScopedChanges(scope, {
    createSource: () => {
      source = new FakeEventSource();
      return source as unknown as EventSource;
    },
    invalidate: (reason) => {
      tableOwner.invalidateAll(reason);
      window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", { detail: { reason } }));
    },
    changed: (notice) => {
      tableOwner.invalidateObject(notice.object.qualifiedId);
      window.dispatchEvent(new window.CustomEvent("dnd2024-object-changed", { detail: notice }));
    },
  });
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[5, 5, 5]}
    loadEnvelope={async (perspective, campaignId) => await tableOwner.loadCampaign({ perspective, campaignId }) as ReadyHubEnvelope}
    subscribeChanges={subscribeChanges} />);
  const fallback = (cursor: number) => source!.frame("invalidate", { reason: "dependency-fallback", cursor });
  const item = (cursor: number) => source!.frame("object-change", {
    contractVersion: 1, cursor, applicationId: initial.applicationId, stateSpaceId: initial.stateSpaceId,
    object: { qualifiedId: "dnd2024.object.inventory-item-instance-records", version: cursor % 2 === 0 ? 1 : 2 },
  });
  const frames = (...cursors: number[]) => {
    for (const cursor of cursors) {
      if ([1, 2, 3, 8, 9, 14, 15, 20, 21, 26, 27].includes(cursor)) fallback(cursor);
      else item(cursor);
    }
  };
  try {
    await act(async () => { frames(1); });
    await settle(10);
    assert.equal(reads, 1);
    await act(async () => { frames(...Array.from({ length: 30 }, (_, index) => index + 2)); });
    await settleUntil(() => reads === 2 &&
      !/server changed|live connection was interrupted/u.test(mounted.container.textContent ?? ""));
    assert.equal(reads, 2, "the first owner-cancelled read receives one bounded fresh retry after the replay burst");
    assert.doesNotMatch(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u,
      "the fresh retry covers cursor 27; item-only cursors 28–31 do not keep a scope banner alive");
  } finally { await mounted.cleanup(); }
});

test("repeated same-scope Table owner cancellations exhaust the finite retry budget", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  let reads = 0;
  const tableOwner = new TableResourceOwner({
    readCampaign: async (_request, signal) => {
      reads += 1;
      return new Promise((_, reject) => signal.addEventListener("abort", () =>
        reject(new DOMException("Table resource invalidated", "AbortError")), { once: true }));
    },
    validateCampaign: (value): value is ReadyHubEnvelope => Boolean(value && typeof value === "object" &&
      (value as { status?: unknown }).status === "ready"),
    readFactionPage: async () => ({}) as never,
    readCampaignDetails: async () => ({}) as never,
    readCampaignContext: async () => ({}) as never,
  });
  let source: FakeEventSource | null = null;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[5, 5, 5]}
    loadEnvelope={async (perspective, campaignId) => await tableOwner.loadCampaign({ perspective, campaignId }) as ReadyHubEnvelope}
    subscribeChanges={(scope) => subscribeScopedChanges(scope, {
      createSource: () => {
        source = new FakeEventSource();
        return source as unknown as EventSource;
      },
      invalidate: (reason) => {
        tableOwner.invalidateAll(reason);
        window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", { detail: { reason } }));
      },
      changed: () => {},
    })} />);
  const fallback = (cursor: number) => source!.frame("invalidate", { reason: "dependency-fallback", cursor });
  try {
    await act(async () => fallback(1));
    await settleUntil(() => reads === 1);
    for (let cursor = 2; cursor <= 4; cursor += 1) {
      await act(async () => fallback(cursor));
      await settleUntil(() => reads === cursor);
    }
    await act(async () => fallback(5));
    await settle(40);
    assert.equal(reads, 4, "three delayed retries are the complete cancellation budget");
    assert.match(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u,
      "an exhausted cycle keeps the uncovered change visible");
  } finally { await mounted.cleanup(); }
});

test("authorization failures and disposed cancellation timers never retry", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  let authorizationReads = 0;
  const authorizationOwner = new TableResourceOwner({
    readCampaign: async () => {
      authorizationReads += 1;
      throw new ViewReadError("authorization", "Campaign access was denied.");
    },
    validateCampaign: (_value): _value is ReadyHubEnvelope => true,
    readFactionPage: async () => ({}) as never,
    readCampaignDetails: async () => ({}) as never,
    readCampaignContext: async () => ({}) as never,
  });
  const authorizationMount = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[10, 10, 10]}
    loadEnvelope={async (perspective, campaignId) => await authorizationOwner.loadCampaign({ perspective, campaignId }) as ReadyHubEnvelope} />);
  try {
    await act(async () => window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", {
      detail: { reason: "stream-recovery" },
    })));
    await settle(60);
    assert.equal(authorizationReads, 1, "authorization loss is not a recoverable owner cancellation");
  } finally { await authorizationMount.cleanup(); }

  let cancellationReads = 0;
  const cancellationOwner = new TableResourceOwner({
    readCampaign: async (_request, signal) => {
      cancellationReads += 1;
      return new Promise((_, reject) => signal.addEventListener("abort", () =>
        reject(new DOMException("Table resource invalidated", "AbortError")), { once: true }));
    },
    validateCampaign: (_value): _value is ReadyHubEnvelope => true,
    readFactionPage: async () => ({}) as never,
    readCampaignDetails: async () => ({}) as never,
    readCampaignContext: async () => ({}) as never,
  });
  let source: FakeEventSource | null = null;
  const cancellationMount = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[50, 50, 50]}
    loadEnvelope={async (perspective, campaignId) => await cancellationOwner.loadCampaign({ perspective, campaignId }) as ReadyHubEnvelope}
    subscribeChanges={(scope) => subscribeScopedChanges(scope, {
      createSource: () => {
        source = new FakeEventSource();
        return source as unknown as EventSource;
      },
      invalidate: (reason) => {
        cancellationOwner.invalidateAll(reason);
        window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", { detail: { reason } }));
      },
      changed: () => {},
    })} />);
  let disposed = false;
  try {
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 1 }));
    await settleUntil(() => cancellationReads === 1);
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 2 }));
    await settle(10);
    await cancellationMount.cleanup();
    disposed = true;
    await new Promise((resolve) => setTimeout(resolve, 80));
    assert.equal(cancellationReads, 1, "disposing the scope retires its pending cancellation retry");
  } finally {
    if (!disposed) await cancellationMount.cleanup();
  }
});

test("a perspective scope change retires the prior scope's cancellation retry", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  let dmReads = 0;
  let playerReads = 0;
  const tableOwner = new TableResourceOwner({
    readCampaign: async (request, signal) => {
      if (request.perspective === "player") {
        playerReads += 1;
        return envelope("player");
      }
      dmReads += 1;
      return new Promise((_, reject) => signal.addEventListener("abort", () =>
        reject(new DOMException("Table resource invalidated", "AbortError")), { once: true }));
    },
    validateCampaign: (value): value is ReadyHubEnvelope => Boolean(value && typeof value === "object" &&
      (value as { status?: unknown }).status === "ready"),
    readFactionPage: async () => ({}) as never,
    readCampaignDetails: async () => ({}) as never,
    readCampaignContext: async () => ({}) as never,
  });
  let source: FakeEventSource | null = null;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[80, 80, 80]}
    loadEnvelope={async (perspective, campaignId) => await tableOwner.loadCampaign({ perspective, campaignId }) as ReadyHubEnvelope}
    subscribeChanges={(scope) => subscribeScopedChanges(scope, {
      createSource: () => {
        source = new FakeEventSource();
        return source as unknown as EventSource;
      },
      invalidate: (reason) => {
        tableOwner.invalidateAll(reason);
        window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", { detail: { reason } }));
      },
      changed: () => {},
    })} />);
  try {
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 1 }));
    await settleUntil(() => dmReads === 1);
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 2 }));
    await settle(10);
    await click(button(mounted.container, "Player"));
    await settle(110);
    assert.equal(dmReads, 1, "the retired DM scope cannot run its delayed cancellation retry");
    assert.equal(playerReads, 1, "the explicit perspective request remains the sole new-scope read");
  } finally { await mounted.cleanup(); }
});

test("a manual refresh that covers a failed recovery cancels its delayed retry", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  let reads = 0;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[50, 50, 50]}
    loadEnvelope={async () => {
      reads += 1;
      if (reads === 1) throw new ViewReadError("transport", "The host is restarting.");
      return structuredClone(initial);
    }} />);
  try {
    await act(async () => window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", {
      detail: { reason: "stream-recovery" },
    })));
    await settleUntil(() => reads === 1);
    await click(button(mounted.container, "Refresh view"));
    await settle(80);
    assert.equal(reads, 2, "the acknowledged manual read consumes the pending timer's exact notice");
    assert.doesNotMatch(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u);
  } finally { await mounted.cleanup(); }
});

test("a newer notice survives an older manual acknowledgement while a retry timer exists", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  let reads = 0;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[50, 50, 50]}
    loadEnvelope={async () => {
      reads += 1;
      if (reads === 1) throw new ViewReadError("transport", "The host is restarting.");
      return structuredClone(initial);
    }} />);
  const notice = () => window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", {
    detail: { reason: "stream-recovery" },
  }));
  try {
    await act(async () => notice());
    await settleUntil(() => reads === 1);
    await click(button(mounted.container, "Refresh view"));
    assert.equal(reads, 2, "the manual read acknowledges the first notice");
    await act(async () => notice());
    await settleUntil(() => reads === 3);
    assert.equal(reads, 3, "the retained timer compares authority with the newest notice before retiring");
    assert.doesNotMatch(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u);
  } finally { await mounted.cleanup(); }
});

test("a cancellation retry and fallback notice wait behind a campaign write", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  let reads = 0;
  let writes = 0;
  let writeSignal: AbortSignal | null = null;
  let finishWrite: (() => void) | null = null;
  const tableOwner = new TableResourceOwner({
    readCampaign: async (_request, signal) => {
      reads += 1;
      if (reads > 2) return structuredClone(initial);
      return new Promise((_, reject) => signal.addEventListener("abort", () =>
        reject(new DOMException("Table resource invalidated", "AbortError")), { once: true }));
    },
    validateCampaign: (value): value is ReadyHubEnvelope => Boolean(value && typeof value === "object" &&
      (value as { status?: unknown }).status === "ready"),
    readFactionPage: async () => ({}) as never,
    readCampaignDetails: async () => ({}) as never,
    readCampaignContext: async () => ({}) as never,
  });
  let source: FakeEventSource | null = null;
  const clock = controlledRecoveryDelay(23_456);
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryRetryDelaysMs={[23_456, 23_456, 23_456]}
    loadEnvelope={async (perspective, campaignId) => await tableOwner.loadCampaign({ perspective, campaignId }) as ReadyHubEnvelope}
    writeCampaignPremise={async ({ premise }, signal) => {
      writes += 1;
      writeSignal = signal;
      await new Promise<void>((resolve) => { finishWrite = resolve; });
      return { premise, applied: true, replayed: false, noOp: false,
        operationId: "operation.scheduler-write", sourceRevisionFingerprint: "B".repeat(64) };
    }}
    subscribeChanges={(scope) => subscribeScopedChanges(scope, {
      createSource: () => {
        source = new FakeEventSource();
        return source as unknown as EventSource;
      },
      invalidate: (reason) => {
        tableOwner.invalidateAll(reason);
        window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", { detail: { reason } }));
      },
      changed: () => {},
    })} />);
  try {
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 1 }));
    await settleUntil(() => reads === 1);
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 2 }));
    await settle(5);
    await click(button(mounted.container, "Campaign"));
    await click(button(mounted.container, "Edit campaign premise"));
    await enterTextarea(mounted.container.querySelector("#campaign-premise-draft") as HTMLTextAreaElement,
      "A write owns this refresh window.");
    await click(button(mounted.container, "Save premise"));
    assert.equal(writes, 1);
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 3 }));
    await act(async () => clock.fire());
    await settle(150);
    assert.equal(reads, 1, "neither the retry timer nor fallback recovery competes with the pending write");
    assert.equal(writeSignal?.aborted, false, "automatic recovery cannot abort the write owner");
    await act(async () => finishWrite!());
    await settleUntil(() => reads === 2);
    await act(async () => source!.frame("invalidate", { reason: "dependency-fallback", cursor: 4 }));
    await settleUntil(() => reads === 3);
    await settle(50);
    assert.equal(reads, 3, "a fallback that cancels the post-write bootstrap receives one separately owned read");
    assert.equal(writes, 1, "stream recovery never resubmits the write");
    assert.doesNotMatch(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u);
  } finally { await mounted.cleanup(); clock.restore(); }
});

test("disposing the hub cancels its queued delayed trailing refresh", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  const completions: Array<() => void> = [];
  let reads = 0;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    recoveryTrailingDelayMs={100}
    loadEnvelope={async () => {
      reads += 1;
      return new Promise<ReadyHubEnvelope>((resolve) => completions.push(() => resolve(structuredClone(initial))));
    }} />);
  const notice = () => window.dispatchEvent(new window.CustomEvent("dnd2024-view-invalidated", {
    detail: { reason: "stream-recovery" },
  }));
  let disposed = false;
  try {
    await act(async () => { notice(); });
    for (let index = 0; index < 4; index += 1) {
      await settle(10);
      assert.equal(reads, index + 1);
      await act(async () => { notice(); });
      await act(async () => completions[index]!());
    }
    assert.equal(reads, 4);
    await mounted.cleanup();
    disposed = true;
    await new Promise((resolve) => setTimeout(resolve, 130));
    assert.equal(reads, 4, "the retired scope cannot start its delayed trailing request");
  } finally {
    if (!disposed) await mounted.cleanup();
  }
});

test("a superseded recovery completion cannot discard a notice queued by same-scope navigation", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const { navigateItemRoute } = await import("../../src/data/item-view-route");
  const initial = envelope("dm");
  const completions: Array<() => void> = [];
  let reads = 0;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial}
    loadContent={async () => ({ records: [], extensions: [] })}
    loadEnvelope={async () => {
      reads += 1;
      return new Promise<ReadyHubEnvelope>((resolve) => completions.push(() => resolve(structuredClone(initial))));
    }} />);
  let cursor = 0;
  const notice = () => window.dispatchEvent(new window.CustomEvent("dnd2024-object-changed", {
    detail: { cursor: ++cursor, object: { qualifiedId: "dnd2024.object.campaign-summary", version: 4 } },
  }));
  try {
    await act(async () => { notice(); });
    await settle(10);
    assert.equal(reads, 1);
    await act(async () => navigateItemRoute({ kind: "inventory", characterId: initial.party[0]!.id,
      campaignId: initial.contextSelection!.selectedCampaignId, perspective: "dm" }));
    await act(async () => { notice(); });
    await act(async () => completions[0]!());
    await settle(10);
    assert.equal(reads, 2, "obsolete recovery must preserve and schedule the queued newer notice");
    assert.match(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u);
    await act(async () => completions[1]!());
    await settle(20);
    assert.doesNotMatch(mounted.container.textContent ?? "", /server changed|live connection was interrupted/u);
  } finally { await mounted.cleanup(); }
});

test("an already-aborted character cache read rejects without touching any facet", async () => {
  const current = envelope("dm");
  const actor = current.party[0]!;
  const store = createHubStore();
  const scope = characterScope(current);
  const confirmedAt = Date.now();
  const sheet = { ...actor, sheetState: { status: "ready" as const, source: "canonical" as const, data: actor.sheet } };
  const details = { ...sheet, backstory: actor.backstory, origin: actor.origin };
  const inventoryValue = inventory(actor);
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  for (const [facet, value, requestToken] of [
    ["sheet", sheet, 101], ["details", details, 102],
  ] as const) {
    store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet, requestToken }));
    store.dispatch({
      type: hubActions.characterFacetCommitted.type, payload: { scope, actorId: actor.id, facet, value },
      meta: { generation: 1, requestToken, confirmedAt, bytes: 100 },
    });
  }
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "inventory", requestToken: 103 }));
  store.dispatch({
    type: hubActions.inventoryCommitted.type, payload: { scope, actorId: actor.id, value: inventoryValue },
    meta: { generation: 1, requestToken: 103, confirmedAt, bytes: 100 },
  });
  const reads = { sheet: 0, details: 0, inventory: 0 };
  let cacheReads = 0;
  const owner = new CharacterResourceOwner({
    readSheet: async () => { reads.sheet += 1; return sheet; },
    readDetails: async () => { reads.details += 1; return details; },
    readInventory: async () => { reads.inventory += 1; return inventoryValue; },
    readConfirmed: (facet, request, maximumAgeMs) => {
      cacheReads += 1;
      return peekCharacterFacet(
        store.getState(), characterScope(request.envelope), request.actorId, facet, maximumAgeMs);
    },
  });
  const request = { envelope: current, actorId: actor.id };
  for (const facet of ["sheet", "details", "inventory"] as const) {
    assert.ok(peekCharacterFacet(store.getState(), scope, actor.id, facet, 60_000),
      `${facet} seed is cache-eligible before the abort`);
  }
  for (const facet of ["sheet", "details", "inventory"] as const) {
    const controller = new AbortController();
    controller.abort();
    await assert.rejects(
      facet === "sheet" ? owner.loadSheetOutcome(request, controller.signal)
        : facet === "details" ? owner.loadDetailsOutcome(request, controller.signal)
          : owner.loadInventoryOutcome(request, controller.signal),
      (error: unknown) => (error as { category?: string })?.category === "cancelled",
    );
  }
  assert.deepEqual(reads, { sheet: 0, details: 0, inventory: 0 });
  assert.equal(cacheReads, 0, "a pre-aborted read does not consult the confirmed cache");
  assert.equal(store.getState().confirmed.facetsById[actor.id]?.sheet?.confirmedAt, confirmedAt);
  assert.equal(store.getState().confirmed.facetsById[actor.id]?.details?.confirmedAt, confirmedAt);
  assert.equal(store.getState().confirmed.facetsById[actor.id]?.inventory?.confirmedAt, confirmedAt);

  const postLookupController = new AbortController();
  let postLookupReads = 0;
  const postLookupOwner = new CharacterResourceOwner({
    readSheet: async () => { postLookupReads += 1; return sheet; },
    readDetails: async () => details,
    readInventory: async () => inventoryValue,
    readConfirmed: (facet, postLookupRequest, maximumAgeMs) => {
      postLookupController.abort();
      return peekCharacterFacet(store.getState(), characterScope(postLookupRequest.envelope),
        postLookupRequest.actorId, facet, maximumAgeMs);
    },
  });
  await assert.rejects(
    postLookupOwner.loadSheetOutcome(request, postLookupController.signal),
    (error: unknown) => (error as { category?: string })?.category === "cancelled",
    "an abort during cache lookup cannot return the cached value or start transport",
  );
  assert.equal(postLookupReads, 0);
});

test("an aborted mounted character read cannot commit its late result", async () => {
  const { DndInformationHub } = await import("../../src/components/DndInformationHub");
  const initial = envelope("dm");
  const actor = initial.party[0]!;
  const store = createHubStore();
  const scope = characterScope(initial);
  const oldSheet = { ...actor, detail: "confirmed before abort" };
  store.dispatch(hubActions.bootstrapCommitted({ scope, party: [actor] }));
  store.dispatch(hubActions.characterRequestStarted({ scope, actorId: actor.id, facet: "sheet", requestToken: 111 }));
  store.dispatch({
    type: hubActions.characterFacetCommitted.type,
    payload: { scope, actorId: actor.id, facet: "sheet", value: oldSheet },
    meta: { generation: 1, requestToken: 111, confirmedAt: 11_100, bytes: 100 },
  });
  let finish: ((value: PartyMemberReadModel) => void) | null = null;
  let requestSignal: AbortSignal | null = null;
  const lateSheet = { ...actor, detail: "must not commit after abort" };
  const route = `https://table.example.test/#view?tab=party&section=sheet&character=${encodeURIComponent(actor.id)}`;
  const mounted = await mount(<DndInformationHub initialEnvelope={initial} store={store}
    loadContent={async () => ({ records: [], extensions: [] })}
    loadCharacterSheet={async (_envelope, _actorId, signal) => {
      requestSignal = signal;
      return new Promise<PartyMemberReadModel>((resolve) => { finish = resolve; });
    }}
  />, route);
  try {
    await settle(20);
    assert.equal(store.getState().confirmed.requestsByKey[`${actor.id}|sheet`]?.requestToken !== undefined, true);
    // Move to a neighboring section while the root is still mounted. This
    // follows the normal effect cleanup path, aborting the owned request
    // without tearing down DOM globals before its late promise settles.
    await click(button(mounted.container, "Knowledge"));
    assert.equal(requestSignal?.aborted, true, "section change aborts the mounted character request");
    finish!(lateSheet);
    await settle(20);
    assert.equal(store.getState().confirmed.facetsById[actor.id]?.sheet?.value.detail, oldSheet.detail);
    assert.equal(store.getState().confirmed.requestsByKey[`${actor.id}|sheet`], undefined,
      "the canceled wrapper clears only its matching request marker");
  } finally {
    await mounted.cleanup();
  }
});
