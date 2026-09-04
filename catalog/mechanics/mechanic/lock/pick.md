---
id: mechanic.lock.pick
category: check
name: Pick a lock
status: deprecated
createdBy: "llm"
changeNote: "Added during assisted cold-walk rehearsal for reusable lock-picking resolution."
---

## Description
Resolves an attempt to pick any lock by rolling against the target lock's stored difficulty using the subject's agility.

## Matches
pick a lock
pick the lock
unlock a lock
open a locked door

## Requirements
```json
{"roles":{"subject":{"components":["fixture.legacy.stats"],"description":"The entity attempting to pick the lock."},"lock":{"components":["fixture.legacy.lock"],"description":"The locked entity being opened."}}}
```
