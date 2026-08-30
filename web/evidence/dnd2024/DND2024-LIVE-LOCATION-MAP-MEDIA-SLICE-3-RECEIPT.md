# DND2024 live location-owned map media — Slice 3 receipt

Status: **accepted 2026-08-30**
Ruleset alignment: ruleset-neutral presentation projection

## Delivered boundary

- The connected hub now reads live location containment, map anchors, and map visuals.
- It derives the Thalos → region → city/location map tree generically, with no location-ID image,
  containment, placement, or crop tables.
- A bounded asset registry maps reviewed asset keys to local files without deciding ownership,
  hierarchy, audience, or visibility.
- The server emits only the requested exact Player or DM visual variant. Missing, malformed,
  inactive, hidden, or unknown-key records fail closed.
- The raw directory cache remains server-only and its projected response carries an audience stamp,
  preventing DM data from being reused in a Player response.

## Evidence

- Full website test suite: **164 passed, 0 failed**.
- Production website build: **passed**.
- Local connected readback produced 12 live map scopes: Thalos, nine regions, Crownmere, and
  Merrowgate, with the expected live child-marker counts.
- Player readback used Player assets only and contained no DM asset URL or key.
- A focused arbitrary-city test proves that a new live location with a registered visual key works
  without adding its entity ID to website code.

## Deliberate exclusions

No map-authoring UI, new illustration generation, travel/distance/discovery rules, polygonal
geography, or campaign mechanic was added.
