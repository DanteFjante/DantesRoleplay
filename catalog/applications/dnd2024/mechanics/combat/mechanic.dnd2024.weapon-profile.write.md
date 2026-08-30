---
id: mechanic.dnd2024.weapon-profile.write
category: ruleset.dnd2024.core.data.weapon-profile
name: Record weapon profile
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Records or corrects one normalized base weapon activity. The weapon definition owns category and
activity membership; the selected active activity definition owns activation, attack, damage, and
exact-metre range facets.

## Matches

record weapon profile
set weapon profile

## Requirements

```json
{"roles":{"weapon":{"components":["dnd2024.item.weapon","dnd2024.activity.membership"],"description":"The canonical weapon definition whose category and activity membership are recorded."},"activity":{"components":["dnd2024.core.version","dnd2024.activity.activation","dnd2024.activity.attack","dnd2024.activity.damage","dnd2024.activity.range"],"description":"The active activity definition whose normalized base-attack facets are recorded."}}}
```
