import assert from "node:assert/strict";
import test from "node:test";
import { createHubReadScope } from "../src/server/hub-read-scope.js";
import { readGameServerContext } from "../src/server/game-server-context.js";

const listing = "http://localhost/api/applications/test/state-spaces/world/entities?limit=100";

test("a large view limits concurrent reads and gives each listing consumer an independent body", async () => {
  let active = 0; let peak = 0; let calls = 0;
  const fetchImpl = async () => {
    calls += 1; active += 1; peak = Math.max(peak, active);
    await new Promise((resolve) => setTimeout(resolve, 1));
    active -= 1;
    return Response.json({ items: [] });
  };
  const scope = createHubReadScope(fetchImpl);
  const copies = await Promise.all(Array.from({ length: 4 }, () => scope.fetch(listing)));
  assert.deepEqual(await Promise.all(copies.map((response) => response.json())),
    Array.from({ length: 4 }, () => ({ items: [] })));
  assert.equal(calls, 1);
  await Promise.all(Array.from({ length: 80 }, (_, index) => scope.fetch(`${listing}/${index}`)));
  assert.equal(peak, 8);
  await createHubReadScope(fetchImpl).fetch(listing);
  assert.equal(calls, 82, "a new view must reauthorize and read fresh state");
});

test("failed listings are not cached and oversized listings are read afresh", async () => {
  let calls = 0;
  const scope = createHubReadScope(async () => {
    calls += 1;
    return calls === 1 ? Response.json({}, { status: 403 }) : Response.json({ text: "x".repeat(70_000) });
  });
  await scope.fetch(listing); await scope.fetch(listing); await scope.fetch(listing);
  assert.equal(calls, 3);
});

test("rate limiting stops queued work and prevents a partial view from being accepted", async () => {
  let calls = 0;
  const scope = createHubReadScope(async () => { calls += 1; return Response.json({}, { status: 429 }); });
  await Promise.allSettled(Array.from({ length: 100 }, (_, index) => scope.fetch(`${listing}/${index}`)));
  assert.equal(calls, 8);
  assert.match(scope.failure, /server is busy/u);
  const result = await readGameServerContext({ serverOrigin: "http://localhost", fetchImpl: async () =>
    Response.json({}, { status: 429 }) });
  assert.equal(result.status, "unavailable");
  assert.match(result.message, /server is busy/u);
});

test("a superseded view never dispatches its queued requests", async () => {
  const controller = new AbortController();
  let calls = 0;
  const scope = createHubReadScope(async () => {
    calls += 1; controller.abort(); return Response.json({});
  });
  await Promise.allSettled(Array.from({ length: 100 }, (_, index) =>
    scope.fetch(`${listing}/${index}`, { signal: controller.signal })));
  assert.equal(calls, 1);
});
