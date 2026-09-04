import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/TacticalBoard.tsx", import.meta.url), "utf8");
const preview = readFileSync(new URL("../src/components/PreviewViews.tsx", import.meta.url), "utf8");
const styles = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");

test("tactical board renders only projected geometry and authoritative labels", () => {
  assert.match(preview, /combat\.board \? <TacticalBoard board=\{combat\.board\}/u);
  assert.match(component, /board\.terrain\.map/u);
  assert.match(component, /board\.obstacles\.map/u);
  assert.match(component, /board\.participants\.map/u);
  assert.match(component, /Current turn:/u);
  assert.match(component, /Movement legality comes from the\s*encounter mechanics, not this display/iu);
  assert.doesNotMatch(component, /movementSpeed|collision|pathfinding|difficultTerrain/iu);
  assert.match(styles, /background-size:[\s\S]*--board-columns[\s\S]*--board-rows/u);
});

test("tactical board has uniform explicit controls without scroll-wheel zoom", () => {
  assert.doesNotMatch(component, /onWheel=/u);
  assert.doesNotMatch(component, /addEventListener\(\s*["']wheel["']/u);
  assert.match(component, /aria-label="Zoom tactical board out"/u);
  assert.match(component, /aria-label="Zoom tactical board in"/u);
  assert.match(component, /> Fit board</u);
  assert.match(component, /> Reset view</u);
  assert.match(component, /aria-keyshortcuts="ArrowLeft ArrowRight ArrowUp ArrowDown 0 F"/u);
  assert.match(styles, /\.tactical-board-viewport\s*\{[^}]*touch-action:\s*pan-y pinch-zoom;/su);
});

test("tokens expose position, footprint, elevation, selection, and active-turn state", () => {
  assert.match(component, /Grid \$\{participant\.position\.x \+ 1\}/u);
  assert.match(component, /Footprint \$\{participant\.position\.width\}/u);
  assert.match(component, /Elevation \$\{participant\.position\.elevationFeet\}/u);
  assert.match(component, /aria-pressed=\{selectedId === participant\.id\}/u);
  assert.match(component, /participant\.active \? "Current turn/u);
});
