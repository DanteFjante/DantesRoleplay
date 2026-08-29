---
id: mechanic.trail-survival.event.choose
category: trail-survival.event
name: Resolve a Trail Survival event choice
scope: ""
status: active
---

## Description

Resolves one offered scenario-authored event choice, derives its complete bounded state changes,
clears pending state, and stores any resulting terminal outcome.

## Matches
choose an event option
resolve the pending trail event

## Requirements
```json
{"roles":{"run":{"components":["trail-survival.scenario-pin","trail-survival.run","trail-survival.clock","trail-survival.pending-choice","trail-survival.outcome"],"includeContents":true,"contentsDepth":2,"contentComponentIds":["trail-survival.party","trail-survival.resources","trail-survival.member","trail-survival.conveyance"],"componentReferences":[{"sourceComponentId":"trail-survival.scenario-pin","field":"scenarioId","targetComponentIds":["trail-survival.scenario"]}],"description":"The canonical run root and its bounded party graph."}}}
```
