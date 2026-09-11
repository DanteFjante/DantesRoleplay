import assert from "node:assert/strict";
import test from "node:test";
import { projectPartyKnowledge, readRegisteredPartyKnowledge } from "../src/server/party-knowledge.js";

const scope = { campaignId: "campaign.fixture", worldId: "world.fixture", perspective: "player" };
const entry = { knowledgeId: "knowledge.fixture", documentRevision: "revision.1", text: "A disputed fact.",
  stance: "mixed", presentationKind: "statement", admissions: [
    { actorId: "actor.one", actorName: "One", stance: "known", source: "explicit" },
    { actorId: "actor.two", actorName: "Two", stance: "doubted", source: "baseline", scopeId: "world.fixture" },
  ] };
const data = (overrides = {}) => {
  const result = { status: "ready", audience: "party", campaignId: scope.campaignId,
    worldId: scope.worldId, entries: [entry], locations: [], sourceRevision: "5".repeat(64),
    coverage: "complete", fieldCoverage: "complete", ...overrides };
  return result;
};

test("shared knowledge preserves a single canonical row and the individual admission states", () => {
  const result = projectPartyKnowledge(data({ extra: { anything: true } }), scope);
  assert.deepEqual(result.entries, [entry]);
  assert.equal(result.coverage, "complete");
  assert.equal(result.audience, "party");
  assert.equal(Object.hasOwn(result, "extra"), false);
});

test("knowledge authorization scope is exact and never inferred from useful display fields", () => {
  for (const mutation of [{ campaignId: "campaign.other" }, { worldId: "world.other" },
    { audience: "dm" }, { status: "unavailable" }])
    assert.equal(projectPartyKnowledge(data(mutation), scope), null);
  assert.equal(projectPartyKnowledge(data({ entries: [{ ...entry, stance: "dm" }] }), scope).entries.length, 0);
  assert.equal(projectPartyKnowledge(data({ audience: "dm", entries: [{ ...entry, stance: "dm", admissions: [] }] }),
    { ...scope, perspective: "dm" }).entries.length, 1);
});

test("recognition drops canonical identity, document revision, subject, and media fields", () => {
  const result = projectPartyKnowledge(data({ entries: [{ ...entry, stance: "familiar", recognitionKey: "opaque.recognition",
    subject: { id: "hidden.identity", name: "Hidden identity" }, mediaOwnerId: "hidden.identity" }] }), scope);
  assert.deepEqual(result.entries, [{ text: entry.text, stance: "familiar", presentationKind: "recognition",
    recognitionKey: "opaque.recognition", admissions: entry.admissions }]);
  assert.doesNotMatch(JSON.stringify(result), /knowledge\.fixture|hidden\.identity|Hidden identity|revision\.1/);
});

test("player serialization never exposes graph paging totals or undisclosed secret records", () => {
  const result = projectPartyKnowledge(data({ entries: [], locations: [], status: "empty", coverage: "partial",
    nextCursor: "40", graph: { page: { totalCount: 1237, hidden: "secret.caldris" } },
    internalTotal: 1237, secretId: "secret.caldris", secretText: "DM-only text", secretType: "secret" }), scope,
  { ignorePagingCoverage: true });
  assert.deepEqual(result, { status: "empty", audience: "party", entries: [], locations: [], coverage: "complete" });
  assert.doesNotMatch(JSON.stringify(result), /1237|secret\.caldris|DM-only text|secret/);
});

test("bad knowledge display fields stay local and never establish a false empty union", () => {
  const result = projectPartyKnowledge(data({ entries: [null, { ...entry, presentationKind: false }], locations: false }), scope);
  assert.equal(result.status, "ready");
  assert.equal(result.coverage, "partial");
  assert.equal(result.entries.length, 1);
  assert.equal(result.entries[0].text, entry.text);
  assert.equal(result.entries[0].presentationKind, "unavailable");
  assert.equal(projectPartyKnowledge(data({ status: "empty", entries: [], locations: false }), scope), null);
  assert.equal(projectPartyKnowledge(data({ status: "empty", entries: [] }), scope).status, "empty");
  assert.equal(projectPartyKnowledge(data({ entries: [null] }), scope).coverage, "partial");
  assert.equal(projectPartyKnowledge(data({ coverage: "partial" }), scope).coverage, "partial");
  assert.equal(projectPartyKnowledge(data({ status: "empty", entries: [], coverage: "partial" }), scope).coverage, "partial");
});

test("valid ECS entity names remain usable at the registered 400-character bound", () => {
  const name = "N".repeat(400);
  const row = { ...entry, subject: { id: "location.test", name },
    admissions: [{ ...entry.admissions[0], actorName: name }] };
  const result = projectPartyKnowledge(data({ entries: [row], locations: [{ name, entries: [row] }] }), scope);
  assert.equal(result.coverage, "complete");
  assert.equal(result.entries[0].admissions[0].actorName, name);
  assert.equal(result.entries[0].subject.name, name);
  assert.equal(result.locations[0].name, name);
});

test("duplicate canonical entries and oversized collections cannot claim a complete union", () => {
  const result = projectPartyKnowledge(data({ entries: [entry, entry] }), scope);
  assert.deepEqual(result.entries, []);
  assert.equal(result.coverage, "partial");
  assert.equal(projectPartyKnowledge(data({ entries: Array.from({ length: 201 }, () => entry) }), scope), null);
  const incomplete = projectPartyKnowledge(data({ entries: [{ ...entry, admissions: [entry.admissions[0], { actorName: "bad" }] }] }), scope);
  assert.deepEqual(incomplete.entries[0].admissions, [entry.admissions[0]]);
  assert.equal(incomplete.coverage, "partial");
});

test("optional or malformed subjects preserve authorized content with partial coverage", () => {
  const withoutSubject = projectPartyKnowledge(data({
    entries: [{ ...entry, subject: undefined }], fieldCoverage: "partial", coverage: "partial",
  }), scope);
  assert.equal(withoutSubject.entries[0].knowledgeId, entry.knowledgeId);
  assert.equal(Object.hasOwn(withoutSubject.entries[0], "subject"), false);
  assert.equal(withoutSubject.coverage, "partial");

  const malformedSubject = projectPartyKnowledge(data({
    entries: [{ ...entry, subject: { id: "subject.one" } }],
  }), scope);
  assert.equal(malformedSubject.entries[0].knowledgeId, entry.knowledgeId);
  assert.equal(Object.hasOwn(malformedSubject.entries[0], "subject"), false);
  assert.equal(malformedSubject.coverage, "partial");
});

test("canonical and familiar identities use separate namespaces while same-kind duplicates remain ambiguous", () => {
  const shared = "shared.identity";
  const familiar = { ...entry, stance: "familiar", recognitionKey: shared };
  const projected = projectPartyKnowledge(data({ entries: [
    { ...entry, knowledgeId: shared }, familiar,
  ] }), scope);
  assert.equal(projected.entries.length, 2);
  assert.equal(projected.coverage, "complete");
  assert.equal(projected.entries[0].knowledgeId, shared);
  assert.equal(projected.entries[1].recognitionKey, shared);
  const repeated = projectPartyKnowledge(data({ entries: [familiar, familiar] }), scope);
  assert.deepEqual(repeated.entries, []);
  assert.equal(repeated.coverage, "partial");
});

test("party read uses one generic registered query, ignores display schema drift, and never falls back to a notebook", async () => {
  const request = { ...scope, origin: "http://localhost:6219", applicationId: "dnd2024", stateSpaceId: "space.fixture" };
  const envelope = { applicationId: request.applicationId, stateSpaceId: request.stateSpaceId,
    qualifiedQueryId: "dnd2024.query.party-knowledge", outputSchemaHash: "F".repeat(64),
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data: data() };
  let calls = 0;
  const result = await readRegisteredPartyKnowledge({ ...request, fetchImpl: async (input, init) => {
    ++calls;
    const url = new URL(input);
    assert.equal(url.pathname, "/api/applications/dnd2024/state-spaces/space.fixture/entities/campaign.fixture/read-models/dnd2024.query.party-knowledge");
    assert.equal(url.search, "?perspective=player");
    assert.equal(init.cache, "no-store");
    return Response.json(envelope);
  } });
  assert.equal(calls, 1);
  assert.deepEqual(result.entries, [entry]);
  for (const status of [403, 404, 409, 503]) {
    calls = 0;
    await assert.rejects(readRegisteredPartyKnowledge({ ...request, fetchImpl: async () => {
      ++calls; return Response.json({}, { status });
    } }), /unavailable/);
    assert.equal(calls, 1);
  }
});

test("party read accepts a short source prefix and aggregates later-page partial coverage", async () => {
  const request = { ...scope, origin: "http://localhost:6219", applicationId: "dnd2024", stateSpaceId: "space.fixture" };
  const pageEntry = (id) => ({ ...entry, knowledgeId: id });
  const envelope = (value) => ({ applicationId: request.applicationId, stateSpaceId: request.stateSpaceId,
    qualifiedQueryId: "dnd2024.query.party-knowledge", outputSchemaHash: "F".repeat(64),
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data: value });
  const calls = [];
  const result = await readRegisteredPartyKnowledge({ ...request, fetchImpl: async (input) => {
    const target = new URL(input); calls.push(target);
    const cursor = target.searchParams.get("input") ? JSON.parse(target.searchParams.get("input")).cursor : null;
    if (cursor === null) return Response.json(envelope(data({ entries: [pageEntry("knowledge.one")],
      locations: [], coverage: "partial", fieldCoverage: "complete", nextCursor: "7" })));
    assert.deepEqual(JSON.parse(target.searchParams.get("input")), { cursor: "7",
      expectedSourceRevision: "4".repeat(64), expectedGraphSourceRevision: "5".repeat(64) });
    return Response.json(envelope(data({ entries: [pageEntry("knowledge.two")], locations: [],
      coverage: "partial", fieldCoverage: "partial",
      })));
  } });
  assert.deepEqual(result.entries.map((value) => value.knowledgeId), ["knowledge.one", "knowledge.two"]);
  assert.equal(result.coverage, "partial");
  assert.equal(calls.length, 2);

  const progress = [];
  await readRegisteredPartyKnowledge({ ...request, onPage: async (prefix) => progress.push(prefix), fetchImpl: async (input) => {
    const target = new URL(input);
    const cursor = target.searchParams.get("input") ? JSON.parse(target.searchParams.get("input")).cursor : null;
    return Response.json(envelope(cursor === null ? data({ entries: [pageEntry("knowledge.one")], locations: [],
      coverage: "partial", fieldCoverage: "complete", nextCursor: "7" }) : data({
      entries: [pageEntry("knowledge.two")], locations: [], coverage: "complete", fieldCoverage: "complete",
    })));
  } });
  assert.deepEqual(progress, [{ status: "ready", audience: "party", entries: [pageEntry("knowledge.one")],
    locations: [], coverage: "partial" }], "only a source-fenced nonterminal prefix becomes visible");

  await assert.rejects(readRegisteredPartyKnowledge({ ...request, fetchImpl: async (input) => {
    const target = new URL(input);
    if (!target.searchParams.get("input")) return Response.json(envelope(data({ entries: [pageEntry("knowledge.one")],
      locations: [], coverage: "partial", fieldCoverage: "complete", nextCursor: "7" })));
    return Response.json(envelope(data({ entries: [pageEntry("knowledge.two")], locations: [],
      sourceRevision: "6".repeat(64) })));
  } }), /unavailable/);
});

test("party read accepts varied short advances and retains both source fences", async () => {
  const request = { ...scope, origin: "http://localhost:6219", applicationId: "dnd2024", stateSpaceId: "space.fixture" };
  const envelope = (value) => ({ applicationId: request.applicationId, stateSpaceId: request.stateSpaceId,
    qualifiedQueryId: "dnd2024.query.party-knowledge", outputSchemaHash: "F".repeat(64),
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data: value });
  const cursors = [];
  const result = await readRegisteredPartyKnowledge({ ...request, fetchImpl: async (input) => {
    const encoded = new URL(input).searchParams.get("input");
    const cursor = encoded === null ? null : JSON.parse(encoded).cursor;
    cursors.push(cursor);
    if (encoded !== null) assert.deepEqual(JSON.parse(encoded), { cursor,
      expectedSourceRevision: "4".repeat(64), expectedGraphSourceRevision: "5".repeat(64) });
    const nextCursor = cursor === null ? "1" : cursor === "1" ? "3" : undefined;
    return Response.json(envelope(data({ entries: [{ ...entry, knowledgeId: `knowledge.${cursor ?? "root"}` }],
      coverage: nextCursor ? "partial" : "complete", nextCursor })));
  } });
  assert.deepEqual(cursors, [null, "1", "3"]);
  assert.equal(result.entries.length, 3);
  assert.equal(result.coverage, "complete");
});

test("party read rejects invalid cursor advances and inconsistent terminal coverage", async () => {
  const request = { ...scope, origin: "http://localhost:6219", applicationId: "dnd2024", stateSpaceId: "space.fixture" };
  const envelope = (value) => ({ applicationId: request.applicationId, stateSpaceId: request.stateSpaceId,
    qualifiedQueryId: "dnd2024.query.party-knowledge", outputSchemaHash: "F".repeat(64),
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data: value });
  for (const nextCursor of ["0", "01", "41", "2049"]) {
    await assert.rejects(readRegisteredPartyKnowledge({ ...request, fetchImpl: async () => Response.json(envelope(data({
      entries: [], locations: [], status: "empty", coverage: "partial", nextCursor,
    }))) }), /unavailable/, nextCursor);
  }
  for (const [coverage, fieldCoverage] of [["complete", "partial"], ["partial", "complete"]]) {
    await assert.rejects(readRegisteredPartyKnowledge({ ...request, fetchImpl: async () => Response.json(envelope(data({
      coverage, fieldCoverage,
    }))) }), /unavailable/, `${coverage}/${fieldCoverage}`);
  }
});

test("party read fails unavailable at the conservative 16 MiB page bound", async () => {
  const request = { ...scope, origin: "http://localhost:6219", applicationId: "dnd2024", stateSpaceId: "space.fixture" };
  let calls = 0;
  await assert.rejects(readRegisteredPartyKnowledge({ ...request, fetchImpl: async () => {
    calls += 1;
    return Response.json({ applicationId: request.applicationId, stateSpaceId: request.stateSpaceId,
      qualifiedQueryId: "dnd2024.query.party-knowledge", outputSchemaHash: "F".repeat(64),
      stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
      resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
      data: data({ status: "empty", entries: [], locations: [], coverage: "partial",
        nextCursor: String(calls) }) });
  } }), /unavailable/);
  assert.equal(calls, 256);
});

test("party read enforces the aggregate entry bound and cross-page identity ambiguity", async () => {
  const request = { ...scope, origin: "http://localhost:6219", applicationId: "dnd2024", stateSpaceId: "space.fixture" };
  const envelope = (value) => ({ applicationId: request.applicationId, stateSpaceId: request.stateSpaceId,
    qualifiedQueryId: "dnd2024.query.party-knowledge", outputSchemaHash: "F".repeat(64),
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data: value });
  let page = 0;
  await assert.rejects(readRegisteredPartyKnowledge({ ...request, fetchImpl: async () => {
    const offset = page * 40;
    page += 1;
    return Response.json(envelope(data({ entries: Array.from({ length: 40 }, (_, index) => ({
      ...entry, knowledgeId: `knowledge.${offset + index}`,
    })), locations: [], coverage: page < 52 ? "partial" : "complete",
    nextCursor: page < 52 ? String(page * 40) : undefined })));
  } }), /unavailable/);
  assert.equal(page, 52);

  let duplicatePage = 0;
  const familiar = { ...entry, stance: "familiar", recognitionKey: "recognition.same" };
  await assert.rejects(readRegisteredPartyKnowledge({ ...request, fetchImpl: async () => {
    duplicatePage += 1;
    return Response.json(envelope(data({ entries: [familiar], locations: [],
      coverage: duplicatePage === 1 ? "partial" : "complete",
      nextCursor: duplicatePage === 1 ? "1" : undefined })));
  } }), /unavailable/);
  assert.equal(duplicatePage, 2);
});

test("player paging advances through undisclosed source pages without exposing their count", async () => {
  const request = { ...scope, origin: "http://localhost:6219", applicationId: "dnd2024", stateSpaceId: "space.fixture" };
  const envelope = (value) => ({ applicationId: request.applicationId, stateSpaceId: request.stateSpaceId,
    qualifiedQueryId: "dnd2024.query.party-knowledge", outputSchemaHash: "F".repeat(64),
    stateSpaceFingerprint: "1".repeat(64), resolutionFingerprint: "2".repeat(64),
    resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64), data: value });
  let calls = 0;
  const result = await readRegisteredPartyKnowledge({ ...request, fetchImpl: async (input) => {
    calls += 1;
    const continuation = new URL(input).searchParams.get("input");
    if (continuation === null) return Response.json(envelope(data({ status: "empty", entries: [], locations: [],
      coverage: "partial", fieldCoverage: "complete", nextCursor: "40" })));
    assert.deepEqual(JSON.parse(continuation), { cursor: "40", expectedSourceRevision: "4".repeat(64),
      expectedGraphSourceRevision: "5".repeat(64) });
    return Response.json(envelope(data({ status: "empty", entries: [], locations: [], coverage: "complete" })));
  } });
  assert.equal(calls, 2);
  assert.deepEqual(result, { status: "empty", audience: "party", entries: [], locations: [], coverage: "complete" });
  assert.doesNotMatch(JSON.stringify(result), /40|total|secret/i);
});
