# Feature 20 Slice 5 receipt — difficult terrain and occupied spaces

Status: **Verified**
Date: 2026-08-21

## Delivered

Slice 5 extends the existing voluntary tactical movement action without creating a second movement spender or a parallel position writer.

- `dnd2024.encounter-sides` stores canonically ordered direct-roster assignments and explicit, canonical hostile side pairs. Its writer records/corrects the complete state and its relation reader returns `ally`, `enemy`, `neutral`, or `unknown` without faction or initiative inference.
- `mechanic.dnd2024.encounter-participant-movement-state.read` returns each participant's Size, position, and Feature-13-effective Incapacitated state as closed child evidence.
- `mechanic.dnd2024.tactical-move.path` derives five feet normally or ten feet when an entered footprint meets difficult terrain or a qualifying non-ally, non-Tiny creature. Difficult effects never stack.
- A creature may pass through an ally, an Incapacitated creature, a Tiny creature, or a creature at least two Size categories different. Every other overlapping intermediate footprint rejects, and every occupied final footprint rejects.

The existing derived budget-input child and single `turn-budget.spend` child still execute with the position update in one root transaction.

## Evidence

| Check | Result |
| --- | --- |
| Focused movement coverage | `CatalogFeature20TacticalMovementTests`: **3 passed, 0 failed**. Covers difficult terrain, enemy refusal, ally/Tiny/two-Size-difference/Incapacitated passage, final-footprint refusal, exact 5/10-foot costs, and rollback. |
| Test-project build | `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --no-dependencies`: **succeeded** (one pre-existing xUnit analyzer warning). |
| Catalog validation | `roleplay validate catalog`: **385 valid records** (93 mechanics, 107 procedures, 76 components, 12 event types, 5 subscriptions, 92 entities), **70 advisory warnings**, no errors, and no live data touched. |

## Boundary retained

This slice does not add special movement modes, forced movement, Dash, Disengage, reactions, opportunity attacks, pathfinding, cover, or sight. Feature 19 remains responsible for the later pre-departure reaction handoff.
