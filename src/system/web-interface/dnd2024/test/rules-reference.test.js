import assert from "node:assert/strict";
import test from "node:test";

import { filterRuleReferences } from "../src/data/rules-reference.js";
import {
  projectCatalogRuleRecord,
  readRulesReference,
  REVIEWED_RULE_REFERENCE_IDS,
} from "../src/server/rules-reference.ts";

const SOURCE_ID = "source.dnd2024.srd-5.2.1";

function catalogRecord(id, {
  economy = id.endsWith("opportunity-attack") ? "reaction" : "action",
  sourceId = SOURCE_ID,
  status = "active",
} = {}) {
  const title = id.split(".").at(-1).split("-")
    .map((word) => `${word[0].toUpperCase()}${word.slice(1)}`)
    .join(" ");
  return {
    summary: { qualifiedId: id, status },
    contentJson: JSON.stringify({
      id,
      name: title,
      archetype: "dnd2024.archetype.activity-definition",
      components: {
        "dnd2024.core.source": {
          citations: [{
            sourceRef: { entityId: sourceId },
            locator: `Playing the Game > Actions > ${title} (SRD 5.2.1, pages 10-10)`,
          }],
        },
        "dnd2024.core.version": { revision: 1, status: "active" },
        "dnd2024.core.presentation": { summary: `${title} table reference.` },
        "dnd2024.activity.activation": { economy },
      },
    }),
  };
}

function response(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json" },
  });
}

test("reads the fixed reviewed activity allowlist in deterministic order", async () => {
  const calls = [];
  const rules = await readRulesReference({
    serverOrigin: "http://localhost:6217",
    applicationId: "dnd2024",
    fetchImpl: async (input) => {
      const requested = new URL(input);
      calls.push(requested);
      const id = decodeURIComponent(requested.pathname.split("/").at(-1));
      return response(200, catalogRecord(id));
    },
  });

  assert.equal(rules.length, 14);
  assert.deepEqual(rules.map(({ id }) => id), REVIEWED_RULE_REFERENCE_IDS);
  assert.equal(rules.find(({ id }) => id.endsWith("opportunity-attack")).category, "Reaction");
  assert.equal(rules.every(({ source }) => source.id === SOURCE_ID), true);
  assert.equal(calls.length, 14);
  assert.equal(calls.every((request) => request.searchParams.get("collection") === "dnd2024"), true);
  assert.equal(calls.every((request) => request.pathname.includes("/catalog/records/")), true);
});

test("fails closed per record when catalog identity or source fidelity is invalid", async () => {
  const expected = REVIEWED_RULE_REFERENCE_IDS[0];
  assert.equal(projectCatalogRuleRecord(catalogRecord(expected), expected).id, expected);
  assert.equal(projectCatalogRuleRecord(catalogRecord(expected, { sourceId: "source.other" }), expected), null);
  assert.equal(projectCatalogRuleRecord(catalogRecord(expected, { economy: "bonus-action" }), expected), null);
  assert.equal(projectCatalogRuleRecord(catalogRecord(expected, { status: "draft" }), expected), null);
  assert.equal(projectCatalogRuleRecord({
    ...catalogRecord(expected),
    summary: { qualifiedId: "dnd2024.shared.action.dash", status: "active" },
  }, expected), null);

  const rules = await readRulesReference({
    serverOrigin: "http://localhost:6217",
    applicationId: "dnd2024",
    fetchImpl: async (input) => {
      const id = decodeURIComponent(new URL(input).pathname.split("/").at(-1));
      if (id === REVIEWED_RULE_REFERENCE_IDS[1]) return response(200, catalogRecord(id, { sourceId: "source.other" }));
      if (id === REVIEWED_RULE_REFERENCE_IDS[2]) return response(503, { error: "unavailable" });
      return response(200, catalogRecord(id));
    },
  });

  assert.equal(rules.length, 12);
  assert.equal(rules.some(({ id }) => id === REVIEWED_RULE_REFERENCE_IDS[1]), false);
  assert.equal(rules.some(({ id }) => id === REVIEWED_RULE_REFERENCE_IDS[2]), false);
});

test("filters reference title, summary, source, and exact category without mutating input", () => {
  const rules = [
    projectCatalogRuleRecord(catalogRecord("dnd2024.shared.action.search"), "dnd2024.shared.action.search"),
    projectCatalogRuleRecord(
      catalogRecord("dnd2024.shared.action.opportunity-attack"),
      "dnd2024.shared.action.opportunity-attack",
    ),
  ];
  const before = JSON.stringify(rules);

  assert.deepEqual(filterRuleReferences(rules, "opportunity", "All").map(({ title }) => title), ["Opportunity Attack"]);
  assert.deepEqual(filterRuleReferences(rules, "pages 10", "Action").map(({ title }) => title), ["Search"]);
  assert.deepEqual(filterRuleReferences(rules, "", "Reaction").map(({ title }) => title), ["Opportunity Attack"]);
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
