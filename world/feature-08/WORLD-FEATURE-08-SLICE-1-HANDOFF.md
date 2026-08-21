# World Feature 8 — Slice 1 implementation handoff

**Assignment ID:** world-feature-08-slice-1-route-foundation  
**Status:** Complete and awaiting review  
**Owning plan:** [World Feature 8 dependency plan](WORLD-FEATURE-08-DEPENDENCY-PLAN.md)  
**Exact slice:** Closed route data, directed route links, the 30-minute gate-to-market fixture, and
governing contract revisions.  
**Stop point:** Evidence is in the [Slice 1 receipt](WORLD-FEATURE-08-SLICE-1-RECEIPT.md); do not
add the journey mechanic before Slice 2.

## Confirmed contract

- `game.core.world.route` is a closed active/archived on-foot route with descriptive visibility and
  1–1,440 declared duration minutes.
- `route.feature-08.gate-to-market-on-foot` is an active 30-minute route from the existing gate to
  the existing market. Its world, origin, and destination are three directed empty-data links.
- Containment remains current location, canonical adjacency remains topology, and Feature 2 local
  movement stays route-free and time-free.

## Required Slice 2 boundary

Implement only `mechanic.game.core.world.route.travel-on-foot`: validate the five confirmed roles,
then atomically move the traveller and replace the scoped root clock from the stored duration. Do
not add a route table, new MCP surface, a semantic event, reverse inference, distance, geometry,
or player policy.
