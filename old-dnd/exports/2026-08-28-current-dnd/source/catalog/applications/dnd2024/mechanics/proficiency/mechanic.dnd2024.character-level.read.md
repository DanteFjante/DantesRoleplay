---
id: mechanic.dnd2024.character-level.read
category: ruleset.dnd2024.character.advancement
name: Derive total D&D 2024 character level
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Derives total character level and Proficiency Bonus from the character's independently addressable
class-membership entities without storing either derived value.

## Matches

read character level
derive proficiency bonus

## Requirements

```json
{"roles":{"subject":{"components":[],"includeRelationships":true,"relationshipComponents":[{"kind":"character.has-class-membership","direction":"outgoing","targetComponentIds":["dnd2024.character.class-membership"]}],"description":"The character whose related class memberships determine total level."}}}
```
