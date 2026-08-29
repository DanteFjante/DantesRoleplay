---
id: mechanic.dnd2024.temporary-hit-points.write
category: dnd2024.core.data.temporary-hit-points
name: Grant Temporary Hit Points
scope: dnd2024-srd-5.2.1
status: active
---

## Description
Grants, keeps or replaces one Temporary Hit Point buffer, or expires it. Temporary Hit Points are
separate from ordinary healing and damage.

## Matches
grant temporary hit points
grant temp hp
replace temporary hit points
expire temporary hit points
remove temporary hit points

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.creature.temporary-hit-points"],"description":"The creature whose one current Temporary Hit Point buffer is changed."}}}
```

## Input and result
Use `{"mode":"grant","amount":n}` for a first buffer. When one exists, add
`"onExisting":"keep"` or `"onExisting":"replace"`. Use `{"mode":"expire"}` to remove it.
Zero is represented by component absence. Results propose only the corresponding typed component
effect and report discarded or replaced amounts.
