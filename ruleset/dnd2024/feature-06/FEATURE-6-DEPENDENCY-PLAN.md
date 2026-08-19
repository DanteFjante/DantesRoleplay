# Feature 6 dependency plan — Authoritative Armor Class and Hit Point state

Status: **Complete; both authoritative state slices are verified**
Last updated: 2026-08-19

## Execution rule

Runtime content is authored in `catalog/`, reviewed there, imported with `roleplay import catalog`,
then checked with `roleplay verify catalog`. Each implementation pass completes exactly one lowest
slice and stops for review; Feature 6 completed after its separately verified Armor Class and Hit
Point passes.

## Target capability

A GM can give a creature one authoritative Armor Class and one authoritative current/maximum Hit
Point state, so later attack and damage rules have stable inputs without inventing their own values.

### Included

- Reusable, source-cited Armor Class and current/maximum Hit Point components.
- Closed administrative record/correction mechanics with audited component effects.
- Strict data bounds, source ownership, deterministic result shapes, routing, and cleanup tests.

### Excluded

- Armor, shields, class features, spells, magic items, natural armor, and AC-building formulas.
- Attack rolls, hit/miss comparison, weapon data, targeting, range, cover, and critical hits.
- Damage application, healing, temporary Hit Points, resistance, vulnerability, immunity,
  unconsciousness, death, massive damage, and death saves.
- Speed, size, senses, resources, creature/stat-block import, or a database schema change.

## Official source basis

`source.dnd2024.srd-5.2.1` identifies SRD 5.2.1, published 2025-05-01 under CC-BY-4.0.

- *Playing the Game > D20 Tests > Attack Rolls > Armor Class*, PDF page 6: AC represents how a
  creature avoids being wounded; an attack meets or exceeds AC to hit; base AC is 10 plus Dexterity
  modifier, later modified by armor and other sources.
- *Playing the Game > Damage and Healing > Hit Points*, PDF page 16: damage loses HP, healing
  cannot raise current HP above maximum, and zero HP begins separate later rules.

Feature 6 records final AC and bounded HP state. It deliberately does not pretend to implement the
unmodelled formulas or consequences behind those two facts.

## Verified existing dependencies

| Dependency | Evidence |
| --- | --- |
| File-first workflow | The current `procedure.system.create-feature` requires catalog authoring, dry-run/import, and verify. |
| Source registry | Catalog entity `source.dnd2024.srd-5.2.1` contains SRD identity, URL, license, and locator format. |
| State model | `procedure.world.model` and `procedure.world.change` define reusable components and `component.add`/`component.set`. |
| Execution path | Existing mechanic/action contracts and the ActionRunner apply closed-input mechanics and audited effects. |
| Import guard | `roleplay verify catalog` reports 56 matching records; catalog-import regression tests cover fresh databases. |
| Ownership search | Before Feature 6, the catalog had no AC, HP, damage, healing, armor, or defense owner; this feature now owns only final AC and bounded HP state. |

## Recursive dependency analysis

```text
Feature 6: defensive and durability state
├─ SRD source identity and locators                         [implemented]
├─ component/effect model and catalog workflow              [implemented]
├─ final Armor Class state                                  [Slice 1 verified]
│  ├─ dnd2024.armor-class definition                        [verified]
│  └─ record/correction procedure and mechanic              [verified]
└─ current/maximum Hit Point state                          [Slice 2 verified]
   ├─ dnd2024.hit-points definition                         [verified]
   └─ record/correction procedure and mechanic              [verified]

Later consumers: attack versus AC [Feature 8]; damage and HP loss [Feature 9].
```

## Dependency and ownership decisions

1. `dnd2024.armor-class` owns one final positive integer AC on the defended creature. It is not
   copied into weapons, encounters, action input, or a roll result.
2. `dnd2024.hit-points` owns the atomic pair `current` and `maximum`. `maximum` is a positive safe
   integer; `current` is an integer in `0..maximum`. Splitting these would allow invalid half-state.
3. AC is not derived in Feature 6. The SRD base formula is provenance, but armor/class/spell/
   monster inputs are excluded. A final recorded value is more truthful than an incomplete formula.
4. HP recording is not damage or healing. Feature 6 has no delta, damage type, temporary HP, death,
   or condition input. Feature 9 will own causal HP loss.
5. Both components write a fixed `sourceRef`; callers never supply source ids, locators, modifiers,
   or effects.
6. Each writer has a closed `mode` of `record` or `correct`. Record requires absence and proposes
   `component.add`; correct requires presence and proposes `component.set`. This is the normal
   safe path without silent overwrite or per-creature mechanics.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Armor Class state | Plan reviewed | **Verified 2026-08-19:** file-authored component, contract, and writer were imported/read back and passed closed-input, state, routing, boundary, corrupt-state, replay, and cleanup checks. |
| 2 | Hit Point state | Slice 1 reviewed and verified | **Verified 2026-08-19:** file-authored component, contract, and writer passed bounds, correction, state, routing, replay, cleanup, and catalog verification. |

## Slice 1 — Authoritative Armor Class state

### Runtime artifacts

- `dnd2024.armor-class` component definition.
- `procedure.mechanic.dnd2024.armor-class` in
  `ruleset.dnd2024.core.data.armor-class`.
- `mechanic.dnd2024.armor-class.write`, active under the same category/scope
  `dnd2024-srd-5.2.1`.

### Contract

Component data is exactly:

```json
{"value":14,"sourceRef":{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > D20 Tests > Attack Rolls > Armor Class"}}
```

`value` is a positive safe integer. The writer has required role `subject` and exact input
`{"mode":"record"|"correct","value":<positive safe integer>}`. It rejects null/arrays/extra
keys, base-AC or Dexterity/armor/shield inputs, caller source fields, and caller effects.

### Behavior and effects

Validate input and existing state before proposing anything. `record` requires no AC component and
returns one canonical `component.add`; `correct` requires one and returns one canonical
`component.set`. Result data names mode, final value, and source. It uses no dice and performs no
other effect.

### Acceptance matrix and exit gate

Test initial values 1, 10, 14, and a high safe-integer boundary; correction; duplicate record and
absent correction; every wrong root/type/range/mode/extra/derived/source field; corrupt stored
state; replay; intent routing against abilities/saves/Initiative and generic component language;
exact effect/data/state changes; readback; cleanup; full tests; and clean catalog verify. Import
the reviewed files only after a clean dry run, then stop once every row has evidence.

### Slice 1 evidence — 2026-08-19

- `roleplay import catalog --dry-run` reported exactly three new records: the component,
  procedure, and writer; 50 existing records were unchanged.
- The apply pass created all three records. `roleplay verify catalog` then reported 53 unchanged
  records, proving the shared database and catalog agree.
- `CatalogFeature6Tests` imports a copy of the catalog into a fresh database and proves routing,
  first record, guarded correction, duplicate/absent rejection, malformed input preservation,
  low/ordinary/maximum safe-integer boundaries, and corrupt-state non-repair.
- The full repository suite passed 294/294. This is the stop point for the slice.

## Slice 2 — Authoritative current and maximum Hit Point state

### Status and artifacts

Implemented as `dnd2024.hit-points`, `procedure.mechanic.dnd2024.hit-points`, and
`mechanic.dnd2024.hit-points.write` under `ruleset.dnd2024.core.data.hit-points` and scope
`dnd2024-srd-5.2.1`.

### Contract, behavior, and exit gate

Data is exactly `{"current":<integer>,"maximum":<positive integer>,"sourceRef":{...}}` with
`0 <= current <= maximum`, safe-integer values, and fixed source
`source.dnd2024.srd-5.2.1` / `Playing the Game > Damage and Healing > Hit Points`. The closed
writer input mirrors Slice 1, substituting `current` and `maximum`; record/add and correct/set the
whole pair atomically. It does not implement damage, healing, temporary HP, or zero-HP effects.

Test `(0,1)`, `(1,1)`, ordinary partial/full pairs, a safe-integer boundary, every invalid order/
type/range/mode/source/extra-key shape, absent/existing/corrupt state, deterministic replay,
routing, one-effect atomicity, no AC mutation, artifact readback, fixture removal, full regression,
and clean catalog verify. Feature 6 completes only once this entire gate is met.

### Slice 2 evidence — 2026-08-19

- `roleplay import catalog --dry-run` reported exactly three new records: the component,
  procedure, and writer; 53 existing records were unchanged.
- The apply pass created all three records. `roleplay verify catalog` then reported 56 unchanged
  records, proving the shared database and catalog agree.
- `CatalogFeature6Tests` imports a copy of the catalog into a fresh database and proves atomic
  first record/correction, `(0,1)`, `(1,1)`, ordinary/full, and maximum-safe-integer boundaries,
  `current > maximum` rejection, malformed input preservation, absent correction, corrupt-state
  non-repair, intent routing, and no Armor Class mutation.
- The full repository suite passed 295/295. Feature 6 is complete; later attack and damage
  features consume this state but still require their own plans and implementation passes.

## Plan-quality audit

The target, SRD locators, existing owners, independent leaves, state ownership, closed inputs,
derived/transient exclusions, safe write/correction paths, acceptance matrices, cleanup, import
flow, and one-slice stop gate are explicit. Both artifacts were created from reviewed catalog files
and independently verified after import.

## Plan-change rule

Stop and revise if an existing AC/HP owner appears, a source rule changes a boundary, AC derivation
requires unmodelled inputs, or HP recording begins to encode damage/healing/death. Do not bypass
such a dependency with caller-supplied totals, duplicate components, generic deltas, or C# game
helpers.
