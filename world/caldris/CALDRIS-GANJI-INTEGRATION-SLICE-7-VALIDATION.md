# Caldris Slice 7 validation — Ganji narrative integration

Status: **blocked without live changes**
Date: 2026-08-30

## Repository result

- Added `CALDRIS-GANJI-CHARACTER-INTEGRATION.md` with Ganji's approved narrative foundation, the
  Tensor Sect, two locations, three personal hooks, and the opening-campaign connection.
- Added `runtime/caldris-ganji-integration-v7.json` with eight additive entities, twelve governed
  relationships, closed component values, and no D&D rule or played outcome.
- Reserved no playable actor. `actor.caldris.ganji` remains absent so the D&D basic-character
  mechanic can later create the actor and campaign participation atomically.

## Live plugin evidence

- The personal plugin host was started at its configured loopback MCP endpoint.
- The live database returned Caldris, *The Measure of Mercy*, Highmead, the Button Hills, and the
  Caldris atlas through the application ECS read surface.
- All eight proposed entity IDs were absent before preview.
- The complete manifest preview was rejected with `WORLD_SCOPE_TOO_LARGE`, operation
  `773527a71fff4050912d695e71a67300`; no state changed.
- A smaller Caldris-root World package was rejected with the same bound, operation
  `22ee17b296274831950cdd7e8e35b597`; no state changed.
- Alderwick- and Campaign-root previews were rejected with `WORLD_SCOPE_INVALID` because required
  relationship endpoints were outside the selected root, operations
  `33056df6e0ec467bb31986e164605447` and `0874faf82e8b47f3b348459119496fa6`;
  no state changed.

## Retained gates

Live import requires a separately approved platform repair for bounded additive authoring beneath a
mature World root. Ganji's executable character additionally requires the player's species
representation, SRD background, Standard Array placement, languages, Monk tool choice, and starting
equipment choice. Neither gate is silently guessed or bypassed.
