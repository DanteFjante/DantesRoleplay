# D&D 2024 web UI Slice 7C implementation — current location and scene people

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 7C / F1
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. This slice reads generic containment and existing world
presentation state without implementing or changing a D&D rule.
Outcome: add a Scene view that shows the selected character's exact recorded current location and
a bounded switcher for campaign actors and recurring world actors directly present there.
Exclusions: player-knowledge publication, images, maps, recursive place trees, encounter/tactical
position, movement, NPC editing, inferred scene membership, and live-state repair.
Allowed files/areas: this document, the D&D web UI plan/roadmap/status, the generic web read
explorer/endpoint/remote allowlist, the existing D&D browser component, focused web tests, and the
Slice 7C receipt.
Stop point: stop after the exact direct-parent read and Scene presentation pass focused tests and
live browser smoke; do not add knowledge, maps, imagery, or repair absent character containment.

## Confirmed decisions

- The user's instruction to continue accepts Slice 7B and confirms the next slice's private,
  ruleset-neutral read-only parent-context surface.
- The public route identity is
  `/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/containment`.
  It returns `{ "containment": <existing record or null> }`; it is not a scene view model.
- Scene is a fourth presentation tab. Character remains the default accepted view.
- `presence` plus a valid active `dnd2024.game.core.world.location` parent is the closed current-
  location interpretation. The browser never scans arbitrary containers or infers from names,
  paths, summaries, IDs, campaign prose, or legacy dossier text.
- A scene-person candidate must be the selected/campaign actor or carry the existing
  `dnd2024.game.core.world.motive` recurring-world-actor marker. Motive text is not displayed as
  player knowledge. There is no new person/NPC identity or schema.
- World visibility remains descriptive under the existing world contract. This slice serves the
  already private local player/GM table and does not claim a general player-safe publication API.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Character location | Not a D&D rule in this slice. | Generic state-space containment plus `game.core.world.location`. | Read exact current state; do not calculate travel, range, movement, or tactical position. |
| Scene people | Not a D&D rule in this slice. | Campaign participation and `game.core.world.motive` recurring actor marker. | Present names from bounded direct `presence`; do not invent NPC stats, knowledge, disposition, or visibility. |
| Character/encounter controls | Preserve accepted behavior. | Slices 5–7B. | Adding Scene cannot alter selection, roles, inputs, effects, receipts, or transactions. |

## External implementation reference

No Foundry dnd5e rule/data-flow implementation is relevant. This slice uses the repository's
generic one-parent containment invariant and browser-native tab/button semantics. No external code,
template, data model, scene abstraction, token system, or styling is copied.

## Prerequisite evidence

- [Slice 7B receipt](DND2024-WEB-UI-SLICE-7B-RECEIPT.md) accepts the existing viewport navigation
  and absence of unsupported Scene/Map/Knowledge placeholders.
- `IStateSpaceEdgeStore.GetContainmentAsync` already owns exact O(1)-shape direct-parent reads by
  `(stateSpaceId, containedEntityId)`; SQLite keys containment by those fields.
- `ControlStructureExplorer` already validates exact application/state/entity scope for application
  ECS reads and owns generic containment summaries.
- `procedure.game.core.world.location` defines containment as the only hierarchy and actor presence
  as direct containment in slot `presence`; the location component owns kind/status/summary.
- `game.core.world.motive` is the existing durable marker for a recurring world actor. Its summary
  and descriptive visibility are deliberately not player-knowledge authority.

## Runtime artifacts

- Add the confirmed private GET route and remote-path allowlist entry for one direct containment.
- Add `ControlStructureExplorer.GetApplicationContainmentAsync`, returning one generic wrapper
  around the existing `ContainmentSummary?`; create no new persistence query or projection state.
- Add focused positive/null/wrong-app/missing-entity/read-only/route/remote-boundary tests.
- Add a fourth game-styled Scene tab inside `<dnd2024-workspace>` with Current location and People
  here panels.
- Add bounded current-context loading: one direct-parent read, exact parent entity/components,
  location detail, and at most 24 direct contents. Preserve server order and expose a visible
  boundary note when more contents exist.
- Add no custom-element ID, catalog record, component/schema, mechanic, procedure, migration,
  database record, page revision, write endpoint, effect, or transaction.

## Authoritative state and closed input

The route takes only path-bound application, state-space, and contained entity IDs, each under the
existing bounded-ID and exact application-state validation. The store resolves the parent,
container, slot, revision, and timestamps. The caller cannot supply or override parent identity,
slot, revision, component state, location meaning, campaign scope, or visibility.

The browser takes no free-form scene input. Scene-person selection is one in-memory ID from the
closed loaded candidate set and changes presentation only.

## Behavior, result, and typed effects

The generic route returns HTTP 200 with `{ "containment": <ContainmentSummary or null> }`. Unknown
application/state/entity failures use existing typed structure errors. It never lists children or
walks ancestors.

The browser treats `null`, a non-`presence` parent, or a missing/invalid/inactive location component
as an explicit missing or unavailable current-place state. For a valid current location it renders
the exact location name, kind, and summary. It reads the first 24 direct contents, validates every
returned containment against the exact location, keeps only `presence`, and offers tactile person
cards for the selected/campaign actors and recurring world actors. Selecting a card changes only
the visible name/role detail. Motive summaries and GM visibility are not presented as player facts.

This slice emits no typed effects and owns no transaction. All existing action owners are unchanged.

## Failure, replay, and rollback contract

- Unknown/wrong application or state, missing entity, malformed ID, and missing edge service fail
  through existing typed errors without data change.
- No containment is normal and renders “Current location is not recorded.”
- Malformed/mismatched containment, invalid location state, failed parent/component/content read,
  or invalid page shape renders current context unavailable without changing other panels.
- More than 24 direct contents are not followed; a boundary note is visible.
- View/person switching performs no request, persistence, replay, optimistic update, or rollback.
- Existing action stale/replay/rollback behavior is unaffected.

## Implementation sequence

1. Add and test the generic exact direct-containment explorer method, GET route, and remote allowlist.
2. Add bounded browser hydration and the Scene tab/panels using existing owner IDs.
3. Add focused source-contract, missing, boundary, isolation, and compatibility assertions.
4. Run syntax, focused web tests, build, browser smoke, and whitespace checks; record the receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive route | Exact scoped entity with a parent returns that one current containment unchanged. |
| Null route | Existing uncontained entity returns JSON `null`. |
| Negative route | Wrong application/state, missing entity, invalid identifier/path, and missing edge owner fail closed. |
| No-change | Direct-parent reads leave SQLite total changes unchanged. |
| Positive UI | Valid active `presence` location renders exact name/kind/summary and bounded actor cards. |
| Missing UI | Uncontained actor explicitly says current location is not recorded. |
| Boundary UI | Non-presence, invalid/inactive location, malformed rows, unavailable reads, and pagination cutoff are visible and never inferred. |
| Compatibility | Character remains default; Campaign/Combat, inventory, actions, receipts, and refresh remain green. |
| Surface | One GET route only; no custom element, write, schema, migration, database state, map, knowledge, or image surface. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~WebInterfaceTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- focused browser smoke at `/ui/dnd2024-play`, including Scene and Character return
- focused `git diff --check` over Slice 7C files

Catalog validation, D&D rule tests, protocol walk, and database synchronization are not triggered
because this slice changes no catalog/rule/protocol dependency or live state.

## Completion receipt and exit gate

Record evidence and deliberate exclusions in `DND2024-WEB-UI-SLICE-7C-RECEIPT.md`. Mark the slice
implemented with acceptance pending after all stated evidence passes. Stop before player-knowledge
query exposure, map anchors/rendering, visual attachments, new actor identity, location mutation,
travel, combat placement, or tactical movement.
