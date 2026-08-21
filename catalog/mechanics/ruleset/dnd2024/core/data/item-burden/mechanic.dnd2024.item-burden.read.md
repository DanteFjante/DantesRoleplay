---
id: mechanic.dnd2024.item-burden.read
category: ruleset.dnd2024.core.data.item-burden
name: Derive nested containment physical mass
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Returns exact rational pounds for every physical item and the complete declared containment
subtree below one explicit root. It proposes no effects.

## Matches

derive nested physical mass
calculate containment-tree mass
measure carried item weight

## Requirements

```json
{"roles":{"root":{"components":["dnd2024.item-instance","dnd2024.item-quantity"],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}],"description":"A custody root or physical item whose bounded containment subtree is measured."}}}
```
