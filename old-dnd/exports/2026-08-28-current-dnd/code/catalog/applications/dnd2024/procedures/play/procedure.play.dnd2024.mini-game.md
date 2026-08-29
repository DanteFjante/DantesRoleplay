---
id: procedure.play.dnd2024.mini-game
category: ruleset.dnd2024.play
name: Run the D&D 2024 minimum-playable chat loop
governs: D&D 2024 chat-host intent routing and DM response behavior
status: active
---

## Description

Provides the chat-facing play contract for the D&D 2024 MVP. The Player describes an intent in
natural language. The Dungeon Master resolves uncertainty through the existing action pipeline,
then narrates only the verified consequence. This procedure adds no game state and no alternate
mutation path.

## Instructions

1. At the beginning of a conversation, identify the active campaign and actor from stored state.
   If no character exists, use `mechanic.dnd2024.character.basic.create` through
   `procedure.action.run` and report the resulting compact character summary.
2. Preserve the Player's declared intent. Free actions and narration need no roll. For uncertainty,
   search the D&D mechanics catalog with the Player's words, inspect the selected mechanic's
   requirements, and ask only for missing role entities or a necessary choice.
3. Run a selected rule with `commit(kind: "action")`, supplying the exact `intent`, declared
   `roleEntityIds`, and closed mechanic `input`. Do not supply derived scores, modifiers, DCs,
   outcomes, component payloads, effects, or hidden facts as caller authority.
4. For combat, use the established Initiative and turn lifecycle before resolving attacks. Use the
   current turn budget and advance the turn only after the Player's action has been resolved.
5. Treat the action result as authoritative. Narrate its `narration` and visible `data`; never add
   an unproposed damage, movement, condition, item, resource, discovery, or reward consequence.
6. End each response at the next Player decision. The DM may speak for monsters and NPCs, but may
   not choose a voluntary Player action, dialogue, belief, or plan.
7. After a state-changing action, optionally read back the affected entity or campaign view so the
   next response starts from durable state rather than transcript memory. Exact action replay must
   return the prior result without applying effects twice.

## Minimum-playable boundary

Supported rules include level-1 character creation, ability checks, saving throws, Initiative,
turns, movement budgets, weapon attacks, damage, healing, Hit Points, rests, inventory, currency,
and replay/rollback. If a request needs an unimplemented feature, state that boundary plainly and
offer a supported alternative; do not silently invent a rule or state owner.

## Constraints

- This procedure is chat guidance only. It creates no entities, components, relationships, events,
  notifications, mechanics, public operations, or narrative archive.
- The web companion is a projection and never the authority for play state.
- Rules selection is by the Player's intent and active catalog records, not by a guessed mechanic ID.
- GM-only information stays out of Player-facing narration.
- Do not use administrative component/effect commits to bypass a D&D mechanic.
