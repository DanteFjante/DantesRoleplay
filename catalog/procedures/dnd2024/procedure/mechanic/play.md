---
id: dnd2024.procedure.mechanic.play
category: ruleset.dnd2024.core.play
name: Play an SRD 5.2.1 game
governs: A participant declaring what their adventurer attempts and receiving the result in a D&D ruleset session with scope "dnd2024-srd-5.2.1".
status: active
createdBy: "llm"
changeNote: "Moves the player-facing D\u0026D 2024 contract under ruleset.dnd2024.core.play, leaving room for campaign- or instance-specific overrides."
---

## Description
Player-facing session protocol for declaring an adventurer intent, supplying relevant character context, and receiving a narrated result. Non-goals: host adjudication, scene creation, and implementing game mechanics.

## Matches

## Instructions
1. Receive the current scene, known facts, and any applicable limitations from the host.
2. State what the adventurer tries to do in fiction; do not prescribe a rule, DC, die result, or consequence.
3. Identify the acting character and provide only relevant declared choices or resources. If a required character component or rule does not exist, ask the host to establish it rather than inventing it.
4. Read the component contract governing the applicable game mechanic before invoking an action. If no applicable mechanic exists, report that gap to the host; do not resolve it by narration alone.
5. Read the result returned by the resolved action, accept its recorded effects and narration, then state the next intended action or ask a clarifying question.
6. SRD explanation — SRD v5.2.1, Playing the Game, Rhythm of Play (PDF p. 5): play cycles through the host describing the scene, players describing what their characters do, and the host narrating results. The pattern applies across social interaction, exploration and combat; turn structure for combat is a later component.
7. Catalog location — this is the ruleset.dnd2024.core.play leaf. Until recursive catalog queries exist, locate it with an exact category filter or its permanent procedure id.
8. Verify a play step by confirming the action result or recording that no rule currently supports the attempted activity. Revision: preserve this player/host boundary unless a later contract explicitly replaces it.

## Constraints
- The player must not directly create or modify world data, choose a DC, select a mechanic, or apply effects; those are host or mechanic responsibilities.
- A player-declared intent must identify an existing actor before it can be resolved.
- A result must come from an existing scoped mechanic or be reported as unsupported; it must not be invented in narration.
- This contract creates no persistent data component and authorizes no game mechanic by itself.
- Any SRD explanation in this contract must name SRD v5.2.1 and its source section.
- This contract's category is ruleset.dnd2024.core.play; a revision must retain that primary purpose or move to a new contract id.
