---
id: procedure.game.core.world.reactive
category: game.core.world.reactive
name: React to one committed world change
governs: reaction mechanics and subscriptions that turn accepted world events into bounded world consequences
status: active
---

## Description

Defines the fixture-bound consequence of the Lantern Compact agenda advancing: reveal Oren's
unsent letter once, inside the same committed event chain.

## Instructions

1. Register only the fixed active reaction subscription for the existing faction replacement
   event, with the fixed Oren-letter clue role, faction tracking, scalar entity/component filters,
   order zero, and one execution per chain.
2. Let the reaction inspect the committed before/after agenda values and return no effects for an
   ordinary nonmatch. It may replace only a valid unrevealed GM-only fixed clue.

## Constraints

- Only `world.component.replaced` for `faction.feature-03.fixture` and
  `game.core.world.faction` may route this reaction.
- The reaction does not write the agenda, secret, fact, rumour, relationships, or an event. It may
  replace only the fixed clue from unrevealed/gm to revealed/party.
- Corrupt fixed clue state aborts the root transaction; ordinary nonmatches return no effects.
- No generic reaction authoring, scheduling, notifications, campaigns, quests, or new event type
  belongs here.
