# DND2024 Thalorien world expansion Slice 4 implementation — application-world authoring transaction

Status: **blocked — implementation complete; full-suite acceptance awaits unrelated worktree repair**
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`, canonical SQLite/application-kernel boundary
Dependency tree/leaf: `DND2024-THALORIEN-WORLD-EXPANSION-DEPENDENCY-TREE.md`, Leaf 2b
Ruleset alignment: **ruleset-neutral**
Source ID and locator: not applicable; this is generic application-state infrastructure
Outcome: expose one private, dry-run-first, replay-safe manifest transaction for root-scoped
application ECS entities, components, containment, and relationships.
Exclusions: D&D rules, game-specific IDs or vocabulary in C#, deletions/removals/renames, schema
registration, state-space creation/adoption/upgrades, automatic lore generation, direct SQL, live
Thalorien mutation, player-facing writes, and web authoring UI.
Allowed files/areas: application ECS effect contracts/persistence/registration/tests, generic MCP
commit dispatcher/capability/tool/protocol tests, `procedure.system.use`, this plan/tree, and receipt.
Stop point: the new commit kind is discoverable, privately authorized, dry-runs and commits the
same closed manifest through one application-ECS transaction, proves replay/rollback/scope/schema
failures, passes catalog validation/full tests/protocol walk, and writes no live Thalorien state.

## Confirmed decisions

- The user's 2026-08-30 request confirms implementation of the missing world-authoring transaction
  and the new permanent public commit kind `system.world-state.sync`.
- The surface is private-operator-only and accepts a semantic manifest, never raw application ECS
  effects, schema versions/hashes, audit identities, authorization, or derived scope evidence.
- The first slice is additive/update-only. Entity deletion, component/edge removal, and entity
  renaming remain excluded because they need a separate destructive-operation contract.
- One manifest is scoped to one existing application, state space, and existing root entity.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Game rules | none | application catalog JavaScript | no D&D branch, ID, formula, or outcome enters C# |
| State shape | exact registered component schemas | application component-type registry | caller supplies values; host resolves latest exact version/hash and schema-validates in the existing store |
| Mutation | typed application ECS effects | `IApplicationEcsEffectApplier` | the adapter derives effects and delegates one root transaction |
| World meaning | catalog procedures/components | application-authored contracts | callers cite governing procedures; generic C# enforces only structural scope and concurrency |

## External implementation reference

No Foundry dnd5e reference applies. This slice is ruleset-neutral infrastructure and does not
implement a D&D mechanic or consume Foundry behavior.

## Prerequisite evidence

- `ApplicationEcsEffectApplierTests` prove ordered multi-effect commit, exact component contracts,
  dry-run rollback, stale late-effect rollback, deterministic replay, edge atomicity, audit
  rollback, and containment snapshots.
- `procedure.system.use` already requires private administrative writes to dry-run and then commit
  an identical payload through the application ECS transaction owner.
- The accepted Thalorien Slice 3 receipt proves that canonical additions are the only remaining
  blocker; no live mutation is part of this infrastructure slice.

## Runtime artifacts

- New public commit kind: `system.world-state.sync`.
- One ruleset-neutral request/result contract and synchronizer beside the application ECS effect
  owner.
- One private MCP adapter that parses an exact closed JSON manifest and delegates authorization and
  execution.
- No database migration, component type, mechanic, application record, state-space record, or live
  world record.

## Authoritative state and closed input

Input is exactly `{requestToken, applicationId, stateSpaceId, rootEntityId, entities,
relationships}`. Each entity supplies `entityId`, `name`, `expectedRevision`, `components`, and an
optional containment. Components supply `qualifiedTypeId`, `expectedRevision`, and `value`.
Containments supply `containerEntityId`, `slot`, and `expectedRevision`. Relationships supply
`fromEntityId`, `toEntityId`, `qualifiedKind`, `expectedRevision`, and object `value`.

Ambient MCP policy supplies private-operator authorization. The host resolves the state-space
application binding, latest exact component version/hash, current entity/component/edge revisions,
root descendants, containment ancestry snapshots, typed effects, operation ID, request
fingerprint, audit evidence, and transaction. Callers cannot supply any of those derived values.

## Behavior, result, and typed effects

The adapter canonicalizes records by stable IDs. `expectedRevision: 0` means create/add/set an
absent entity/component/edge; positive values must match the exact current revision. All entity
creates run first, then complete component adds/sets, containment moves, and relationship sets.
Every new entity must terminate through declared containment at the selected existing root or an
existing descendant. Existing touched entities and endpoints must already be in that root.

The adapter snapshots every relevant existing containment ancestry, resolves exact component
references, derives at most 128 typed effects, and delegates them once to
`IApplicationEcsEffectApplier`. Dry run executes the same path and rolls back. The request token
owns separate deterministic dry-run and commit operation IDs; identical retries replay, while
conflicting reuse fails.

## Failure, replay, and rollback contract

Malformed/extra/missing fields, duplicate records, wrong application/state-space binding,
unknown/changed root, out-of-root endpoints, disconnected/cyclic new containment, unknown or
cross-application component types/relationship kinds, mismatched expected revisions, invalid JSON
or schemas, effect-limit overflow, stale ancestry, reference races, and unauthorized callers reject
without world changes. A failure after an earlier staged effect rolls back the entire batch and
records only failure evidence. Dry-run and commit payloads are byte-equivalent at the MCP surface.

## Implementation sequence

1. Add domain manifest/result contracts and the generic synchronizer.
2. Register the synchronizer with the existing ECS-effects component.
3. Add the private MCP parser/dispatcher/capability entry and revise `procedure.system.use`.
4. Add focused service and protocol tests for positive, malformed, authorization, scope, stale,
   schema, replay, dry-run, and late rollback cases.
5. Run catalog validation, focused/full tests, and the MCP protocol walk; write the receipt.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| new root children | entities, components, containment, and relationships commit atomically |
| existing component | exact positive revision produces one complete `component.set` |
| dry run | exact receipts return; all state is rolled back |
| replay | identical token+mode+manifest replays without a second write |
| conflicting token | same token/mode with different manifest is rejected |
| wrong scope/binding | no effects are delegated |
| stale component/edge/ancestry | full manifest rolls back with typed problem |
| invalid schema or late failure | earlier creates/adds do not survive |
| unauthorized MCP caller | rejected before payload parsing |
| destructive intent | no delete/remove/rename shape exists |

## Verification commands

- focused `ApplicationWorldAuthoringSynchronizerTests`
- focused generic MCP capability/dispatch/protocol tests
- `roleplay validate catalog`
- full `dotnet test`
- MCP protocol walk because the public commit catalog and dependency registration change

## Completion receipt and exit gate

Write `DND2024-THALORIEN-WORLD-EXPANSION-SLICE-4-RECEIPT.md` and stop before using the transaction
to add or change any live Thalorien record. Mark Leaf 2b accepted only after the complete suite is
green in the synchronized worktree.
