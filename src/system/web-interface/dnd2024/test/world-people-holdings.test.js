import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../../..");
const source = await readFile(path.join(root, "catalog/applications/dnd2024/mechanics/world",
  "dnd2024.mechanic.world.people-holdings.project.js"), "utf8");
const project = new Function("ctx", source);
const component = (value) => JSON.stringify(value);
const location = (id, name, contains = [], status = "active") => ({
  id, name, components: { "game.core.world.location": component({
    kind: "region", status, summary: `${name} summary.`, visibility: "gm",
  }) }, contains,
});
const context = (perspective = "dm") => ({
  audience: { perspective }, input: {}, roles: { world: {
    id: "realm-root-7", name: "The Seventh Realm",
    components: { "game.core.world.root": component({
      status: "active", summary: "A world without an ID convention.", visibility: "party",
    }) },
    contains: [
      location("azure-reach", "Azure Reach", [
        { id: "subject-9", name: "Mara", components: { "game.core.world.motive": component({
          status: "active", summary: "Protect the harbour.", visibility: "gm",
        }) } },
        { id: "not-a-creature-prefix", name: "Glass Drake", components: {
          "dnd2024.creature.classification": component({
            creatureTypeRef: { entityId: "creature-type.dragon" }, descriptiveTagRefs: [],
          }),
        } },
        { id: "plain-identity", name: "Rope", components: {
          "dnd2024.core.definition-link": component({ definition: { entityId: "item.rope" } }),
        } },
        { id: "actor.looks-like-person", name: "Not classified", components: {} },
        location("inner-harbour", "Inner Harbour", [
          { id: "someone-nested", name: "Dockmaster", components: {
            "game.core.world.motive": component({
              status: "draft", summary: "Keep the berths orderly.", visibility: "party",
            }),
          } },
        ]),
      ]),
      location("gone-place", "Archived Place", [{
        id: "hidden-person", name: "Hidden", components: { "game.core.world.motive": component({
          status: "active", summary: "Remain hidden.", visibility: "gm",
        }) },
      }], "archived"),
    ],
  } },
});

test("World people and holdings use containment and components rather than identifier spelling", () => {
  const result = project(context()).data;
  assert.equal(result.state, "ready");
  assert.deepEqual(result.locations.map(({ id, parentId }) => [id, parentId]), [
    ["azure-reach", "realm-root-7"], ["inner-harbour", "azure-reach"],
  ]);
  assert.deepEqual(result.people.map(({ id, kind, locationId }) => [id, kind, locationId]), [
    ["someone-nested", "NPC", "inner-harbour"],
    ["not-a-creature-prefix", "Creature", "azure-reach"],
    ["subject-9", "NPC", "azure-reach"],
  ]);
  assert.deepEqual(result.holdings, [{
    id: "plain-identity", name: "Rope", locationId: "azure-reach", kind: "Item",
  }]);
  assert.ok(!result.people.some(({ id }) => id === "actor.looks-like-person"));
  assert.ok(!result.people.some(({ id }) => id === "hidden-person"));
});

test("World people and holdings are a DM-only directory with an explicit empty denial", () => {
  assert.deepEqual(project(context("player")).data, {
    version: 1, state: "forbidden", world: null, locations: [], people: [], holdings: [],
    limits: { contentsDepth: 4, recordCount: 200, complete: true },
  });
});

test("World people projection rejects results beyond its fixed complete bound", () => {
  const value = context();
  value.roles.world.contains = [location("large-place", "Large Place",
    Array.from({ length: 201 }, (_, index) => ({
      id: `person-${index}`, name: `Person ${index}`,
      components: { "game.core.world.motive": component({
        status: "active", summary: "A bounded person.", visibility: "gm",
      }) },
    })))];
  assert.throws(() => project(value), /declared bound/u);
});
