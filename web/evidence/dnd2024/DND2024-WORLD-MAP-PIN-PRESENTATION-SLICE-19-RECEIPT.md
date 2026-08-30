# D&D 2024 World tab Slice 19 receipt — uncropped atlas and anchored pins

Status: **accepted 2026-08-30**

Implementation owner: `web/DND2024-WORLD-MAP-PIN-PRESENTATION-SLICE-19-IMPLEMENTATION.md`

## Delivered boundary

- Removed the fixed 1672:941 map frame and `cover` crop. Every present base image now determines
  the canvas height from its own intrinsic aspect ratio and renders with `contain` behavior.
- Replaced label-width-dependent marker placement with one fixed 40×46 map-button footprint whose
  bottom center is the declared map coordinate.
- Kept the existing teardrop icon and placed its visual tip at that same bottom-center origin.
- Changed place names from always-visible rows into compact tooltips shown on pointer hover,
  keyboard focus, or selection. Existing full button accessible names remain available at rest.
- Preserved scope navigation, layer filtering, current-location, faction influence, annotations,
  audience filtering, and missing-base behavior.
- Published the bounded five-entry page bundle as active `dnd2024-play` revision **12**. Earlier
  immutable revisions remain available as the rollback boundary.

## Verification evidence

- New focused presentation tests: **3 passed, 0 failed**.
- Full D&D 2024 React website suite: **114 passed, 0 failed**.
- Server-bundle build: passed; **1,622 modules** transformed.
- Revision 12 archive: **5 entries**, **8,026,056 uncompressed bytes**, **7,666,339 compressed
  bytes**; upload returned four assets and the existing page identity.
- Live Player World map after publication:
  - Thalos source 1448×1086 rendered at 514.97×386.22, preserving a 1.33336 ratio with no crop;
  - the resting Aldros tooltip was hidden and pointer hover revealed exactly `Aldros`;
  - the fixed marker bottom center and rotated pin tip differed by about 0.22 CSS px;
  - Aldros Region preserved the same complete 1448×1086 ratio with both labels hidden at rest; and
  - Crownmere City preserved its complete 1254×1254 square base.
- Active CSS and JavaScript assets, plus the authoritative host-served Thalos PNG, returned HTTP
  200 after publication.

## Deliberate exclusions

No image association, atlas artwork, canonical coordinate, containment, hierarchy, World record,
history record, directory, profile, audience policy, schema, migration, route, or D&D mechanic
changed. City/Location geography remains limited to the canonical anchors currently present in the
World read model; this slice does not invent missing markers.
