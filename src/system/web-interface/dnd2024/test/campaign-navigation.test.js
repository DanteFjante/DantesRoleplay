import assert from "node:assert/strict";
import test from "node:test";

import { resolveCampaignWorldTarget } from "../src/data/campaign-navigation.ts";

test("Campaign links resolve only an exact record in the projected World", () => {
  const projected = [
    { id: "location.visible", name: "Visible place" },
    { id: "person.visible", name: "Visible person" },
  ];
  const original = structuredClone(projected);

  assert.equal(resolveCampaignWorldTarget(projected, "person.visible"), projected[1]);
  assert.equal(resolveCampaignWorldTarget(projected, "person.hidden"), null);
  assert.equal(resolveCampaignWorldTarget(projected, "Visible person"), null);
  assert.deepEqual(projected, original);
});
