# DND2024-VOCABULARY-RECORDS V1 completion receipt

Status: **complete**
Implementation document: `DND2024-VOCABULARY-RECORDS-IMPLEMENTATION.md`
Dependency tree/leaf: `DND2024-SRD-RECORD-INVENTORY-DEPENDENCY-TREE.md`, leaf 3
Ruleset alignment: **dnd2024-owned**
Source: `source.dnd2024.srd-5.2.1`

## Delivered boundary

All 266 entries in `planning/record-inventory/vocabulary.json` are now materialized as one
concrete JSON entity per file under `prototype/dnd2024/records/vocabulary/`:

- 39 vocabulary-category entities;
- 227 vocabulary-term entities;
- deterministic IDs formed as `dnd2024.` plus the inventory code;
- category references resolved to category entity IDs;
- SRD source citations and revision-1 active version metadata on every entity.

No shared rule formulas, catalog files, C# files, database state, or UI changed.

## Verification

| Command | Result |
| --- | --- |
| `node tools/convert-vocabulary-inventory.js` | validated 266 records without writing |
| `node tools/convert-vocabulary-inventory.js --write` | materialized 266 records deterministically |
| `npm test` from `prototype/dnd2024` | 19 passed, 0 failed |

The focused vocabulary test validates every record against the closed entity envelope and all
component schemas, checks one-file-per-entry cardinality, IDs, archetypes, source references,
category references, and deterministic directory placement.

## Deliberate exclusions

Vocabulary terms remain data-only. Skill-to-ability modifiers, D20 tests, movement, damage,
conditions, and other executable behavior remain catalog JavaScript owners. Existing embedded
enums/constants are represented as promoted prototype terms; they are not copied into the canonical
catalog during this slice.

## Next leaf

Shared authored rule building blocks are now the active next slice. It must add activities,
challenge-rating definitions, rest policy, effects, resources, grants, and choices needed by later
equipment, spell, character, magic-item, monster, and animal records.
