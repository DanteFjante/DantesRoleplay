# Character creation CC2H1 receipt - automatic weapon-damage rest interruption

Status: **accepted**
Date: 2026-08-27
Owner: [CC2H1 implementation](../DND2024-CHARACTER-CREATION-CC2H1-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185) and
*Rules Glossary > Short Rest* (PDF p. 187)

## Delivered boundary

- Extended the accepted weapon-damage root to project optional target rest state and relationships
  without adding any caller role or input.
- Positive resolved weapon damage now stops an active Short Rest or adds one interruption/hour to
  an active Long Rest in the same transaction as Temporary HP and HP effects.
- Damage absorbed completely by Temporary HP still interrupts. Immunity/zero damage, no episode,
  and duration-ready episodes preserve rest state.
- Corrupt or orphaned rest state fails before any damage effect. Exact replay does not repeat damage
  or interruption.
- Updated the authoritative damage/rest procedures; no C# rules branch, schema change, permanent ID,
  migration, public surface, event, notification, recovery, or benefit was added.

## Evidence

- Focused weapon-damage matrix: 10 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 200 passed, 0 failed.
- Fresh disposable catalog validation: 144 valid records and 21 existing non-blocking advisories;
  no live data touched.
- Sequential full solution: 1,238 shared tests and 21 Local AI tests passed, 0 failed.
- Existing generic transaction tests continue to prove multi-effect rollback; focused corruption
  cases prove no effects are emitted before an invalid rest scope can reach the transaction.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected for its
  separate damage-update and rest-completion phases. No source code, data, UI, assets, or runtime
  dependency was adopted.

## Deliberate exclusions

Initiative, non-Cantrip spell, walking/physical-exertion, non-weapon damage, administrative HP
writes, rest finish/recovery, Resourceful, other species grants, public endpoints, and final actor
composition remain later gated work.
