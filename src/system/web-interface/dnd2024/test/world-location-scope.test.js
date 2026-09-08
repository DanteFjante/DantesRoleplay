import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../../..");
const source = await readFile(path.join(root, "catalog/applications/dnd2024/mechanics/world",
  "dnd2024.mechanic.world.location-scope.project.js"), "utf8");
const project = new Function("ctx", source);
const pageSource = await readFile(path.join(root, "catalog/applications/dnd2024/mechanics/world",
  "dnd2024.mechanic.world.location-scope.page.js"), "utf8");
const projectPage = new Function("ctx", pageSource);

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

function pagedScope(id, name, children, { world = false, visibility = "party", parentId = null } = {}) {
  return {
    roles: {
      scope: {
        id, name, containerId: parentId, containerSlot: parentId ? "region" : "",
        components: {
          [world ? "game.core.world.root" : "game.core.world.location"]: JSON.stringify(world
            ? { status: "active", summary: `${name} summary.`, visibility }
            : { kind: "region", status: "active", summary: `${name} summary.`, visibility }),
        },
        contains: children,
      },
    },
    input: { offset: 0, expectedSourceRevision: null },
    audience: { perspective: "player" },
  };
}

function numberedLocation(index, parent = "scope", visibility = "public") {
  const value = String(index).padStart(3, "0");
  return location(`${parent}.place-${value}`, `Place ${value}`, visibility, index % 1001);
}

test("paged world scopes expose 101 siblings through a source-bound continuation", () => {
  const context = pagedScope("realm-root-7", "The Seventh Realm",
    Array.from({ length: 101 }, (_, index) => numberedLocation(index)), { world: true });
  delete context.roles.scope.contains[0].components["game.core.world.map.anchor"];
  const first = projectPage(context).data;
  assert.equal(first.totalCount, 101);
  assert.equal(first.locations.length, 100);
  assert.equal(first.complete, false);
  assert.equal(first.nextCursor, "100");
  assert.equal(first.locations[0].mapAnchor, null, "an image or marker is not required for navigation");

  const second = projectPage({
    ...context,
    input: { offset: 100, expectedSourceRevision: "A".repeat(64) },
  }).data;
  assert.deepEqual(second.locations.map((entry) => entry.id), ["scope.place-100"]);
  assert.equal(second.complete, true);
  assert.equal(second.nextCursor, null);
});

test("audience filtering happens before location counts and page selection", () => {
  const children = [
    ...Array.from({ length: 101 }, (_, index) => numberedLocation(index)),
    ...Array.from({ length: 99 }, (_, index) => numberedLocation(index + 101, "secret", "gm")),
  ];
  const context = pagedScope("realm-root-7", "The Seventh Realm", children, { world: true });
  const player = projectPage(context).data;
  const dm = projectPage({ ...context, audience: { perspective: "dm" } }).data;
  assert.equal(player.totalCount, 101);
  assert.equal(dm.totalCount, 200);
  assert.equal(player.locations.length, 100);
  assert.equal(dm.locations.length, 100);
});

test("one thousand distributed locations remain reachable without hydrating unrelated branches", () => {
  const regions = Array.from({ length: 10 }, (_, index) => numberedLocation(index, "region"));
  const rootPage = projectPage(pagedScope("world.large", "Large World", regions, { world: true })).data;
  assert.equal(rootPage.totalCount, 10);

  let reachable = rootPage.locations.length;
  for (const [index, region] of rootPage.locations.entries()) {
    const children = Array.from({ length: 99 }, (_, child) => numberedLocation(child, `region-${index}`));
    const branch = projectPage(pagedScope(region.id, region.name, children, {
      parentId: "world.large",
    })).data;
    assert.equal(branch.totalCount, 99);
    reachable += branch.locations.length;
  }
  assert.equal(reachable, 1_000);
});
