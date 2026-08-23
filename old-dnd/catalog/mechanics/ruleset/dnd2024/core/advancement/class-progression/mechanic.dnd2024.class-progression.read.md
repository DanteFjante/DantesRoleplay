---
id: mechanic.dnd2024.class-progression.read
category: ruleset.dnd2024.core.advancement.class-progression
name: Read D&D 2024 class progression entitlement
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads an immutable D&D 2024 class-content progression declaration for one exact class level. It
reports entitlement identities as diagnostics only; a declared class feature is not an available
action until its dedicated behavior owner exists.

## Matches

inspect class progression
read class progression
inspect fighter class level

## Requirements

```json
{"roles":{"class":{"components":["dnd2024.character.content-definition","dnd2024.class-progression"],"description":"The immutable active class content entity whose exact declared progression level is inspected."}}}
```
