import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/PreviewViews.tsx", import.meta.url), "utf8");
const hub = readFileSync(new URL("../src/components/DndInformationHub.tsx", import.meta.url), "utf8");
const play = readFileSync(new URL("../src/components/PlayConversationPanel.tsx", import.meta.url), "utf8");

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

test("Current View presents durable recorded play continuity as a distinct non-authoritative branch", () => {
  assert.match(component, /situation\.kind === "recorded"/u);
  assert.match(component, /latest durable play situation shared across AI clients and browser refreshes/iu);
  assert.match(component, /situation\.recorded\.participants/u);
  assert.match(component, /situation\.recorded\.summary/u);
  assert.match(component, />Recent interactions</u);
  assert.match(component, /message\.text/u);
});

test("Current tab binds the durable play conversation to the selected campaign and refreshes after turns", () => {
  assert.match(hub, /<PlayConversationPanel/u);
  assert.match(hub, /sessionContextId=\{contextSelection\.selectedCampaignId\}/u);
  assert.match(hub, /requestHub\(perspective, contextSelection\.selectedCampaignId, false, true\)/u);
  assert.match(play, /application-conversation/u);
  assert.match(play, /conversation-change/u);
  assert.doesNotMatch(play, /dnd2024|thalorien|caldris/iu);
});
