---
id: mechanic.trail-survival.forage
category: trail-survival.turn
name: Forage for Trail Survival food
scope: ""
status: active
---

## Description

Uses the recorded action seed to derive a bounded food yield, consumes scenario-defined time, and
enforces cargo capacity.

## Matches
forage for food
gather trail supplies

## Requirements
```json
{"roles":{"run":{"components":["trail-survival.scenario-pin","trail-survival.run","trail-survival.clock","trail-survival.pending-choice","trail-survival.outcome"],"includeContents":true,"contentsDepth":2,"contentComponentIds":["trail-survival.party","trail-survival.resources","trail-survival.member","trail-survival.conveyance"],"componentReferences":[{"sourceComponentId":"trail-survival.scenario-pin","field":"scenarioId","targetComponentIds":["trail-survival.scenario"]}],"description":"The canonical run root and its bounded party graph."}}}
```
