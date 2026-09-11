import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const project = new Function("ctx", readFileSync(new URL(
  "../../../../../catalog/applications/dnd2024/mechanics/data/dnd2024.mechanic.inventory-container-page.project.js",
  import.meta.url), "utf8"));

function definition(id, components = {}) {
  return { id, name: "Canonical item", components };
}

function item(id, components = {}, name = "Contained item") {
  return { id, name, slot: "carried", components };
}

function context(contains, references = {}, subjectId = "actor.test") {
  return {
    input: {}, references,
    roles: { subject: { id: subjectId, name: "Container", contains } },
  };
}

test("additive component fields do not reject independently readable inventory fields", () => {
  const references = {
    "definition.bag": definition("definition.bag", {
      "dnd2024.item.container": JSON.stringify({ capacity: { itemCount: 10 }, future: "ignored" }),
    }),
  };
  const row = item("item.bag", {
    "dnd2024.core.definition-link": JSON.stringify({
      definition: { entityId: "definition.bag", label: "Ignored producer label" },
      producerMetadata: { version: 3 },
    }),
    "dnd2024.item.quantity": JSON.stringify({ current: 3, unit: "count", future: true }),
    "dnd2024.item.equipment": JSON.stringify({
      equippedBy: { entityId: "actor.test", displayName: "Ignored actor label" },
      slots: [{ entityId: "slot.main-hand", label: "Ignored slot label" }],
      configuration: { entityId: "configuration.future" },
      future: "ignored",
    }),
    "dnd2024.item.container": JSON.stringify({ capacity: { itemCount: 10 }, future: "ignored" }),
  });

  const result = project(context([row], references));
  assert.equal(result.data.items.length, 1);
  const [projected] = result.data.items;
  assert.equal(projected.definition.id, "definition.bag");
  assert.equal(projected.quantity, 3);
  assert.deepEqual(projected.equipmentSlots, [{ id: "slot.main-hand", label: "Main Hand" }]);
  assert.equal(projected.isContainer, true);
  assert.equal(result.data.state, "ready");
  assert.deepEqual(result.data.reasons, []);
});

test("malformed optional quantity, equipment, and container facets leave the row visible", () => {
  const references = { "definition.knife": definition("definition.knife") };
  const result = project(context([
    item("item.malformed", {
      "dnd2024.core.definition-link": JSON.stringify({
        definition: { entityId: "definition.knife", future: "ignored" },
        producerMetadata: "ignored",
      }),
      "dnd2024.item.quantity": "{\"current\":\"not-a-number\"}",
      "dnd2024.item.equipment": "not-json",
      "dnd2024.item.container": "not-json",
    }),
    item("item.unloaded", {
      "dnd2024.core.definition-link": JSON.stringify({ definition: { entityId: "definition.knife" } }),
    }),
  ], references));

  assert.deepEqual(result.data.items.map(value => value.id), ["item.malformed", "item.unloaded"]);
  assert.deepEqual(result.data.items.map(value => value.quantity), [null, null]);
  assert.equal(Object.hasOwn(result.data.items[0], "equipmentSlots"), false);
  assert.equal(Object.hasOwn(result.data.items[1], "equipmentSlots"), true);
  assert.deepEqual(result.data.items[1].equipmentSlots, []);
  assert.equal(Object.hasOwn(result.data.items[0], "isContainer"), false);
  assert.equal(Object.hasOwn(result.data.items[1], "isContainer"), true);
  assert.equal(result.data.items[1].isContainer, false);
  assert.equal(result.data.state, "partial", "optional facet loss is honest without erasing direct membership");
  assert.deepEqual(result.data.reasons, ["source-incomplete"]);
});

test("identity and authorized definition references remain strict", () => {
  assert.throws(() => project(context([
    item("item.invalid", {}, ""),
  ])), /Inventory containment is malformed or cyclic/);

  assert.throws(() => project(context([item("item.unknown", {
    "dnd2024.core.definition-link": JSON.stringify({ definition: { entityId: "definition.missing" } }),
  })])), /Item definition is unavailable/);
});

test("nested containers retain equipment from their actual wearer", () => {
  const references = { "definition.pouch": definition("definition.pouch") };
  const result = project(context([item("item.pouch", {
    "dnd2024.core.definition-link": JSON.stringify({ definition: { entityId: "definition.pouch" } }),
    "dnd2024.item.quantity": JSON.stringify({ current: 1 }),
    "dnd2024.item.equipment": JSON.stringify({
      equippedBy: { entityId: "actor.wearer", producerMetadata: "ignored" },
      slots: [{ entityId: "slot.belt", producerMetadata: "ignored" }],
      producerMetadata: "ignored",
    }),
  })], references, "item.container"));
  const [projected] = result.data.items;
  assert.deepEqual(projected.equipmentSlots, [{ id: "slot.belt", label: "Belt" }]);
  assert.equal(projected.isContainer, false);
});
