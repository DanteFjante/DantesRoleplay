import assert from "node:assert/strict";
import test from "node:test";

import { ReferenceResourceOwner } from "../../src/data/reference-resource-owner";
import { characterScope, createHubStore, referenceActions } from "../../src/data/hub-store";
import type { InstalledContentPage } from "../../src/server/effective-content";
import { integrationEnvelope } from "../fixtures/item-integration";
import { ViewReadError } from "../../src/data/view-read-client";

const fingerprint = "A".repeat(64);
const page: InstalledContentPage = {
  resolutionFingerprint: fingerprint, extensions: [], records: [], availableKinds: [],
  totalCount: 0, nextCursor: null, coverage: "ready", notices: [],
};

test("reference in-flight coordination accepts a bounded server-valid continuation without shortening Redux identity", async () => {
  const store = createHubStore();
  let receivedCursor = "";
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async (request) => { receivedCursor = request.cursor ?? ""; return page; },
  });
  const cursor = "c".repeat(4_096);
  await owner.loadContent(integrationEnvelope(), {
    ownerId: null, kinds: [], query: "", cursor, expectedResolutionFingerprint: fingerprint,
  });
  assert.equal(receivedCursor, cursor);
  assert.equal(Object.keys(store.getState().references.content).length, 1);
});

test("a denied continuation clears every previously confirmed content page in its authorized scope", async () => {
  const store = createHubStore();
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async (request) => request.cursor
      ? Promise.reject(new ViewReadError("authorization", "not available"))
      : { ...page, nextCursor: "next" },
  });
  const envelope = integrationEnvelope();
  await owner.loadContent(envelope, { ownerId: null, kinds: [], query: "", cursor: null, expectedResolutionFingerprint: null });
  await assert.rejects(owner.loadContent(envelope, {
    ownerId: null, kinds: [], query: "", cursor: "next", expectedResolutionFingerprint: fingerprint,
  }), (error) => error instanceof ViewReadError && error.category === "authorization");
  assert.equal(Object.keys(store.getState().references.content).length, 0);
});

test("a player scope rejects a DM rules publication even when its transport is otherwise valid", async () => {
  const store = createHubStore();
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "dm", articleCount: 0, rules: [] }),
    readContent: async () => page,
  });
  await assert.rejects(owner.loadRules(integrationEnvelope("player")),
    (error) => error instanceof ViewReadError && error.category === "authorization");
  assert.equal(store.getState().references.rules, null);
});

test("a player scope rejects a contradictory public publication containing a DM-only rule", async () => {
  const store = createHubStore();
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 1, rules: [{
        id: "rule.private", resolutionKey: "rule.private", title: "Private", summary: "Private", order: 1,
        section: { id: "private", label: "Private", order: 1 }, blocks: [], examples: [], relatedRuleIds: [],
        relatedContent: [], citations: [], authority: { mechanicIds: [], procedureIds: [] }, visibility: "dm",
        source: { ownerId: "base", label: "Core", classification: "core" },
      }] }),
    readContent: async () => page,
  });
  await assert.rejects(owner.loadRules(integrationEnvelope("player")),
    (error) => error instanceof ViewReadError && error.category === "authorization");
  assert.equal(store.getState().references.rules, null);
});

test("rules publication must match the scope's campaign resolution evidence before it is committed", async () => {
  const store = createHubStore();
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: "B".repeat(64),
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async () => page,
  });
  const envelope = integrationEnvelope();
  envelope.objectQueries = { campaignSummary: { qualifiedQueryId: "dnd2024.query.campaign-summary",
    stateSpaceFingerprint: fingerprint, resolutionFingerprint: fingerprint, outputSchemaHash: fingerprint,
    resultFingerprint: fingerprint, sourceRevisionFingerprint: fingerprint } };
  await assert.rejects(owner.loadRules(envelope),
    (error) => error instanceof ViewReadError && error.category === "stale-data");
  assert.equal(store.getState().references.rules, null);
});

test("installed-content Redux retention cannot consume the Rules allocation when no publication is loaded", () => {
  const store = createHubStore();
  store.dispatch(referenceActions.scopeReplaced({ scope: "reference-scope" }));
  for (let index = 0; index < 17; index += 1) {
    const key = `page-${index}`;
    store.dispatch(referenceActions.contentRequestStarted({ scope: "reference-scope", key, requestToken: index + 1 }));
    store.dispatch({ type: referenceActions.contentCommitted.type,
      payload: { scope: "reference-scope", key, value: page },
      meta: { generation: 1, requestToken: index + 1, bytes: 524_288, confirmedAt: index + 1 },
    });
  }
  assert.ok(store.getState().references.retainedBytes <= 8 * 1024 * 1024);
  assert.ok(Object.keys(store.getState().references.content).length <= 16);
});

test("a transient refresh failure keeps the original confirmed timestamp rather than renewing stale content", async () => {
  const store = createHubStore();
  let available = true;
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async () => {
      if (!available) throw new ViewReadError("transport", "offline");
      return page;
    },
  });
  const envelope = integrationEnvelope();
  const request = { ownerId: null, kinds: [], query: "", cursor: null, expectedResolutionFingerprint: null };
  await owner.loadContent(envelope, request);
  const key = JSON.stringify([null, [], "", null, null]);
  const confirmedAt = store.getState().references.content[key]!.confirmedAt;
  available = false;
  await assert.rejects(owner.loadContent(envelope, request, undefined, false));
  assert.equal(store.getState().references.content[key]!.confirmedAt, confirmedAt);
});

test("an aborted newer joined content consumer cannot fence the original commit", async () => {
  const store = createHubStore();
  let resolvePage: ((value: InstalledContentPage) => void) | undefined;
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async () => new Promise<InstalledContentPage>((resolve) => { resolvePage = resolve; }),
  });
  const envelope = integrationEnvelope();
  const request = { ownerId: null, kinds: [], query: "", cursor: null, expectedResolutionFingerprint: null };
  const first = owner.loadContent(envelope, request);
  await new Promise((resolve) => setTimeout(resolve, 0));
  const controller = new AbortController();
  const newer = owner.loadContent(envelope, request, controller.signal);
  controller.abort();
  await assert.rejects(newer, (error) => error instanceof ViewReadError && error.category === "cancelled");
  resolvePage!(page);
  await first;
  assert.equal(Object.keys(store.getState().references.content).length, 1);
});

test("a pre-aborted joined content consumer cannot release the original flight key", async () => {
  const store = createHubStore();
  let resolvePage: ((value: InstalledContentPage) => void) | undefined;
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async () => new Promise<InstalledContentPage>((resolve) => { resolvePage = resolve; }),
  });
  const envelope = integrationEnvelope();
  const request = { ownerId: null, kinds: [], query: "", cursor: null, expectedResolutionFingerprint: null };
  const first = owner.loadContent(envelope, request);
  await new Promise((resolve) => setTimeout(resolve, 0));
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(owner.loadContent(envelope, request, controller.signal),
    (error) => error instanceof ViewReadError && error.category === "cancelled");
  resolvePage!(page);
  await first;
  assert.equal(Object.keys(store.getState().references.content).length, 1);
});

test("an obsolete scope cleanup cannot remove a newer same-query flight key", async () => {
  const store = createHubStore();
  let reads = 0;
  let rejectOld: ((reason: unknown) => void) | undefined;
  let resolveNew: ((value: InstalledContentPage) => void) | undefined;
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async () => {
      reads += 1;
      return new Promise<InstalledContentPage>((resolve, reject) => {
        if (reads === 1) rejectOld = reject;
        else if (reads === 2) resolveNew = resolve;
        else reject(new Error("A same-query current flight was duplicated."));
      });
    },
  });
  const envelope = integrationEnvelope();
  const request = { ownerId: null, kinds: [], query: "", cursor: null, expectedResolutionFingerprint: null };
  const oldRead = owner.loadContent(envelope, request);
  await new Promise((resolve) => setTimeout(resolve, 0));
  owner.replaceScope(envelope, true);
  const currentRead = owner.loadContent(envelope, request);
  rejectOld!(new DOMException("Cancelled", "AbortError"));
  await assert.rejects(oldRead, (error) => error instanceof ViewReadError && error.category === "cancelled");
  const joinedCurrentRead = owner.loadContent(envelope, request);
  resolveNew!(page);
  await Promise.all([currentRead, joinedCurrentRead]);
  assert.equal(reads, 2);
  assert.equal(Object.keys(store.getState().references.content).length, 1);
});

test("a denied content query fences every other in-flight query in that scoped collection", async () => {
  const store = createHubStore();
  let resolveFirst: ((value: InstalledContentPage) => void) | undefined;
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async (request) => request.query === "denied"
      ? Promise.reject(new ViewReadError("authorization", "not available"))
      : new Promise<InstalledContentPage>((resolve) => { resolveFirst = resolve; }),
  });
  const envelope = integrationEnvelope();
  const first = owner.loadContent(envelope, { ownerId: null, kinds: [], query: "first", cursor: null,
    expectedResolutionFingerprint: null });
  await new Promise((resolve) => setTimeout(resolve, 0));
  await assert.rejects(owner.loadContent(envelope, { ownerId: null, kinds: [], query: "denied", cursor: null,
    expectedResolutionFingerprint: null }), (error) => error instanceof ViewReadError && error.category === "authorization");
  resolveFirst!(page);
  await assert.rejects(first, (error) => error instanceof ViewReadError && error.category === "cancelled");
  assert.equal(Object.keys(store.getState().references.content).length, 0);
});

test("an obsolete authorization result cannot fence a current scope's content flight", async () => {
  const store = createHubStore();
  let rejectOld: ((reason: unknown) => void) | undefined;
  let resolveCurrent: ((value: InstalledContentPage) => void) | undefined;
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => ({ applicationId: "dnd2024", resolutionFingerprint: fingerprint,
      rulesFingerprint: fingerprint, audience: "public", articleCount: 0, rules: [] }),
    readContent: async (request) => request.query === "old"
      ? new Promise<InstalledContentPage>((_, reject) => { rejectOld = reject; })
      : new Promise<InstalledContentPage>((resolve) => { resolveCurrent = resolve; }),
  });
  const envelope = integrationEnvelope();
  const oldRead = owner.loadContent(envelope, { ownerId: null, kinds: [], query: "old", cursor: null,
    expectedResolutionFingerprint: null });
  await new Promise((resolve) => setTimeout(resolve, 0));
  // The Redux scope fence can advance before an old network promise settles.
  // The owner must not let that obsolete authorization outcome clear current work.
  store.dispatch(referenceActions.scopeReplaced({ scope: characterScope(envelope), force: true }));
  const currentRead = owner.loadContent(envelope, { ownerId: null, kinds: [], query: "current", cursor: null,
    expectedResolutionFingerprint: null });
  rejectOld!(new ViewReadError("authorization", "old scope denied"));
  await assert.rejects(oldRead, (error) => error instanceof ViewReadError && error.category === "authorization");
  resolveCurrent!(page);
  await currentRead;
  assert.equal(Object.keys(store.getState().references.content).length, 1);
});

test("an aborted newer joined Rules consumer cannot fence the original commit", async () => {
  const store = createHubStore();
  let resolveRules: ((value: { applicationId: "dnd2024"; resolutionFingerprint: string; rulesFingerprint: string;
    audience: "public"; articleCount: number; rules: never[] }) => void) | undefined;
  const owner = new ReferenceResourceOwner({
    store,
    readRules: async () => new Promise((resolve) => { resolveRules = resolve; }),
    readContent: async () => page,
  });
  const envelope = integrationEnvelope();
  const first = owner.loadRules(envelope);
  await new Promise((resolve) => setTimeout(resolve, 0));
  const controller = new AbortController();
  const newer = owner.loadRules(envelope, controller.signal);
  controller.abort();
  await assert.rejects(newer, (error) => error instanceof ViewReadError && error.category === "cancelled");
  resolveRules!({ applicationId: "dnd2024", resolutionFingerprint: fingerprint, rulesFingerprint: fingerprint,
    audience: "public", articleCount: 0, rules: [] });
  await first;
  assert.equal(store.getState().references.rules?.value.articleCount, 0);
});
