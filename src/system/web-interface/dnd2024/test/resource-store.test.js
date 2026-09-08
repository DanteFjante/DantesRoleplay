import assert from "node:assert/strict";
import test from "node:test";

import { ResourceStore } from "../src/data/resource-store.ts";
import { ViewReadError } from "../src/data/view-read-client.ts";

const valid = (value) => Boolean(value && value.version === 1 && typeof value.value === "string");
const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((accept, decline) => { resolve = accept; reject = decline; });
  return { promise, resolve, reject };
};

test("simultaneous consumers share one same-key read and leave it active for the remaining consumer", async () => {
  const store = new ResourceStore();
  const pending = deferred();
  let reads = 0;
  const resource = store.define({ name: "campaign", cacheKey: ({ id }) => id,
    read: async () => { reads += 1; return pending.promise; }, validate: valid });
  const firstAbort = new AbortController();
  const first = resource.load({ id: "one" }, { signal: firstAbort.signal });
  const second = resource.load({ id: "one" });
  firstAbort.abort();
  pending.resolve({ version: 1, value: "shared" });
  await assert.rejects(first, (error) => error instanceof ViewReadError && error.category === "cancelled");
  assert.equal((await second).value.value, "shared");
  assert.equal(reads, 1);
});

test("separate resources and keys load concurrently without cancelling each other", async () => {
  const store = new ResourceStore();
  const campaignPending = deferred();
  const factionPending = deferred();
  const campaign = store.define({ name: "campaign", cacheKey: ({ id }) => id,
    read: () => campaignPending.promise, validate: valid });
  const factions = store.define({ name: "factions", cacheKey: ({ id }) => id,
    read: () => factionPending.promise, validate: valid });
  const first = campaign.load({ id: "one" });
  const second = campaign.load({ id: "two" });
  const third = factions.load({ id: "one" });
  campaignPending.resolve({ version: 1, value: "campaign" });
  factionPending.resolve({ version: 1, value: "factions" });
  assert.deepEqual((await Promise.all([first, second, third])).map((result) => result.value.value),
    ["campaign", "campaign", "factions"]);
});

test("scope invalidation fences an old response even when its reader ignores cancellation", async () => {
  const store = new ResourceStore();
  const pending = deferred();
  const resource = store.define({ name: "campaign", cacheKey: ({ id }) => id,
    read: () => pending.promise, validate: valid });
  const states = [];
  resource.subscribe({ id: "old" }, (state) => states.push(state.status));
  const old = resource.load({ id: "old" });
  store.invalidateAll();
  pending.resolve({ version: 1, value: "private-old-scope" });
  await assert.rejects(old, (error) => error instanceof ViewReadError && error.category === "cancelled");
  assert.equal(resource.peek({ id: "old" }), null);
  assert.deepEqual(states, ["unloaded", "loading", "unloaded"]);
});

test("a failed refresh retains last-good data as stale and never caches the failed response", async () => {
  const store = new ResourceStore();
  let fail = false;
  const resource = store.define({ name: "campaign", cacheKey: ({ id }) => id, retryDelayMs: 0,
    read: async () => {
      if (fail) throw new ViewReadError("transport", "temporary failure");
      return { version: 1, value: "last-good" };
    }, validate: valid });
  await resource.load({ id: "one" });
  fail = true;
  await assert.rejects(resource.load({ id: "one" }), /temporary failure/u);
  const state = resource.state({ id: "one" });
  assert.equal(state.status, "stale");
  assert.equal(state.data.value, "last-good");
  assert.equal(resource.peek({ id: "one" }).value.value, "last-good");
});

test("retention is bounded by entry count, bytes, freshness, and invalid response shape", async () => {
  const store = new ResourceStore({ maximumEntries: 2, maximumRetainedBytes: 1000 });
  const resource = store.define({ name: "campaign", cacheKey: ({ id }) => id, maximumAgeMs: 0,
    maximumEntryBytes: 100, read: async ({ id }) => ({ version: 1, value: id }), validate: valid });
  await resource.load({ id: "one" });
  assert.equal(resource.peek({ id: "one" }), null, "expired entries are not returned as fresh");
  assert.equal(resource.state({ id: "one" }).data.value, "one",
    "the last valid value remains available to an allowed refresh");

  const incompatible = store.define({ name: "factions", cacheKey: ({ id }) => id,
    maximumEntryBytes: 10, read: async () => ({ version: 1, value: "too-large" }), validate: valid });
  await assert.rejects(incompatible.load({ id: "one" }),
    (error) => error instanceof ViewReadError && error.category === "incompatible-data");
  assert.equal(incompatible.peek({ id: "one" }), null);

  const countStore = new ResourceStore({ maximumEntries: 2, maximumRetainedBytes: 1000 });
  const counted = countStore.define({ name: "counted", cacheKey: ({ id }) => id,
    read: async ({ id }) => ({ version: 1, value: id }), validate: valid });
  for (const id of ["one", "two", "three"]) await counted.load({ id });
  assert.equal(counted.peek({ id: "one" }), null);
  assert.notEqual(counted.peek({ id: "two" }), null);
  assert.notEqual(counted.peek({ id: "three" }), null);

  const byteStore = new ResourceStore({ maximumEntries: 4, maximumRetainedBytes: 100 });
  const bytes = byteStore.define({ name: "bytes", cacheKey: ({ id }) => id, maximumEntryBytes: 100,
    read: async ({ id }) => ({ version: 1, value: id.repeat(30) }), validate: valid });
  await bytes.load({ id: "a" });
  await bytes.load({ id: "b" });
  assert.equal(bytes.peek({ id: "a" }), null, "least-recently-used data is evicted by total bytes");
  assert.notEqual(bytes.peek({ id: "b" }), null);
});

test("expired refreshes preserve last-good data and expose bounded cache counters", async () => {
  const originalNow = Date.now;
  let now = 1_000;
  Date.now = () => now;
  try {
    let fail = false;
    const store = new ResourceStore({ maximumEntries: 1, maximumRetainedBytes: 1_000 });
    const resource = store.define({ name: "measured", cacheKey: ({ id }) => id, maximumAgeMs: 10,
      retryDelayMs: 0, read: async ({ id }) => {
        if (fail) throw new ViewReadError("transport", "offline");
        return { version: 1, value: id };
      }, validate: valid });

    await resource.load({ id: "one" }, { preferCached: true });
    assert.equal(resource.peek({ id: "one" }).value.value, "one");
    now += 11;
    assert.equal(resource.peek({ id: "one" }), null);
    fail = true;
    await assert.rejects(resource.load({ id: "one" }), /offline/u);
    assert.equal(resource.state({ id: "one" }).status, "stale");
    assert.equal(resource.state({ id: "one" }).data.value, "one");
    fail = false;
    await resource.load({ id: "two" });
    resource.invalidate({ id: "two" }, "object-change");

    const metrics = store.metrics();
    assert.ok(metrics.hits >= 1);
    assert.ok(metrics.misses >= 3);
    assert.equal(metrics.expiries, 1);
    assert.equal(metrics.invalidationsByReason["object-change"], 1);
    assert.equal(metrics.retainedEntries, 0);
    assert.equal(metrics.retainedBytes, 0);
    assert.equal(metrics.activeRequests, 0);
  } finally { Date.now = originalNow; }
});

test("cache metrics count in-flight sharing and capacity eviction without retaining request keys", async () => {
  const store = new ResourceStore({ maximumEntries: 1, maximumRetainedBytes: 1_000 });
  const pending = deferred();
  const resource = store.define({ name: "measured", cacheKey: ({ id }) => id,
    read: async ({ id }) => id === "shared" ? pending.promise : { version: 1, value: id }, validate: valid });
  const first = resource.load({ id: "shared" });
  const second = resource.load({ id: "shared" });
  assert.equal(store.metrics().activeRequests, 1);
  pending.resolve({ version: 1, value: "shared" });
  await Promise.all([first, second]);
  await resource.load({ id: "replacement" });
  const metrics = store.metrics();
  assert.equal(metrics.inFlightShares, 1);
  assert.equal(metrics.evictions, 1);
  assert.equal(metrics.invalidationsByReason.capacity, 1);
  assert.equal(metrics.retainedEntries, 1);
  assert.ok(metrics.retainedBytes > 0);
  assert.doesNotMatch(JSON.stringify(metrics), /shared|replacement/u);
});
