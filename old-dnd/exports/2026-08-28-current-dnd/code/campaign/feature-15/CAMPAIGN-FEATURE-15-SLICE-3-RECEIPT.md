# Campaign Feature 15 — Slice 3 receipt

Status: **accepted**
Date: 2026-08-21

## Delivered boundary

C15 now provides `ICampaignCharacterParticipationWithdrawalPlanner` for a lifecycle root. Its
closed request contains only an actor id; it reuses the canonical active-scope verifier and
returns exactly one `component.set` effect replacing the participation state with
`{"status":"withdrawn"}`.

The planner is internal composition only. It does not expose a campaign operation, accept a
campaign assertion, create a transaction, apply effects, emit an event, or record an audit entry.
CH13 remains the owner of retirement and of the root transaction that combines its lifecycle
effect with this fragment.

## Evidence

- `CampaignFeature15Slice3Tests.Returns_one_withdrawal_fragment_without_writing_and_the_containing_root_can_roll_it_back`
  proves the exact fragment, zero-write planning, and transaction rollback restoration.
- `CampaignFeature15Slice3Tests.Rejects_absent_or_non_active_scope_without_returning_effects`
  proves absent and already-withdrawn scopes return no fragment.
- Focused command: `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~CampaignFeature15`
  passed **10/10** tests.
- `roleplay validate catalog` passed **387 records** with **71 advisory warnings** and no errors;
  no live data was touched.

## Consumer handoff

Character CH13 can inject the withdrawal planner into its lifecycle root, append the returned
effect to its staged bundle, dry-run and apply the whole bundle once, then commit/record its one
root operation. If any lifecycle, guard, event, or audit stage fails, that root must roll back
both its lifecycle change and this participation-state replacement.
