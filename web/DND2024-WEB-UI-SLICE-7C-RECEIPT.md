# D&D 2024 web UI Slice 7C receipt — current location and scene people

Status: **accepted 2026-08-27**
Implementation: [Slice 7C](DND2024-WEB-UI-SLICE-7C-IMPLEMENTATION.md)
Parent: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), Order 7C / F1
Ruleset alignment: **dnd2024-compatible**
Model assignment: `gpt-5.6-sol`, xhigh reasoning

## Delivered boundary

- Added one private, read-only, ruleset-neutral route at
  `/api/applications/{applicationId}/state-spaces/{stateSpaceId}/entities/{entityId}/containment`.
  It validates the exact application, state space, and existing entity before returning only that
  entity's direct containment record or `null`.
- Added a fourth game-styled Scene tab while preserving Character as the default view. Scene has
  separate Current location and People here panels and uses native tab/panel and button semantics.
- Interprets current location only from the selected entity's exact direct `presence` containment
  whose parent has one valid active `dnd2024.game.core.world.location` component. It never infers a
  place from names, IDs, campaign prose, legacy dossier text, or arbitrary container scans.
- Reads at most the first 24 direct contents of that exact location, preserves server order, and
  offers tactile switch cards only for the selected/campaign actors or recurring world actors
  carrying the existing `dnd2024.game.core.world.motive` marker.
- Displays exact location name, kind, and summary. Motive content is neither fetched nor exposed as
  player knowledge. Missing containment and bounded/partial reads have explicit visible states.
- Added no rule calculation, custom-element ID, state write, effect, transaction, catalog record,
  schema, migration, page revision, or database synchronization.

## Evidence

- Browser-module syntax validation passed.
- Focused `WebInterfaceTests` passed: **89 passed, 0 failed**. The checks cover route registration
  and read-rate limiting; exact positive/null/wrong-application/missing-entity/missing-edge cases;
  no SQLite changes; the remote path boundary; Scene composition; exact `presence`/location owner
  checks; the 24-entry cutoff; missing-state copy; and non-publication of motive text.
- `dotnet build DantesRoleplay.slnx --no-restore` passed with **0 warnings and 0 errors**.
- The restarted private host served `/ui/dnd2024-play` with Character selected and a working Scene
  tab. The live selected actor Orban returned `{ "containment": null }`, and Scene rendered the
  explicit missing-location and unavailable-people messages. Returning to Character restored only
  the Character panel.
- A separate read of the same live endpoint for Elian Voss returned the exact Brackenford parent in
  slot `presence`, demonstrating the positive route without changing live campaign state.
- Focused whitespace validation passed apart from the checkout's existing line-ending notices.

## Deliberate exclusions and next gate

This slice does not repair Orban's absent current-location record. It does not publish player
knowledge, map anchors, images, recursive place trees, tactical positions, movement, NPC edits, or
encounter setup.

The user's next instruction to continue accepts this slice and confirms the desired player-knowledge
outcome. Order 7D remains blocked by its current-runtime audience and state prerequisites; its
implementation document records the exact evidence. The location/person visual-reference contract
remains independently unowned and unconfirmed.
