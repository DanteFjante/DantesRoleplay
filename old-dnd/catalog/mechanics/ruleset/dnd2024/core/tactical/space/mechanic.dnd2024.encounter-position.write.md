---
id: mechanic.dnd2024.encounter-position.write
category: ruleset.dnd2024.core.tactical.space
name: Record encounter position
scope: dnd2024-srd-5.2.1
status: active
---

## Matches

place encounter participant
correct encounter participant position

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.creature-size","dnd2024.encounter-position"]},"encounter":{"components":["dnd2024.encounter-space"],"includeContents":true}},"children":{"participants":{"mechanicId":"mechanic.dnd2024.encounter-participant-tactical-state.read","roleBindings":{"participant":"$item"},"forEachContentsOf":"encounter","inheritInput":false,"input":"{}"}}}
```
