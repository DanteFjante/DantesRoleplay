# D&D 2024 web UI map atlas corrective slice — active React map registry

Status: **active**

Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5

Dependency boundary: corrective presentation change inside the accepted React server bundle and
the already-delivered same-origin map-image route. No dependency-tree change is required because
the location-owned live map keys, hierarchy, audience projection, and page-bundle contract remain
unchanged.

Ruleset alignment: **dnd2024-compatible presentation**

Outcome: make the active `/ui/dnd2024-play` React map registry resolve Thalos and its nine regional
map asset keys to the approved PNGs under `/components/maps/`, rebuild the existing page bundle,
and publish it through the existing `dnd2024-play` identity.

Allowed files/areas:

- `prototype/dnd2024/src/data/map-assets.ts`;
- `prototype/dnd2024/vite.server.config.ts`;
- focused prototype map tests;
- generated `prototype/dnd2024/server-dist` and page ZIP;
- the existing live page-bundle publication boundary;
- this implementation document and one receipt.

Exclusions: no world or campaign record write, no map key or location ID change, no containment or
hierarchy change, no schema or migration, no new route, no hosted-Site deployment, no city-map
replacement, and no deletion of prior source artwork or page revisions.

Stop point: after the active React page shows `thalos-world.png`, a regional navigation shows its
matching PNG, focused tests/build pass, and live page/image readback is recorded.

## Corrective evidence and decisions

- Browser inspection proved the active page rendered
  `/ui/dnd2024-play/assets/thalos-map.svg`; the earlier atlas slice changed the superseded custom
  element rather than the active React registry.
- The ten reviewed PNGs already respond from `/components/maps/*.png` with exact source hashes.
- The page-bundle store permits 25 MiB uncompressed while the PNG atlas is about 33 MiB. The active
  React registry therefore uses the existing same-origin image route instead of duplicating the
  bytes into the page ZIP.
- Player and DM map keys resolve to the same public-player-safe illustrative PNG. Audience-specific
  overlays and live feature projection remain server-owned and unchanged.
- Crownmere and Merrowgate retain their existing page-bundled city images.

## Verification

- focused prototype live-map placement tests;
- server-bundle build and bounded ZIP inventory;
- live same-origin page/script/image readback;
- in-app browser confirmation of the world image and one regional image.

