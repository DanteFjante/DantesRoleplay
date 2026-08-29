# World Feature 16 — Slice 1 receipt

Status: **verified**  
Date: 2026-08-20

## Delivered

- Added the trusted read-only `query(kind: "itinerary-plan")` surface and its governing
  `procedure.game.core.world.itinerary` contract.
- Added a state-aware deterministic planner over existing on-foot, selected ground, selected air,
  and fixed-portal owners. Selected conveyance location is modeled in the search state, so walking
  or teleporting cannot quietly carry it to a later leg.
- Returned closed terminal states, indexed legs, derived estimates, and a stable opaque fingerprint
  without any world mutation.

## Evidence

- Focused planner/protocol tests: **10 passed**.
- `roleplay validate catalog`: **158 records valid**, 26 advisory similarity warnings, no live data
  touched.
- Full suite including the required public-surface protocol walk: **446 passed**.
- `git diff --check`: W16 introduces no reported whitespace error; the existing dirty checkout
  reports CRLF conversion warnings and one unrelated trailing-whitespace line in
  `CHARACTER_CREATION_PLAN.md`.

## Next boundary

Slice 2 will accept one exact ready fingerprint and leg index, re-read state, invoke exactly the
existing mode owner for that leg, and return a freshly rebuilt itinerary. It will not batch or
directly mutate any later leg.
