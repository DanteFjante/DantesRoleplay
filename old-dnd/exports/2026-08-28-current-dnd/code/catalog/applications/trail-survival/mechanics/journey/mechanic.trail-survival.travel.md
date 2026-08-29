---
id: mechanic.trail-survival.travel
category: trail-survival.journey
name: Travel one Trail Survival turn
scope: ""
status: active
---

## Description

Advances one authored route leg, applying policy-derived food, health, wear, time, deterministic
event draw, landmark arrival, and terminal outcome in one root transaction.

## Matches
travel one turn
continue the journey
take the next trail leg

## Requirements
```json
{"roles":{"run":{"components":["trail-survival.scenario-pin","trail-survival.run","trail-survival.clock","trail-survival.route-progress","trail-survival.policy","trail-survival.pending-choice","trail-survival.outcome"],"includeContents":true,"contentsDepth":2,"contentComponentIds":["trail-survival.party","trail-survival.resources","trail-survival.member","trail-survival.conveyance"],"componentReferences":[{"sourceComponentId":"trail-survival.scenario-pin","field":"scenarioId","targetComponentIds":["trail-survival.scenario"]}],"description":"The canonical run root and its bounded party graph."}}}
```
