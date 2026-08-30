---
id: mechanic.game.core.world.clock.advance
category: game.core.world.time
name: Advance one world clock
scope: ""
status: active
createdBy: "seed"
changeNote: "Re-seeded: the embedded catalog mechanic changed."
---

## Description
Advances one active world root clock by a closed positive minute input. It changes only the clock.

## Matches
advance world time
advance the clock
pass 60 minutes

## Requirements
```json
{"roles":{"world":{"components":["game.core.world.root","game.core.world.clock"],"description":"The active root whose only clock may advance."}}}
```

