# Feature 33 Slice 2 receipt — clock-scoped rest episode

Status: **Accepted**  
Implementation: `FEATURE-33-SLICE-2-IMPLEMENTATION.md`  
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Short Rest* (PDF p. 186) and *Long Rest* (PDF p. 184).

## Delivered boundary

- Added closed creature-owned `dnd2024.rest-episode` state with canonical standard-policy identity, world, start minute, duration, status, and source reference.
- Added JavaScript `rest.begin`, which admits only a creature at 1+ HP and atomically records the active episode plus `world -> creature` membership.
- Added JavaScript clock reconciliation through the accepted scoped `game.core.world.clock.advanced` event and E8 fan-out. It changes only `active` to `ready` once the immutable policy duration has elapsed.
- Added the one scoped subscription with a fixed immutable policy role and an eight-candidate bounded selector.

## Evidence

- Focused Feature 33, clock-event, E8 routing, and subscription tests: **34 passed**.
- `roleplay validate catalog`: **420 records valid** (88 advisory warnings; no live data touched).
- Full repository suite: **807 passed, 0 failed**.
- Protocol walk: **7 passed, 0 failed**.
- `git diff --check`: passed (repository-wide line-ending notices only).

## Deliberate exclusions

This slice provides timing evidence only. It adds no interruption, resumption, rest completion benefit, Hit Dice, healing, Temporary Hit Point expiry, Exhaustion recovery, resource/slot recovery, party rest, scheduler, clock copy, or C# D&D rule logic.
