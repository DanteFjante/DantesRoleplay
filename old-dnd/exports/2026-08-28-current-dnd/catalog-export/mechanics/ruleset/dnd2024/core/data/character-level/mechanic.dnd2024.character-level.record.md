---
id: mechanic.dnd2024.character-level.record
category: ruleset.dnd2024.core.data.character-level
name: Record total character level
scope: dnd2024-srd-5.2.1
status: active
createdBy: "llm"
changeNote: "Created for Feature 2 Slice 2 as the validated write path for total character level."
---

## Description
Validates and records one player character total level from 1 through 20, fixes its SRD 5.2.1 source reference, and reports the derived Proficiency Bonus without storing it. Administrative setup/correction only; no class advancement or monster CR.

## Matches
record character level
set character level
assign character level
character is level

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.character-level"],"description":"The player character whose total level is being recorded or corrected."}}}
```

