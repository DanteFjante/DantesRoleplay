---
id: mechanic.dnd2024.encounter-initiative-order
category: ruleset.dnd2024.core.combat.initiative.order
name: Set an encounter Initiative order
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Composes per-participant Initiative results into one immutable encounter-owned snapshot and applies
their optional active-rest interruption plans in the same transaction.

## Matches

start combat encounter
set encounter initiative order
order encounter by initiative
determine encounter turn order

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order"],"includeContents":true,"description":"The encounter containing all Initiative participants."}},"children":{"initiative":{"mechanicId":"mechanic.dnd2024.initiative.roll","roleBindings":{"subject":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"inputFromParentProperty":"participants","inputForEachItem":true}}}
```
