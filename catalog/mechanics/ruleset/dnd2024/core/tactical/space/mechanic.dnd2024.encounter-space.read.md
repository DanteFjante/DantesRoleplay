---
id: mechanic.dnd2024.encounter-space.read
category: ruleset.dnd2024.core.tactical.space
name: Read encounter space diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads one encounter grid without changing it. Missing, malformed, and invalid stored state are
reported explicitly; it never supplies a default map.

## Matches

inspect encounter space
read encounter space diagnostics

## Requirements

```json
{"roles":{"encounter":{"components":["dnd2024.encounter-space"],"includeContents":true}}}
```
