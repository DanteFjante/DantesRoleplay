# Character creation CC2H2 receipt - automatic Initiative rest interruption

Status: **accepted**
Date: 2026-08-27
Owner: [CC2H2 implementation](../DND2024-CHARACTER-CREATION-CC2H2-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Playing the Game > Combat > The Order of Combat > Initiative*
(PDF p. 13), *Rules Glossary > Long Rest* (PDF p. 185), and *Rules Glossary > Short Rest* (PDF p. 187)

## Delivered boundary

- Extended each effect-free individual Initiative child to project only that participant's optional
  rest episode/relationships and return a closed interruption plan.
- Extended the existing encounter-order root to validate all child plans and atomically add the
  Initiative order, stop active Short Rests, and add one count/hour to active Long Rests.
- Preserved empty events/notifications/benefits, ready-rest stability, no-rest compatibility,
  deterministic ties, and exact replay.
- Orphaned/corrupt participant rest fails the entire encounter-order action before any order/rest
  effect. No production C#, schema, permanent ID, migration, or public kind changed.

## Evidence

- Focused Initiative matrix: 4 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 202 passed, 0 failed.
- Fresh disposable catalog validation: 144 valid records and 21 existing non-blocking advisories;
  no live data touched.
- Sequential full solution: 1,240 shared tests and 21 Local AI tests passed, 0 failed.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected for its
  separated actor roll, combatant update, and pre/post hook phases. No implementation was reused.

## Deliberate exclusions

Standalone Initiative preview mutation, non-Cantrip spellcasting, walking/physical exertion,
finish/recovery, Resourceful, other species grants, and atomic character creation remain later work.
