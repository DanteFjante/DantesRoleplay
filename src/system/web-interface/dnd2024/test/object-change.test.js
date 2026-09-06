import assert from "node:assert/strict";
import test from "node:test";

import { parseCursorCheckpoint, parseObjectChange } from "../src/data/object-change.js";

const boundary = { applicationId: "dnd2024", stateSpaceId: "state.fixture" };
const notice = (cursor, applicationId = boundary.applicationId, stateSpaceId = boundary.stateSpaceId) =>
  JSON.stringify({ contractVersion: 1, cursor, applicationId, stateSpaceId,
    scope: "object", object: { qualifiedId: "dnd2024.object.campaign-summary", version: 2 } });

test("object change notices ignore duplicates, out-of-order cursors, and other boundaries", () => {
  assert.equal(parseObjectChange(notice(7), boundary, 7), null);
  assert.equal(parseObjectChange(notice(6), boundary, 7), null);
  assert.equal(parseObjectChange(notice(8, "other"), boundary, 7), null);
  assert.equal(parseObjectChange(notice(8, boundary.applicationId, "other"), boundary, 7), null);
  assert.equal(parseObjectChange("not-json", boundary, 7), null);
  assert.equal(parseObjectChange(notice(8), boundary, 7)?.cursor, 8);
});

test("cursor checkpoints advance monotonically without carrying an object identity", () => {
  assert.equal(parseCursorCheckpoint('{"cursor":9}', 8), 9);
  assert.equal(parseCursorCheckpoint('{"cursor":8}', 8), 8);
  assert.equal(parseCursorCheckpoint('{"cursor":7}', 8), 8);
  assert.equal(parseCursorCheckpoint('{"cursor":"10"}', 8), 8);
});
