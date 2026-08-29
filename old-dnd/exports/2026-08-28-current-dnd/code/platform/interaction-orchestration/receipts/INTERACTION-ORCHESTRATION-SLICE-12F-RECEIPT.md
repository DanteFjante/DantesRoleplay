# Interaction orchestration Slice 12F completion receipt

Accepted: **2026-08-24**  
Ruleset alignment: **ruleset-neutral**  
Implementation contract: [Slice 12F implementation](../INTERACTION-ORCHESTRATION-SLICE-12F-IMPLEMENTATION.md)

## Delivered boundary

- Kept the public MCP surface at exactly three verbs and added the confirmed kinds
  `system.feature-search`, `system.interaction-plan`, `system.interaction-receipt`, and
  `system.interaction-execute`. Planning requires the closed `resolve` or `submit` mode; submitted
  plans use the same current-authority verifier as model-produced plans.
- Added `IApplicationActionRunner` as the exact application-state action owner. It rehydrates the
  active qualified mechanic and effective component mappings, evaluates only catalog JavaScript,
  translates the existing generic effect vocabulary, and delegates one atomic batch to the
  application ECS effect owner. The legacy intent-matching `IActionRunner` remains unchanged for
  direct-action compatibility and is not used by orchestration.
- Added deterministic ECS operation identity, equal replay, conflicting-reuse rejection, complete
  proposal preflight, sequential stop-on-failure execution, and succeeded/replayed/failed/skipped
  receipt evidence. A committed earlier step is retained and never described as rolled back.
- Added basic private-host plan/execute/receipt authorization bound to the verified opaque
  principal, exact application, and current state-space binding.
- Added the process-local bounded outer conversation coordinator, fixed no-tools Luna High outer
  decision/narration adapters, the five confirmed application routes, and reusable
  `<application-conversation>` element. Only the server can select inner/outer role, model,
  reasoning effort, prompts, tools, authorization, delegation parent, or execution authority.
- Added safe narrator projections and deterministic fallback summaries. Neither model receives an
  execution callback, hidden application state, effects, raw operation rows, private prompts, or
  reasoning traces.

## Acceptance evidence

- Focused interaction/application-action/ECS replay/protocol/web/authorization/guard suite:
  **102 passed, 0 failed**.
- Complete shared suite: **775 passed, 0 failed, 0 skipped**.
- Standalone local-AI suite: **20 passed, 0 failed**.
- Protocol walk with `IncludeProtocolWalkTests=true`: **6 passed, 0 failed, 2 skipped**. The skipped
  rows exercise deliberately retired authored-procedure commit/read paths; the live three-verb
  discovery, interaction-kind, authorization, and fail-closed walk passes.
- Isolated-output solution build: **0 warnings, 0 errors**.
- Catalog validation: **144 valid records** with 21 existing near-duplicate warnings and explicit
  confirmation that no live data was touched.
- `git diff --check`: passed; only working-copy line-ending notices were emitted.
- Architecture searches found no game-specific vocabulary/formulas in production orchestration,
  application execution, or ECS additions; no outer provider dependency on mutation owners; no
  caller-controlled conversation ID; and no browser reference to MCP, control-center, model,
  effort, prompt, tool, approval, or filesystem authority.
- The Responses request shape and fixed `gpt-5.6-luna` profile were checked against the official
  OpenAI Responses API and model documentation. Acceptance made no real provider/network call.

## State and compatibility

- Planning remains inert. Application state changes only after a separate exact execute request.
- Equal action/execution retries are at-most-once; conflicting reuse fails closed.
- Existing direct actions, catalog/navigation, private Tailscale web access, control-center
  assistant, and hosts with orchestration providers disabled remain compatible under the full
  suite.
- No migration, proposal storage, durable conversation storage, normal database initialization,
  or live-database mutation was performed for this slice.

## Deliberate exclusions

Slice 12F does not add recipes, candidate learning, review/promotion, durable conversations,
arbitrary query-step execution, event-chain integration, distributed rollback, public remote MCP,
or model-authored effects. Recipe learning and promotion remain Slice 12G; combined final
acceptance remains Slice 12H.
