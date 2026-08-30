import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/PreviewViews.tsx", import.meta.url), "utf8");

test("Conversation and Combat retain the exact projected location context", () => {
  assert.match(component, /function LocationContextPanel/u);
  assert.match(component, /current-conversation-location/u);
  assert.match(component, /current-combat-location/u);
  assert.match(component, />Where you are</u);
  assert.match(component, /location\.description/u);
  assert.match(component, /location\.observations/u);
});

test("Current View presents authored scene affordances without an execution contract", () => {
  assert.match(component, /function SceneAffordancesPanel/u);
  assert.match(component, />Available now</u);
  assert.match(component, /No scene actions have been declared for this situation/u);
  assert.doesNotMatch(component, /application-action|mechanic-id|prepare-action|execute-action/iu);
  assert.match(component, /function DmLocationContext/u);
});
