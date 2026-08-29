---
id: mechanic.trail-survival.run.create
category: trail-survival.run
name: Create a Trail Survival run
scope: ""
status: active
---

## Description

Creates one complete run, party, member set, conveyance, and canonical starting state derived from
one immutable scenario component and a recorded deterministic seed.

## Matches
create a trail survival run
start a new journey
set up my party

## Requirements
```json
{"roles":{"scenario":{"components":["trail-survival.scenario","trail-survival.scenario-pin","trail-survival.run","trail-survival.clock","trail-survival.route-progress","trail-survival.party","trail-survival.member","trail-survival.conveyance","trail-survival.resources","trail-survival.policy"],"description":"The immutable scenario entity selected for the new run; the remaining declared types bound the setup effect vocabulary."}}}
```
