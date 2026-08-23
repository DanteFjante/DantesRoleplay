---
id: mechanic.dnd2024.armor-equipment.read
category: ruleset.dnd2024.core.data.armor-equipment
name: Read equipped armor and Shield
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads at most one direct worn armor suit and one direct held Shield for a creature without changing
any state. It reports the immutable profiles for later composition but does not interpret them.

## Matches

inspect equipped armor
read equipped armor and shield
read armor equipment diagnostics

## Requirements

```json
{"roles":{"subject":{"components":[],"includeContents":true,"contentsDepth":1,"contentComponentIds":["dnd2024.item-instance","dnd2024.item-quantity","dnd2024.equipment-state"],"componentReferences":[{"sourceComponentId":"dnd2024.item-instance","field":"definitionId","targetComponentIds":["dnd2024.item-definition"]}],"description":"The creature whose direct physical custody is inspected for one worn armor suit and one held Shield."}}}
```
