import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const component = readFileSync(new URL("../src/components/MapCanvas.tsx", import.meta.url), "utf8");
const styles = readFileSync(new URL("../src/styles.css", import.meta.url), "utf8");

test("map bases keep their intrinsic aspect ratio instead of cropping into a fixed frame", () => {
  assert.doesNotMatch(styles, /\.world-map-canvas\s*\{[^}]*aspect-ratio:/s);
  assert.match(styles, /\.world-map-canvas > img\s*\{[^}]*height:\s*auto;/s);
  assert.match(styles, /\.world-map-canvas > img\s*\{[^}]*object-fit:\s*contain;/s);
  assert.doesNotMatch(styles, /\.world-map-canvas > img\s*\{[^}]*object-fit:\s*cover;/s);
});

test("marker bottom center is the coordinate anchor and label width cannot move it", () => {
  assert.match(styles, /\.world-map-marker\s*\{[^}]*width:\s*40px;[^}]*height:\s*46px;/s);
  assert.match(styles, /\.world-map-marker\s*\{[^}]*transform:\s*translate\(-50%,\s*-100%\);/s);
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
