---
id: mechanic.dnd2024.armor-class.write
category: ruleset.dnd2024.core.data.armor-class
name: Legacy manual Armor Class writer
scope: dnd2024-srd-5.2.1
status: deprecated
---

## Description
Legacy historical writer retained only so older catalog records remain intelligible. It is not
routable and no normal combat mechanic consumes its component. Use
`mechanic.dnd2024.armor-class.read` for derived Armor Class.

## Matches
legacy record armor class

## Requirements
```json
{"roles":{"subject":{"components":["dnd2024.armor-class"],"description":"The creature whose final Armor Class is being recorded or corrected."}}}
```
