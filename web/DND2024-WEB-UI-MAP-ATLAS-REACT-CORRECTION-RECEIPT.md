# D&D 2024 web UI map atlas corrective slice receipt

Status: **accepted 2026-08-30**

Implementation owner: `DND2024-WEB-UI-MAP-ATLAS-REACT-CORRECTION-IMPLEMENTATION.md`

## Delivered correction

- Confirmed in the live browser that revision 8 still rendered
  `/ui/dnd2024-play/assets/thalos-map.svg` from the active React registry.
- Changed the existing Thalos and nine regional Player/DM asset-key pairs to the reviewed public
  PNG routes under `/components/maps/`.
- Kept audience-specific live features, overlays, names, and visibility projection unchanged; the
  shared generated bases are explicitly public-player-safe and illustrative only.
- Kept Crownmere and Merrowgate on their existing page-bundled city maps.
- Removed the now-unused Thalos SVG copies from the generated server-bundle inventory without
  deleting their source files.
- Exported active revision 8 to a temporary rollback ZIP before publication.
- Published the corrected bounded page bundle as active `dnd2024-play` revision **9**.

## Verification evidence

- Focused live-map placement tests: **7 passed, 0 failed**.
- Full React website suite: **165 passed, 0 failed**.
- Server-bundle build: passed.
- Revision 9 archive: **5 entries**, **7,989,446 uncompressed bytes**, **7,660,052 compressed
  bytes**, largest entry **3,788,828 bytes**; all remain within the existing page-bundle limits.
- Live page and new hashed JavaScript: HTTP **200**; the script contains
  `/components/maps/thalos-world.png` and no old page-bundled Thalos URL.
- Live browser after reload:
  - Player Thalos map uses `/components/maps/thalos-world.png`, 1448×1086;
  - Aldros uses `/components/maps/region-aldros.png`, 1448×1086;
  - Player perspective was restored and the corrected Thalos map was left open.

## Deliberate exclusions

No world/campaign record, location ID, map asset key, containment, hierarchy, schema, migration,
route, city-map artwork, hosted Site, or gameplay rule changed. Prior SVG source files and page
revision 8 remain available for rollback.
