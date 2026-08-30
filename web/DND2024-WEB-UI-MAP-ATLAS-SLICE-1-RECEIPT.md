# D&D 2024 web UI map atlas Slice 1 completion receipt

Corrected 2026-08-30: this slice delivered the image route and superseded custom-element mapping,
but the active React page still referenced its old SVG registry. The active-page correction and
revision 9 publication are recorded in
`DND2024-WEB-UI-MAP-ATLAS-REACT-CORRECTION-RECEIPT.md`.

Completed: **2026-08-30**

Implementation owner: `web/DND2024-WEB-UI-MAP-ATLAS-SLICE-1-IMPLEMENTATION.md`

## Delivered boundary

- Copied the ten reviewed Thalorien atlas PNGs into
  `BrowserComponents/MapImages` without changing their bytes.
- Added the same-origin private route `GET /components/maps/{name}.png`, constrained to the
  existing lowercase/digit/hyphen asset-name grammar and returning 404 for missing or invalid
  names.
- Made the world scope display `thalos-world.png` and mapped all nine existing region IDs to their
  corresponding regional images.
- Kept Brackenford's authored settlement artwork and the prior inline continent artwork as the
  load-failure fallback.
- Converted regional marker positions into each region's independent 4:3 coordinate plane.
- Lazy-loads regional PNGs only when their scope is opened, avoiding a ten-image initial download.
- Rebuilt and restarted the current private website on its existing `localhost:6217` address.

No catalog record, SQLite state, permanent ID, containment, topology, world authority, movement
rule, or public application data contract changed.

## Verification evidence

- Source-to-web-copy SHA-256 comparison: **10/10 matched**.
- JavaScript syntax: `node --check` **passed**.
- Focused `Application_surface_is_exact_and_components_have_no_control_authority` test in Release:
  **1 passed, 0 failed**. This covers the route, exact image bytes, MIME type, missing/traversal
  behavior, scope mapping, and fallback declaration.
- Debug website build before restart: **succeeded with 0 warnings and 0 errors**.
- Live current-host checks after restart:
  - `/api/session`: **200**;
  - `/components/dnd2024-workspace.js`: **200**, with the atlas and lazy-load mapping present;
  - all ten `/components/maps/*.png` routes: **200**, `image/png`, exact source hash;
  - `/components/maps/missing-map.png`: **404**.
- `git diff --check` for the touched text files: **passed**.

The full Release test assembly was also started. It encountered unrelated existing repository
failures before the map surface, including absent files under
`catalog/applications/dnd2024/components` and an expected/actual prototype-schema count drift. The
run was stopped after those repeated prerequisite failures; the focused map/web test remained
green.

## Deliberate exclusions

- No Thalorien root or Thalos continent entity was added.
- No location-owned map metadata or database migration was introduced.
- No new city map was authored; Brackenford retains its existing close-up.
- No public hosting or external deployment was performed; the requested current private website
  was refreshed in place.
