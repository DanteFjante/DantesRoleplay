# D&D 2024 complete-campaign G4 operation identity receipt

Status: **accepted**
Date: 2026-08-30

## Delivered boundary

The already-authorized root action operation id now reaches catalog JavaScript as frozen host
context. Application execution derives a distinct deterministic id for each child invocation from
the root id, immediate parent id, host ordinal, exact qualified mechanic id, and exact content
fingerprint. Parent mechanics receive the same child identity in their frozen child result.

Caller input remains separate and cannot replace or mutate `ctx.execution`. Read-only evaluation
without execution authority receives `null`. Child mechanics remain data-only; the typed effect
batch and commit/replay boundary continue to use the root `ApplicationEcsExecutionIdentity`.

No public request field, campaign rule, catalog id, migration, or live data changed.

## Evidence

- [Implementation contract](../../DND2024-G4-MECHANIC-OPERATION-IDENTITY-IMPLEMENTATION.md)
  records the exact identity shape, canonical derivation, failure behavior, and exclusions.
- `ApplicationMechanicExecutionTests.Host_derives_and_projects_immutable_child_operation_identity`
  proves the exact derivation, root/parent correlation, parent visibility, frozen context, and input
  separation.
- `ApplicationMechanicExecutionTests.Exact_application_action_applies_to_bound_state_once_and_replays_by_identity`
  proves the root context equals the typed-effect replay identity and retains once-only behavior.
- `SandboxTests.Host_execution_identity_is_separate_from_input_and_deeply_frozen` and
  `SandboxTests.Read_only_projection_exposes_no_execution_identity` prove the JavaScript boundary.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c CodexG4 --no-restore --filter
  "FullyQualifiedName~ApplicationMechanicExecutionTests|FullyQualifiedName~SandboxTests"` passed
  33 of 33 tests.
- The same isolated build with application execution, sandbox, interaction coordinator, and
  interaction orchestration acceptance filters passed 37 of 37 tests.

## Deliberate exclusions and next gate

G4 does not assign application-owned entity ids, create a child transaction boundary, or implement
event/reaction timing. G6 remains the next independent foundation gate for authoritative campaign
time; G5 may now consume this accepted identity seam later.
