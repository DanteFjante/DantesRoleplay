import assert from "node:assert/strict";
import test from "node:test";
import { objectConsumers, subscribeScopedChanges } from "../src/data/scoped-change-stream.ts";

class Source extends EventTarget {
  closed = false;
  readyState = 0;
  close() { this.closed = true; this.readyState = 2; }
  frame(name, value) { this.dispatchEvent(new MessageEvent(name, { data: JSON.stringify(value) })); }
}
const envelope = (perspective) => ({ applicationId: "dnd2024", stateSpaceId: "fixture",
  audience: { perspective } });
const notice = (cursor, qualifiedId = "dnd2024.object.inventory-item-instance-records") => ({
  contractVersion: 1, cursor, applicationId: "dnd2024", stateSpaceId: "fixture",
  object: { qualifiedId, version: 2 },
});

test("every Character and Item registered object reaches its browser consumers", () => {
  for (const id of ["instance-records", "definition-records", "recipe-record", "activity-record"]) {
    assert.deepEqual(objectConsumers(`dnd2024.object.inventory-item-${id}`),
      { item: true, character: true, world: true, known: true });
  }
  assert.deepEqual(objectConsumers("dnd2024.object.character-dossier-records"),
    { item: false, character: true, world: true, known: true });
  assert.equal(objectConsumers("future.object").known, false);
});

test("scope teardown closes the old stream, resets cursors and fences queued frames in both directions", () => {
  for (const perspectives of [["player", "dm"], ["dm", "player"]]) {
    const sources = [], urls = [], changes = [];
    const invalidations = [];
    const options = {
      createSource: (url) => { const source = new Source(); sources.push(source); urls.push(new URL(url, "http://localhost")); return source; },
      changed: (value) => changes.push(value.cursor), invalidate: (reason) => invalidations.push(reason),
      lifecycle: new EventTarget(),
    };
    const closeFirst = subscribeScopedChanges(envelope(perspectives[0]), options);
    sources[0].frame("invalidate", { reason: "connected" });
    sources[0].frame("object-change", notice(100));
    closeFirst();
    const closeSecond = subscribeScopedChanges(envelope(perspectives[1]), options);
    assert.equal(sources[0].closed, true);
    assert.deepEqual(urls.map((url) => url.searchParams.get("perspective")), perspectives);
    assert.ok(urls.every((url) => url.searchParams.get("cursor") === "0"));
    sources[0].frame("object-change", notice(101));
    sources[0].frame("invalidate", { reason: "late-private" });
    sources[1].frame("object-change", notice(1));
    sources[1].frame("object-change", notice(1));
    assert.deepEqual(changes, [100, 1]);
    assert.deepEqual(invalidations, []);
    closeSecond();
  }
});

test("disconnect invalidates, and bfcache resume reconnects without reviving the hidden stream", () => {
  const lifecycle = new EventTarget();
  const sources = [];
  const invalidations = [];
  let reconnects = 0;
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle, createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: (reason) => invalidations.push(reason), changed: () => {}, reconnected: () => { reconnects += 1; },
  });
  sources[0].dispatchEvent(new Event("error"));
  lifecycle.dispatchEvent(new Event("pagehide"));
  assert.equal(sources[0].closed, true);
  lifecycle.dispatchEvent(new Event("pageshow"));
  assert.equal(sources.length, 2);
  sources[1].frame("invalidate", { reason: "connected" });
  assert.equal(reconnects, 1, "page restoration revalidates even though it owns a new EventSource instance");
  sources[0].dispatchEvent(new Event("error"));
  assert.deepEqual(invalidations, ["stream-error", "pagehide"]);
  close();
  lifecycle.dispatchEvent(new Event("pageshow"));
  assert.equal(sources.length, 2);
});

test("a fast native reconnect cancels the watchdog and requests one bounded revalidation", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const lifecycle = new EventTarget();
  const sources = [];
  const invalidations = [];
  let reconnects = 0;
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle, createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: (reason) => invalidations.push(reason), changed: () => {}, reconnected: () => { reconnects += 1; },
  });
  sources[0].frame("invalidate", { reason: "connected" });
  sources[0].dispatchEvent(new Event("error"));
  sources[0].dispatchEvent(new Event("error"));
  assert.deepEqual(invalidations, ["stream-error"], "repeated EventSource errors are coalesced until reconnect");
  sources[0].frame("invalidate", { reason: "connected" });
  sources[0].frame("invalidate", { reason: "connected" });
  assert.equal(reconnects, 1, "only the first post-error connection requests revalidation");
  t.mock.timers.tick(300_000);
  assert.equal(sources.length, 1, "an authoritative connected frame cancels replacement");
  close();
});

test("an error before the first connected frame still recovers on that frame", () => {
  const sources = [];
  let reconnects = 0;
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {}, reconnected: () => { reconnects += 1; },
  });
  sources[0].dispatchEvent(new Event("error"));
  sources[0].frame("invalidate", { reason: "connected" });
  assert.equal(reconnects, 1);
  close();
});

test("a CONNECTING watchdog replaces an unconfirmed stream and fences old frames", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const sources = [], invalidations = [], changes = [];
  let reconnects = 0;
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: (reason) => invalidations.push(reason), changed: (value) => changes.push(value.cursor),
    reconnected: () => { reconnects += 1; },
  });
  sources[0].dispatchEvent(new Event("error"));
  sources[0].dispatchEvent(new Event("error"));
  t.mock.timers.tick(19_999);
  assert.equal(sources.length, 1);
  t.mock.timers.tick(1);
  assert.equal(sources.length, 2);
  sources[0].frame("invalidate", { reason: "connected" });
  sources[0].frame("object-change", notice(100));
  assert.equal(reconnects, 0);
  sources[1].frame("invalidate", { reason: "connected" });
  sources[1].frame("object-change", notice(1));
  assert.equal(reconnects, 1);
  assert.deepEqual(changes, [1]);
  assert.deepEqual(invalidations, ["stream-error"]);
  close();
});

test("a CLOSED source gets a prompt fresh attempt", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const sources = [];
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {},
  });
  sources[0].readyState = 2;
  sources[0].dispatchEvent(new Event("error"));
  t.mock.timers.tick(999);
  assert.equal(sources.length, 1);
  t.mock.timers.tick(1);
  assert.equal(sources.length, 2);
  close();
});

test("a CONNECTING source that becomes CLOSED upgrades one watchdog to the prompt retry", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const sources = [];
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {},
  });
  sources[0].dispatchEvent(new Event("error"));
  t.mock.timers.tick(5_000);
  sources[0].readyState = 2;
  sources[0].dispatchEvent(new Event("error"));
  sources[0].dispatchEvent(new Event("error"));
  t.mock.timers.tick(999);
  assert.equal(sources.length, 1);
  t.mock.timers.tick(1);
  assert.equal(sources.length, 2, "the CLOSED retry replaces the slower CONNECTING watchdog instead of adding a timer");
  t.mock.timers.tick(300_000);
  assert.equal(sources.length, 2, "the cancelled watchdog cannot later create another source");
  close();
});

test("a sustained outage beyond one minute recovers once from an authoritative connected frame", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const sources = [];
  let reconnects = 0;
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {}, reconnected: () => { reconnects += 1; },
  });
  sources[0].frame("invalidate", { reason: "connected" });
  for (const delay of [20_000, 20_000, 30_000]) {
    sources.at(-1).dispatchEvent(new Event("error"));
    t.mock.timers.tick(delay);
  }
  assert.equal(sources.length, 4, "a fresh source is still attempted after seventy seconds offline");
  sources.at(-1).frame("invalidate", { reason: "connected" });
  assert.equal(reconnects, 1);
  t.mock.timers.tick(300_000);
  assert.equal(sources.length, 4, "confirmed recovery cancels the pending watchdog");
  close();
});

test("CLOSED retries retain a bounded recovery attempt beyond one minute", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const sources = [];
  let reconnects = 0;
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {}, reconnected: () => { reconnects += 1; },
  });
  sources[0].frame("invalidate", { reason: "connected" });
  for (const delay of [1_000, 2_000, 4_000, 8_000, 15_000, 30_000, 60_000]) {
    sources.at(-1).readyState = 2;
    sources.at(-1).dispatchEvent(new Event("error"));
    t.mock.timers.tick(delay);
  }
  assert.equal(sources.length, 8, "a fresh CLOSED-source attempt remains after 120 seconds offline");
  sources.at(-1).frame("invalidate", { reason: "connected" });
  assert.equal(reconnects, 1);
  close();
});

test("unconfirmed CONNECTING replacements exhaust their finite multi-minute budget without churn", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const sources = [];
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {},
  });
  for (const delay of [20_000, 20_000, 30_000, 60_000, 120_000, 120_000, 120_000, 120_000]) {
    sources.at(-1).dispatchEvent(new Event("error"));
    t.mock.timers.tick(delay);
  }
  assert.equal(sources.length, 9);
  sources.at(-1).dispatchEvent(new Event("error"));
  t.mock.timers.tick(300_000);
  assert.equal(sources.length, 9, "persistent rejection cannot cause unbounded fresh connections");
  close();
});

test("an authoritative connected frame resets the fresh-source budget for a later outage", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const sources = [];
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle: new EventTarget(), createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {},
  });
  for (const delay of [20_000, 20_000, 30_000, 60_000, 120_000, 120_000, 120_000, 120_000]) {
    sources.at(-1).dispatchEvent(new Event("error"));
    t.mock.timers.tick(delay);
  }
  sources.at(-1).frame("invalidate", { reason: "connected" });
  sources.at(-1).dispatchEvent(new Event("error"));
  t.mock.timers.tick(20_000);
  assert.equal(sources.length, 10, "confirmed recovery allows a later independent outage to recover");
  close();
});

test("pageshow starts a fresh bounded retry budget after an exhausted visible session", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const lifecycle = new EventTarget(), sources = [];
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle, createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {},
  });
  for (const delay of [20_000, 20_000, 30_000, 60_000, 120_000, 120_000, 120_000, 120_000]) {
    sources.at(-1).dispatchEvent(new Event("error"));
    t.mock.timers.tick(delay);
  }
  sources.at(-1).dispatchEvent(new Event("error"));
  lifecycle.dispatchEvent(new Event("pageshow"));
  t.mock.timers.tick(300_000);
  assert.equal(sources.length, 9, "duplicate pageshow while visible neither reconnects nor resets exhaustion");
  lifecycle.dispatchEvent(new Event("pagehide"));
  lifecycle.dispatchEvent(new Event("pageshow"));
  assert.equal(sources.length, 10, "pageshow creates exactly one new scoped source");
  sources.at(-1).dispatchEvent(new Event("error"));
  t.mock.timers.tick(20_000);
  assert.equal(sources.length, 11, "the restored visibility session owns a new bounded watchdog budget");
  close();
});

test("pagehide and scope disposal cancel pending terminal retries", (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const lifecycle = new EventTarget(), sources = [];
  const close = subscribeScopedChanges(envelope("player"), {
    lifecycle, createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: () => {}, changed: () => {},
  });
  sources[0].readyState = 2;
  sources[0].dispatchEvent(new Event("error"));
  lifecycle.dispatchEvent(new Event("pagehide"));
  t.mock.timers.tick(300_000);
  assert.equal(sources.length, 1);
  lifecycle.dispatchEvent(new Event("pageshow"));
  assert.equal(sources.length, 2);
  sources[1].readyState = 2;
  sources[1].dispatchEvent(new Event("error"));
  close();
  t.mock.timers.tick(300_000);
  assert.equal(sources.length, 2);
});
