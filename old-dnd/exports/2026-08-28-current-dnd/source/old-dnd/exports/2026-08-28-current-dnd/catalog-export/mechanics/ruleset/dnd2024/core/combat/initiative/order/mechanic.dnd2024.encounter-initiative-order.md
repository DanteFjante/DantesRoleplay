---
id: mechanic.dnd2024.encounter-initiative-order
category: ruleset.dnd2024.core.combat.initiative.order
name: Set an encounter's Initiative order
scope: dnd2024-srd-5.2.1
status: active
createdBy: "llm"
changeNote: "Replaces the temporary diagnostic version and restores the real rule. The diagnostic proved the running host does not expose ctx.children to the sandbox even though the composer produced three correct child results, so this version fails with an explicit, actionable message when the host is stale rather than reporting a misleading count mismatch. It also reports the received count when the counts genuinely differ."
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

