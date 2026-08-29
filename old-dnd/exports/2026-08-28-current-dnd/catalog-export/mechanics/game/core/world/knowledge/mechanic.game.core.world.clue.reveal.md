---
id: mechanic.game.core.world.clue.reveal
category: game.core.world.knowledge
name: Reveal one scoped world clue
scope: ""
status: active
createdBy: "seed"
changeNote: "Seeded from the embedded catalog mechanic."
---

## Description
Reveals an unrevealed GM-only clue after proving its stored world-root scope. It changes only the
clue's status and descriptive visibility.

## Matches
reveal a clue
show the ledger seal clue
discover a world clue

## Requirements
```json
{"roles":{"clue":{"components":["game.core.world.clue"],"includeRelationships":true,"description":"The scoped unrevealed clue to reveal."},"world":{"components":["game.core.world.root"],"description":"The world root the clue must already be scoped to."}}}
```

