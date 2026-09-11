import assert from "node:assert/strict";
import test from "node:test";
import { register } from "tsx/esm/api";

// These integration cases use the actual TypeScript Redux owners, just like
// the mounted suite. The ordinary node-suite runner has no global TS loader.
register();
const { ConnectedSourceWorkspace, MAXIMUM_CONNECTED_SOURCE_BYTES, connectedSourceScope, projectedSourceScope } = await import("../src/data/connected-source-workspace.ts");
const { createConnectedSourceOwner, createHubStore, hubActions } = await import("../src/data/hub-store.ts");
const { selectConnectedSource } = await import("../src/data/connected-source-state.ts");
const { CharacterResourceOwner } = await import("../src/data/object-resources.ts");

const source = (name = "original") => ({ campaign: { id: "campaign.test", name }, contextSelection: {} });
const cancelled = (read) => assert.throws(read, { name: "AbortError" });
const workspace = () => {
  const store = createHubStore();
  const owner = new ConnectedSourceWorkspace();
  owner.attach(createConnectedSourceOwner(store));
  return { owner, store };
};
const projected = (seat, campaign = "campaign.test") => ({
  applicationId: "dnd2024", stateSpaceId: "state.test", revision: campaign,
  audience: { seat, perspective: seat },
  contextSelection: { selectedWorldId: "world.test", selectedCampaignId: campaign },
  world: { id: "world.test" }, party: [{ id: `${seat}.actor`, isCurrent: true }],
  objectQueries: { campaignSummary: { resolutionFingerprint: "A".repeat(64) } },
});

test("T12 projection workspace identity includes the current actors and selected catalog resolution", () => {
  const raw = { applicationId: "dnd2024", stateSpaceId: "state", contextSelection: { selectedWorldId: "world" },
    campaign: { id: "campaign", projection: { resolutionFingerprint: "resolution" } },
    audience: { seat: "dm", perspective: "player" }, actor: { id: "actor.one" } };
  const envelope = { applicationId: "dnd2024", stateSpaceId: "state", world: { id: "world" }, revision: "campaign",
    audience: raw.audience, party: [{ id: "actor.one", isCurrent: true }],
    objectQueries: { campaignSummary: raw.campaign.projection } };
  const scope = connectedSourceScope(raw);
  assert.equal(scope, projectedSourceScope(envelope));
  for (const changed of [
    { ...envelope, applicationId: "foreign" }, { ...envelope, stateSpaceId: "foreign" },
    { ...envelope, world: { id: "foreign" } }, { ...envelope, revision: "foreign" },
    { ...envelope, audience: { seat: "player", perspective: "player" } },
    { ...envelope, audience: { seat: "dm", perspective: "dm" } },
    { ...envelope, party: [{ id: "actor.other", isCurrent: true }] },
    { ...envelope, objectQueries: { campaignSummary: { resolutionFingerprint: "changed" } } },
  ]) assert.notEqual(scope, projectedSourceScope(changed));
  assert.equal(connectedSourceScope({ ...raw, party: [{ id: "actor.two", current: true }, { id: "actor.one", current: true }] }),
    projectedSourceScope({ ...envelope, party: [{ id: "actor.one", isCurrent: true }, { id: "actor.two", isCurrent: true }] }));
});

test("T20 an equal-value same-scope bootstrap still fences an older Current read", () => {
  const { owner } = workspace();
  const initial = source();
  owner.replace("scope", initial);
  const old = owner.begin("scope", "current");
  owner.replace("scope", initial);
  cancelled(() => owner.current(old));
  cancelled(() => owner.update(old, source("stale")));
  assert.equal(owner.get("scope"), initial);
});

test("T20 a newer same-family read wins even if the older read completes last", async () => {
  const { owner } = workspace();
  owner.replace("scope", source());
  let finish;
  const old = owner.begin("scope", "current");
  const pending = new Promise((resolve) => { finish = resolve; }).then((value) => owner.update(old, value));
  const current = owner.begin("scope", "current");
  const newest = source("newest");
  owner.update(current, newest);
  finish(source("late"));
  await assert.rejects(pending, { name: "AbortError" });
  assert.equal(owner.get("scope"), newest);
});

test("independent context discovery and Current projection retain both disjoint patches", () => {
  const { owner } = workspace();
  owner.replace("scope", source());
  const current = owner.begin("scope", "current");
  const context = owner.begin("scope", "context");
  owner.update(context, { ...owner.current(context), contextSelection: { selectedWorldId: "world.test" } });
  owner.update(current, { ...owner.current(current), currentSituation: { status: "unavailable", message: "No scene" } });
  assert.equal(owner.get("scope").contextSelection.selectedWorldId, "world.test");
  assert.equal(owner.get("scope").currentSituation.message, "No scene");
});

test("overlapping Current and World producers start from the preceding committed directory", async () => {
  const { owner } = workspace();
  owner.replace("scope", { ...source(), locationDirectory: [{ id: "root", name: "Root" }] });

  const world = await owner.acquireMerge("scope", "locations");
  const currentPending = owner.acquireMerge("scope", "current");
  owner.update(world.ticket, { ...world.source,
    locationDirectory: [...world.source.locationDirectory, { id: "nested", name: "Nested" }],
    locationDirectoryComplete: true,
  });
  world.release();
  const current = await currentPending;
  assert.equal(current.source.locationDirectoryComplete, true);
  assert.deepEqual(current.source.locationDirectory.map((entry) => entry.id), ["root", "nested"]);
  owner.update(current.ticket, { ...current.source,
    locationDirectory: current.source.locationDirectory.map((entry) => entry.id === "nested"
      ? { ...entry, media: { portrait: { url: "/nested.webp" } } } : entry),
    currentLocationId: "nested",
  });
  current.release();

  const nextCurrent = await owner.acquireMerge("scope", "current");
  const nextWorldPending = owner.acquireMerge("scope", "locations");
  owner.update(nextCurrent.ticket, { ...nextCurrent.source, currentSituation: { status: "ready" } });
  nextCurrent.release();
  const nextWorld = await nextWorldPending;
  assert.equal(nextWorld.source.currentLocationId, "nested");
  assert.equal(nextWorld.source.locationDirectory.find((entry) => entry.id === "nested").media.portrait.url,
    "/nested.webp");
  nextWorld.release();
});

test("people and faction producers cannot erase one another's complete staged pages", async () => {
  const { owner } = workspace();
  owner.replace("scope", { ...source(), worldDirectory: { people: [], holdings: [], factions: [] } });
  const factions = await owner.acquireMerge("scope", "factions");
  const peoplePending = owner.acquireMerge("scope", "people");
  owner.update(factions.ticket, { ...factions.source,
    worldDirectory: { ...factions.source.worldDirectory, factions: [{ id: "faction.one" }] },
  });
  factions.release();
  const people = await peoplePending;
  assert.deepEqual(people.source.worldDirectory.factions.map((entry) => entry.id), ["faction.one"]);
  owner.update(people.ticket, { ...people.source,
    worldDirectory: { ...people.source.worldDirectory, people: [{ id: "person.one" }] },
  });
  people.release();
  assert.deepEqual(owner.get("scope").worldDirectory.factions.map((entry) => entry.id), ["faction.one"]);
  assert.deepEqual(owner.get("scope").worldDirectory.people.map((entry) => entry.id), ["person.one"]);
});

test("queued overlapping reads are fenced by consumer abort and bootstrap replacement", async () => {
  const { owner } = workspace();
  owner.replace("scope", source());
  const active = await owner.acquireMerge("scope", "locations");
  const controller = new AbortController();
  const aborted = owner.acquireMerge("scope", "current", controller.signal);
  controller.abort();
  await assert.rejects(aborted, { name: "AbortError" });
  const retired = owner.acquireMerge("scope", "people");
  owner.replace("scope", source("replacement"));
  await assert.rejects(retired, { name: "AbortError" });
  cancelled(() => owner.update(active.ticket, source("stale")));
  active.release();
  assert.equal(owner.get("scope").campaign.name, "replacement");
});

test("the newest queued same-family merge supersedes its predecessor and starts from the active commit", async () => {
  const { owner } = workspace();
  owner.replace("scope", source());
  const active = await owner.acquireMerge("scope", "locations");
  const older = owner.acquireMerge("scope", "current");
  const newer = owner.acquireMerge("scope", "current");
  await assert.rejects(older, { name: "AbortError" });
  owner.update(active.ticket, source("world-refreshed"));
  active.release();
  const current = await newer;
  assert.equal(current.source.campaign.name, "world-refreshed");
  current.release();
});

test("an active consumer abort releases the merge slot without letting its late finally release the successor", async () => {
  const { owner } = workspace();
  owner.replace("scope", source());
  const controller = new AbortController();
  const abandoned = await owner.acquireMerge("scope", "locations", controller.signal);
  const currentPending = owner.acquireMerge("scope", "current");
  controller.abort();
  const current = await currentPending;
  cancelled(() => owner.current(abandoned.ticket));
  cancelled(() => owner.update(abandoned.ticket, source("late-without-signal")));
  cancelled(() => owner.current(abandoned.ticket, controller.signal));
  cancelled(() => owner.update(abandoned.ticket, source("late"), controller.signal));

  abandoned.release();
  let thirdStarted = false;
  const peoplePending = owner.acquireMerge("scope", "people").then((lease) => {
    thirdStarted = true;
    return lease;
  });
  await Promise.resolve();
  assert.equal(thirdStarted, false, "an abandoned finally must not release the successor's slot");
  owner.update(current.ticket, source("current"));
  current.release();
  const people = await peoplePending;
  assert.equal(people.source.campaign.name, "current");
  people.release();
});

test("T20 abort and stream invalidation prevent stale mutation without deleting source inputs", () => {
  const { owner } = workspace();
  const initial = source();
  owner.replace("scope", initial);
  const controller = new AbortController();
  const aborted = owner.begin("scope", "current");
  controller.abort();
  cancelled(() => owner.update(aborted, source("aborted"), controller.signal));
  const stale = owner.begin("scope", "locations");
  owner.invalidate();
  cancelled(() => owner.update(stale, source("stale")));
  assert.equal(owner.get("scope"), initial);
  assert.equal(owner.current(owner.begin("scope", "current")), initial);
});

test("T12 authority replacement retains only one bounded raw projection lease", () => {
  const { owner, store } = workspace();
  owner.replace("player", source("player"));
  const player = owner.begin("player", "current");
  owner.replace("dm", source("private"));
  assert.equal(owner.get("player"), undefined);
  assert.equal(selectConnectedSource(store.getState(), "player"), null);
  cancelled(() => owner.current(player));
  assert.equal(owner.metrics().retainedScopes, 1);
  assert.ok(owner.metrics().retainedBytes < MAXIMUM_CONNECTED_SOURCE_BYTES);
  assert.equal(selectConnectedSource(store.getState(), "dm").campaign.name, "private");
  owner.replace("player", source("replacement"));
  cancelled(() => owner.update(player, source("retired")));
  assert.equal(owner.get("player").campaign.name, "replacement");
});

test("the hub Redux store owns deferred inputs and authority clear retires them with all tickets", () => {
  const { owner, store } = workspace();
  const input = { ...source(), rules: [{ id: "unneeded-rule" }] };
  owner.replace("scope", input);
  const ticket = owner.begin("scope", "current");
  const retained = selectConnectedSource(store.getState(), "scope");
  assert.equal(retained, owner.get("scope"));
  assert.deepEqual(retained.rules, [], "rules are projected elsewhere and are not staged raw");

  // Direct store scope teardown is also an authority boundary; it cannot
  // leave raw deferred inputs visible when a resource owner is not involved.
  store.dispatch(hubActions.scopeCleared());
  assert.equal(selectConnectedSource(store.getState(), "scope"), null);
  assert.deepEqual(owner.metrics(), { retainedScopes: 0, retainedBytes: 0 });
  cancelled(() => owner.current(ticket));
});

test("CharacterResourceOwner clears the retired scope before the next Redux source is staged", () => {
  const { owner, store } = workspace();
  const characterOwner = new CharacterResourceOwner({
    readSheet: async () => { throw new Error("not used"); },
    readDetails: async () => { throw new Error("not used"); },
    readInventory: async () => { throw new Error("not used"); },
    clearScope: () => owner.clear(),
  });
  const player = projected("player");
  const dm = projected("dm");
  owner.replace("player", source("player"));
  characterOwner.replaceScope(player, true);

  // This is the ordering in main.readEnvelope: retire old resource authority
  // first, then commit the newly authorized deferred source input.
  characterOwner.replaceScope(dm, true);
  assert.equal(selectConnectedSource(store.getState(), "player"), null);
  owner.replace("dm", source("dm"));
  assert.equal(selectConnectedSource(store.getState(), "dm").campaign.name, "dm");
});

test("raw projection staging rejects an oversized bootstrap before replacing the active scope", () => {
  const { owner } = workspace();
  owner.replace("scope", source());
  const oversized = source("x".repeat(MAXIMUM_CONNECTED_SOURCE_BYTES));
  assert.throws(() => owner.replace("oversized", oversized), /staging bound/u);
  assert.equal(owner.get("scope").campaign.name, "original");
});

test("all existing projection read families use independently fenced tickets", () => {
  const { owner } = workspace();
  owner.replace("scope", source());
  for (const family of ["context", "history", "lore", "locations", "people", "current", "campaign-details", "factions"]) {
    const older = owner.begin("scope", family);
    const newer = owner.begin("scope", family);
    cancelled(() => owner.current(older));
    assert.equal(owner.current(newer).campaign.name, "original");
  }
});
