---
id: mechanic.trail-survival.rest
category: trail-survival.turn
name: Rest the Trail Survival party
scope: ""
status: active
---

## Description

Consumes scenario-derived food and time and heals each living party member within the scenario
health bound.

## Matches
rest the party
take a trail rest

## Requirements
```json
{"roles":{"run":{"components":["trail-survival.scenario-pin","trail-survival.run","trail-survival.clock","trail-survival.pending-choice","trail-survival.outcome"],"includeContents":true,"contentsDepth":2,"contentComponentIds":["trail-survival.party","trail-survival.resources","trail-survival.member","trail-survival.conveyance"],"componentReferences":[{"sourceComponentId":"trail-survival.scenario-pin","field":"scenarioId","targetComponentIds":["trail-survival.scenario"]}],"description":"The canonical run root and its bounded party graph."}}}
```
