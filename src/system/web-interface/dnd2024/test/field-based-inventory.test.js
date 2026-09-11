import assert from "node:assert/strict";
import test from "node:test";

import { contract as inventoryContainerContract } from "../src/server/inventory-container-contract.js";
import {
  readCanonicalInventory,
  readCanonicalInventoryPage,
} from "../src/server/game-server-context.js";

const ACTOR_ID = "actor.synthetic.inventory";
const APPLICATION_ID = "dnd2024";
const STATE_SPACE_ID = "state.synthetic";
const FINGERPRINT = "1".repeat(64);

function response(status, body, headers = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", ...headers },
  });
}

function row(id, overrides = {}) {
  return {
    id,
    name: `Synthetic ${id}`,
    definition: { id: `definition.${id}`, label: "Synthetic definition" },
    quantity: 1,
    slot: "carried",
    order: 0,
    equipmentSlots: [],
    classification: "item",
    ...overrides,
  };
}

function containerData(items, overrides = {}) {
  return {
    version: 2,
    container: { id: ACTOR_ID, label: "Synthetic actor" },
    state: "ready",
    reasons: [],
    items,
    limits: { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false },
    ...overrides,
  };
}

function envelope(data, overrides = {}) {
  return {
    applicationId: APPLICATION_ID,
    stateSpaceId: STATE_SPACE_ID,
    qualifiedQueryId: inventoryContainerContract.id,
    stateSpaceFingerprint: FINGERPRINT,
    resolutionFingerprint: "2".repeat(64),
    outputSchemaHash: inventoryContainerContract.outputSchemaHash,
    resultFingerprint: "3".repeat(64),
    sourceRevisionFingerprint: "4".repeat(64),
    data,
    ...overrides,
  };
}

function page(data, overrides = {}, fetchOverrides = {}) {
  return readCanonicalInventoryPage({
    origin: "http://localhost:6217",
    applicationId: APPLICATION_ID,
    stateSpaceId: STATE_SPACE_ID,
    scopeId: ACTOR_ID,
    perspective: "dm",
    fetchImpl: async () => response(200, envelope(data, overrides), fetchOverrides.headers),
  });
}

test("inventory consumes old and newer field shapes without an output-schema admission gate", async () => {
  const variants = [
    {
      name: "legacy version without container capability",
      data: containerData([row("item.legacy")], { version: 2, legacyMetadata: { source: "old" } }),
      outputSchemaHash: "a".repeat(64),
      expectedContainer: null,
    },
    {
      name: "newer version with additive fields",
      data: containerData([row("item.modern", { isContainer: true, nestedMetadata: { rarity: "uncommon" } })], {
        version: 3,
        newMetadata: { componentVersion: "inventory.v3" },
      }),
      outputSchemaHash: "b".repeat(64),
      expectedContainer: true,
    },
  ];

  for (const variant of variants) {
    await test(variant.name, async () => {
      const result = await page(variant.data, {
        outputSchemaHash: variant.outputSchemaHash,
        additiveEvidence: { producer: "synthetic" },
      });
      assert.equal(result.status, "ready");
      assert.equal(result.data.container.id, ACTOR_ID);
      assert.equal(result.data.items.length, 1);
      assert.equal(result.data.items[0].id, variant.data.items[0].id);
      assert.equal(result.data.items[0].name, variant.data.items[0].name);
      assert.equal(result.data.items[0].isContainer, variant.expectedContainer);
      assert.equal(result.data.items[0].unavailableFields.includes("container"), variant.expectedContainer === null);
      assert.equal(result.data.items[0].classification, "item");
    });
  }
});

test("unknown container capability stays unknown and root inventory makes no speculative child read", async () => {
  const calls = [];
  const result = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: APPLICATION_ID, stateSpaceId: STATE_SPACE_ID,
    actorId: ACTOR_ID, perspective: "player",
    fetchImpl: async (input) => {
      const request = new URL(input);
      calls.push(request);
      const wallet = request.pathname.endsWith("inventory-wallet");
      return response(200, wallet
        ? {
          applicationId: APPLICATION_ID, stateSpaceId: STATE_SPACE_ID,
          qualifiedQueryId: "dnd2024.query.inventory-wallet", stateSpaceFingerprint: FINGERPRINT,
          resolutionFingerprint: "2".repeat(64), outputSchemaHash: "c".repeat(64),
          resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
          data: {
            version: 1, owner: { id: ACTOR_ID, label: "Synthetic actor" }, state: "ready", reasons: [],
            wallet: { coinCount: 0, copperValue: 0, gpCount: 0, denominations: [] },
            limits: { contentsDepth: 4, complete: true },
          },
        }
        : envelope(containerData([
          row("item.absent-capability"),
          row("item.null-capability", { isContainer: null }),
          row("item.false-capability", { isContainer: false }),
        ], { version: 3 }), { outputSchemaHash: "d".repeat(64) }));
    },
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.data.items.map((item) => item.id), [
    "item.absent-capability", "item.null-capability", "item.false-capability",
  ]);
  const absent = result.data.items[0];
  const unknown = result.data.items[1];
  const knownFalse = result.data.items[2];
  assert.equal(Object.hasOwn(absent, "isContainer"), true);
  assert.equal(absent.isContainer, null);
  assert.equal(unknown.isContainer, null);
  assert.equal(knownFalse.isContainer, false);
  assert.equal(absent.containerState, "absent");
  assert.equal(unknown.containerState, "invalid");
  assert.equal(knownFalse.containerState, "value");
  assert.equal(absent.childCount, null);
  assert.equal(unknown.childCount, null);
  assert.equal(knownFalse.childCount, 0);
  assert.equal(absent.deeperContentsOmitted, false);
  assert.equal(unknown.deeperContentsOmitted, false);
  assert.equal(knownFalse.deeperContentsOmitted, false);
  assert.equal(calls.length, 2, "only root container and independent wallet reads are allowed");
  assert.ok(calls.every((request) => !request.pathname.includes("/components/")));
});

test("malformed wallet fields remain locally unavailable while useful inventory rows survive", async () => {
  const result = await readCanonicalInventory({
    origin: "http://localhost:6217", applicationId: APPLICATION_ID, stateSpaceId: STATE_SPACE_ID,
    actorId: ACTOR_ID, perspective: "dm",
    fetchImpl: async (input) => {
      const request = new URL(input);
      if (request.pathname.endsWith("inventory-wallet")) return response(200, {
        applicationId: APPLICATION_ID, stateSpaceId: STATE_SPACE_ID,
        qualifiedQueryId: "dnd2024.query.inventory-wallet", stateSpaceFingerprint: FINGERPRINT,
        resolutionFingerprint: "2".repeat(64), outputSchemaHash: "e".repeat(64),
        resultFingerprint: "3".repeat(64), sourceRevisionFingerprint: "4".repeat(64),
        data: {
          version: 9, owner: { id: ACTOR_ID, label: "Synthetic actor" }, state: "ready", reasons: [],
          wallet: {
            coinCount: 4, copperValue: 400, gpCount: 4,
            denominations: [{ denomination: { id: "currency.gp", label: "Gold" }, code: "gp", count: "unknown",
              copperValuePerCoin: 100, totalCopperValue: 400 }],
          },
          limits: { contentsDepth: 4, complete: true }, additive: true,
        },
      });
      return response(200, envelope(containerData([row("item.wallet-survivor", { quantity: 0, isContainer: false })]), {
        outputSchemaHash: "f".repeat(64),
      }));
    },
  });
  assert.equal(result.status, "ready");
  assert.deepEqual(result.data.items.map((item) => item.id), ["item.wallet-survivor"]);
  assert.deepEqual(result.data.wallet, { coinCount: 4, copperValue: 400, gpCount: 4, denominations: [] });
  assert.deepEqual(result.data.walletState, { status: "partial", reason: "field-unavailable" });
});

test("quantity fields preserve zero and explicit null while invalid or absent values stay unavailable", async () => {
  const missing = row("item.quantity-missing");
  delete missing.quantity;
  const result = await page(containerData([
    row("item.quantity-zero", { quantity: 0, isContainer: false }),
    row("item.quantity-null", { quantity: null, isContainer: false }),
    missing,
    row("item.quantity-invalid", { quantity: "not-a-number", isContainer: false }),
  ]));
  assert.equal(result.status, "ready");
  assert.equal(result.data.state, "partial");
  const byId = new Map(result.data.items.map((item) => [item.id, item]));
  const zero = byId.get("item.quantity-zero");
  assert.equal(zero.quantity, 0, "zero remains distinct from an unavailable quantity");
  assert.equal(zero.quantityState, "value");
  const explicitNull = byId.get("item.quantity-null");
  assert.equal(explicitNull.quantity, null, "explicit null stays distinct from invalid zero");
  assert.equal(explicitNull.quantityState, "null");
  for (const [id, state] of [["item.quantity-missing", "absent"], ["item.quantity-invalid", "invalid"]]) {
    const item = byId.get(id);
    assert.ok(item, `${id} remains a visible row`);
    assert.equal(item.quantity, null);
    assert.equal(item.quantityState, state);
    assert.ok(item.unavailableFields.includes("quantity"), `${id} records local quantity unavailability`);
  }
});

test("invalid and duplicate identities produce a bounded partial list with no mergeable duplicate", async () => {
  const result = await page(containerData([
    row("item.good", { isContainer: false }),
    row("item.duplicate", { isContainer: false }),
    row("item.duplicate", { name: "Conflicting duplicate", isContainer: true }),
    row("", { name: "Malformed identity", isContainer: false }),
    row(ACTOR_ID, { name: "Actor must not become an item", isContainer: false }),
  ]));
  assert.equal(result.status, "ready");
  assert.equal(result.data.state, "partial");
  assert.deepEqual(result.data.items.map((item) => item.id), ["item.good"]);
  assert.equal(new Set(result.data.items.map((item) => item.id)).size, result.data.items.length);
  assert.ok(result.data.notices.includes("duplicate-item-identity"));
  assert.ok(result.data.notices.includes("invalid-item-identity"));
});

test("omitted completeness metadata stays unknown and an explicit forbidden projection never exposes rows", async () => {
  const omitted = containerData([row("item.partial")]);
  delete omitted.limits.directComplete;
  const partial = await page(omitted);
  assert.equal(partial.status, "ready");
  assert.equal(partial.data.items[0].id, "item.partial");
  assert.equal(partial.data.limits.directComplete, null);

  const forbidden = await page(containerData([row("item.secret")], { state: "forbidden" }));
  assert.equal(forbidden.status, "error");
  assert.equal(forbidden.failureCategory, "incompatible-data");
});

test("recognized unclassified-content evidence prevents a contradictory ready-empty claim", async () => {
  const result = await page(containerData([], {
    state: "ready",
    reasons: ["unclassified-content"],
  }));
  assert.equal(result.status, "ready");
  assert.equal(result.data.items.length, 0);
  assert.equal(result.data.state, "partial");
  assert.deepEqual(result.data.reasons, ["unclassified-content"]);
});

test("inherited container and row properties do not become an admitted inventory identity", async () => {
  const inheritedData = Object.create({
    container: { id: ACTOR_ID, label: "Inherited actor" },
    items: [row("item.inherited")],
  });
  inheritedData.version = 99;
  inheritedData.state = "ready";
  inheritedData.reasons = [];
  inheritedData.limits = { contentsDepth: 1, itemCount: 200, directComplete: true, recursiveComplete: false };
  const result = await page(inheritedData);
  assert.equal(result.status, "error");
  assert.equal(result.failureCategory, "incompatible-data");

  const inheritedRow = Object.create({ id: "item.inherited-id", name: "Inherited identity" });
  Object.assign(inheritedRow, {
    definition: { id: "definition.safe", label: "Safe" }, quantity: 1, slot: "carried", order: 0,
    equipmentSlots: [], classification: "item", isContainer: false,
  });
  const rowResult = await page(containerData([inheritedRow]));
  assert.equal(rowResult.status, "ready");
  assert.deepEqual(rowResult.data.items, []);
  assert.ok(rowResult.data.notices.includes("invalid-item-identity"));
});

test("foreign scope/query responses and malformed transport evidence remain rejected", async () => {
  const foreignCases = [
    ["application", { applicationId: "other-app" }],
    ["state space", { stateSpaceId: "other-state" }],
    ["query", { qualifiedQueryId: "dnd2024.query.other" }],
    ["actor", { data: containerData([row("item.foreign")], { container: { id: "actor.other", label: "Other" } }) }],
  ];
  for (const [name, overrides] of foreignCases) {
    await test(`rejects foreign ${name}`, async () => {
      const result = await page(containerData([row("item.valid")]), overrides);
      assert.equal(result.status, "error");
      assert.equal(result.failureCategory, "incompatible-data");
    });
  }

  for (const field of ["outputSchemaHash", "stateSpaceFingerprint", "resolutionFingerprint",
    "resultFingerprint", "sourceRevisionFingerprint"]) {
    const badHash = await page(containerData([row("item.valid")]), { [field]: "not-a-fingerprint" });
    assert.equal(badHash.status, "error", `${field} is mandatory transport evidence`);
    assert.equal(badHash.failureCategory, "incompatible-data");
  }

  const tooLarge = await page(containerData([row("item.valid")], { padding: "x".repeat(5 * 1024 * 1024) }));
  assert.equal(tooLarge.status, "error");
  assert.equal(tooLarge.failureCategory, "incompatible-data");
});
