import assert from "node:assert/strict";
import test from "node:test";

import { summarizeSamples } from "../scripts/collect-baseline.mjs";

test("baseline percentiles always retain sample count, p50, and p95", () => {
  assert.deepEqual(summarizeSamples([30, 10, 20, 40, 50]), {
    sampleCount: 5,
    p50Ms: 30,
    p95Ms: 50,
  });
  assert.equal(summarizeSamples([]), null);
  assert.equal(summarizeSamples([10, Number.NaN]), null);
});
