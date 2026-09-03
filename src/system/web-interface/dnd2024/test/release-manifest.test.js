import assert from "node:assert/strict";
import test from "node:test";
import {
  extractAssetReferences,
  findRetiredMapSignatures,
  isContentAddressedAsset,
  sha256,
} from "../scripts/create-release-manifest.mjs";

test("release manifests recognize exact page assets and hashes", () => {
  const html = '<script src="/ui/dnd2024-play/assets/index-Ab12_cd3.js"></script>';
  assert.deepEqual(extractAssetReferences(html), ["assets/index-Ab12_cd3.js"]);
  assert.equal(isContentAddressedAsset("assets/index-Ab12_cd3.js"), true);
  assert.equal(isContentAddressedAsset("assets/index.js"), false);
  assert.equal(sha256(Buffer.from("release")), "A4D451EC23463726F72C43D64C710968F6B602CD653B4DE8ADEE1B556240A829");
});

test("release manifests reject the retired map help signature", () => {
  assert.deepEqual(findRetiredMapSignatures("pinch, scroll, or use + and − to zoom"), [
    "pinch, scroll, or use + and − to zoom",
  ]);
  assert.deepEqual(findRetiredMapSignatures("Page scrolling never changes map zoom."), []);
});
