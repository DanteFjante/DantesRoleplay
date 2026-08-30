# DND2024 adaptive Current View — Slice 5 implementation

Status: **source implementation complete; feature acceptance pending**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, F5 authored scene affordances
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: not applicable; this is authored campaign presentation, not a D&D rule
Outcome: show audience-safe actions explicitly declared for the exact current scene
Exclusions: mechanic discovery/eligibility, execution, travel, dialogue generation, inferred actions, and writes
Allowed files/areas: generic campaign catalog component/procedure/manifest; DND2024 Current View read adapter,
types, validation, presentation, focused tests; this plan, receipt, roadmap, and dependency tree
Stop point: read-only affordance cards render and fail closed when absent, malformed, stale, or unauthorized

## Confirmed decisions

The user's 2026-08-30 instruction to continue after the explicit permanent-contract gate confirms:

- authored component ID `game.core.campaign.scene-affordances`, runtime-qualified for this application
  as `dnd2024.game.core.campaign.scene-affordances`;
- authored procedure ID `procedure.campaign.scene-affordances`;
- a closed current-scene selector copy used only for stale-state comparison;
- bounded authored items containing local key, label, summary, and `party`/`gm` visibility; and
- read-only website projection with no mechanic or write semantics.

## D&D 5e 2024 alignment

This slice declares narrative campaign opportunities, not D&D actions, action-economy eligibility,
targets, checks, DCs, outcomes, or costs. Existing DND2024 mechanics remain the only rules owners.
C# remains unchanged. No Foundry dnd5e review applies because no D&D rule is implemented.

## Prerequisite evidence

- `game.core.campaign.current-scene` already selects the exact current location and optional
  conversation/encounter.
- The connected adapter already validates that selector and resolves the audience perspective.
- Current View Slices 2–4 compose Exploration, Conversation, Combat, routes, and safe place context.
- The generic application action control requires a preselected mechanic and is not action discovery.

## Runtime artifacts

### Component

`game.core.campaign.scene-affordances` is attached only to an active campaign root. Its closed value:

- `scene`: the exact current-scene selector shape (`location`, optional `conversation`, optional
  `encounter` entity references);
- `items`: zero to 24 unique local-key records;
- each item: `key`, `label`, `summary`, and `visibility` (`party` or `gm`).

The component stores no mechanic/procedure ID, input, roles, target, cost, D&D action type,
eligibility, result, ordering inference, revision copy, or generated text.

### Procedure

`procedure.campaign.scene-affordances` governs reviewed add/replace/remove effects. A writer reads the
current-scene record, copies its exact references, validates unique bounded items, and replaces the
component atomically. Removing/changing the current scene requires replacing or removing affordances
in the same reviewed boundary.

## Authoritative state and closed input

The campaign component is authority. The web adapter independently validates both selectors against
the audience-authorized current location and requires exact location/conversation/encounter equality.
Player projection includes only `party`; DM projection includes `party` and `gm`. The browser never
supplies, expands, reorders, or persists items.

## Behavior and failure contract

- Valid matching records preserve authored item order after rejecting duplicate keys.
- Missing component: render the friendly empty state.
- Valid component with no visible items: render the same empty state.
- Malformed/open/oversized/duplicate-key record: omit the entire projection.
- Stale scene selector, unknown location, or mismatched optional reference: omit the entire projection.
- Perspective changes re-read and re-filter at the server adapter; no hidden item reaches Player bytes.
- Reads are deterministic and side-effect free. There is no replay/rollback behavior because there is
  no website write or transaction.

## Implementation sequence

1. Add component schema/definition and procedure; register exact manifest hashes.
2. Validate the catalog in a fresh disposable database.
3. Add the DND2024-qualified read and exact stale/audience filtering.
4. Add optional closed-envelope typing/validation and read-only Current View cards.
5. Run focused tests, full web suite, production build, and focused catalog tests.
6. Record the completion receipt and collapse the dependency status.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Matching party item | Player and DM receive exact label/summary. |
| Matching GM item | DM receives it; Player serialization omits it completely. |
| Both optional scene refs match | Combat priority remains unchanged; affordances remain bound to the full selector. |
| Missing/empty | Friendly empty presentation; current scene remains usable. |
| Stale/malformed/duplicate/oversized | No affordance item is emitted. |
| Generic action control/mechanics | Not queried, selected, or executed. |
| Retry | Byte-equivalent read and no state change. |

## Verification commands

- `.\roleplay.cmd validate catalog`
- focused Node tests for affordance validation/projection/presentation
- `npm test` in `src/system/web-interface/dnd2024`
- `npm run build:server` in `src/system/web-interface/dnd2024`
- focused catalog manifest/coverage tests if catalog validation identifies an affected assertion

## Completion receipt and exit gate

Completed in `web/DND2024-ADAPTIVE-CURRENT-VIEW-SLICE-5-RECEIPT.md`. Stop before any action button,
mechanic descriptor, prepare/execute request, travel write, live database mutation, activation,
deployment, or final feature acceptance.
