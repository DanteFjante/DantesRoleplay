import assert from "node:assert/strict";
import test from "node:test";

import { resolveHubSurface } from "../src/data/hub-availability.js";

test("ready campaign context opens the table", () => {
  assert.equal(resolveHubSurface({ status: "ready" }), "table");
});

test("startup distinguishes actual denial, connection failure and application failure", () => {
  assert.equal(resolveHubSurface({ status: "denied" }), "denied");
  assert.equal(resolveHubSurface({ status: "unavailable" }), "unavailable");
  assert.equal(resolveHubSurface({ status: "unavailable", reason: "connection" }), "connection");
  assert.equal(resolveHubSurface({ status: "character-creation-required" }), "unavailable");
  assert.equal(resolveHubSurface(null), "unavailable");
});
