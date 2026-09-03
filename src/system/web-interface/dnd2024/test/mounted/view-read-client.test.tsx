import assert from "node:assert/strict";
import test from "node:test";

import { ViewReadClient, ViewReadError } from "../../src/data/view-read-client";

type Request = { scope: string };
type Result = { version: 1; value: string };

function isResult(value: unknown): value is Result {
  return Boolean(value && typeof value === "object" &&
    (value as Result).version === 1 && typeof (value as Result).value === "string");
}

test("latest view request wins even when the obsolete reader ignores cancellation", async () => {
  const pending = new Map<string, (value: Result) => void>();
  const client = new ViewReadClient<Request, Result>({
    cacheKey: ({ scope }) => scope,
    read: ({ scope }) => new Promise((resolve) => pending.set(scope, resolve)),
    validate: isResult,
  });

  const obsolete = client.load({ scope: "campaign-a" });
  const current = client.load({ scope: "campaign-b" });
  pending.get("campaign-b")?.({ version: 1, value: "current" });
  const currentResult = await current;
  pending.get("campaign-a")?.({ version: 1, value: "obsolete" });

  assert.equal(currentResult.value.value, "current");
  await assert.rejects(obsolete, (error) =>
    error instanceof ViewReadError && error.category === "cancelled");
  assert.equal(client.peek({ scope: "campaign-a" }), null);
  assert.equal(client.peek({ scope: "campaign-b" })?.value.value, "current");
});

test("transient reads retry once and cache only the validated response fingerprint", async () => {
  let attempts = 0;
  const client = new ViewReadClient<Request, Result>({
    cacheKey: ({ scope }) => scope,
    read: async () => {
      attempts += 1;
      if (attempts === 1) throw new TypeError("temporary network failure");
      return { version: 1, value: "ready" };
    },
    retryDelayMs: 0,
    validate: isResult,
  });

  const result = await client.load({ scope: "campaign-a" });

  assert.equal(attempts, 2);
  assert.match(result.fingerprint, /^[0-9A-F]{64}$/u);
  assert.deepEqual(client.peek({ scope: "campaign-a" }), result);
});

test("incompatible payloads never replace the last-good cached result", async () => {
  let valid = true;
  const client = new ViewReadClient<Request, Result>({
    cacheKey: ({ scope }) => scope,
    read: async () => valid
      ? { version: 1, value: "last-good" }
      : ({ version: 2, value: "invalid" } as unknown as Result),
    validate: isResult,
  });

  await client.load({ scope: "campaign-a" });
  valid = false;

  await assert.rejects(client.load({ scope: "campaign-a" }), (error) =>
    error instanceof ViewReadError && error.category === "incompatible-data");
  assert.equal(client.peek({ scope: "campaign-a" })?.value.value, "last-good");

  client.invalidate({ scope: "campaign-a" });
  assert.equal(client.peek({ scope: "campaign-a" }), null);
});
