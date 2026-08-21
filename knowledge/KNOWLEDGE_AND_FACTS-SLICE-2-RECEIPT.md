# Knowledge and facts — Slice 2 receipt

Completed: 2026-08-21

## Delivered

- `game.core.world.interaction` and `game.core.world.knowledge.acquisition` catalog components
  with closed schemas and the approved relationship vocabulary.
- `IKnowledgeAcquisitionCoordinator` plus its data-access implementation, registered in host DI.
  It validates active world/knowledge/source constraints, writes interaction and acquisitions in one
  transaction, rejects source-triple duplicates, and makes exact replays idempotent.
- Monotonic current-state integration: a new acquisition can strengthen an actor's explicit state;
  it cannot replace `known` with weaker learning. Corrections remain explicit Slice 1 writes.
- Oren conversation fixture demonstrating one learner gains a personal `believed` state without
  teaching other participants.
- Focused tests covering fixture behavior, replay, monotonic state, and pre-write rejection.

## Verification

- `roleplay validate catalog` passed: 355 records, 80 mechanics, 100 procedures, 73 components,
  12 event types, 2 subscriptions, and 88 entities. It reported 56 existing near-duplicate
  warnings and touched no live data.
- The focused test command could not reach discovery because unrelated in-progress code currently
  fails the shared test build: `DantesRoleplay.DataAccess/ActionRunner.cs(250)` references missing
  `MergeChildProposals`. The Slice 2 test source is present but requires that unrelated compile
  failure to be resolved before execution.

## Explicit exclusions retained

No player/public MCP query surface, authorization policy, generic dialogue/combat/exploration
engine, transcript storage, world clock, event-ledger inference, vector search, or Ollama tool
orchestration was added in this slice.
