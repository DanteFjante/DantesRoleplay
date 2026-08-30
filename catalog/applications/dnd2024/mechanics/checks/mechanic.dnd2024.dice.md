---
id: mechanic.dnd2024.dice
category: ruleset.dnd2024.core.gameplay.dice
name: Roll seeded dice
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Rolls bounded dice with an optional modifier using the mechanic's seeded random source.

## Matches
roll dice
roll a d20
roll 2d6

## Requirements
```json
{}
```

## Input and result

Pass an object containing any of `count`, `sides`, and `modifier`; omitted values default to
`1d20+0`. Count is 1–100, sides is 2–1,000,000, and the modifier and possible totals must remain
safe integers. The result lists every seeded roll and the total and never proposes effects.
