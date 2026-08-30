import assert from "node:assert/strict";
import { resolve } from "node:path";
import test from "node:test";

import { filterRuleReferences, ruleCategoryOptions } from "../src/data/rules-reference.js";
import {
  projectCatalogRuleRecord,
  projectCatalogRuleSummary,
  readRuleReferenceDetail,
  readRulesReference,
} from "../src/server/rules-reference.ts";
import { buildBundledRulesCatalog } from "../src/server/bundled-rules-catalog.ts";

const SOURCE_ID = "dnd2024.source.srd-5.2.1";

function summary(id, path, name, overrides = {}) {
  return {
    collection: "dnd2024",
    kind: "entity",
    qualifiedId: id,
    name,
    description: name,
    path,
    status: "active",
    version: 1,
    contentFingerprint: `HASH-${id}`,
    sourceId: "dnd2024",
    sourceLogicalPath: `content/${path}/${id}.json`,
    ...overrides,
  };
}

function detail(indexEntry, {
  revision = 1,
  sourceId = SOURCE_ID,
  presentation = `${indexEntry.title} authored summary.`,
  status = "active",
} = {}) {
  const presentationComponent = presentation === null
    ? {}
    : { "dnd2024.core.presentation": { summary: presentation } };
  return {
    summary: summary(indexEntry.id, indexEntry.path, indexEntry.title, {
      contentFingerprint: indexEntry.contentFingerprint,
    }),
    contentJson: JSON.stringify({
      id: indexEntry.id,
      name: indexEntry.title,
      archetype: "dnd2024.archetype.fixture-definition",
      components: {
        "dnd2024.core.source": {
          citations: [{
            sourceRef: { entityId: sourceId },
            locator: `Rules > ${indexEntry.title} (SRD 5.2.1, page 1)`,
          }],
        },
        "dnd2024.core.version": { revision, status },
        ...presentationComponent,
      },
    }),
  };
}

function node(path) {
  return { kind: 0, node: { path }, record: null };
}

function record(value) {
  return { kind: 1, node: null, record: value };
}

function response(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

function browseFixture(requested) {
  const branch = requested.searchParams.get("branch");
  const cursor = requested.searchParams.get("cursor");
  const pages = {
    entities: [node("entities/character-options"), node("entities/shared-rules"), node("entities/spells")],
    "entities/character-options": [node("entities/character-options/classes")],
    "entities/character-options/classes": [
      record(summary("dnd2024.class.fighter", "entities/character-options/classes", "Fighter")),
    ],
    "entities/shared-rules": [node("entities/shared-rules/activity")],
    "entities/shared-rules/activity": cursor === "page-2"
      ? [record(summary("dnd2024.shared.action.search", "entities/shared-rules/activity", "Search"))]
      : [
          record(summary("dnd2024.shared.action.attack", "entities/shared-rules/activity", "Attack")),
          record(summary("dnd2024.internal.mechanic", "entities/shared-rules/activity", "Internal", { kind: "mechanic" })),
        ],
    "entities/spells": [node("entities/spells/definition")],
    "entities/spells/definition": [
      record(summary("dnd2024.spell.fireball", "entities/spells/definition", "Fireball")),
    ],
  };
  return {
    entries: pages[branch] ?? [],
    nextCursor: branch === "entities/shared-rules/activity" && cursor === null ? "page-2" : null,
  };
}

test("discovers every active entity summary recursively without a maintained ID list", async () => {
  const calls = [];
  const rules = await readRulesReference({
    serverOrigin: "http://localhost:6217",
    applicationId: "dnd2024",
    fetchImpl: async (input) => {
      const requested = new URL(input);
      calls.push(requested);
      return response(200, browseFixture(requested));
    },
  });

  assert.deepEqual(rules.map(({ id }) => id), [
    "dnd2024.class.fighter",
    "dnd2024.shared.action.attack",
    "dnd2024.shared.action.search",
    "dnd2024.spell.fireball",
  ]);
  assert.deepEqual(ruleCategoryOptions(rules), ["All", "Character Options", "Shared Rules", "Spells"]);
  assert.equal(rules.find(({ id }) => id.endsWith("fighter")).subcategory, "Classes");
  assert.equal(rules.find(({ id }) => id.endsWith("fireball")).category, "Spells");
  assert.equal(calls.every((request) => request.pathname.endsWith("/catalog/browse")), true);
  assert.equal(calls.every((request) => request.searchParams.get("collection") === "dnd2024"), true);
  assert.equal(calls.some((request) => request.searchParams.get("cursor") === "page-2"), true);
});

test("build snapshot includes the current source-cited implementation without a rule allowlist", () => {
  const entitiesRoot = resolve(import.meta.dirname, "../../../../../catalog/applications/dnd2024/content/entities");
  const rules = buildBundledRulesCatalog(entitiesRoot);

  assert.ok(rules.length > 2_000);
  assert.ok(rules.some(({ id }) => id === "dnd2024.class.fighter"));
  assert.ok(rules.some(({ id }) => id === "dnd2024.spell.fireball"));
  assert.ok(rules.some(({ category }) => category === "Creatures"));
  assert.equal(rules.every(({ source }) => source?.id === SOURCE_ID), true);
  assert.equal(new Set(rules.map(({ id }) => id)).size, rules.length);
});

test("falls back to the bundled implementation index when the activated catalog is unavailable", async () => {
  const bundled = projectCatalogRuleRecord(
    detail(projectCatalogRuleSummary(summary(
      "dnd2024.spell.fireball",
      "entities/spells/definition",
      "Fireball",
    ))),
    projectCatalogRuleSummary(summary(
      "dnd2024.spell.fireball",
      "entities/spells/definition",
      "Fireball",
    )),
  );
  const calls = [];
  const rules = await readRulesReference({
    serverOrigin: "http://localhost:6217",
    applicationId: "dnd2024",
    fetchImpl: async (input) => {
      const requested = new URL(input);
      calls.push(requested.pathname);
      return requested.pathname.endsWith("/catalog/browse")
        ? response(503, { error: "unavailable" })
        : response(200, [bundled]);
    },
  });

  assert.deepEqual(rules.map(({ id }) => id), ["dnd2024.spell.fireball"]);
  assert.deepEqual(calls, [
    "/api/applications/dnd2024/catalog/browse",
    "/ui/dnd2024-play/assets/rules-catalog.json",
  ]);
});

test("selected detail reflects revised content and supports a neutral missing-summary fallback", async () => {
  const indexEntry = projectCatalogRuleSummary(
    summary("dnd2024.spell.fireball", "entities/spells/definition", "Fireball"),
  );
  const revised = projectCatalogRuleRecord(detail(indexEntry, {
    revision: 3,
    presentation: "A revised Fireball catalog summary.",
  }), indexEntry);
  assert.equal(revised.revision, 3);
  assert.equal(revised.summary, "A revised Fireball catalog summary.");
  assert.equal(revised.source.id, SOURCE_ID);

  const noPresentation = projectCatalogRuleRecord(detail(indexEntry, {
    revision: 4,
    presentation: null,
  }), indexEntry);
  assert.equal(noPresentation.summary, "Definition reference registered in the D&D 2024 catalog.");
  assert.equal(noPresentation.revision, 4);

  const fetched = await readRuleReferenceDetail({
    serverOrigin: "http://localhost:6217",
    applicationId: "dnd2024",
    rule: indexEntry,
    fetchImpl: async (input) => {
      const requested = new URL(input);
      assert.match(requested.pathname, /\/catalog\/records\/dnd2024\.spell\.fireball$/u);
      assert.equal(requested.searchParams.get("collection"), "dnd2024");
      return response(200, detail(indexEntry, { revision: 5 }));
    },
  });
  assert.equal(fetched.revision, 5);
});

test("index and detail gates reject inactive, mismatched, non-entity, and wrong-source records", () => {
  const validSummary = summary("dnd2024.shared.action.search", "entities/shared-rules/activity", "Search");
  const indexEntry = projectCatalogRuleSummary(validSummary);
  assert.equal(indexEntry.id, validSummary.qualifiedId);
  assert.equal(projectCatalogRuleSummary({ ...validSummary, kind: "procedure" }), null);
  assert.equal(projectCatalogRuleSummary({ ...validSummary, status: "draft" }), null);
  assert.equal(projectCatalogRuleSummary({ ...validSummary, path: "mechanics/activity" }), null);
  assert.equal(projectCatalogRuleRecord(detail(indexEntry, { sourceId: "source.other" }), indexEntry), null);
  assert.equal(projectCatalogRuleRecord(detail(indexEntry, { status: "retired" }), indexEntry), null);
  assert.equal(projectCatalogRuleRecord({
    ...detail(indexEntry),
    summary: { ...validSummary, qualifiedId: "dnd2024.shared.action.attack" },
  }, indexEntry), null);
});

test("malformed or unavailable browse fails closed instead of returning a partial index", async () => {
  assert.deepEqual(await readRulesReference({
    serverOrigin: "http://localhost:6217",
    applicationId: "dnd2024",
    fetchImpl: async () => response(503, { error: "unavailable" }),
  }), []);

  assert.deepEqual(await readRulesReference({
    serverOrigin: "http://localhost:6217",
    applicationId: "dnd2024",
    fetchImpl: async () => response(200, {
      entries: [node("entities/shared-rules"), node("entities/shared-rules")],
      nextCursor: null,
    }),
  }), []);
});

test("filters dynamic families, names, IDs, summaries, and loaded sources without mutating input", () => {
  const fireball = projectCatalogRuleSummary(
    summary("dnd2024.spell.fireball", "entities/spells/definition", "Fireball"),
  );
  const searchIndex = projectCatalogRuleSummary(
    summary("dnd2024.shared.action.search", "entities/shared-rules/activity", "Search"),
  );
  const search = projectCatalogRuleRecord(detail(searchIndex), searchIndex);
  const rules = [fireball, search];
  const before = JSON.stringify(rules);

  assert.deepEqual(filterRuleReferences(rules, "fireball", "All").map(({ title }) => title), ["Fireball"]);
  assert.deepEqual(filterRuleReferences(rules, "shared.action.search", "Shared Rules").map(({ title }) => title), ["Search"]);
  assert.deepEqual(filterRuleReferences(rules, "page 1", "All").map(({ title }) => title), ["Search"]);
  assert.deepEqual(filterRuleReferences(rules, "", "Spells").map(({ title }) => title), ["Fireball"]);
  assert.deepEqual(filterRuleReferences(rules, "missing", "All"), []);
  assert.equal(JSON.stringify(rules), before);
});

test("returns no rules for an untrusted origin or another application", async () => {
  let calls = 0;
  const fetchImpl = async () => {
    calls += 1;
    return response(200, {});
  };
  assert.deepEqual(await readRulesReference({
    serverOrigin: "file:///table",
    applicationId: "dnd2024",
    fetchImpl,
  }), []);
  assert.deepEqual(await readRulesReference({
    serverOrigin: "http://localhost:6217",
    applicationId: "another-game",
    fetchImpl,
  }), []);
  assert.equal(calls, 0);
});
