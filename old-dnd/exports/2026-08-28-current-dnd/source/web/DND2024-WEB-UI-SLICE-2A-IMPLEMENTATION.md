# D&D 2024 web UI Slice 2A implementation — character dossier and encounter detail

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Order 2 partial delivery
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**; this slice presents accepted component state and does not
implement or change a D&D rule.
Outcome: deepen the existing `<dnd2024-workspace>` with a game-styled character dossier and selected
encounter Initiative/turn detail using the already accepted application-state read seam.
Exclusions: containment/inventory traversal, public catalog routes, content browsing, action
descriptors, writes, plus/minus controls, dice/check execution, rule calculations, live activation,
new custom-element IDs, catalog changes, and application-page association persistence.
Allowed files/areas: the existing D&D browser asset and authored page under
`src/system/web-interface/**`, focused web tests, this document/receipt, the D&D web dependency plan,
and the web roadmap.
Stop point: current profile/size/experience and selected encounter Initiative/turn state render as
cards with explicit missing/corrupt states. No browser control mutates or prepares game state.

## Confirmed decisions

The user's 2026-08-27 instruction to continue authorizes the next preplanned read-only Order 2 work.
This slice introduces no new permanent route, page, module, or custom-element ID. It reuses the
confirmed `dnd2024-play`, `/components/dnd2024-workspace.js`, and `<dnd2024-workspace>` identities.

Inventory is deliberately excluded from 2A. Its item-instance component is not custody authority;
containment is. Static item facts belong to the explicitly published application catalog. Neither
owner may be reconstructed from names or guessed browser relationships.

## D&D 5e 2024 alignment

| Concern | Accepted owner | Slice consequence |
| --- | --- | --- |
| Optional profile text | `dnd2024.character.profile` | Display present pronouns, appearance, and biography verbatim as bounded text; absence is “not recorded.” |
| Creature Size | `dnd2024.creature-size` | Display the stored canonical Size label only. |
| Experience | `dnd2024.character-experience` | Display the stored total only; do not calculate thresholds, level eligibility, or progress percentages. |
| Initiative order | `dnd2024.encounter-initiative-order` | Preserve stored order and final Initiative counts; do not sort, reroll, or resolve ties. |
| Encounter lifecycle | `dnd2024.encounter-turn-state` | Display stored status, round, and turn index. Visually mark the matching stored order position without persisting a duplicate active participant. |
| Participant identity | current entity summaries from the exact state space | Resolve a display name when already present; retain the exact participant ID when unavailable. |

## External implementation reference

No Foundry dnd5e review is required because this slice introduces no D&D behavior, calculation,
eligibility rule, state transition, or external UI dependency. No external code or asset is adopted.

## Prerequisite evidence

- [Slice 1 receipt](DND2024-WEB-UI-SLICE-1-RECEIPT.md) accepts exact scoped state reads, the bounded
  browser asset host, and the game-styled workspace.
- Current accepted component schemas close the profile, Size, experience, Initiative order, and
  encounter turn-state shapes.
- Existing browser hydration already loads bounded component summaries/details for the selected
  entity and a bounded entity-name roster for its exact state space.

## Runtime artifacts

Revise only the reviewed `dnd2024-workspace.js` browser asset and its focused assertions. Add no
route, endpoint, C# ruleset literal, server DTO, database row, migration, catalog record, MCP kind,
page revision, or live activation.

## Authoritative state and closed input

The element continues to accept only `application-id`, optional `state-space-id`, and optional
`entity-id`. It requests the five Slice 1 read routes and reads the five additional accepted
component IDs from the selected entity. It accepts no profile JSON, order, counts, turn index,
level/experience threshold, active-participant ID, derived value, or authorization through HTML.

## Behavior, result, and typed effects

- The character dossier displays optional profile text, Size, exact experience total, level, and
  entity revision in compact game cards.
- Missing optional profile state is distinct from invalid/unavailable JSON. Individual absent
  profile fields remain “Not recorded” rather than becoming empty invented prose.
- The encounter tracker preserves stored participant order. It displays entity names when already
  available, exact IDs otherwise, and final Initiative counts unchanged.
- Stored `turnIndex` may visually mark only the corresponding order position while status is active.
  Missing turn state means “not started”; ended state means “ended”; out-of-range/corrupt shapes are
  unavailable.
- Entity selection and Refresh remain the only controls. No typed effect or transaction exists.

## Failure, replay, and rollback contract

Malformed/corrupt component values fail within their dossier or encounter panel without replacing
other valid panels. Unknown participant IDs remain visible as exact IDs. Missing components remain
distinct from corrupt components. Existing wrong-application, unknown, denied, stale, pagination,
and request-abort behavior is unchanged. Repeated reads are side-effect free. Replay and rollback
are not applicable because no write or transaction exists.

## Implementation sequence

1. Extend the existing browser component vocabulary and selected-entity hydration only.
2. Add dossier cards and ordered encounter cards with responsive/accessible states.
3. Extend focused asset/page tests, run JavaScript syntax, focused/full suites, and diff checks.
4. Write the Slice 2A receipt, mark partial Order 2 progress, and stop.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Character detail | Valid profile, Size, experience, level, and revision render exactly. |
| Optional state | Missing profile and missing individual prose fields read “Not recorded.” |
| Encounter detail | Stored order/counts, status, round, and index render without sorting or recalculation. |
| Identity fallback | Known participant names render; unknown participants retain exact IDs. |
| Corrupt state | Invalid order/index/profile/value JSON is isolated and visibly unavailable. |
| No authority drift | No action route, write verb, formula, local storage, or caller-supplied state appears. |
| Game presentation | Dossier and encounter state use cards, badges, and a turn marker rather than generic forms/tables. |
| Compatibility | Slice 1 viewport and existing web/application surfaces remain green. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- Focused `WebInterfaceTests` for the asset vocabulary, presentation controls, failure labels, and
  no-write boundary.
- `dotnet build DantesRoleplay.slnx --no-restore`
- Full core and local-AI test suites.
- `git diff --check` plus trailing-whitespace checks for new/untracked slice files.

Catalog validation and the MCP protocol walk are not required because this slice changes no catalog
record, dependency registration, or protocol operation.

## Completion receipt and exit gate

[The Slice 2A receipt](DND2024-WEB-UI-SLICE-2A-RECEIPT.md) records passing evidence and partial
Order 2 acceptance. The slice stops before containment/catalog reads, inventory rendering, any new
custom-element/route ID, action preparation, plus/minus mutation, dice/check execution, or live
activation.
