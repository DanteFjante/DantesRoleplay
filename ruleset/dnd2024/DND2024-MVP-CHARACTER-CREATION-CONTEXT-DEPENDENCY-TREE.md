# D&D 2024 MVP character-creation context dependency tree

Status: lowest leaf ready
Ruleset alignment: dnd2024-compatible
Source: not applicable; this is host binding and creation routing, not a D&D rule calculation.

## Outcome and non-goals

A configured player with no actor entity can begin character creation through the ordinary chat
loop. The same query must still deny an existing actor with invalid or inactive participation.

No caller-selected actor, player-seat record, endpoint, MCP verb, D&D component, or alternate
creation transaction is added.

## Existing owners and evidence

| Concern | Owner | State |
| --- | --- | --- |
| Host-selected player seat | `LocalKnowledgeAudiencePolicy` | verified |
| Active application/campaign binding | activated knowledge binding resolver | verified |
| Actor existence and participation | knowledge participation verifier | verified |
| Character creation transaction | `mechanic.dnd2024.character.basic.create` | verified |
| Chat context query | `system.audience-context` | verified |

## Dependency tree

```text
Player can create a first character [ready]
└─ audience context [verified]
   ├─ active actor participation -> bound play context [verified]
   ├─ actor is absent -> creation-required context [ready]
   └─ actor exists but participation is invalid -> denial [ready]
```

## Confirmed decision

The user confirmed this exact semantic extension on 2026-08-29. A missing configured actor returns
a creation-required context with the host-reserved ID; every other failed participation check stays
denied.

## Planning receipt

- Runtime artifacts created: none.
