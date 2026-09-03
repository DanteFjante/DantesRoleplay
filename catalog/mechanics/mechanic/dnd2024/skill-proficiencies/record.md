---
id: mechanic.dnd2024.skill-proficiencies.record
category: ruleset.dnd2024.core.data.skill-proficiencies
name: Record skill proficiencies
scope: dnd2024-srd-5.2.1
status: active
createdBy: "llm"
changeNote: "Created for Feature 2 Slice 3 as the validated creation and correction path for character skill-proficiency state."
---

## Description
Validates and records a character's complete known D&D 2024 SRD skill-proficiency list. It accepts only the 18 stable ids, rejects duplicates and extra input, canonicalizes ordering, fixes source attribution, and returns advisory default abilities without storing them.

## Matches
train in skills
choose trained skills
update trained skills
known trained skills

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.skill-proficiencies"],"description":"The character whose complete known skill-proficiency list is being recorded or corrected."}}}
```
