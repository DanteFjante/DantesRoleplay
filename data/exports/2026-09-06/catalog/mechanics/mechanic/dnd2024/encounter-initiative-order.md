---
id: mechanic.dnd2024.encounter-initiative-order
category: ruleset.dnd2024.core.combat.initiative.order
name: Set an encounter's Initiative order
scope: dnd2024-srd-5.2.1
status: deprecated
createdBy: "seed"
changeNote: "Re-seeded: the embedded catalog mechanic changed."
---

## Description
Composes the individual D&D 2024 Initiative resolver once per contained participant, orders the encounter by descending Initiative count with authorized tie decisions, and records one encounter-owned order snapshot. It reads no ability scores, makes no D20 Test of its own, and writes nothing to a participant.

## Matches
start combat
start the encounter
set the encounter initiative order
order the encounter by initiative
determine the turn order

## Requirements
```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order"],"includeContents":true,"description":"The encounter whose contained participants roll Initiative. It must not already carry an order snapshot."}},"children":{"initiative":{"mechanicId":"mechanic.dnd2024.initiative.roll","roleBindings":{"subject":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"inputFromParentProperty":"participants","inputForEachItem":true}}}
```
