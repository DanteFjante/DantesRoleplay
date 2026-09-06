import assert from "node:assert/strict";
import test from "node:test";

import { readCompletePages } from "../src/server/complete-pagination.js";

const entries = (count) => Array.from({ length: count }, (_, index) => ({ id: `entry-${index + 1}` }));

for (const count of [99, 100, 101]) {
  test(`complete pagination returns all ${count} entries`, async () => {
    const source = entries(count);
    const cursors = [];
    const result = await readCompletePages({
      fetchPage: async (cursor) => {
        cursors.push(cursor);
        const offset = Number(cursor ?? 0);
        return {
          items: source.slice(offset, offset + 100),
          nextCursor: offset + 100 < source.length ? String(offset + 100) : null,
        };
      },
    });
    assert.equal(result.status, "complete");
    assert.deepEqual(result.items, source);
    assert.deepEqual(cursors, count > 100 ? [null, "100"] : [null]);
  });
}

test("complete pagination rejects repeated cursors instead of returning the first page", async () => {
  const result = await readCompletePages({
    fetchPage: async () => ({ items: entries(1), nextCursor: "again" }),
  });
  assert.deepEqual(result, { status: "incomplete", reason: "repeated-cursor", pagesRead: 2, items: [] });
});

test("complete pagination rejects a failure after a successful first page", async () => {
  let calls = 0;
  const result = await readCompletePages({
    fetchPage: async () => {
      calls += 1;
      if (calls === 2) throw new Error("second page unavailable");
      return { items: entries(100), nextCursor: "second" };
    },
  });
  assert.deepEqual(result, { status: "incomplete", reason: "page-unavailable", pagesRead: 1, items: [] });
});

test("complete pagination rejects oversized and malformed pages", async () => {
  const oversized = await readCompletePages({
    fetchPage: async () => ({ items: entries(101), nextCursor: null }),
  });
  assert.deepEqual(oversized, { status: "incomplete", reason: "oversized-page", pagesRead: 0, items: [] });

  for (const page of [null, {}, { items: "not-an-array" }, { items: [], nextCursor: "" }]) {
    const malformed = await readCompletePages({ fetchPage: async () => page });
    assert.equal(malformed.status, "incomplete");
    assert.deepEqual(malformed.items, []);
  }
});

test("complete pagination stops at explicit item and page bounds", async () => {
  const itemBound = await readCompletePages({
    maximumItems: 100,
    fetchPage: async () => ({ items: entries(100), nextCursor: "more" }),
  });
  assert.deepEqual(itemBound, { status: "incomplete", reason: "item-limit", pagesRead: 1, items: [] });

  const pageBound = await readCompletePages({
    maximumPages: 1,
    maximumItems: 200,
    fetchPage: async () => ({ items: entries(100), nextCursor: "more" }),
  });
  assert.deepEqual(pageBound, { status: "incomplete", reason: "page-limit", pagesRead: 1, items: [] });
});
