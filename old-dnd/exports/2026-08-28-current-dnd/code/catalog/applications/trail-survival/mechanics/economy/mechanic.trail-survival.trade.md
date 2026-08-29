---
id: mechanic.trail-survival.trade
category: trail-survival.economy
name: Trade Trail Survival resources
scope: ""
status: active
---

## Description

Buys or sells one scenario-defined resource at the current landmark while enforcing funds, stock,
cargo capacity, phase, pin, and deterministic action-seed state.

## Matches
buy trail supplies
sell trail supplies
trade resources

## Requirements
```json
{"roles":{"run":{"components":["trail-survival.scenario-pin","trail-survival.run","trail-survival.route-progress","trail-survival.pending-choice","trail-survival.outcome","trail-survival.resources"],"includeContents":true,"contentsDepth":2,"contentComponentIds":["trail-survival.party","trail-survival.resources","trail-survival.member","trail-survival.conveyance"],"componentReferences":[{"sourceComponentId":"trail-survival.scenario-pin","field":"scenarioId","targetComponentIds":["trail-survival.scenario"]}],"description":"The canonical run root and its bounded party graph."}}}
```
