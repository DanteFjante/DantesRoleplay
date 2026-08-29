# Status remediation Slice S4 prerequisite receipt — CH13 lifecycle

Status: **Not started; blocked on the required Campaign owner transition**  
Date: 2026-08-21

## Evidence

Campaign Feature 15 currently supplies only:

- `game.core.campaign.character-participation` with `active`/`withdrawn` state;
- read-only active-scope verification; and
- the public `attach-character-participation` operation.

Its governing contract, `procedure.campaign.character-participation`, states that withdrawal is a
later CH13 composition seam. No Campaign-owned root-composable withdrawal/retirement transition,
dry-run result, or atomic child contract exists for CH13 to call.

## Why implementation stops here

CH13 must atomically update its new character lifecycle state and the Campaign-owned participation
state. Adding a withdrawal operation would create a permanent Campaign public/state contract and
choose its transition, rollback, readback, and action surface semantics. Those decisions belong to
the Campaign owner and require its confirmation before CH13 can implement its lifecycle component.

## Required re-entry condition

Confirm and implement one Campaign-owned, root-composable participation transition that:

1. accepts only one valid active campaign/participation/actor graph;
2. changes only the participation state from `active` to `withdrawn`;
3. exposes a typed no-write validation result CH13 can consume;
4. composes with one caller-owned lifecycle component set in the existing root transaction; and
5. proves no partial state under component/event/audit/guard/reaction/cancellation failure.

After that owner contract is accepted, resume CH13 Slice 1 (lifecycle state and consumer
preconditions), then its atomic retire/archive slice. No CH13 runtime artifact was added here.
