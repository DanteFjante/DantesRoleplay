---
id: mechanic.game.core.world.rumour.confirm
category: game.core.world.knowledge
name: Confirm one scoped world rumour
scope: ""
status: active
---

## Description

Confirms an unconfirmed rumour after proving its stored world-root scope. It changes only the
rumour's explicit resolution state.

## Matches
confirm a rumour
confirm the observatory signal
verify a world rumour

## Requirements
```json
{"roles":{"rumour":{"components":["game.core.world.rumour"],"includeRelationships":true,"description":"The scoped unconfirmed rumour to confirm."},"world":{"components":["game.core.world.root"],"description":"The world root the rumour must already be scoped to."}}}
```
