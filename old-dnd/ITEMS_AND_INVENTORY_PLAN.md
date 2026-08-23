# Items and inventory roadmap

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Ruleset Feature 23 accepted; adjacent equipment features remain separately owned**
Last reviewed: 2026-08-21

## Ownership

- Immutable, source-backed item definitions are catalog content.
- Physical items are entity instances; containment is custody/location and provides nesting.
- Quantity, equipment state, and other mutable facts are components on instances.
- Inventory is a bounded projection, never an actor-owned array.
- Weight, capacity, currency value, Armor Class, attack use, and similar totals are derived from
  authoritative facts, not stored or caller supplied.
- Generic effect/transaction/audit code stays in C#; D&D item rules stay in catalog JavaScript.

Feature boundaries:

| Concern | Owner |
| --- | --- |
| Core definitions, instances, containment, quantities, burden, carrying, transfer, equipment state, currency, fixed activities | D&D Feature 23 |
| Armor/shields and derived Armor Class | Feature 24 |
| Weapon properties/mastery and range vocabulary | Features 25 and 21 |
| Magic items and attunement | Feature 29 |
| Starting-equipment grants inside character creation | Feature 30 / Character CH5 composition |
| Action-economy interactions | Feature 12 and the consuming action owner |

## Accepted Feature 23 surface

| Slice | Delivered | Evidence |
| --- | --- | --- |
| 1 | Bounded inventory projection | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-1-RECEIPT.md) |
| 2 | Immutable item definitions and item instances | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-2-RECEIPT.md) |
| 3 | Quantity/stack lifecycle | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-3-RECEIPT.md) |
| 4 | Nested projection and burden | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-4-RECEIPT.md) |
| 5 | Creature size and carrying capacity | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-5-RECEIPT.md) |
| 6 | Atomic transfer and container validation | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-6-RECEIPT.md) |
| 7 | Equip/unequip and administrative-path separation | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-7-RECEIPT.md) |
| 8 | Physical currency and exact payment | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-8-RECEIPT.md) |
| 9 | Fixed item activity/use | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-9-RECEIPT.md) |
| 10 | Starting-equipment capability | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-10-RECEIPT.md) |
| 11 | Read-only inventory UI/projection boundary | [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-11-RECEIPT.md) |

Catalog contracts, tests, and these receipts own the implementation details. The completed Feature 23
dependency plan was removed to avoid a second historical specification.

## Load-bearing invariants

1. One physical item has one containment parent and cannot form a cycle.
2. Moving a container moves its subtree without rewriting descendants.
3. Definition revisions do not silently mutate the identity of existing instances.
4. Stack merge/split/consume preserves quantity, compatibility, containment, and zero-removal rules.
5. Burden/capacity projections are bounded, deterministic, and derived from instance/definition
   facts; missing facts fail rather than becoming zero.
6. Transfer/equip/payment/use validates the entire ordered change and commits once or not at all.
7. Administrative fixture/bootstrap mechanics are not player activities and cannot be selected by
   ordinary play intent.
8. A weapon attack uses the exact possessed/equipped instance through existing rule owners; it does
   not copy weapon statistics onto the actor.
9. Rejection and replay preserve state and produce useful audit evidence.

## Remaining work

Use [the D&D roadmap](ruleset/dnd2024/ROADMAP.md) for Features 24, 25, 29, and 30. Character creation
must call the accepted starting-equipment capability inside the single CH5 root; it must not create
a second transaction or direct component-write path.

Later economic/crafting/identification/durability systems need separate owners and evidence. They
must not widen Feature 23's generic fields into ambiguous slots, sizes, resources, or arbitrary item
scripts.

## Deferred

- Universal slot inventories or item grids.
- Guessed item dimensions or creature-size categories for ordinary items.
- Complete equipment/magic-item content packs.
- Shops, market simulation, procedural loot, crafting, repair, theft, or durability.
- Caller-supplied effects/totals, direct browser writes, or item-specific C# rule classes.
