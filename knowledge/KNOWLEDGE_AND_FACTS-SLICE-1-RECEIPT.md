# Knowledge and facts — Slice 1 receipt

Status: **Implemented and verified**  
Date: 2026-08-21

## Delivered

- Added the approved `game.core.world.knowledge.classification` companion component with closed
  subject-kind and descriptive-sensitivity fields.
- Added the approved baseline and explicit-state relationship vocabulary. Baselines are exact
  `current-scope` links from a world, region, or faction; actor states use the closed seven-state
  set and replace the actor's one current state for that knowledge record.
- Added the trusted-host `IKnowledgeStateCoordinator`, registered through DataAccess. It validates
  knowledge kind/classification/world scope, scope endpoints, region containment, faction membership,
  and relationship payloads before writes or reads.
- Added a deterministic trusted-GM effective-state reader: explicit actor state, faction baseline,
  containing-region baseline, world baseline, then derived `unknown`.
- Preserved existing fact/rumour/secret/clue payloads and reveal/confirmation behavior. `visibility`
  and descriptive sensitivity are not treated as authorization.

## Proof

- Six focused tests passed: legacy Feature 4 compatibility plus global knowledge with an explicit
  unknown exception, regional inheritance, faction inheritance, unknown outsiders, correction, and
  invalid state/scope rejection.
- `roleplay validate catalog` passed against a fresh migrated disposable database: 322 records,
  zero errors. Its 44 pre-existing near-duplicate warnings are unrelated to this slice.
- `DantesRoleplay.DataAccess` and the isolated focused-test build completed with zero warnings/errors.

## Deliberate exclusions

No acquisition history or interaction event, world-time validity, contradiction/supersession, FTS or
vector backfill, `qwen3:8b` answering/orchestration, player authorization, MCP tool, or migration was
added. Slice 2 must wait for an authoritative interaction owner before it writes durable acquisition
records.
