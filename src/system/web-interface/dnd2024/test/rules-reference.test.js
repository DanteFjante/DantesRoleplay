import assert from "node:assert/strict";
import test from "node:test";

import { filterRuleReferences, ruleSectionOptions } from "../src/data/rules-reference.js";
import { projectResolvedRules, projectRulesPublication, readRulesReference,
  RulesReferenceClient } from "../src/server/rules-reference.ts";
import { ViewReadError } from "../src/data/view-read-client.ts";

function rule({
  id = "dnd2024.rule.combat.attack",
  resolutionKey = "rule.combat.attack",
  title = "Attack",
  classification = "core",
  ownerId = "base",
  sourceLabel = "Core",
} = {}) {
  return {
    id,
    resolutionKey,
    title,
    summary: "Resolve an attack through the active mechanic.",
    order: 10,
    blocks: [
      { kind: "steps", heading: "Resolution", body: null, items: ["Choose a target.", "Resolve the attack."] },
      { kind: "callout", heading: "Authority", body: "The mechanic owns the outcome.", items: [] },
    ],
    examples: [{ title: "A nearby target", body: "The recorded attack activity supplies the input." }],
    relatedRuleIds: ["dnd2024.rule.characters.sheet"],
    relatedContent: [],
    citations: [{ sourceId: "source.fixture", locator: "Fixture rules, page 1" }],
    authority: {
      mechanicIds: ["dnd2024.mechanic.weapon-attack"],
      procedureIds: ["dnd2024.procedure.mechanic.weapon-attack"],
    },
    visibility: "public",
    source: { ownerId, label: sourceLabel, classification },
  };
}

function payload(sections = [{ id: "combat", label: "Combat", order: 20, rules: [rule()] }], overrides = {}) {
  return {
    applicationId: "dnd2024",
    resolutionFingerprint: "A".repeat(64),
    rulesFingerprint: "B".repeat(64),
    audience: "public",
    sections,
    articleCount: sections.flatMap((section) => section.rules).length,
    ...overrides,
  };
}

test("projects catalog-defined sections and source ownership from the resolved rules response", () => {
  const projected = projectResolvedRules(payload([
    { id: "characters", label: "Characters", order: 10, rules: [] },
    {
      id: "combat",
      label: "Combat",
      order: 20,
      rules: [
        rule(),
        rule({
          id: "dnd2024.extension.caldris.rule.combat.flourish",
          resolutionKey: "rule.combat.flourish",
          title: "Caldris Flourish",
          classification: "homebrew",
          ownerId: "caldris-homebrew",
          sourceLabel: "Caldris Homebrew",
        }),
      ],
    },
  ]));

  assert.equal(projected.length, 2);
  assert.deepEqual(projected.map(({ section }) => section), [
    { id: "combat", label: "Combat", order: 20 },
    { id: "combat", label: "Combat", order: 20 },
  ]);
  assert.equal(projected[1].source.classification, "homebrew");
  assert.equal(projected[1].source.label, "Caldris Homebrew");
});

test("keeps identity-safe rules when unrelated readable fields are unavailable", () => {
  assert.equal(projectResolvedRules({ ...payload(), applicationId: "other" }), null);
  assert.equal(projectResolvedRules({ ...payload(), rulesFingerprint: "" }), null);
  const missingAuthority = projectResolvedRules(payload([{ id: "combat", label: "Combat", order: 20, rules: [
    { ...rule(), authority: { mechanicIds: [], procedureIds: [] } },
  ] }]));
  assert.equal(missingAuthority?.[0].fieldStatus.authority, undefined);
  const unknownClassification = projectResolvedRules(payload([{ id: "combat", label: "Combat", order: 20, rules: [
    { ...rule(), source: { ownerId: "base", label: "Core", classification: "unknown" } },
  ] }]));
  assert.equal(unknownClassification?.[0].source.classification, "unknown");
  assert.equal(unknownClassification?.[0].fieldStatus.sourceClassification, "unavailable");
  assert.equal(projectRulesPublication(payload([{ id: "combat", label: "Combat", order: 20, rules: [
    { ...rule(), visibility: "dm" },
  ] }], { audience: "public" })), null, "A public publication cannot carry a DM-only row.");
  assert.equal(projectRulesPublication(payload(undefined, { resolutionFingerprint: "none" }))?.resolutionFingerprint, "none",
    "Core-only rules retain the server's explicit no-resolution marker.");
  assert.equal(projectRulesPublication(payload(undefined, { rulesFingerprint: "none" })), null,
    "No extension resolution does not remove the publication's own integrity fingerprint.");
});

test("loads only the resolved rules endpoint and has no static fallback", async () => {
  const requested = [];
  const projected = await readRulesReference({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    fetchImpl: async (url) => {
      requested.push(String(url));
      return new Response(JSON.stringify(payload()), { status: 200, headers: { "Content-Type": "application/json" } });
    },
  });

  assert.equal(projected.rules.length, 1);
  assert.equal(projected.articleCount, 1);
  assert.deepEqual(requested, ["https://localhost:5144/api/applications/dnd2024/rules"]);

  const unavailableRequests = [];
  await assert.rejects(readRulesReference({
    serverOrigin: "https://localhost:5144", applicationId: "dnd2024",
    fetchImpl: async (url) => { unavailableRequests.push(String(url));
      return new Response("unavailable", { status: 503 }); },
  }), (error) => error instanceof ViewReadError && error.category === "transport");
  assert.equal(unavailableRequests.length, 1);
});

test("does not request rules for a credential-bearing origin or another application", async () => {
  let requests = 0;
  const fetchImpl = async () => {
    requests += 1;
    return new Response(JSON.stringify(payload()), { status: 200 });
  };
  await assert.rejects(readRulesReference({
    serverOrigin: "https://user@example.com",
    applicationId: "dnd2024",
    fetchImpl,
  }), (error) => error instanceof ViewReadError && error.category === "incompatible-data");
  await assert.rejects(readRulesReference({
    serverOrigin: "https://localhost:5144",
    applicationId: "other",
    fetchImpl,
  }), (error) => error instanceof ViewReadError && error.category === "incompatible-data");
  assert.equal(requests, 0);
});

test("rules reject an oversized response before retaining or parsing it", async () => {
  await assert.rejects(readRulesReference({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    fetchImpl: async () => new Response("x", {
      status: 200,
      headers: { "Content-Length": "2097153" },
    }),
  }), (error) => error instanceof ViewReadError && error.category === "incompatible-data");
});

test("section navigation and search use readable content rather than directory names", () => {
  const projected = projectResolvedRules(payload([
    { id: "resting", label: "Resting", order: 30, rules: [rule({ id: "dnd2024.rule.resting.long-rest", resolutionKey: "rule.resting.long-rest", title: "Long Rest" })] },
    { id: "combat", label: "Combat", order: 20, rules: [rule()] },
  ]));

  assert.deepEqual(ruleSectionOptions(projected).map(({ id }) => id), ["combat", "resting"]);
  assert.deepEqual(filterRuleReferences(projected, "nearby target", "").map(({ title }) => title), ["Attack", "Long Rest"]);
  assert.deepEqual(filterRuleReferences(projected, "weapon-attack", "combat").map(({ title }) => title), ["Attack"]);
  assert.deepEqual(filterRuleReferences(projected, "", "resting").map(({ title }) => title), ["Long Rest"]);
});

test("publication retains safe rows when a count or unrelated link is malformed", () => {
  const linked = rule();
  linked.relatedContent = [{ kind: "item", entityId: "dnd2024.item.backpack.v1", title: "Backpack",
    collection: "items", contentFingerprint: "C".repeat(64), available: true }];
  const projected = projectRulesPublication(payload([
    { id: "combat", label: "Combat", order: 20, rules: [linked] },
  ]));
  assert.equal(projected.articleCount, 1);
  assert.equal(projected.rules[0].relatedContent[0].title, "Backpack");
  const countUnavailable = projectRulesPublication(payload(undefined, { articleCount: 2 }));
  assert.equal(countUnavailable?.articleCount, null);
  assert.equal(countUnavailable?.coverage, "partial");
  const unavailableLink = projectRulesPublication(payload([{ id: "combat", label: "Combat", order: 20, rules: [
    { ...linked, relatedContent: [{ ...linked.relatedContent[0], available: false }] },
  ] }]));
  assert.equal(unavailableLink?.rules[0].relatedContent.length, 0);
  assert.equal(unavailableLink?.rules[0].fieldStatus.relatedContent, "unavailable");
});

test("rules retain safe identity and readable fields when display labels or ordering are unavailable", () => {
  const sparse = rule();
  delete sparse.title;
  delete sparse.order;
  const projected = projectRulesPublication(payload([{ id: "combat", rules: [sparse] }]));
  assert.equal(projected?.rules.length, 1);
  assert.equal(projected?.rules[0]?.id, "dnd2024.rule.combat.attack");
  assert.equal(projected?.rules[0]?.summary, "Resolve an attack through the active mechanic.");
  assert.equal(projected?.rules[0]?.title, "Unnamed rule");
  assert.equal(projected?.rules[0]?.order, null);
  assert.equal(projected?.rules[0]?.section.label, "combat");
  assert.equal(projected?.rules[0]?.section.order, null);
  assert.equal(projected?.rules[0]?.fieldStatus.title, "unavailable");
  assert.equal(projected?.rules[0]?.fieldStatus.sectionOrder, "unavailable");
});

test("rules retain useful block and example text when unrelated display fields are malformed", () => {
  const sparse = rule();
  sparse.blocks = [
    { kind: "paragraph", heading: 42, body: "A confirmed readable paragraph.", items: null },
    { kind: "steps", heading: null, body: null, items: ["Confirmed step.", 7] },
  ];
  sparse.examples = [{ title: null, body: "A confirmed example body." }];
  const projected = projectRulesPublication(payload([{ id: "combat", label: "Combat", order: 20, rules: [sparse] }]));
  assert.deepEqual(projected?.rules[0]?.blocks, [
    { kind: "paragraph", heading: null, body: "A confirmed readable paragraph.", items: [] },
    { kind: "steps", heading: null, body: null, items: ["Confirmed step."] },
  ]);
  assert.deepEqual(projected?.rules[0]?.examples, [{ title: "Unnamed example", body: "A confirmed example body." }]);
  assert.equal(projected?.rules[0]?.fieldStatus.blocks, "partial");
  assert.equal(projected?.rules[0]?.fieldStatus.examples, "partial");
});

test("rules client coordinates only in-flight reads; Redux owns completed publications", async () => {
  let calls = 0;
  const client = new RulesReferenceClient({ serverOrigin: "https://localhost:5144",
    fetchImpl: async () => {
      calls += 1;
      return new Response(JSON.stringify(payload(undefined, {
        resolutionFingerprint: String(calls).repeat(64),
        rulesFingerprint: String(calls + 1).repeat(64),
      })), { status: 200, headers: { "Content-Type": "application/json" } });
    } });
  const first = await client.load();
  const second = await client.load();
  const changed = await client.load(undefined, false);
  assert.equal(calls, 3);
  assert.notEqual(first.rulesFingerprint, second.rulesFingerprint);
  assert.notEqual(second.rulesFingerprint, changed.rulesFingerprint);
});
