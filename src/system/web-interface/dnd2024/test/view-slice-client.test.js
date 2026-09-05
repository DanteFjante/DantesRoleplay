import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

function source(path) {
  return readFileSync(new URL(path, import.meta.url), "utf8");
}

test("bootstrap renders the navigation shell before the private v1 adapter resolves", () => {
  const main = source("../src/server-host/main.tsx");
  const shellRender = main.indexOf("<BootstrapShell />");
  const initialRead = main.indexOf('await loadEnvelope("player")');

  assert.ok(shellRender >= 0);
  assert.ok(initialRead > shellRender);
  assert.match(main, /new ViewReadClient</u);
  assert.match(main, /fetchImpl: fetchWithSignal/u);
  assert.match(main, /readGameServerContext/u, "the v1 adapter remains available for rollback");
});

test("inactive high-cost views are lazy module boundaries", () => {
  const main = source("../src/server-host/main.tsx");
  const hub = source("../src/components/DndInformationHub.tsx");
  const world = source("../src/components/WorldView.tsx");

  assert.match(main, /lazy\(\(\) => import\("\.\.\/components\/DndInformationHub"\)/u);
  for (const component of [
    "CampaignView",
    "InstalledContentView",
    "items/ItemWorkspace",
    "PlayConversationPanel",
    "PreviewViews",
    "RulesView",
  ]) assert.match(hub, new RegExp(`lazy\\(\\(\\) => import\\(\"\\./${component}\"\\)`, "u"));
  assert.match(world, /lazy\(\(\) => import\("\.\/ScopedMapWorkspace"\)/u);
});

test("rapid scope changes do not serialize behind the prior busy flag", () => {
  const hub = source("../src/components/DndInformationHub.tsx");

  assert.doesNotMatch(hub, /if\s*\(\s*hubBusy\s*\|\|/u);
  assert.match(hub, /requestId !== hubRequestSequence\.current/u);
  assert.match(hub, /error instanceof ViewReadError && error\.category === "cancelled"/u);
});
