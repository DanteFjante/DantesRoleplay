import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/PreviewViews.tsx", import.meta.url), "utf8");
const hub = readFileSync(new URL("../src/components/DndInformationHub.tsx", import.meta.url), "utf8");
const styles = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");

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
  assert.match(component, /if \(items\.length === 0 && !partial\) return null/u);
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

test("Current tab connects neither a live conversation composer nor board editing", () => {
  assert.doesNotMatch(hub, /PlayConversationPanel|application-conversation|conversation-change/u);
  assert.doesNotMatch(styles, /play-conversation-panel|application-conversation/u);
  assert.match(hub, /<CurrentViewPreview/u);
  assert.doesNotMatch(hub, /onBoardAccepted\s*=|draftScope\s*=/u);
});

test("Current keeps the last confirmed scene visible through a local refresh failure", () => {
  assert.match(hub, /deferredNotice && currentSituation\.status !== "ready"/u);
  assert.match(hub, /Showing the last confirmed scene/u);
  assert.match(hub, /Retry current scene/u);
  assert.doesNotMatch(hub, /kind: "exploration" as const/u,
    "a missing Current projection must not be guessed from the selected or current place");
});

test("Current uses meaningful scene headings and omits empty image and action regions", () => {
  assert.match(component, /title=\{situation\.conversation\.name\}/u);
  assert.match(component, /title=\{combat\.name\}/u);
  assert.match(component, /<h1 id="main-view-heading" tabIndex=\{-1\}>\{location\.name\}<\/h1>/u);
  assert.match(component, /title="No current scene"/u);
  assert.match(component, /current-scene-card--text-only/u);
  assert.match(styles, /\.current-scene-card--text-only/u);
});

test("Combat Current View renders the canonical tactical board when projected", () => {
  assert.match(component, /import \{ CombatBoard \} from "\.\/CombatBoard"/u);
  assert.match(component, /<CombatBoard key=\{combat\.id\} board=\{combat\.board\}/u);
});
