# Interaction orchestration Slice 13E receipt — safe outer-fallback learning and promotion

Status: **accepted 2026-08-25**  
Implemented: **2026-08-25**  
Confirmed contract: [Slice 13E implementation](../INTERACTION-ORCHESTRATION-SLICE-13E-IMPLEMENTATION.md)

## Delivered boundary

- The existing explicit per-execution `learn` opt-in now evaluates deterministic automatic
  verification only after candidate append/replay.
- A new durable evidence reader proves the learning execution belongs to an immutable outer role,
  follows exactly one eligible correlated inner `unknown`/`unsupported`/`unavailable` receipt, uses
  the same principal/application/state/session/conversation/delegation/batch identity, completed
  every step, and retains valid operation audit links.
- Eligible candidates reuse the accepted append-only review service with fixed host principal
  `system.interaction.recipe-auto-verifier`, fixed reason, and execution-derived replay token.
  Direct outer, inner, incomplete, stale, or uncorrelated candidates remain inert and manually
  reviewable.
- The action-only v1 template now explicitly rejects result bindings in addition to queries and
  non-empty inputs. Stored templates retain exact current action references, dependency order, and
  role-slot names while discarding every bound entity value.
- A unique current verified recipe with incomplete current role hints now supplies one bounded
  `verifiedRoute` planner observation. It contains only recipe/action references, dependencies, and
  role slots; it is included in trace evidence and cannot replace trusted search, exact inspection,
  common verification, consent, execution, or receipts.
- Existing deterministic direct rebinding remains unchanged when all current role hints are
  present. Existing exact/lexical/vector selection, stale transition, manual review/retirement, and
  recipe use evidence remain authoritative.
- The system-use procedure and component ownership description now document the narrow automatic-
  verification exception and safe guidance boundary.

## Evidence

- Focused recipe, receipt, execution, planning, conversation, and orchestration acceptance tests:
  **57 passed, 0 failed**.
- Full shared repository suite: **857 passed, 0 failed**.
- Local-AI suite: **20 passed, 0 failed**.
- Protocol walk: **6 passed, 2 intentionally skipped, 0 failed**.
- Solution build: **0 warnings, 0 errors**.
- Catalog validation: **144 records valid** with the existing **21 near-duplicate warnings**; no
  live data touched.
- Entity Framework pending-model check: **no model changes since the last migration**.
- Diff whitespace check passed; output contained only existing line-ending conversion notices.
- Static runtime scans found no D&D/ruleset vocabulary and no query output, prior input/entity
  value, prompt, path, code/effect, or transcript field in automatic-verification evidence or route
  guidance.

## Acceptance coverage

- Positive correlated outer fallback, automatic review, and equal replay create one candidate and
  one verified revision.
- Direct outer success without an inner receipt is ineligible and creates no automatic review.
- Result bindings, query steps, and non-empty inputs fail before recipe storage.
- Stored templates and route guidance contain no prior entity binding.
- Local and remote planners receive the same guidance shape and still perform trusted
  search/inspection plus common proposal verification.
- Current-contract checks require trusted active mechanics with exact version/hash; authority drift
  supplies no guidance and follows the existing stale path.
- No database entity/table/column, migration, HTTP route, MCP tool/kind, authorization capability,
  application/catalog ID, recipe status, or public request member was added.

## Deliberate exclusions

Query/result-binding recipes, non-empty input parameterization, learning without player opt-in,
automatic promotion of direct/inner routes, durable agendas, background promotion/execution,
model-authored review decisions/code/effects, and application-specific rules remain excluded.
Query-bearing executions stay valid receipt history but do not create a recipe under this format.

The user confirmed the complete Slice 13E semantic, permanent-identifier, model-observation,
no-migration, and no-new-transport package and completed-feature acceptance on 2026-08-25.
