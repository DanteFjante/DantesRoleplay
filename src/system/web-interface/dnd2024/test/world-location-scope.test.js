import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../../..");
const source = await readFile(path.join(root, "catalog/applications/dnd2024/mechanics/world",
  "dnd2024.mechanic.world.location-scope.project.js"), "utf8");
const project = new Function("ctx", source);

const rootRecord = (visibility = "party") => ({
  roles: {
    scope: {
      id: "realm-root-7", name: "The Seventh Realm", containerId: null, containerSlot: "",
      components: {
        "game.core.world.root": JSON.stringify({
          status: "active", summary: "A World whose ID has no naming convention.", visibility,
        }),
      },
      contains: [
        location("place-public", "Public Reach", "public", 100),
        location("place-party", "Party Camp", "party", 200, "draft"),
        location("place-gm", "Hidden Vault", "gm", 300),
        { id: "unrelated-object", name: "Unrelated object", slot: "contents", components: {} },
      ],
    },
  },
  input: {},
});

function location(id, name, visibility, x, status = "active") {
  return {
    id, name, slot: "region",
    components: {
      "game.core.world.location": JSON.stringify({
        kind: "region", status, summary: `${name} summary.`, visibility,
      }),
      "game.core.world.map.anchor": JSON.stringify({ x, y: 500 }),
    },
  };
}

test("world scope projection uses containment and audience instead of identifier spelling", () => {
  const player = project({ ...rootRecord(), audience: { perspective: "player" } }).data;
  assert.equal(player.state, "ready");
  assert.equal(player.scope.id, "realm-root-7");
  assert.deepEqual(player.locations.map((item) => item.id), ["place-party", "place-public"]);
  assert.equal(player.locations[0].status, "draft", "authored draft locations remain visible until archived");
  assert.ok(player.locations.every((item) => item.parentId === "realm-root-7"));

  const dm = project({ ...rootRecord(), audience: { perspective: "dm" } }).data;
  assert.deepEqual(dm.locations.map((item) => item.id), ["place-gm", "place-party", "place-public"]);
});

test("world scope projection fails closed when the exact owner is hidden", () => {
  const result = project({ ...rootRecord("gm"), audience: { perspective: "player" } }).data;
  assert.deepEqual(result, {
    version: 1, state: "forbidden", scope: null, locations: [],
    limits: { contentsDepth: 1, locationCount: 100, complete: true },
  });
});
