---
id: mechanic.dnd2024.currency-value.read
category: ruleset.dnd2024.core.data.currency-value
name: Derive physical currency value
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Returns the bounded, read-only copper-piece value and denomination breakdown of physical currency
stacks contained below one explicit custody root.

## Matches

count carried currency
read physical coin value
inspect coin stacks

## Requirements

```json
{"roles":{"root":{"components":[],"includeContents":true,"contentsDepth":4,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}],"description":"The custody root whose bounded nested physical currency stacks are read."}}}
```
