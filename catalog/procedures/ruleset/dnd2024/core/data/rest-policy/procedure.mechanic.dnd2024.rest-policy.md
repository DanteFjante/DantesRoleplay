---
id: procedure.mechanic.dnd2024.rest-policy
category: ruleset.dnd2024.core.data.rest-policy
name: Define immutable D&D 2024 standard-rest policy
governs: catalog authoring of dnd2024.rest-policy on versioned standard-rest policy entities
status: active
---

## Description

Defines immutable source-cited data for the standard D&D 2024 Short and Long Rest rules. A policy
is a versioned definition, not an actor rest state, timekeeping operation, recovery transaction, or
resource reset.

## Instructions

1. Record the standard policy only on `content.dnd2024.rest-policy.standard.v1`. Its policy key,
   version, source reference, rest kinds, timings, interruption lists, activity limits, and benefit
   handoff vocabulary are write-once. A correction requires a distinct reviewed successor policy;
   never mutate a policy a future rest episode or receipt may cite.
2. The Short Rest declaration is one hour, requires at least 1 Hit Point, permits light activity,
   is interrupted by Initiative, a non-Cantrip spell, or damage, and declares only the ordered
   handoffs `spend-hit-die` then `source-specific-recharge`.
3. The Long Rest declaration is eight hours with at least six hours asleep and at most two hours of
   light activity; it requires at least 1 Hit Point, has a 16-hour restart wait, gives partial
   Short-Rest credit after one hour, adds one hour per interruption, and has the closed source
   interruption and ordered consequence-handoff vocabulary in the component schema.

## Constraints

- Policy benefit names are handoff labels, not executable effects or permission to modify Hit
  Points, Hit Point Dice, maximums, ability scores, Exhaustion, Temporary Hit Points, class
  resources, spell slots, items, or preparation. Their individual state owners remain authoritative.
- Do not attach the policy to an actor, campaign, encounter, class, item, feature, spell, clock,
  event, subscription, or action. Do not add a rest action, episode, scheduler, interruption
  assertion, timer, resource transition, or player-facing route in this slice.
- Do not store source prose, a selected rest kind, elapsed/remaining time, activity record, damage,
  spell, Initiative result, resource ID, Hit Die roll, recovery amount, event ID, or arbitrary
  payload. Feature 33 later owns authenticated rest timing and delegates each consequence.
