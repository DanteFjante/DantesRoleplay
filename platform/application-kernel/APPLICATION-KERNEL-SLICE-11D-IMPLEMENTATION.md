# Application kernel Slice 11D implementation — lossless campaign lifecycle schema translation

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / application-owned component contracts](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible contract migration only**  
Source ID and locator: **not applicable** — this changes campaign contract encoding, not a D&D rule.  
Outcome: Translate the existing arc/chapter `if`/`then` lifecycle constraints losslessly into the
bounded profile's supported boolean composition, then prove both mapped contracts register on a
fresh disposable host.  
Exclusions: Generic profile expansion; weakened lifecycle validation; checkpoint/recap
`pattern`/`format` translation; `stats`; values/backfill; state-space migration; default-host
registration; projections/aliases/mechanics; remote MCP; vectors; and AI orchestration.  
Allowed files/areas: The arc and chapter schema sidecars, focused schema/component-administration
tests, the existing fresh-host `dnd2024` protocol proof, this document/receipt, and concise status
updates.  
Stop point: Stop after 30 mapped contracts register in disposable evidence, the arc/chapter valid
and invalid lifecycle combinations are proved, and checkpoint/recap plus `stats` remain absent.

## Confirmed decisions

- The user confirmed rewriting legacy schemas into the existing bounded profile without expanding
  it. This slice changes only encoding; it preserves the accepted campaign lifecycle meaning.
- `procedure.campaign.chapter` is authoritative: active chapters/arcs have no closing summary;
  closed chapters and resolved/abandoned arcs require a factual closing summary.
- `oneOf` branches using `properties`, `const`/`enum`, `required`, and `not` are already supported
  by the accepted profile and exactly represent that closed status partition.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Campaign lifecycle | Active has no closing summary; terminal requires one. | `procedure.campaign.chapter` and existing sidecars | Preserve all four accepted/rejected combinations. |
| D&D behavior | None. | Existing game mechanics/data | No SRD or Foundry review applies. |

## External implementation reference

No Foundry review applies because this is a repository campaign-continuity contract, not a D&D rule.

## Prerequisite evidence

- [Slice 11C receipt](receipts/APPLICATION-KERNEL-SLICE-11C-RECEIPT.md) proves 28 safe contracts
  register and isolates these two conditionals from the remaining regex/date-time constraints.

## Runtime artifacts

- Remove only the top-level `title` annotation from the two sidecars.
- Replace each `allOf` of `if`/`then` pairs with two mutually exclusive `oneOf` status branches.
- Add no ID, profile keyword, public surface, migration, or default registration.

## Authoritative state and closed input

Catalog sidecars remain authored authority. SQLite registration remains runtime authority and uses
the existing closed component-type command. Status and closing-summary presence are component
values validated against the schema; callers supply no schema version/profile/hash.

## Behavior, result, and typed effects

For each schema, the active status accepts only absence of `closingSummary`; every terminal status
accepts only a present, otherwise-valid `closingSummary`. Other fields retain their exact existing
constraints. Fresh-host evidence registers both mapped contracts at version 1, raising the safe
set from 28 to 30.

## Failure, replay, and rollback contract

Active-with-summary and terminal-without-summary values remain invalid. Unsupported checkpoint and
recap schemas create no rows. Registration retains Slice 11B dry-run/replay/rollback behavior.

## Implementation sequence

1. Add lifecycle equivalence cases for arc and chapter.
2. Rewrite the two sidecars using supported keywords only.
3. Update the fresh-host proof from 28 to 30 registrations and exact deferred absence.
4. Run focused/full/local-AI/catalog/build/model-drift/diff checks; record receipt and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Arc | Active/no summary and terminal/summary valid; inverse combinations invalid. |
| Chapter | Active/no summary and closed/summary valid; inverse combinations invalid. |
| Registration | 30 mapped version-1 contracts register through exact dry-run/commit. |
| Deferred safety | Checkpoint, recap, and `dnd2024.stats` remain absent. |
| Boundary | No generic profile, migration, values, state, mechanics, or AI change. |

## Verification commands

- Focused schema validation, component administration, catalog, and fresh-host MCP tests.
- Catalog validation; full shared/local-AI suites; warning-free solution build; migration/model
  drift coverage; `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11D-RECEIPT.md`, mark this document
accepted, update Slice 11 status, and stop before checkpoint/recap constraint redesign.
