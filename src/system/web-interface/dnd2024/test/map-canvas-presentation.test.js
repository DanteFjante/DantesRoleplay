import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/MapCanvas.tsx", import.meta.url), "utf8");
const workspace = readFileSync(new URL("../src/components/ScopedMapWorkspace.tsx", import.meta.url), "utf8");
const styles = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");

test("map bases keep their intrinsic aspect ratio inside the interactive viewport", () => {
  assert.doesNotMatch(styles, /\.world-map-canvas\s*\{[^}]*aspect-ratio:/s);
  assert.match(styles, /\.world-map-stage > img\s*\{[^}]*height:\s*auto;/s);
  assert.match(styles, /\.world-map-stage > img\s*\{[^}]*object-fit:\s*contain;/s);
  assert.match(styles, /\.world-map-stage\s*\{[^}]*transform-origin:\s*0 0;/s);
});

test("marker bottom center is the coordinate anchor and label width cannot move it", () => {
  assert.match(styles, /\.world-map-marker\s*\{[^}]*width:\s*40px;[^}]*height:\s*46px;/s);
  assert.match(styles, /\.world-map-marker\s*\{[^}]*transform:\s*translate\(-50%,\s*-100%\) scale\(var\(--map-marker-scale, 1\)\);/s);
  assert.match(styles, /\.world-map-marker__pin\s*\{[^}]*position:\s*absolute;[^}]*bottom:\s*6px;[^}]*left:\s*50%;/s);
});

test("place names behave as visual tooltips without replacing accessible button names", () => {
  assert.match(component, /aria-label=\{`\$\{feature\.name\}/);
  assert.match(component, /<span aria-hidden="true" className="world-map-marker__label">/);
  assert.match(styles, /\.world-map-marker__label\s*\{[^}]*opacity:\s*0;[^}]*visibility:\s*hidden;/s);
  assert.match(styles, /\.world-map-marker:hover \.world-map-marker__label,/);
  assert.match(styles, /\.world-map-marker:focus-visible \.world-map-marker__label,/);
  assert.match(styles, /\.world-map-marker\[aria-pressed="true"\] \.world-map-marker__label\s*\{[^}]*visibility:\s*visible;/s);
});

test("selected place stays in page flow and empty map space clears marker selection", () => {
  assert.match(styles, /\.world-map-selection\s*\{[^}]*position:\s*static;/s);
  assert.doesNotMatch(styles, /\.world-map-selection\s*\{[^}]*position:\s*sticky;/s);
  assert.match(component, /className="world-map-canvas"[\s\S]*?onClick=\{\(\) => \{[\s\S]*?onFeatureSelect\(""\);/s);
  assert.match(component, /onClick=\{\(event\) => \{\s*event\.stopPropagation\(\);\s*onFeatureSelect\(feature\.id\);/s);
});

test("map viewport state stays local and touch policy leaves browser gestures available", () => {
  assert.doesNotMatch(component, /onWheel=/);
  assert.doesNotMatch(component, /addEventListener\(\s*["']wheel["']/);
  assert.doesNotMatch(styles, /\.world-map-canvas\s*\{[^}]*touch-action:\s*none;/s);
  assert.match(styles, /\.world-map-canvas\s*\{[^}]*touch-action:\s*pan-y pinch-zoom;/s);
  assert.match(styles, /\.map-viewport-toolbar button\s*\{[^}]*min-width:\s*44px;[^}]*min-height:\s*44px;/s);
  assert.match(styles, /\.map-viewport-toolbar button:focus-visible\s*\{[^}]*outline:\s*2px solid/s);
  assert.match(component, /Fit map/);
  assert.match(component, /Focus selected/);
  assert.match(workspace, /window\.sessionStorage\.setItem\(MAP_VIEW_SESSION_KEY/);
  assert.doesNotMatch(workspace, /fetch\([^)]*MAP_VIEW_SESSION_KEY/);
});

test("marker previews use only projected feature imagery and retain an accessible button name", () => {
  assert.match(component, /feature\.preview \? <img alt="" draggable=\{false\} src=\{feature\.preview\.imageUrl\}/);
  assert.match(component, /aria-label=\{`\$\{feature\.name\}\. \$\{feature\.detail\}/);
  assert.match(styles, /\.world-map-marker:hover \.world-map-marker__preview,/);
  assert.match(styles, /\.world-map-marker:focus-visible \.world-map-marker__preview/);
});
