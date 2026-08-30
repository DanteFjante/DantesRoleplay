# D&D 2024 world/campaign owner convergence — runtime identity decision

Status: **accepted; application-local ID allowance superseded by G7N namespace containment**
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `DND2024-COMPLETE-CAMPAIGN-DEPENDENCY-GRAPH.md`, G7
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: not applicable; this slice resolves application identity and catalog/runtime placement only
Outcome: record and enforce the one canonical runtime owner for D&D world and campaign state.
Exclusions: live-record rewrites, database migration, backup/export, world or campaign creation, deleting legacy catalog records, schema changes, D&D rules, and UI changes.
Allowed files/areas: the complete-campaign graph and roadmap, one convergence evidence record, and one focused regression test.
Stop point: every owner layer has one explicit disposition and tests prove that D&D runtime bindings use only the application-qualified identities.

## Confirmed decisions

- The user confirmed on 2026-08-30 that the canonical live owner is the installed D&D application:
  `dnd2024.game.core.*`.
- `game.core.*` remains the reusable catalog-local contract namespace. Application mechanic mappings
  resolve those local keys to the registered, application-qualified live component type.
- `dnd2024.world.*` and `dnd2024.campaign.*` are legacy prototype component definitions. They are
  not aliases and must not be newly registered or written to a D&D state space. Their live rewrite
  remains blocked on the separate immutable-backup and migration leaves.

## Prerequisite evidence

- `complete-campaign-owner-ledger.json` preserves the observed pre-decision conflict.
- `authorized-knowledge.json` and the D&D game server context already bind live reads to
  `dnd2024.game.core.*`.
- `ApplicationMechanicProjectionResolver` proves that catalog-local component keys are resolved
  through exact application component mappings rather than treated as live qualified IDs.

## Runtime identity contract

| Layer | World root | Campaign root | Disposition |
| --- | --- | --- | --- |
| Reusable catalog key | `game.core.world.root` | `game.core.campaign.root` | Retained as source/mechanic mapping key. |
| D&D live component type | `dnd2024.game.core.world.root` | `dnd2024.game.core.campaign.root` | Sole canonical runtime owner. |
| Prototype component shape | `dnd2024.world.root` | `dnd2024.campaign.root` | Historical migration input; no new live registration/write. |

The same application qualification applies to location components and world/campaign relationship
kinds. The D&D web binding and all future world authoring must use the second row; no caller may
substitute a catalog-local key or a prototype key for a registered runtime type.

## Failure and compatibility contract

This slice changes no database state. Existing records are neither interpreted as migrated nor
rewritten. A future migration must first create and verify a restorable full-state backup, then
map each legacy prototype record once to the canonical application-qualified owner with explicit
schema/version handling and rollback evidence.

## Implementation sequence

1. Add a durable owner/disposition matrix tied to the earlier observed-conflict ledger.
2. Update the complete-campaign graph and roadmap to distinguish catalog keys from runtime IDs.
3. Add a focused regression test for the binding, resolver, and legacy-shape dispositions.
4. Run the focused test and catalog validation; record the result and stop before migration or
   live world authoring.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| D&D knowledge/web binding | References `dnd2024.game.core.*` runtime types. |
| Catalog mechanics | May retain `game.core.*` as mapping keys, never as claimed runtime types. |
| Legacy prototype roots | Have a migration-only disposition, not an alias or live-write owner. |
| New world work | Is blocked from legacy writes and from migration before backup evidence. |

## Verification commands

- focused `Dnd2024WorldCampaignOwnerConvergenceTests`
- `roleplay validate catalog`
- full `dotnet test` at the next cross-worktree acceptance boundary; existing unrelated failures are
  not repaired by this ownership-only slice.

## Completion receipt and exit gate

The receipt is recorded at
`ruleset/dnd2024/adoption/evidence/DND2024-COMPLETE-CAMPAIGN-G7-OWNER-CONVERGENCE-RECEIPT.md`.
Stop before G9/G8 acceptance or any live world/campaign mutation.
