# D&D code-adoption Slice 8 design — complete native-recovery inventory

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), native-recovery lane  
Dependency source: [D&D code-adoption dependency plan](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md) and
accepted Slice 1B/2B recovery classifications  
Ruleset alignment: `dnd2024-owned` for every activated rule-bearing artifact  
Evidence: [Parent Slice 8 receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-8-RECEIPT.md)

## Parent outcome

Resolve every archived D&D mechanic row classified `recover-archive` by the accepted coverage
matrix. Each row receives one explicit current disposition—keep, adapt, replace, or drop—plus
current source, owner, dependency, behavior, transaction, replay, rollback, and test evidence.
Parent 8 is complete only when no classified mechanic row remains unresolved.

The user's 2026-08-25 instruction to implement all of Slice 8 confirms reuse of the permanent IDs
already present in the accepted matrix. It does not authorize invented aliases, archive deletion,
live-state migration, or bypassing a family semantic gate.

## Accepted closure

The accepted matrix contains 51 `mechanic.dnd2024.*` recovery rows, 26 D&D component recovery rows,
and 39 D&D governing-procedure recovery rows. Current application evidence accounts for all 51
mechanics, all 39 procedures, and all 26 component dispositions: 25 activated components plus the
explicit replacement of archived `dnd2024.source` by the immutable application-source registry and
the D&D source-governance procedure. The unresolved surface is 0 mechanics, 0 components, and 0
procedures.

This inventory is bounded by the accepted matrix commit. Later archive-only records absent from
that matrix are not silently added to Parent 8; they require classification before activation.

## Family schedule

| Leaf | Family | Unresolved mechanics | Dependency/stop point |
| --- | --- | ---: | --- |
| 8A | Creature base Speed | 0 | **accepted**; standalone base state only |
| 8B | Turn-budget foundation and action economy | 0 | **accepted**; admission, diagnostics, spend, and lifecycle refresh |
| 8C | Conditions and D20 state effects | 0 | **accepted**; stop before damage/dying automation |
| 8D | Character identity and origin-state recorders | 0 | **accepted**; no content cohort or character builder |
| 8E | Inventory canonical state and transitions | 0 | **accepted**; no static item imports or AC derivation |
| 8F | Inventory/currency/carrying readers | 0 | **accepted**; effect-free bounded derived views only |
| 8G | Experience and class-progression state | 0 | **accepted**; historically bounded diagnostic scope preserved |
| 8H | D&D dice primitive | 0 | **accepted**; kernel RNG only, no generic-host rule branching |
| 8I | Contract/disposition closure and parent acceptance | 0 | **accepted**; exact matrix-hash-pinned 51/26/39 closure and full parent acceptance |

## Delivered mechanic ledger

### 8B — turn budget

- `mechanic.dnd2024.turn-budget.write`
- `mechanic.dnd2024.turn-budget.read`
- `mechanic.dnd2024.turn-budget.spend`

### 8C — Conditions

- `mechanic.dnd2024.conditions.write`
- `mechanic.dnd2024.d20-test.state-effects`

### 8D — character identity and origin state

- `mechanic.dnd2024.character-content-definition.record`
- `mechanic.dnd2024.character-profile.record`
- `mechanic.dnd2024.creature-size.record`
- `mechanic.dnd2024.language-proficiencies.record`
- `mechanic.dnd2024.tool-proficiencies.record`

### 8E — inventory state and transitions

- `mechanic.dnd2024.item-activity.use`
- `mechanic.dnd2024.item-instance.create-and-place`
- `mechanic.dnd2024.item-instance.move`
- `mechanic.dnd2024.item-instance.read`
- `mechanic.dnd2024.item-instance.record`
- `mechanic.dnd2024.item-stack.consume`
- `mechanic.dnd2024.item-stack.create-and-place`
- `mechanic.dnd2024.item-stack.merge`
- `mechanic.dnd2024.item-stack.record`
- `mechanic.dnd2024.item-stack.split`
- `mechanic.dnd2024.item.equip`
- `mechanic.dnd2024.item.equipment.read`
- `mechanic.dnd2024.item.transfer`
- `mechanic.dnd2024.item.unequip`

### 8F — inventory and carrying readers

- `mechanic.dnd2024.carrying-capacity.read`
- `mechanic.dnd2024.currency-value.read`
- `mechanic.dnd2024.inventory.read`
- `mechanic.dnd2024.item-burden.read`

### 8G — progression

- `mechanic.dnd2024.character-experience.read`
- `mechanic.dnd2024.character-experience.write`
- `mechanic.dnd2024.class-progression.read`

### 8H — dice

- `mechanic.dnd2024.dice`

## Resolved state and contract owners

The 15 component rows closed by Slices 8B–8I are `dnd2024.character-experience`,
`dnd2024.character.content-definition`, `dnd2024.character.profile`,
`dnd2024.class-progression`, `dnd2024.conditions`, `dnd2024.creature-size`,
`dnd2024.equipment-state`, `dnd2024.item-activity`, `dnd2024.item-definition`,
`dnd2024.item-instance`, `dnd2024.item-quantity`, `dnd2024.language-proficiencies`,
`dnd2024.source`, `dnd2024.tool-proficiencies`, and `dnd2024.turn-budget`.

The 27 procedure rows closed by Slices 8B–8I are the corresponding family procedures plus carrying-capacity,
currency-value, D20 state effects, dice, inventory-read, item-burden, item-transfer, play, ruleset,
source-registry, and the weapon attack/damage/profile contracts for already activated Slice 7
mechanics. Leaf 8I recovered every missing procedure and explicitly replaced `dnd2024.source` with
the current application-source registry plus D&D source-registry governance, avoiding duplicate
runtime authority.

## Per-family gate

Before each leaf changes runtime artifacts it must have one active feature implementation document,
exact SRD locator review, pinned Foundry review, current-owner search, closed schema/input/output,
declared projection dependencies, typed-effect allowlist, failure/no-change behavior, transaction and
replay owner, focused activated-path tests, catalog validation, and a receipt. Cross-owner changes
remain separate subslices even when the final parent goal includes them all.

## Parent acceptance

Parent acceptance requires:

1. a machine-checkable comparison showing all 51 classified mechanic IDs have an activated current
   owner or an explicit accepted replace/drop disposition;
2. the same closure for all 26 component and 39 procedure rows;
3. syntax, application preview/activation, focused family, combined kernel, catalog, full-suite, and
   diff validation;
4. no duplicate source/state/transaction owner, no game rules in generic C#, and no live-data or
   archive mutation; and
5. one final Parent 8 receipt that links every family receipt and records deliberate exclusions.
