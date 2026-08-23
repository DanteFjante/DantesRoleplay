# Generic information Slice 1 implementation — local grounded answer

Status: **active**
Owner/roadmap: `knowledge/KNOWLEDGE_AND_FACTS_PLAN.md`
Dependency tree/leaf: `knowledge/generic-information/GENERIC_INFORMATION_DEPENDENCY_TREE.md` / generic grounded answer
Ruleset alignment: **ruleset-neutral**
Source ID and locator: Not applicable.

## Outcome and boundary

Implement a generic, non-game information source and record store with bounded local grounded
answers plus declared action contracts. The MCP surface gains `information-source`,
`information-record`, `information-answer`, `information-action-contract`,
`information-actions`, and `information-action`. Every read or execution is restricted to an
authorized generic namespace and every answer cites only host-selected records.

Excluded: campaign/world integration, game mechanics, file/web connectors, external identity,
model-authored writes, arbitrary tool execution, and replacing `knowledge-answer`.

Allowed files/areas: generic domain/retrieval contracts, DataAccess entities/store/migration and
registrations, MCP configuration/tools/surface, generic catalog procedures, and focused tests.
Stop point: local sources, records, and action contracts can be committed, queried, and executed
through the three existing verbs on a fresh host with no campaign state.

## Confirmed decisions

- User approved neutral persistence, public generic kinds, and a fixed local information-scope
  policy on 2026-08-22.
- IDs/kinds: `information-source`, `information-record`, `information-answer`,
  `information-action-contract`, `information-actions`, and `information-action`.
- A scope is generic ownership/access context, never a campaign requirement.
- The user confirmed hierarchical selectors such as `game.worldname.*` and explicit action
  contracts whose registered executor runs the permitted action.

## Prerequisite evidence

- Generic kernel boundary: `ARCHITECTURE.md`.
- Current campaign-specific adapter: `DantesRoleplay.DataAccess/AuthorizedKnowledgeCandidateResolver.cs`.
- Completion boundaries: `DantesRoleplay.DataAccess/KnowledgeFactAnswerCoordinator.cs`.

## Runtime artifacts

- New neutral tables and migration: information source, record, and action contract.
- New source/record store and generic scope policy interface.
- New query kinds `information-answer` and `information-actions`; commit kinds
  `information-source`, `information-record`, `information-action-contract`, and
  `information-action`.
- New procedures `procedure.information.manage`, `procedure.information.answer`, and
  `procedure.information.action`.

## Authoritative state and closed input

`information-source` accepts `{id, scopeId, name, description?, metadataSchema?}`.
`information-record` accepts `{id, sourceId, title, content, metadata?}`.
`information-answer` accepts `{scopeId, question, sourceIds?, limit?}`.
`information-action-contract` accepts `{id, scopeId, name, description?, executorId, inputSchema,
ruleRecordIds?}`. `information-action` accepts `{scopeId, contractId, input}`.

IDs, scope IDs/selectors, text bounds, metadata JSON-object shape, action input schema, source
ownership, content hash, and revision are validated by the host. A caller cannot select records or
contracts outside its requested namespace or claim authorization. The local policy resolves one
configured development selector.

## Behavior, result, and transactions

Source, record, and contract writes are single-store transactions. Repeating identical content is
idempotent; changed content advances its revision. Retrieval resolves namespace policy before
reading records, searches bounded records only in that namespace, and passes only candidate
text/IDs/revisions to the model. The model's citations must name exactly supplied records. Action
execution resolves the same policy, validates its declared JSON Schema, then dispatches only to its
registered executor.

## Failure, replay, and rollback contract

Malformed payload, duplicate/missing source, invalid selector, denied namespace, malformed
metadata/schema, unknown citation, unavailable executor, and stale source/record state return
stable failures or unknown answers without partial writes. No answer invokes effects; only an
explicit `information-action` commit can execute a declared contract.

## Implementation sequence

1. Add neutral contracts, persistence, and migration.
2. Register fixed local policy and generic answer services.
3. Add hierarchical selectors, contracts, and the action-runner adapter.
4. Add governed MCP handlers and catalog procedures.
5. Add unit/protocol coverage for non-game operation, namespace isolation, and contracts.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Non-game happy path | Source/record write then cited answer in a fresh database |
| Scope isolation | Policy denial before record read; wrong scope returns no candidate data |
| Grounding | Invented/uncited model records are rejected to unknown |
| Replay | Identical source/record payloads do not create duplicate state |
| Compatibility | Existing campaign `knowledge-answer` behavior remains covered |
| Contract execution | Wrong namespace/schema does not invoke the executor |
| Action execution | A declared contract delegates only to its named registered executor |
| Surface | Guard test proves all new kinds are advertised and dispatched |

## Verification commands

`dotnet test --filter FullyQualifiedName~Information`

`roleplay validate catalog`

`dotnet test`

## Completion receipt and exit gate

Write `GENERIC_INFORMATION-SLICE-1-IMPLEMENTATION-RECEIPT.md` after the focused tests, catalog
validation, full suite, and protocol walk pass. Stop after this local-host slice.
