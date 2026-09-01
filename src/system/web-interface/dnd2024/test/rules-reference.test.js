import assert from "node:assert/strict";
import test from "node:test";

import { filterRuleReferences, ruleSectionOptions } from "../src/data/rules-reference.js";
import { projectResolvedRules, readRulesReference } from "../src/server/rules-reference.ts";

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
    citations: [{ sourceId: "source.fixture", locator: "Fixture rules, page 1" }],
    authority: {
      mechanicIds: ["dnd2024.mechanic.weapon-attack"],
      procedureIds: ["dnd2024.procedure.mechanic.weapon-attack"],
    },
    visibility: "public",
    source: { ownerId, label: sourceLabel, classification },
  };
}

function payload(sections = [{ id: "combat", label: "Combat", order: 20, rules: [rule()] }]) {
  return {
    applicationId: "dnd2024",
    resolutionFingerprint: "A".repeat(64),
    rulesFingerprint: "B".repeat(64),
    audience: "public",
    sections,
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

test("rejects malformed resolved rules instead of falling back to catalog folders", () => {
  assert.equal(projectResolvedRules({ ...payload(), applicationId: "other" }), null);
  assert.equal(projectResolvedRules({ ...payload(), rulesFingerprint: "" }), null);
  assert.equal(projectResolvedRules(payload([{ id: "combat", label: "Combat", order: 20, rules: [
    { ...rule(), authority: { mechanicIds: [], procedureIds: [] } },
  ] }])), null);
  assert.equal(projectResolvedRules(payload([{ id: "combat", label: "Combat", order: 20, rules: [
    { ...rule(), source: { ownerId: "base", label: "Core", classification: "unknown" } },
  ] }])), null);
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

  assert.equal(projected.length, 1);
  assert.deepEqual(requested, ["https://localhost:5144/api/applications/dnd2024/rules"]);

  const unavailableRequests = [];
  const unavailable = await readRulesReference({
    serverOrigin: "https://localhost:5144",
    applicationId: "dnd2024",
    fetchImpl: async (url) => {
      unavailableRequests.push(String(url));
      return new Response("unavailable", { status: 503 });
    },
  });
  assert.deepEqual(unavailable, []);
  assert.equal(unavailableRequests.length, 1);
});

test("does not request rules for a credential-bearing origin or another application", async () => {
  let requests = 0;
  const fetchImpl = async () => {
    requests += 1;
    return new Response(JSON.stringify(payload()), { status: 200 });
  };
  assert.deepEqual(await readRulesReference({
    serverOrigin: "https://user@example.com",
    applicationId: "dnd2024",
    fetchImpl,
  }), []);
  assert.deepEqual(await readRulesReference({
    serverOrigin: "https://localhost:5144",
    applicationId: "other",
    fetchImpl,
  }), []);
  assert.equal(requests, 0);
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
