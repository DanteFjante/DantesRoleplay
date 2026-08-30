import assert from "node:assert/strict";
import test from "node:test";

import { resolveHubSurface } from "../src/data/hub-availability.js";

test("ready campaign context opens the private table", () => {
  assert.equal(resolveHubSurface({ status: "ready" }), "table");
});

test("denied and unavailable campaign contexts keep the public Rules library open", () => {
  assert.equal(resolveHubSurface({ status: "denied" }), "rules");
  assert.equal(resolveHubSurface({ status: "unavailable" }), "rules");
  assert.equal(resolveHubSurface({ status: "character-creation-required" }), "rules");
  assert.equal(resolveHubSurface(null), "rules");
});
