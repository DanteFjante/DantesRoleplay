# DND2024 contract and recipe cutover implementation — activate verified current contracts

Status: **active; bounded retrieval repair in progress and mechanic repair remains a prerequisite**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [contract and recipe cutover](DND2024-CONTRACT-AND-RECIPE-CUTOVER-DEPENDENCY-TREE.md), leaf 1 through leaf 3
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable; no D&D rule meaning changes**
Outcome: activate the exact reviewed current D&D catalog snapshot and prove current contract discovery.
Exclusions: no compatibility layer, no old-rule recreation, no new permanent IDs, no schema changes,
no game-specific C# logic, no state-space migration, no receipt rewrite, and no invented interaction
recipes. Allowed files/areas: this implementation document, its dependency tree and completion
receipt; read-only inspection of the archived/current databases; the existing application
preview/activation protocol; `InteractionFeatureRetrievalContracts.cs`; focused retrieval tests;
the D&D JavaScript project file and its generated intermediate artifact; and focused verification
commands.
Stop point: after exact activation read-back, discovery smoke evidence, and a completion receipt.

## Confirmed decisions

- Reuse the existing `dnd2024-core` source registration and the current 12-source profile.
- Treat current `catalog/applications/dnd2024` contracts as the only authored D&D application source.
- Preserve historical receipts exactly and migrate no learned recipes because none exist.
- Do not restore `procedure.character.playtest-bootstrap`, `mechanic.dnd2024.armor-class.write`, or
  `mechanic.dnd2024.character-level.record`; their old shapes do not match the current derived ECS model.
- Preserve the complete mechanic contract returned by discovery. Raise the generic retrieval
  document ceiling to 64,000 characters; do not truncate, special-case D&D, or exclude executable
  source from the exact catalog contract.
- Keep the Visual Studio project, but redirect its generated intermediate output outside the
  canonical catalog so the existing broad source glob never fingerprints volatile `obj` files.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Rule calculations and branching | unchanged by this slice | current catalog JavaScript mechanics | activation fingerprints existing files; no rule code is authored here |
| Contract/state shape | current ECS component and mechanic contracts | `catalog/applications/dnd2024` | old contracts are retained only where a current exact identity exists |
| Crafting recipes | authored rules/content data, not interaction authority | crafting component/archetype and content records | records are activated as content and never converted to learned interaction recipes |

## External implementation reference

No Foundry behavior review is applicable because this slice does not implement or change a D&D
mechanic. Existing rule implementations retain their own adoption evidence.

## Prerequisite evidence

- Current `dnd2024-core` registration already scans `catalog/applications/dnd2024/**/*`.
- The active revision has 2,775 `dnd2024-core` documents; the current preview has 2,971 and zero
  scan problems, proving the activation is stale rather than the source registration being absent.
- Archived and current database inventories both contain 69 procedure contracts, 30 legacy
  mechanics, zero learned interaction recipes, and seven historical resolution receipts.
- The 2026-08-30 contract-owner audit found 34 active mechanic contracts that still request one or
  more retired component IDs. Activation is paused until the repair dependency tree closes them.

## Runtime artifacts

Activation revisions 8 through 10 already exist as immutable cutover evidence and are not rewritten.
After the mechanic owner audit closes, at most one further activation and its standard receipt are
allowed for the exact post-repair preview. The sole kernel change is the tested generic retrieval
document bound. No source registration, component type, state-space record, interaction recipe, or
interaction receipt is created by this slice.

## Authoritative state and closed input

Activation input is the exact current preview fingerprint, the exact current active activation
fingerprint, application ID `dnd2024`, and the existing closed 12-source profile. The caller may not
supply document hashes, mechanic results, rule values, or replacement content.

## Behavior, result, and typed effects

The generic activation owner rescans the registered sources, proves the supplied preview remains
current, writes one immutable activation revision, advances the current pointer atomically, and
records its normal operation/activation receipt. Current source files remain authored authority.

## Failure, replay, and rollback contract

- Invalid or stale preview: no activation revision or pointer change.
- Active fingerprint mismatch: no activation revision or pointer change.
- Source problem or overlay conflict: no activation revision or pointer change.
- Duplicate request token: standard replay behavior; no duplicate activation.
- Post-activation discovery failure: preserve the immutable new revision and report the failure;
  do not rewrite receipts or directly edit SQLite.

## Implementation sequence

1. Inventory archived/current contract, recipe, and receipt rows read-only.
2. Repair and test the generic bounded retrieval ceiling.
3. Redirect and remove generated Visual Studio intermediate source input.
4. Run prototype record verification and JavaScript syntax checks.
5. Complete the current-component owner audit.
6. Preview the exact existing source profile and require a valid zero-problem result.
7. Activate through `system.application.activate` with optimistic concurrency.
8. Read back the active revision and exercise D&D contract discovery.
9. Record exact evidence and exclusions in the completion receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Positive | exact preview activates and becomes current |
| Negative | zero scan/overlay problems; no fabricated recipe rows |
| Boundary | no source registration, state-space, schema, game-specific C#, or rule-code changes; retrieval remains bounded at 64,000 characters |
| Deterministic | activated fingerprint equals the preview-derived fingerprint returned by the server |
| Replay | activation uses one request token and standard replay-safe command |
| Compatibility | old live evidence remains readable but is not application authority |
| Surface | existing three-verb query/commit protocol only; no new public kind |

## Verification commands

- `npm test` from `prototype/dnd2024`
- syntax-check every `catalog/applications/dnd2024/mechanics/**/*.js`
- `roleplay validate catalog`
- `query(kind: "system.application-preview", applicationId: "dnd2024", sourceIds: [...])`
- `commit(kind: "system.application.activate", payload: "...")`
- read back `system.applications`, activated document counts, and D&D feature discovery

## Current verification evidence

- The prototype suite passes 151 of 151 tests.
- All 69 canonical D&D mechanic bodies compile as JavaScript function bodies.
- `roleplay validate catalog` passes 144 top-level catalog records with the existing 21
  near-duplicate warnings and does not touch live data.
- The current 12-source preview is structurally valid with 3,089 winners and zero source problems.
- The bounded retrieval repair passes all 110 focused interaction tests, including exact untruncated
  return of a 49,000-character payload and fail-closed rejection above 64,000 characters.
- Eleven current-schema mechanic repair tests pass for burden/capacity, item primitives, class
  progression, species selection, Initiative, currency valuation, and weapon-damage application.
- The latest owner audit still finds 16 active mechanics requesting one or more retired component
  IDs. They remain the explicit acceptance blocker; none is hidden by a compatibility component.
- Runtime discovery remains deliberately unavailable while the activated hashes differ from the
  mechanic contract-owner repairs. Do not reactivate until the owner audit is clean.

The stale-path failures in the pre-cutover `Dnd2024AbilityCheckTests` harness are recorded as
pre-existing test migration debt; they do not authorize restoring the old directory layout.

## Completion receipt and exit gate

Write `ruleset/dnd2024/evidence/DND2024-CONTRACT-AND-RECIPE-CUTOVER-RECEIPT.md`, mark this document
accepted, and stop after the current activated fingerprint and discovery result are verified.
