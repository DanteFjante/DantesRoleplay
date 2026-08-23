---
id: mechanic.dnd2024.dying.on-damage
category: ruleset.dnd2024.core.gameplay.dying
name: Apply dropping-to-zero consequences
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reacts to accepted D&D damage facts, beginning death state and Unconsciousness, recording damage
while dying, or setting terminal death as the recorded damage requires.

## Matches

apply automatic dropping to zero consequences

## Requirements

```json
{"event":{"mode":"reaction","types":["dnd2024.damage.dealt"],"components":["dnd2024.hit-points","dnd2024.zero-hit-points-policy","dnd2024.death-state","dnd2024.conditions"]}}
```
