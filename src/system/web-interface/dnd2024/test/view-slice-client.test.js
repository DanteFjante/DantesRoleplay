import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

function source(path) {
  return readFileSync(new URL(path, import.meta.url), "utf8");
}

test("the published game entry does not connect premise editing or combat-map mutations", () => {
  const main = source("../src/server-host/main.tsx");
  const hub = source("../src/components/DndInformationHub.tsx");

  assert.doesNotMatch(main, /campaign-premise-write|writeCampaignPremise\s*=/u,
    "the retained mapped-write client must not be imported or supplied by the read-only entry");
  assert.doesNotMatch(hub, /draftScope\s*=|onBoardAccepted\s*=/u,
    "the game hub must not enable the retained board upload/acceptance workshop");
});

test("bootstrap renders the navigation shell before the connected resource adapter resolves", () => {
  const main = source("../src/server-host/main.tsx");
  const hub = source("../src/components/DndInformationHub.tsx");
  const shellRender = main.indexOf("<BootstrapShell />");
  const initialRead = main.indexOf('await loadInitialHub(loadEnvelope,');

  assert.ok(shellRender >= 0);
  assert.ok(initialRead > shellRender);
  assert.match(main, /new TableResourceOwner/u);
  assert.doesNotMatch(main, /BrowserObjectQueryState/u);
  assert.doesNotMatch(hub, /TableResourceOwner|new ResourceStore/u,
    "the navigation shell does not own resource loading or retention");
  assert.match(main, /new CharacterResourceOwner/u);
  assert.doesNotMatch(main, /new ViewReadClient</u);
  assert.match(main, /fetchImpl: fetchWithSignal/u);
  assert.match(main, /readGameServerContext/u, "the connected resource adapter owns bootstrap reads");
});

test("inactive high-cost views are lazy module boundaries", () => {
  const main = source("../src/server-host/main.tsx");
  const hub = source("../src/components/DndInformationHub.tsx");
  const world = source("../src/components/WorldView.tsx");

  assert.match(main, /lazy\(\(\) => import\("\.\.\/components\/DndInformationHub"\)/u);
  for (const component of [
    "CampaignView",
    "InstalledContentView",
    "items/ItemWorkspaceFeature",
    "character/CharacterWorkspaceFeature",
    "PreviewViewsFeature",
    "RulesView",
  ]) assert.match(hub, new RegExp(`lazy\\(\\(\\) => import\\(\"\\./${component}\"\\)`, "u"));
  assert.match(world, /lazy\(\(\) => import\("\.\/ScopedMapWorkspace"\)/u);
});

test("the World Locations entry reads one bounded scope instead of assembling the complete directory", () => {
  const main = source("../src/server-host/main.tsx");

  assert.doesNotMatch(main, /readWorldLocationDirectory/u);
  assert.doesNotMatch(main, /completeDirectory/u);
  assert.match(main, /connectedCampaignToWorldScopeUpdate/u);
});

test("rapid scope changes do not serialize behind the prior busy flag", () => {
  const hub = source("../src/components/DndInformationHub.tsx");

  const start = hub.indexOf("async function requestHubOutcome(");
  const end = hub.indexOf("async function requestHub(", start);
  assert.ok(start >= 0 && end > start);
  assert.doesNotMatch(hub.slice(start, end), /\bhubBusy\b/u,
    "user scope requests must not wait behind busy state; background/post-write recovery may wait");
  assert.match(hub, /requestId !== hubRequestSequence\.current/u);
  assert.match(hub, /error instanceof ViewReadError && error\.category === "cancelled"/u);
});
