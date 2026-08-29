# D&D 2024 web UI migration Slice 2 implementation — reviewed page publication

Status: **implemented; feature acceptance pending**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), existing authored page lifecycle / H1 publication boundary
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. This is an immutable private page-revision operation, not D&D rules work.
Outcome: publish the reviewed reference-first `dnd2024-play` source as one new active local page revision.
Exclusions: any page-content change, application/catalog activation, game-state mutation, DM/Player visibility policy, route/schema/component change, and remote public hosting.
Allowed files/areas: the existing `dnd2024-play/index.html` source, `DantesRoleplay.MCPServer/data/backups/` operational backup, and this implementation document/receipt.
Stop point: stop after a backup, pre-publication readback, one upload, revision readback, and local HTTP smoke check. Do not alter game state or activate a remote/prototype deployment.

## Confirmed decisions

- The user's request to continue authorizes the explicitly deferred local page publication.
- `dnd2024-play` is an already-confirmed private page ID; the upload creates an immutable revision instead of overwriting history.
- The established authoritative runtime page store is `DantesRoleplay.MCPServer/data/dantesroleplay.db`.

## Authoritative state and behavior

The reviewed source in `src/system/web-interface/examples/dnd2024-play/index.html` is uploaded unchanged to the existing local `PUT /api/pages/dnd2024-play` owner with the required HTML content type. The page store alone assigns the revision and updates its active pointer. The current page is read before mutation; the new active revision and served HTML are read afterward.

## Failure and rollback contract

- A failure before upload leaves the active page untouched.
- A failed upload must not partially activate a page revision; the store's existing transaction boundary owns this invariant.
- The database backup plus immutable prior revision preserves a recovery path.
- No database data other than the page-revision record and active pointer may change as part of this operation.

## Verification commands

- Inspect the active local page before publication.
- Upload the reviewed HTML exactly once to `http://localhost:6217/api/pages/dnd2024-play`.
- Read the resulting `/ui/dnd2024-play` response and confirm its active revision and required workspace markup.
- Record the backup location and exact revision in the receipt.

## Completion receipt and exit gate

Record backup, pre/post revision, upload response, and local smoke evidence in `DND2024-WEB-UI-MIGRATION-SLICE-2-RECEIPT.md`. Stop before any further feature work or remote deployment.
