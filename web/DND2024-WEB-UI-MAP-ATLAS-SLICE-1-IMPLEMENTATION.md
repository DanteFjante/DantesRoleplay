# D&D 2024 web UI map atlas Slice 1 implementation — serve and display the reviewed Thalorien atlas

Status: **completed 2026-08-30**

Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5

Dependency boundary: local extension of the accepted canonical-host browser-component asset seam
and the existing Thalos map workspace. No new dependency tree is required because this slice adds
reviewed files to one existing presentation owner and changes no authoritative world semantics.

Ruleset alignment: **dnd2024-compatible**

Source ID and locator: **not applicable**. This slice displays reviewed world artwork and defines no
D&D rule.

Outcome: the current `/ui/dnd2024-play` map view serves the approved Thalorien atlas from the same
private web host, displays `thalos-world.png` at the top scope, and displays the matching independent
regional image when an existing regional location is opened. Existing markers, live location reads,
audience filtering, breadcrumbs, selection, and campaign-note behavior remain unchanged.

Exclusions: no catalog component or permanent ID, no SQLite or world-state write, no topology or
containment change, no continent entity, no generated imagery, no map-authority claim, no city-map
addition, no movement/travel inference, and no deployment outside the current private host.

Allowed files/areas:

- `src/system/web-interface/DantesRoleplay.Web/BrowserComponents/MapImages/*.png`;
- `src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`;
- `src/system/web-interface/DantesRoleplay.Web/Interactions/BrowserMapAssets.cs`;
- `src/system/web-interface/DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs`;
- `src/system/web-interface/DantesRoleplay.Web/DantesRoleplay.Web.csproj`;
- focused assertions in `src/system/web-interface/tests/WebInterfaceTests.cs`;
- this implementation document and one receipt.

Stop point: after the ten atlas images are copied, the exact same-origin PNG route and regional
switching are verified, focused web tests pass, and the receipt is written. Do not change canonical
world state or begin the larger location-owned map-document feature.

## Confirmed decisions

- The user explicitly approved the generated maps and requested that they be uploaded to the current
  website.
- The top-level image is the Thalos continent map. Each of the nine existing regional scopes uses
  its corresponding independent image rather than a crop of the continent.
- Brackenford keeps its existing authored settlement map because this atlas contains no replacement
  city/settlement image.
- The old inline art remains available as a rendering fallback if a reviewed PNG cannot load.

## Prerequisite evidence

- `DND2024-WEB-UI-CANONICAL-HOST-SLICE-RECEIPT.md` accepts the same-origin browser-component host.
- `DND2024-SCOPED-MAP-VIEWS-SLICE-1-RECEIPT.md` accepts per-scope map navigation and independent
  coordinate spaces as presentation behavior.
- `world/maps/generated/thalorien-atlas-v1/manifest.json` records the ten reviewed files, public
  source boundary, hashes, dimensions, provenance, and illustrative-only status.

## Runtime artifacts and behavior

- `BrowserMapAssets` reads only a closed lowercase/digit/hyphen PNG name from the copied map-image
  directory under the application output root; path traversal and unknown files return not found.
- `GET /components/maps/{name}.png` returns the exact reviewed bytes as `image/png` under the existing
  private security and read-rate boundary.
- The browser component declares one image URL per existing map scope. Switching scope selects that
  image, resets to the scope's full 4:3 plane, and renders the existing markers in that scope's
  coordinate space.
- An image load failure hides only the failed PNG and reveals the old inline art. It does not change
  scope, markers, or game state.

## Failure, replay, and rollback contract

- Invalid, traversal-shaped, missing, or non-PNG names return 404 and disclose no filesystem path.
- Repeated reads return identical bytes and perform no write.
- A missing regional image falls back to the prior inline continent crop behavior.
- Rollback removes the copied map files and route/switching patch; prior inline art is preserved.

## Acceptance matrix

| Concern | Evidence |
| --- | --- |
| Ten reviewed assets | Build output contains all ten manifest-matched PNG files. |
| Same-origin delivery | The PNG route returns 200, `image/png`, and exact source bytes. |
| Closed names | Traversal and missing names return 404. |
| World artwork | `world` selects `thalos-world.png`. |
| Regional artwork | Every existing region ID selects its matching PNG. |
| Settlement compatibility | Brackenford still selects its existing settlement art. |
| Fallback | Image error handling restores prior inline art. |
| No authority widening | No catalog, SQLite, entity, component, containment, or public API data contract changes. |

## Verification commands

- focused `WebInterfaceTests` execution;
- project build for `DantesRoleplay.Web`;
- one live read of the component script and representative PNG route.

## Completion receipt and exit gate

Write `web/DND2024-WEB-UI-MAP-ATLAS-SLICE-1-RECEIPT.md` with the delivered files, focused test/build
results, representative live-route evidence, and exclusions. Stop before location-owned map
metadata, hierarchy migration, or city-map authoring.
