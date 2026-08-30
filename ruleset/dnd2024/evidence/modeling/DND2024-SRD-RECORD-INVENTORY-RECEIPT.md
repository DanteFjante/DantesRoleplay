# DND2024-SRD-RECORD-INVENTORY S1 completion receipt

Status: **accepted by automated evidence; synchronization decision remains open**
Implementation document: `DND2024-SRD-RECORD-INVENTORY-IMPLEMENTATION.md`
Dependency leaf: `DND2024-SRD-RECORD-INVENTORY-DEPENDENCY-TREE.md`, leaf 1
Ruleset alignment: **dnd2024-owned**
Source: `source.dnd2024.srd-5.2.1`
Source SHA-256: `8974902D109D6E63672D7C490BDE9CCF052410503D9CFA768237154FBC5E3D87`

## Delivered boundary

The official 364-page SRD source was inventoried into nine dependency-ordered, planning-only JSON
families. Each primary named definition and reusable named subdefinition in scope has a unique
planning code, source locator, prospective prototype owner, and current implementation state.

This slice created no concrete prototype records, permanent IDs, mechanics, schemas, catalog
records, imports, migrations, database writes, or UI changes.

## Counts

| Order | Family | Entries | Catalog record | Embedded only | Missing | Review notes |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | Vocabulary | 266 | 0 | 102 | 164 | 8 |
| 2 | Shared rules | 50 | 1 | 0 | 49 | 6 |
| 3 | Equipment | 292 | 33 | 47 | 212 | 5 |
| 4 | Spells | 355 | 0 | 0 | 355 | 1 |
| 5 | Character options | 446 | 64 | 58 | 324 | 3 |
| 6 | Gameplay toolbox | 46 | 0 | 0 | 46 | 9 |
| 7 | Magic items | 456 | 0 | 0 | 456 | 7 |
| 8 | Monsters | 265 | 0 | 0 | 265 | 9 |
| 9 | Animals | 95 | 0 | 0 | 95 | 6 |
| **Total** |  | **2,271** | **98** | **207** | **1,966** | **54** |

The 98 logical entries classified as `catalog-record` reference 102 unique existing catalog IDs.
The difference is intentional: some current weapon concepts have both a weapon-profile record and
an item-link record. All 102 current authored catalog records are therefore represented.

Notable reconciled source counts include 339 spells plus eight schools and eight class spell lists;
12 classes and subclasses; 17 feats; 38 weapons; 13 armor entries; 37 tools; 258 primary A–Z
magic-item headings; 235 monster stat blocks plus 30 monster families; and 95 animal stat blocks.

## Deliberate review boundaries

- Catalog Orc currently embeds `powerful-build`, which is absent from the SRD Orc traits. This must
  be resolved at synchronization rather than copied into the new model without review.
- Potion of Healing and Spell Scroll are referenced in both Equipment and Magic Items. Future
  implementation must create one owner for each concept while retaining both source locators.
- `Warriors` is a monster-family heading, not a stat block. `Ogre Zombie` begins before the Animals
  heading on physical PDF page 344 and remains a monster. These boundaries are explicit.
- Random-table rows, unnamed examples, arbitrary cross-products, calculation procedures, and prose
  paragraphs were not promoted into fake primary entities. Scoped activities, effects, grants,
  choices, and relationships are expanded when their owning records are implemented.
- Stable skills, languages, tools, damage types, conditions, sizes, schools, and related terms that
  currently exist only as repeated enums or JavaScript constants are classified `embedded-only`.

## Evidence

| Command | Result |
| --- | --- |
| `npm test` from `prototype/dnd2024` | 18 passed, 0 failed |
| `git diff --check -- prototype/dnd2024` | returned no errors |
| scoped `git diff --no-index --check` over the 16 inventory-slice files | passed |

The tests validate the closed inventory schemas, exact nine-family order, dependency ordering,
global code uniqueness, source bounds, known prototype archetypes, all cited catalog paths and IDs,
and the absence of a concrete `prototype/dnd2024/records/` directory.

`roleplay validate catalog` was deliberately not run because this slice changes no canonical
catalog content.

## Open synchronization decision

Before implementing the 1,966 missing records or promoting the 207 embedded terms, confirm one
single authored destination:

1. Promote the accepted prototype ECS schemas and migrate/transform the existing 102 records into
   that model; or
2. Keep the existing catalog record shapes authoritative and adapt the inventory to those shapes.

Creating a second authoritative copy under the prototype is deliberately excluded.
