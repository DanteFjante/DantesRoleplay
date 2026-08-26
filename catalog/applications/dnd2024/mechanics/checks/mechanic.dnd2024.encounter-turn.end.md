---
id: mechanic.dnd2024.encounter-turn.end
category: ruleset.dnd2024.core.combat.turns
name: End encounter turns
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Explicitly marks an active encounter lifecycle as ended while retaining its historical position.

## Matches

end encounter turns
end combat encounter

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The active ordered encounter to end."}}}
```
