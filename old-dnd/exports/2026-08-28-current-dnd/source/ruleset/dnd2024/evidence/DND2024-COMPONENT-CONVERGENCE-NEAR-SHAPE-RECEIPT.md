# D&D 2024 near-shape component-convergence receipt

Status: **accepted**
Completed: 2026-08-28
Implementation: [NS1 near-shape cohort](../DND2024-COMPONENT-CONVERGENCE-NEAR-SHAPE-IMPLEMENTATION.md)
Dependency tree: [component convergence](../../../prototype/dnd2024/planning/DND2024-COMPONENT-CONVERGENCE-DEPENDENCY-TREE.md)
Ruleset alignment: `dnd2024-compatible`

## Delivered boundary

- Migrated four canonical state owners:
  - `dnd2024.character.profile` to `dnd2024.character.identity`;
  - `dnd2024.character-experience` to `dnd2024.character.experience`;
  - `dnd2024.hit-points` to `dnd2024.creature.hit-points`; and
  - `dnd2024.temporary-hit-points` to `dnd2024.creature.temporary-hit-points`.
- Identity now matches the prototype field set and limits, including `playerNotes`, while retaining
  trimmed nonblank record/correct behavior.
- Experience stores only authoritative `{total}`. Hit Points store current, maximum, and optional
  `maximumReduction`; mutable state no longer duplicates fixed rule citations.
- Temporary Hit Points retain the positive-buffer/absence invariant and now store their source as
  the prototype ECS entity-reference shape.
- Adapted all ten existing JavaScript consumers: character profile record, Heroic Inspiration
  grant, Experience read/write, HP record, healing, Temporary HP transition, weapon damage, rest
  begin, and basic character creation.
- Rebound the companion web interface to the four canonical keys and added explicit regression
  assertions so its character dossier and combat controls cannot drift back to retired state owners.
- Healing and weapon damage preserve an existing maximum reduction without claiming authority to
  calculate or change it. Existing action/procedure IDs, generic effects, transactions, replay,
  and rollback owners remain stable.
- Historical Slice-8 evidence remains immutable; its closure test explicitly recognizes the three
  renamed recovered components as current migrations.
- A read-only live-database audit found zero old/target definitions and zero instances. No live
  state, database schema, alias, or compatibility write path was touched.

## Acceptance evidence

| Check | Result |
| --- | --- |
| JavaScript parsing | all 10 revised mechanics accepted by the JavaScript parser |
| Focused convergence cases | 31 passed across identity, Inspiration eligibility, XP, HP, healing, Temporary HP, weapon damage, rest start, creation, and adoption closure |
| Fresh catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full D&D regression class | 346 passed |
| Full solution | 1,404 shared tests and 21 Local AI tests passed |
| Companion-interface regression | 89 passed after rebinding the four component keys |
| Operational old-key scan | no old state key in active catalog; current tests retain only three explicit historical-to-current closure mappings |
| Public/protocol surface | unchanged; no protocol walk required |

## Deliberate exclusions

This receipt does not claim class-membership/total-level convergence, maximum-HP reduction rules,
death saves, rest completion, new damage or healing sources, normalized abilities/proficiencies,
encounter lifecycle, items, character-content decomposition, conditions/effects, or the final rest
owner. Those remain later dependency cohorts.
