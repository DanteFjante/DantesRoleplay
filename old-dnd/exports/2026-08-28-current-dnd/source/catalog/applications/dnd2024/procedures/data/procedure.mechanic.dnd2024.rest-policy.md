---
id: procedure.mechanic.dnd2024.rest-policy
category: ruleset.dnd2024.core.data.rest-policy
name: Define immutable D&D 2024 standard-rest policy
governs: dnd2024.rest-policy; content.dnd2024.rest-policy.standard.v1
status: active
---

## Description

Defines immutable source-cited data for the standard D&D 2024 Short and Long Rest rules. A policy
is a versioned definition, not actor rest state, timekeeping, recovery, or resource reset.

## Instructions

1. Record the policy only on `content.dnd2024.rest-policy.standard.v1`. Its key, version, source,
   rest timings, interruption lists, activity limits, and benefit handoff vocabularies are
   write-once. Correction requires a separately reviewed successor policy.
2. Short Rest declares 60 minutes, at least 1 current Hit Point, light activity, interruptions by
   Initiative, a non-Cantrip spell, or damage, and only the `spend-hit-point-dice` and
   `source-specific-recharge` handoffs.
3. Long Rest declares 480 minutes with at least 360 asleep and no more than 120 of light activity;
   at least 1 current Hit Point; a 960-minute restart wait; Short Rest credit after 60 minutes; 60
   additional minutes per resumed interruption; the four source interruptions; and only its exact
   recovery/recharge handoff vocabulary.

## Constraints

- Handoff labels are not executable effects or permission to modify Hit Points, Hit Point Dice,
  maximums, ability scores, Exhaustion, resources, items, or features.
- Do not attach the policy to an actor, campaign, encounter, clock, event, subscription, or action.
  Do not add an episode, interruption assertion, timer, recovery amount, or arbitrary payload.
