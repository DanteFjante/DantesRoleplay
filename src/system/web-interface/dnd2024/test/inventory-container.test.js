import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const project = new Function("ctx", readFileSync(new URL(
  "../../../../../catalog/applications/dnd2024/mechanics/data/dnd2024.mechanic.inventory-container-page.project.js", import.meta.url), "utf8"));
const projectLegacy = new Function("ctx", readFileSync(new URL(
  "../../../../../catalog/applications/dnd2024/mechanics/data/dnd2024.mechanic.inventory-container.project.js", import.meta.url), "utf8"));

test("inventory distinguishes ordinary items, inherited and instance containers without name guesses", () => {
  const definition = (id, components = {}) => ({ id, name: "Item", components });
  const references = {
    "definition.knife": definition("definition.knife"),
    "definition.bag": definition("definition.bag", { "dnd2024.item.container": "{}" }),
    "definition.legacy": definition("definition.legacy", { "dnd2024.item-definition": JSON.stringify({ capacity: { itemCount: 10 } }) }),
  };
  const item = (id, name, ref, components = {}) => ({ id, name, slot: "carried", components: {
    ...(ref ? { "dnd2024.core.definition-link": JSON.stringify({ definition: { entityId: ref } }),
      "dnd2024.item.quantity": "{\"current\":1}" } : {}), ...components,
  } });
  const result = project({ input: {}, references, roles: { subject: {
    id: "actor.test", name: "Actor", contains: [
      item("item.knife", "Bag-shaped carving knife", "definition.knife"),
      item("item.empty", "Leather satchel", "definition.bag"),
      item("item.instance", "Case", "definition.knife", { "dnd2024.item.container": "{}" }),
      item("item.legacy", "Old chest", "definition.legacy"),
      item("item.unknown", "Unknown bag", null),
    ],
  } } });
  assert.deepEqual(result.data.items.map(item => item.isContainer), [false, true, true, true, undefined]);
  assert.equal(result.data.items.length, 5);
  assert.equal(result.data.state, "partial");
  assert.deepEqual(result.data.reasons, ["unclassified-content", "source-incomplete"]);
  assert.deepEqual(result.effects, []);
});

for (const [name, projectInventory] of [["direct page", project], ["bounded tree", projectLegacy]]) {
  test(`${name} excludes typed class membership while retaining unknown contents and real items`, () => {
    const membership = JSON.stringify({ classRef: { entityId: "definition.monk" }, level: 1 });
    const itemComponents = {
      "dnd2024.core.definition-link": JSON.stringify({ definition: { entityId: "definition.knife" } }),
      "dnd2024.item.quantity": JSON.stringify({ current: 1 }),
    };
    const node = (id, name, components = {}) => ({ id, name, slot: "class-membership", components });
    const ctx = { input: {}, references: { "definition.knife": { id: "definition.knife", name: "Knife", components: {} } },
      roles: { subject: { id: "actor.test", name: "Actor", contains: [
        node("opaque-record", "An ordinary name", { "dnd2024.character.class-membership": membership }),
        node("actor.test.class-membership.monk", "Monk membership"),
        node("malformed", "Malformed record", { "dnd2024.character.class-membership": "{}" }),
        node("item.knife", "Monk membership", itemComponents),
        node("item.annotated", "Mixed record", { ...itemComponents, "dnd2024.character.class-membership": membership }),
      ] } }, children: { currency: [{ mechanicId: "dnd2024.mechanic.currency-value.read",
        roleEntityIds: { root: "actor.test" }, output: { data: JSON.stringify({ test: "currency-value-read", rootId: "actor.test",
          coinCount: 0, copperValue: 0, denominations: [] }) } }] } };
    const result = projectInventory(ctx);
    assert.deepEqual(result.data.items.map(row => row.id), ["actor.test.class-membership.monk", "malformed", "item.knife", "item.annotated"]);
    assert.equal(result.data.state, "partial", "unknown physical contents still mark coverage incomplete");
    ctx.roles.subject.contains = [ctx.roles.subject.contains[0], ctx.roles.subject.contains[3]];
    const complete = projectInventory(ctx);
    assert.deepEqual(complete.data.items.map(row => row.id), ["item.knife"]);
    assert.equal(complete.data.state, "ready", "a class membership is not missing inventory classification");
    assert.deepEqual(complete.data.reasons, []);
    assert.deepEqual(complete.effects, []);
  });
}
