---
id: mechanic.dnd2024.carrying-capacity.read
category: ruleset.dnd2024.core.data.carrying-capacity
name: Derive creature carrying capacities
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads exact SRD carry and drag/lift/push capacity, converted to canonical kilograms, with composed
physical burden.

## Matches

derive carrying capacity

## Requirements

```json
{"roles":{"creature":{"components":["dnd2024.creature.ability-scores","dnd2024.creature.body"],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.core.definition-link","dnd2024.item.quantity"],"componentReferences":[{"sourceComponentId":"dnd2024.core.definition-link","field":"definition","targetComponentIds":["dnd2024.item.physical"]}]}},"children":{"burden":{"mechanicId":"mechanic.dnd2024.item-burden.read","roleBindings":{"root":"creature"},"inheritInput":false,"input":"{}"}}}
```

## Input and result

Pass exactly `{}`. The mechanic derives Strength and Size from the creature, consumes exactly one
matching burden child result, applies the SRD Size multiplier, and converts the exact pounds formula
to kilogram measures. It reports carry, drag/lift/push, burden, and whether the burden is within
capacity without changing Speed or inventory.
