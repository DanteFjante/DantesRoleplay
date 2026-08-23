---
id: mechanic.dnd2024.encounter-turn.end
category: ruleset.dnd2024.core.combat.turns
name: End encounter turns
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Explicitly ends one active D&D 2024 encounter while preserving its final round and Initiative index
for audit. It derives no winner and does not reset or remove the Initiative order. An ended encounter
has no active participant; restarting is a later lifecycle feature.

## Matches

end encounter turns
end combat turns
end the encounter turns

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-initiative-order","dnd2024.encounter-turn-state"],"includeContents":true,"description":"The active encounter whose valid Initiative snapshot and lifecycle state may become terminal."}}}
```
