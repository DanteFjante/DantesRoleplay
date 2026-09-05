import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/TacticalBoard.tsx", import.meta.url), "utf8");
const preview = readFileSync(new URL("../src/components/PreviewViews.tsx", import.meta.url), "utf8");
const styles = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");

test("tactical board renders only projected geometry and authoritative labels", () => {
  assert.match(preview, /<CombatBoard key=\{combat\.id\} board=\{combat\.board\}/u);
  assert.match(component, /board\.terrain\.map/u);
  assert.match(component, /board\.obstacles\.map/u);
  assert.match(component, /board\.participants\.map/u);
  assert.match(component, /Current turn:/u);
  assert.match(component, /Movement legality comes from the\s*encounter mechanics, not this display/iu);
  assert.doesNotMatch(component, /movementSpeed|collision|pathfinding|difficultTerrain/iu);
  assert.match(component, /viewBox=\{`0 0 \$\{board.columns\} \$\{board.rows\}`\}/u);
});

test("tactical board styles permit native vertical scrolling and browser pinch", () => {
  assert.match(styles, /\.tactical-board-viewport\s*\{[^}]*touch-action:\s*pan-y pinch-zoom;/su);
});

test("tokens expose position, footprint, elevation, selection, and active-turn state", () => {
  assert.match(component, /Grid \$\{participant\.position\.x \+ 1\}/u);
  assert.match(component, /Footprint \$\{participant\.position\.width\}/u);
  assert.match(component, /Elevation \$\{participant\.position\.elevationFeet\}/u);
  assert.match(component, /aria-pressed=\{selectedId === participant\.id\}/u);
  assert.match(component, /participant\.active \? "Current turn/u);
});
