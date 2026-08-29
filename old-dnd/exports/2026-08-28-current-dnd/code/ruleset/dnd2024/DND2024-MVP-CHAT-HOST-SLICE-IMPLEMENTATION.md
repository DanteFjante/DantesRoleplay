# D&D 2024 MVP Chat Host Slice

Status: accepted 2026-08-28

## Outcome

Define the D&D-specific chat-host contract for a minimum playable game and character creation.
Natural-language player messages are routed to existing D&D mechanics through the generic action
runner. The host then gives the DM response enough structure to narrate only authoritative results.

## Boundary

- In scope: intent routing guidance, creation/play entry points, ambiguity handling, refusal of
  unsupported rules, and DM narration guidance.
- Reuses: `procedure.action.run`, `procedure.play.storytelling`, and the accepted D&D 2024
  character, check, turn, combat, and item mechanics.
- Out of scope: a new parser, new MCP/public operation, new transaction path, C# rule logic,
  database migration, and the web companion.

## Authority

- Ruleset alignment: `dnd2024-compatible`.
- Source: `source.dnd2024.srd-5.2.1`; relevant areas are Character Creation, D20 Tests, and
  Combat.
- The new permanent procedure ID was explicitly confirmed by the user in this task.
- No separate Foundry implementation is applicable: this slice contains host routing and
  narration guidance, not a new game-rule calculation.

## Acceptance

1. A player can enter character creation or a supported play intent in natural language.
2. Missing choices or role entities are requested before execution; ambiguous intents are not
   guessed.
3. Mechanics run through `procedure.action.run` with closed input and `commit(kind:"action")`.
4. The DM response separates verified mechanical consequences from narration and preserves player
   agency.
5. Unsupported rules are stated as unsupported rather than silently invented.

## Verification

- `roleplay validate catalog` passes after adding the procedure.
- Existing D&D acceptance tests cover the underlying creation, check, turn, combat, replay, and
  failure paths; this slice adds no runtime implementation to those owners.

## Stop point

The repository now has the executable catalog contract. The next integration step is wiring a
conversational client to submit the procedure's action envelope; that client is outside this slice.
