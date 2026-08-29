---
id: mechanic.trail-survival.policy.set
category: trail-survival.policy
name: Set Trail Survival travel policy
scope: ""
status: active
---

## Description

Selects one scenario-authored pace and ration policy for an active run.

## Matches
set travel policy
change pace and rations

## Requirements
```json
{"roles":{"run":{"components":["trail-survival.scenario-pin","trail-survival.run","trail-survival.policy","trail-survival.pending-choice","trail-survival.outcome"],"componentReferences":[{"sourceComponentId":"trail-survival.scenario-pin","field":"scenarioId","targetComponentIds":["trail-survival.scenario"]}],"description":"The canonical run root."}}}
```
