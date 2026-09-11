import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import test from "node:test";
import { requestId, sha256Hex, sha256HexSync } from "../../src/data/browser-crypto";
import { ViewReadClient } from "../../src/data/view-read-client";
import { ResourceStore } from "../../src/data/resource-store";

test("HTTP browsers load and fingerprint campaign resources without secure-context APIs", async (t) => {
  const crypto = globalThis.crypto;
  t.mock.getter(globalThis, "crypto", () => ({ getRandomValues: crypto.getRandomValues.bind(crypto) }));
  for (const value of ["", "abc", "Caldris — 測試 🐉".repeat(100)]) {
    assert.equal(await sha256Hex(value), createHash("sha256").update(value).digest("hex"));
    assert.equal(sha256HexSync(value), createHash("sha256").update(value).digest("hex"));
  }
  const payload = { status: "ready", campaign: "fixture" };
  const validate = (value: unknown): value is typeof payload =>
    Boolean(value && (value as typeof payload).status === "ready");
  const read = async () => payload;
  const cacheKey = () => "fixture";
  const client = new ViewReadClient({ read, cacheKey, validate });
  const resource = new ResourceStore().define({ name: "campaign", read, cacheKey, validate });
  const expected = createHash("sha256").update(JSON.stringify(payload)).digest("hex").toUpperCase();
  assert.equal((await client.load({})).fingerprint, expected);
  assert.equal((await resource.load({})).fingerprint, expected);
  const firstId = requestId();
  assert.match(firstId, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  assert.notEqual(requestId(), firstId);
});
