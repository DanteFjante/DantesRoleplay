# Knowledge and facts — Slice 5C receipt

Completed: 2026-08-21

## Delivered

- Internal trusted-GM Mode B orchestration. A first schema-bound `qwen3:8b` call may propose at
  most two short search phrases from the original question. The host executes only the closed
  `knowledge.search` operation, with the original world, fact-kind, subject, and as-of filters.
- The complete read chain is bounded to three searches and twenty distinct candidates. The first
  canonical result freezes the as-of minute for subsequent searches. Candidate records are supplied
  only to the final answer call, so untrusted fact text cannot trigger another read.
- The final answer retains Slice 5B's exact citation, epistemic-kind, selected-ID, model-identity,
  token, and fallback checks. Unsafe search plans, changed model identity, invented citations,
  malformed output, unavailable Ollama, and empty retrieval all return deterministic candidates and
  `unknown`.
- Internal Mode C action routing over existing stores. The host retrieves bounded registered active
  mechanics and procedures, parses declared mechanic requirements, and gives the model summaries
  but no source code, entity data, input contents, native tools, or write capability.
- A model may affirm only the deterministic first active mechanic and supplied procedure IDs. The
  host rereads the current ranking, versions, statuses, and source hashes before producing a route.
- The host copies the caller's intent, role/entity map, input JSON, scope, and selected procedure IDs
  into one reviewable action proposal. The existing projection resolver validates those values
  read-only. Missing or unknown roles produce `needs-input`; the action runner is never invoked.

## Safety boundary

- Ollama receives no tool definitions. Mode B tool chaining is a host-owned closed protocol, not
  arbitrary model tool calling.
- No shell, SQL, network URL, arbitrary ID lookup, write operation, action execution, retry token,
  or server-held interaction session is available to the model.
- Candidate text and caller intent are explicitly treated as untrusted data. Generated search
  phrases containing command/tool/URL/SQL markers or dotted fact/secret IDs are rejected.
- There is no verified executable-workflow registry, so Mode C reports `workflowsAvailable=false`
  and cannot propose a workflow.

## Verification

- Focused tests cover read budgets, search scoping, unsafe generated queries, candidate-only
  citations, model-identity drift, invented mechanics/procedures, missing roles, stale definitions,
  exact caller-value preservation, and dependency registration.
- Live tests exercise both the two-call knowledge chain and one-action route selection against the
  installed `qwen3:8b` model.
- The focused Slice 5C matrix passed 11/11 with live Ollama enabled. Combined with the MCP protocol
  walk, the acceptance matrix passed 17/17.
- The solution builds with zero errors. Its only warning is the pre-existing xUnit analyzer warning
  in `KnowledgeAcquisitionCoordinatorTests`; Slice 5C adds no analyzer warning.
- After concurrent feedback-retention work settled, the complete repository suite passed 710/710
  in 1 minute 6 seconds, including catalog coverage and migration atomicity checks.
- No catalog record, database schema, migration, or public MCP surface changed.

## Explicit exclusions retained

This slice does not add `query(kind: "route")`, a player/character knowledge surface, workflow
registration or execution, action execution, model-authored role/entity IDs, or canonical writes.
The planned procedure contracts should be added only with the public integration that they govern;
adding them now would advertise an operation that does not exist.
