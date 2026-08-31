---
id: mechanic.dnd2024.saving-throw-proficiencies.record
category: ruleset.dnd2024.core.data.saving-throw-proficiencies
name: Record saving-throw proficiencies
scope: dnd2024-srd-5.2.1
status: active
createdBy: "llm"
changeNote: "Feature 4 Slice 1: validated creation and correction path for independent saving-throw proficiency state."
---

## Description
Validates and records a character's complete known D&D 2024 saving-throw proficiency list. It accepts only the six stable ability ids, rejects duplicates and extra input, canonicalizes ability order, fixes source attribution, and records membership only.

## Matches
record saving throw proficiencies
set saving throw proficiencies
assign saving throw proficiencies
known saving throw proficiencies

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.saving-throw-proficiencies"],"description":"The creature whose complete known saving-throw proficiency list is being recorded or corrected."}}}
```
