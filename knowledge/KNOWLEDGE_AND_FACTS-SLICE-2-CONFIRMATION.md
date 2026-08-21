# Knowledge and facts — Slice 2 interaction-acquisition confirmation

Status: **Approved by the user and implemented as Slice 2 on 2026-08-21**  
Date: 2026-08-21

## Why this is a new boundary

Slice 1 deliberately did not invent an interaction source. The repository currently has no durable
gameplay interaction/discovery owner:

- campaign-session records are explicitly session evidence, not gameplay consequences;
- the event ledger records structural mutations but has no closed interaction participant, outcome,
  or knowledge-source contract;
- clue reveal and rumour confirmation own their limited existing transitions and cannot be
  reinterpreted as a general learning system.

An acquisition must refer to a real, durable source. Attaching it to any of those existing records
would produce misleading provenance and make replay/idempotency rules ambiguous.

## Smallest safe proposal

Add a minimal generic interaction record now, without trying to create a dialogue, combat, quest,
or exploration subsystem. It only provides a durable, canonical consequence owner for actions that
teach knowledge.

### Proposed permanent vocabulary

| ID | Shape | Closed purpose |
| --- | --- | --- |
| `game.core.world.interaction` | component on a new interaction entity | `{kind, status, summary}`; `kind` is `conversation`, `observation`, `document`, `discovery`, or `other`; status is `accepted` or `void`. |
| `game.core.world.interaction.in-world` | interaction → world relationship | Exact `{}`. One active world root, matching all participants and acquisitions. |
| `game.core.world.interaction.participant` | interaction → actor/entity relationship | Exact `{}`. Participants are evidence of involvement, not a claim that each learned every result. |
| `game.core.world.knowledge.acquisition` | component on a new acquisition entity | `{method, resultingState}`; method is `observed`, `told`, `read`, `inferred`, `taught`, or `recalled`; resulting state uses Slice 1's seven-state vocabulary. |
| `game.core.world.knowledge.acquisition.in-world` | acquisition → world relationship | Exact `{}`. |
| `game.core.world.knowledge.acquisition.knower` | acquisition → actor relationship | Exact `{}`. One knower. |
| `game.core.world.knowledge.acquisition.knowledge` | acquisition → fact/rumour/secret/clue relationship | Exact `{}`. One knowledge record. |
| `game.core.world.knowledge.acquisition.source` | acquisition → interaction relationship | Exact `{}`. One accepted interaction in the same world. |

`summary` is short, nonempty trusted-GM context, never a transcript. The interaction owns no
mechanical result beyond its own record. Existing systems may later specialize it or create it as
part of their accepted outcome.

## Required behavior

1. Record the accepted interaction and every acquisition in one atomic host operation. A failed
   acquisition rolls back the interaction creation/update for that operation.
2. The source link is the idempotency key: exactly one acquisition may exist for the same
   `(source interaction, knower, knowledge)` triple. Replaying the source returns that record and
   does not create another event or overwrite it.
3. The coordinator creates or strengthens the corresponding Slice 1 explicit actor state in the
   same operation. It must not replace `known` with a weaker result such as `familiar`, `suspected`,
   or `unknown`. A later correction/forgetting path is a separate, explicit operation.
4. A source interaction's participants do not automatically receive any knowledge. Each learner
   needs its own acquisition.
5. The knowledge record and all source/knower/world endpoints must exist and belong to the same
   world. The source must be `accepted`; `void` sources cannot teach knowledge.
6. The acquisition is provenance/audit data; it does not decide authorization or alter whether a
   rumour, fact, secret, or clue is canonically true.

## Deliberate exclusions

- no public MCP, player, or character query surface;
- no world clock, authored timestamps, transcript storage, or automatic clue reveal;
- no inferred interaction from arbitrary event-ledger rows;
- no automatic acquisition for all conversation participants;
- no generic interaction execution engine, branching dialogue, social mechanics, or migrations.

## Slice 2 acceptance fixture

An accepted `conversation` interaction between Oren and one outsider teaches the outsider the
existing Oren correspondence secret with resulting state `believed`. The outsider changes from
derived `unknown` to explicit `believed`; no other actor's state changes; replaying the same source
does not duplicate the acquisition; a different source may create a later, stronger acquisition.

## Confirmation requested

The user approved this boundary. Slice 2 now implements the listed component and relationship IDs,
the trusted-host atomic coordinator, the Oren-to-learner fixture, focused tests, and catalog
validation. It remains deliberately outside public MCP/player access and generic interaction play.
