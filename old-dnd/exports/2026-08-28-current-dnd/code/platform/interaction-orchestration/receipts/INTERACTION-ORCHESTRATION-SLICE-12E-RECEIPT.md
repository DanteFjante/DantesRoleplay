# Interaction orchestration Slice 12E receipt — bounded symmetric planners

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 12E](../INTERACTION-ORCHESTRATION-SLICE-12E-IMPLEMENTATION.md)

## Delivered

- Added one strict ruleset-neutral planner protocol. Each completion round may return only a
  trusted-feature search, exact prior-candidate inspection, inert proposal draft, or the closed
  model-selectable `needs-input`, `ambiguous`, or `unknown` result. Unknown properties, tools,
  authority fields, non-object input, oversized values, and forbidden statuses fail closed.
- Added one bounded server-mediated coordinator for local and remote providers. It freshly checks
  plan authorization, fixes the trusted application/lane, enforces round/search/inspection/
  candidate/byte/elapsed limits, supplies bounded search summaries and exact inspected contracts,
  and never silently switches provider or executes an action.
- Added one current-authority verifier shared by both provider paths and future remote submission.
  It rechecks application revision/base order, active effective-set fingerprint, current trusted
  snapshot, prior exact inspection, record status/version/hash, inert DAG/state revision, mechanic
  kind, generic declared roles, and object input. It reconstructs authoritative proposal contracts
  itself rather than trusting model-authored references.
- Added the fixed internal task class `system.interaction.planner-step`, response schema name
  `interaction_planner_step_v1`, and trace domain
  `dantes-roleplay/interaction-planner-trace/v1`. The local adapter reuses the existing no-tools
  structured-completion port and reports its real Ollama identity rather than Luna.
- Added a separate OpenAI Responses adapter for remote planning. Inner is fixed to
  `gpt-5.6-luna`/`low`, outer to `gpt-5.6-luna`/`high`; requests use strict JSON-schema output,
  `tools: []`, `tool_choice: none`, disabled parallel tools, `store: false`, a bounded response,
  no redirects, and an exact HTTPS endpoint. The credential is header-only, environment/config
  supplied, disabled by default, and absent from checked-in settings, observations, receipts, and
  response bodies.
- Kept the private operator `codex-bridge` unchanged and ineligible for product planning. Its
  repository/command/file/MCP/approval capabilities do not cross the new provider boundary.
- Appended every terminal planning outcome through the accepted Slice 12D receipt store. Receipts
  retain a trace fingerprint, fixed role, actual planner/provider/model/revision/profile/effort,
  bounded usage and safe codes; they do not store intent/query text, prompts, completions,
  reasoning, contract bodies, proposal JSON, effects, paths, credentials, or state projections.
  A replay whose stored status/code/proposal fingerprint differs is treated as an idempotency
  conflict and the new proposal is not accepted.
- Added no action/query execution, public MCP kind/route, web component, assistant-conversation
  schema, recipe, catalog/game content, migration, operation write, or live-database change.
  Query/procedure/system-contract execution remains explicitly unsupported until a later confirmed
  trusted resolver exists.

## Review findings closed

- Separated initiating product role from actual planner identity so a local model is never
  misreported as Luna and remote model/effort cannot be caller-selected.
- Allowed ordinary non-visible Responses reasoning items while rejecting every tool/non-message
  output; no reasoning content is retained.
- Reserved receipt evidence capacity for server-derived planner identity and usage even when a
  model returns its maximum safe evidence list.
- Added fail-closed handling for duplicate provider composition, recipe/provider exceptions,
  malformed programmatic drafts, cancellation/timeout, stale activation/catalog records, and
  replay disagreement.
- Included bounded search names/descriptions in model observations so selection does not depend on
  qualified IDs alone, while contract bodies remain inspection-only.

## Evidence

- Focused planner/verifier/provider/receipt/contract/guard checks: 55 passed, 0 failed. The dedicated
  planner class contributes 14 cases, including local/remote fingerprint parity, every closed
  planner outcome class, fresh authorization, isolation, forged/stale/missing-role references,
  search limits, cancellation, replay conflict, local task isolation, and exact remote HTTP shape.
- Full shared suite: 753 passed, 0 failed.
- Standalone local-AI suite: 20 passed, 0 failed.
- Disposable isolated-output solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed; only existing CRLF conversion notices were emitted.
- Static boundary searches found no action/application evaluator, ECS-effect, operation-log, Codex
  bridge, game-specific, or local-AI reverse dependency in the new runtime path. Test-only shell
  strings are rejection fixtures; test credentials are synthetic and never checked into settings.
- Catalog validation and protocol walk were not required because no catalog or public protocol
  artifact changed. No normal live database, migration, real Ollama model, or real OpenAI network
  call was used.

## Deliberate exclusions and next gate

This receipt accepts internal planning and proposal verification only. A receipt fingerprint alone
does not authorize or reconstruct execution. Slice 12F is next and requires a separate confirmed
implementation document for public plan/receipt/execute kinds, application conversations and
reusable outer UI, exact two-phase consent, current revalidation, query/action execution adapters,
idempotency/partial-progress behavior, authorization, and protocol evidence.
