# Feature 23 dependency plan — equipment and inventory foundations

Status: **Accepted. Slices 1–11 verified.**

Last updated: 2026-08-20

## Target capability

The game can represent a distinct physical item, place it in a creature or container using the existing containment graph, and safely reason about nested contents, quantities, physical weight, container limits, creature carrying capacity, equipped state, and currency. Every mutation is an audited, atomic world change.

Inventory is a view of containment. It is **not** an entity-owned inventory array, a duplicate owner field, or a client-maintained total. An item has one immediate container, which gives the model a single source of truth and reuses the world store's cycle prevention.

### Included

- Immutable catalog item definitions and campaign-local physical item instances.
- Direct and nested custody through containment.
- Fungible quantity stacks with deterministic merge and split rules.
- Weight, volume, count, and permitted-content container limits; derived recursive burden.
- SRD 5.2.1 creature Size and carrying, drag, lift, and push capacities.
- Typed held/worn equipment state without inventing universal anatomical slots.
- Coin denominations as normal stackable physical items.
- A constrained activity/grant seam for later consumers.

### Excluded

- Armor calculation and don/doff timing (Feature 24).
- Ammunition use, loading, properties, and weapon mastery (Feature 25).
- Identification, attunement, magic effects, charges, extradimensional exceptions, and special magic containers (Feature 29).
- Character-creation package choice UX and full starting-equipment policy (Feature 30).
- Markets, prices, shopping, selling, services, crafting, loot generation, and persistent vehicle/mount cargo policy.
- The retired 2014 variant encumbrance thresholds. SRD 5.2.1 supplies a carrying maximum, not `Encumbered` or `Heavily Encumbered` speed states.

## Official rule source

`source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the Coast, 2025-05-01, CC-BY-4.0). The canonical and PDF URLs are held by the source entity.

| Locator | PDF page | Rule used by this feature |
| --- | ---: | --- |
| `Playing the Game > Creature Size` | 14 | Closed size categories: Tiny, Small, Medium, Large, Huge, Gargantuan. |
| `Rules Glossary > Carrying Capacity` | 178 | Carry/drag/lift/push formulas by size and Strength. |
| `Equipment > Currency` | 89 | Coin denominations and 50 coins per pound. |
| `Equipment > Adventuring Gear` | 89–91 | Published item weights and container capacities, including named container limits. |
| `Playing the Game > Actions > Attack` | 177 | Equipping or unequipping a weapon before/after its attack is part of that attack. |
| `Equipment > Magic Items` | 102–103 | Boundary only: attunement and magic-container exceptions stay with Feature 29. |

The source supports concrete equipment measurements, not a universal slot grid. “Slots” remain presentation/optional campaign policy rather than core inventory authority.

## Verified platform and feature basis

| Authority | Evidence | Planning decision |
| --- | --- | --- |
| World model | `procedure.world.model`; `Containment.cs`; `WorldStore.MoveAsync` | Containment is already a single-parent, cycle-safe tree and is the inventory location model. |
| Atomic world changes | `procedure.world.change`; `EffectApplier` | Transfers, quantity changes, and state changes validate before producing one all-or-nothing effect list. |
| Existing mechanics projection | `procedure.mechanic.projection`; `ProjectionResolver.cs` | `includeContents` supplies direct child identity/name/slot only. It cannot calculate nested burden or validate a nested transfer. |
| Existing item-like owner | weapon-profile components and Features 7–9 | A weapon profile is a canonical rules definition, not a carried physical item. Feature 23 adds instances without copying combat logic. |
| Feature 12 | `feature-12/FEATURE-12-DEPENDENCY-PLAN.md` | Draw/stow and equipment timing can spend object/action resources only after turn budgets exist; basic custody does not wait for it. |
| Feature 20 | roadmap and movement planning | Size is shared actor state. Feature 23 defines its carrying consumer; movement owns movement consequences. |

Static repository evidence only was available for this planning pass. No runtime catalog/database operation, fixture import, or MCP artifact was created.

## Dependency graph

```text
implemented world containment + atomic effect application
                  |
                  v
Slice 1: generic bounded containment action projection
                  |
        +---------+-------------------------+
        |                                   |
        v                                   v
Slice 2: definition/version boundary   Slice 3: physical item instances
        |                                   |
        +-------------------+---------------+
                            v
                 Slice 4: quantity stacks
                            |
        +-------------------+-------------------------+
        |                                             |
        v                                             v
Slice 5: measures/container capabilities       Slice 6: Size/carry resolver
        |                                             |
        +-------------------+-------------------------+
                            v
              Slice 7: transfer, nesting, capacity admission
                            |
        +---------+-------------------+---------------+
        |                             |               |
        v                             v               v
Slice 8: held/worn state         Slice 9: currency  Slice 10: activities/grants
                            |
                            v
             Slice 11: read-model, fixtures, consumer acceptance
```

The first dependency is intentionally generic. It extends the platform's declared, bounded action projection; it does not add `item`, `weight`, `container`, or D&D vocabulary to the kernel.

## Ownership and confirmation boundary

Feature 23 owns physical item **instances**, their direct custody, permitted nested contents, quantities, measurement/capacity rules, carrying derivation, equipment state, and currency stacks. Feature 7's weapon profile remains the owner of a weapon's combat statistics. An instance references a definition; it never becomes an alternate weapon-profile schema.

Before implementation begins, confirm the following semantic decisions and permanent ids. This plan intentionally does not reserve speculative ids for every future item category.

| Decision or id family | Proposed meaning |
| --- | --- |
| Generic projection request fields | Opt-in `contentsDepth` and `contentComponentIds` (names subject to confirmation) extend an existing role requirement; they are generic public API, not D&D ids. |
| Item definition identity | An immutable catalog entity identified by a stable definition id and cited source. A revised definition is a new definition/revision, never a silent rewrite of properties already instantiated in a campaign. |
| Physical item identity | A campaign-world entity with an item-instance component that refers to precisely one definition identity. |
| Custody | Immediate containment only. Legal ownership, party claims, and theft policy are separate relationship/policy work and are never inferred from custody. |
| Equipment state | A typed state owned by the physical instance (`held`, `worn`, or explicit unset), plus a later consumer-defined eligibility check. It is not an inventory slot index. |
| Currency | Physical stack instances of the five SRD denominations. Values are conversion data; no parallel wallet total is authoritative. |

The item definition's precise cross-version reference form is deliberately an early design gate. The catalog importer's content hash detects import drift; it is not a promised rules-definition version reference for a live campaign. Slice 2 selects and documents the durable reference strategy before any campaign instance is authored.

## Slice plan

### Slice 1 — generic bounded containment action projection

**Status: Verified.** Receipt: [Feature 23 Slice 1 receipt](FEATURE-23-SLICE-1-RECEIPT.md).

**Purpose.** Give an opt-in mechanic role a bounded recursive view of contents and explicitly declared descendant components. This is the prerequisite for rules that must know what is inside a bag, pack, or nested container at the instant they decide an effect.

**Confirmed compatible request contract.**

- Preserve `includeContents: true` as today: direct children only, with identity/name/slot only.
- Add `contentsDepth`, valid only with `includeContents`; default it to `1`, allow `1..4`.
- Add `contentComponentIds`, valid only with `includeContents`; it is a unique, bounded allow-list of component ids visible on contained nodes. Omitted means no descendant components.
- Project recursive descendants as `contains` on each contained node. A contained node has only its id, name, slot, requested component subset, and recursively projected `contains`; it does not inherit root components, relationships, container ancestry, or undeclared data.
- Limit a role's recursive contained-node projection to 100 nodes. If the complete declared view would exceed the limit, fail before JavaScript/effect construction with a stable projection-limit error; never silently truncate and undercount burden.

**Implementation boundary.** Extend `RoleRequirement`, requirement validation and `AllComponentIds`, `ContainedProjection`, the projection resolver, audit serialization, and focused projection/mechanic-store tests. Load each requested depth in bounded set queries, then load the allow-listed components in one set query; no lazy/N+1 reads are permitted. Preserve deterministic ordering with existing direct-child ordering plus explicit id/slot tie-breakers.

**Required tests and exit gate.**

- Existing direct-content mechanics receive exactly the current shape and remain green.
- A depth-two declared role sees only requested descendant components and recursive children.
- Undeclared, root-only, and relationship data never leaks into a contained projection.
- Invalid combinations, unknown descendant component ids, a fifth depth, and a 101st projected node fail deterministically before the mechanic runs.
- Empty contents, stable ordering, and corrupt/cyclic storage fixtures do not cause unbounded traversal; the normal containment writer continues to reject cycles.
- `MechanicComposer` fan-out over direct contents remains compatible.

No D&D component, procedure, mechanic, event, migration, or fixture was introduced in this slice.
The confirmed public wire shape is implemented in `RoleRequirement`, validated at authoring and
resolution, and serialised through the existing action-audit path. Verification is recorded in the
receipt; no persistent database import occurred.

### Slices 2–4 — definitions, instances, and quantity stacks

**Slice 2 status: Verified.** Receipt: [Feature 23 Slice 2 receipt](FEATURE-23-SLICE-2-RECEIPT.md).

**Slice 3 status: Verified.** Receipt: [Feature 23 Slice 3 receipt](FEATURE-23-SLICE-3-RECEIPT.md).

**Slice 4 status: Verified.** Receipt: [Feature 23 Slice 4 receipt](FEATURE-23-SLICE-4-RECEIPT.md).

| Slice | Deliverable | Invariants and exit gate |
| --- | --- | --- |
| 2. Immutable definition and durable reference | A governing procedure, source-cited component definitions for physical properties and container capability, plus a versioned-definition reference contract. Seed only a representative nonmagic set: backpack, pouch, quiver, rope, a simple weapon already represented by Feature 7, and coin types. | Definitions are immutable once referenced; an instance resolves exactly one definition; unknown measures are explicit, never guessed; Feature 7 remains combat-stat owner. Confirm ids/reference strategy, validate catalog, and reject invalid definition combinations. |
| 3. Physical instances and direct custody | Item-instance component, administrative record/correct/read mechanics, and atomic create-and-place/grant primitive with explicit destination role. | An instance names exactly one definition; location is containment only; failed admission creates no orphan; no legal owner is inferred. Test creation, direct move, inspect, missing definition refusal, and existing containment rules. |
| 4. Fungible quantities and stack algebra | Quantity component and stack key derived from exact definition identity; atomic create/record, merge, split, and consume paths. | Quantity is positive integer; stacks never mix keys; merge/split preserve count; zero deletes the stack entity; charged/identified/unique objects have no merge path by default. Merge has an explicit retained target and requires the same direct container. Test conservation, deterministic target, direct-content refusal, and rollback. Capacity and inter-container transfer remain deferred. |

### Slices 5–7 — capacity, carry, and transfer

| Slice | Deliverable | Invariants and exit gate |
| --- | --- | --- |
| 5. Physical measures, containers, recursive burden | Source-backed item/container capability data and a bounded burden reader using Slice 1 plus declared component-reference projection. | Totals are recomputable; limits name their measurement dimension; missing measures fail rules needing them rather than becoming zero; magic exceptions stay excluded. Test nested/empty/limit cases. **Verified:** [receipt](FEATURE-23-SLICE-5-RECEIPT.md). |
| 6. Creature Size and carrying resolver | Shared actor-size component/procedure with six SRD categories; effect-free carry/drag/lift/push resolver consuming Strength and burden. | Tiny is Strength × 7.5 lb carry and ×15 lb drag/lift/push; Small/Medium ×15/×30; larger sizes double successively; explicit size only; no invented speed state. Test every size and missing data; coordinate ids with Feature 20. **Verified:** [receipt](FEATURE-23-SLICE-6-RECEIPT.md). |
| 7. Transfer, nesting, capacity admission | One whole-instance transfer mechanic with explicit source, destination, and item roles; it composes safely with the existing quantity split/merge mechanics. | Validate direct custody, visible self/descendant cycle, permitted content, direct capacity, quantity, and compatibility before effects; rejected transfer mutates nothing; no hidden inventory root. Direct/nested/prohibited/over-capacity/self-descendant/stack-interaction/atomic cases are verified. |

### Slices 8–11 — equipment, currency, activity, acceptance

| Slice | Deliverable | Invariants and exit gate |
| --- | --- | --- |
| 8. Held and worn equipment state | Typed `dnd2024.equipment-state` component, definition-declared eligibility, equip/unequip transitions, and a narrow read seam. | Equipped items remain in declared direct custody; eligibility is definition-driven; inaccessible item and fungible stacks cannot be equipped; normal transfer requires explicit unequip first. Dual-wield/armor/shield rules are not implied. Existing Features 7–9 stay unchanged; costs wait for Feature 12. **Verified:** [receipt](FEATURE-23-SLICE-8-RECEIPT.md). |
| 9. Currency | Source-cited copper, silver, electrum, gold, and platinum definitions, conversion metadata, stack use, and read-only value helper. | 50 coins weigh one pound; conversion never silently changes physical stacks; displayed value is derived; no wallet/economy transaction. Test denomination conservation and mass. **Verified:** [receipt](FEATURE-23-SLICE-9-RECEIPT.md). |
| 10. Item activities and effect grants | Typed activity/grant descriptor and helpers that resolve stated targets, consume stated quantity, and emit explicit effects. | Grant is atomic with placement; no arbitrary authored item script; action/check/time targets belong to later owners; magic stays Feature 29. Test no partial placement/consumption and reject unknown activity types. **Verified:** [receipt](FEATURE-23-SLICE-10-RECEIPT.md). |
| 11. Read-model, fixtures, consumer acceptance | Bounded inventory inspection shape, representative fixtures, catalog validation, and consumption tests for weapon equipment seam, carrying, and currency. | Inspection is read-only and marks omission rather than reporting a false total; clients cannot bypass admission; fixtures create no second owner. Run `roleplay validate catalog`, focused tests, then the full suite at feature acceptance; protocol walk only if MCP registration changes. **Verified:** [receipt](FEATURE-23-SLICE-11-RECEIPT.md). |

## Cross-feature acceptance boundaries

| Consumer | Stable seam from Feature 23 | Still owned by consumer |
| --- | --- | --- |
| Feature 24 armor | Instance, custody, worn state, source-backed item properties | AC formula, armor/shield eligibility, don/doff timing. |
| Feature 25 weapons | Instance, quantity, held state, weapon-profile reference seam | Ammunition, loading, properties, mastery, attack behavior. |
| Feature 29 magic items | Instance and ordinary container/burden model | Attunement, charges, magic identity/effects, exceptional capacity. |
| Feature 30 creation | Atomic create-and-place helper and definitions | Choice legality, class/background packages, guided UI flow. |
| Economy/crafting later | Currency/item definition and transfer seam | Price, availability, recipes, time, outputs, settlement policy. |

## Next implementation handoff when Feature 23 is selected

**Feature 23 is accepted.** The final read model composes the verified custody, quantity, burden,
equipment, currency, and activity seams without recreating inventory state or bypassing admission.
No persistent catalog import is authorized merely by this acceptance; integration play or release
must still follow the repository import/verify boundary.

At that future boundary, re-read `procedure.system.create-feature`, the Feature 23 plan, and the
item-definition/quantity/transfer contracts; implement one coherent slice; then run its focused
tests. No persistent catalog import is authorized merely by this plan.
