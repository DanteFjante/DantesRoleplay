import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const project = new Function("ctx", readFileSync(new URL(
  "../../../../../catalog/applications/dnd2024/mechanics/data/dnd2024.mechanic.inventory-container-page.project.js", import.meta.url), "utf8"));

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
