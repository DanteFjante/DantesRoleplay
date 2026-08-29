---
id: mechanic.dnd2024.armor-training.read
category: ruleset.dnd2024.core.data.armor-training
name: Read armor-training diagnostics
scope: dnd2024-srd-5.2.1
status: active
---

## Description

Reads a creature's present armor-training state without changing it. It reports absent or invalid state as diagnostics and does not infer any training or armor result.

## Matches

inspect armor training
read armor training diagnostics

## Requirements

```json
{"roles":{"subject":{"components":["dnd2024.armor-training"],"description":"The creature whose present, absent, or malformed armor-training state is reported."}}}
```
