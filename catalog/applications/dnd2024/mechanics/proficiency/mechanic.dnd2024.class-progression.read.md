---
id: mechanic.dnd2024.class-progression.read
category: ruleset.dnd2024.core.advancement.class-progression
name: Read D&D 2024 class progression entitlement
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads a class definition's canonical progression entity for one exact level and reports its grant
references without applying them.

## Matches

inspect class progression

## Requirements

```json
{"roles":{"class":{"components":["dnd2024.advancement.class"],"componentReferences":[{"sourceComponentId":"dnd2024.advancement.class","field":"progressionRef","targetComponentIds":["dnd2024.advancement.progression"]}],"description":"The canonical class definition whose progression is inspected."}}}
```
