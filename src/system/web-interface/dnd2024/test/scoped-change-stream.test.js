import assert from "node:assert/strict";
import test from "node:test";
import { objectConsumers, subscribeScopedChanges } from "../src/data/scoped-change-stream.ts";

class Source extends EventTarget {
  closed = false;
  close() { this.closed = true; }
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
  const close = subscribeScopedChanges(envelope("dm"), {
    lifecycle, createSource: () => { const source = new Source(); sources.push(source); return source; },
    invalidate: (reason) => invalidations.push(reason), changed: () => {},
  });
  sources[0].dispatchEvent(new Event("error"));
  lifecycle.dispatchEvent(new Event("pagehide"));
  assert.equal(sources[0].closed, true);
  lifecycle.dispatchEvent(new Event("pageshow"));
  assert.equal(sources.length, 2);
  sources[0].dispatchEvent(new Event("error"));
  assert.deepEqual(invalidations, ["stream-error", "pagehide"]);
  close();
  lifecycle.dispatchEvent(new Event("pageshow"));
  assert.equal(sources.length, 2);
});
