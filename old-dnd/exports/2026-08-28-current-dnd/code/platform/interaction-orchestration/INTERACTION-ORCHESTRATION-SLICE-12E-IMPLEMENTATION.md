# Interaction orchestration Slice 12E implementation — bounded symmetric planners

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Interaction orchestration Slice 12E](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Completion evidence: [Slice 12E receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12E-RECEIPT.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Produce inert interaction proposals through one bounded server-mediated planning loop, use
the same current-authority verifier for local and remote model output, and bind remote Luna roles to
an actually no-tools provider boundary.  
Exclusions: Action/query execution; public MCP kinds or web routes; assistant-conversation schema or
UI; inner delegation creation; recipe persistence/promotion; catalog or game content; migrations;
live-database changes; changes to the existing operator Codex bridge.  
Allowed files/areas after confirmation: `src/system/interaction-orchestration` domain, provider,
hosting, persistence, manifest, and tests; the existing local-completion allowlist/composition in
`DantesRoleplay.MCPServer` only; minimal project references/guard tests if required; this document,
its completion receipt, and concise owner-status links.  
Stop point: Stop after local and role-bound remote planners can return a verified proposal or typed
non-resolution, every terminal result has append-only receipt evidence, and tests prove no planner
can execute or gain tools. Do not begin Slice 12F.

## Confirmation package

Confirmed by the user on 2026-08-24. The following decisions are active together.

1. **One provider-neutral state machine.** Both the optional local completion model and the remote
   Luna model receive the same closed observation and response schema. They may emit exactly one of
   `search`, `inspect`, `propose`, or `non-resolution` per round. The server alone performs search
   and inspection and then independently verifies a proposal. A model never receives a callable
   tool and never calls retrieval or execution directly.
2. **Separate product role from provider identity.** `InteractionHostContext.RoleProfile` continues
   to identify the immutable initiating product role. Planning adds a distinct server-derived
   planner identity (`local` or `remote`, provider, model/revision, and optional reasoning effort).
   Local Ollama identity is never misreported as Luna. Remote inner is fixed to
   `gpt-5.6-luna`/`low`; remote outer is fixed to `gpt-5.6-luna`/`high`. Caller/model attempts to
   supply or change those values fail.
3. **Use a dedicated no-tools Responses adapter for remote planning.** Do not reuse
   `codex-bridge`: that component is an operator coding-agent integration with repository reads,
   command/file/MCP activity, and approval handling. The new adapter sends stateless Responses API
   requests with the server-selected model and reasoning effort, strict JSON-schema output,
   `tools: []`, `tool_choice: "none"`, `parallel_tool_calls: false`, and `store: false`. It exposes
   no filesystem, shell, network tool, arbitrary MCP, approval, or execution callback. The HTTPS
   connection used by the host to call OpenAI is transport, not model network-tool authority.
4. **Closed planner limits.** One resolution permits at most 8 completion rounds, 4 searches, 8
   exact inspections, 50 distinct observed candidate references, 12 hits from one search, and 180
   seconds elapsed. Existing host-owned limits continue to cap proposal steps and each observation
   and model output at no more than 65,536 UTF-8 bytes. Repeated requests, duplicate inspections,
   and total elapsed time still consume the limits. A lower host byte/step budget wins.
5. **Closed observation.** The model receives only the authorized intent text, opaque application
   and state/session references, product role, caller-supplied opaque role hints/fact references,
   remaining budgets, prior bounded search summaries, and exact inspected contract JSON. It never
   receives source roots, host paths, credentials, authorization objects, state projections,
   effects, operation records, receipt internals, hidden prompts, other applications, or the
   untrusted-reference corpus.
6. **Closed response.** A search supplies bounded text plus optional `mechanic`/`procedure` kind
   filters. An inspection must name one candidate from a prior search and repeat its version and
   fingerprint. A proposal step supplies a prior inspected qualified ID/version/fingerprint,
   `action` or `query`, prior-step dependencies, opaque role bindings, and bounded object JSON.
   Only `needs-input`, `ambiguous`, or `unknown` may be model-selected non-resolutions. The server
   derives `unsupported`, `unavailable`, `unsafe`, and `stale` from authoritative evidence.
7. **Current-authority verifier.** Local, remote, and future externally submitted proposals enter
   one verifier. It rehydrates the current active trusted snapshot; compares application,
   effective-set fingerprint, catalog/content version and fingerprint; requires every proposal
   reference to have been exactly inspected in this run; validates the inert DAG and host state
   revision; and checks kind semantics. The initial executable-contract resolver accepts only an
   active `mechanic` as an `action`, parses its generic declared requirements, rejects unknown or
   missing required roles, and accepts object JSON only. No query contract is executable in 12E;
   a proposed query step is `unsupported` until Slice 12F supplies a confirmed trusted query
   resolver. `system.*` references likewise require a later trusted system-contract resolver and
   cannot be invented from an application catalog.
8. **Deterministic non-AI fallback.** Before a completion, exact qualified-ID and lexical trusted
   feature retrieval are available. Empty retrieval returns `unsupported`; materially tied
   candidates that cannot be disambiguated return `ambiguous`; an explicitly requested disabled or
   unavailable provider returns `unavailable`. Vector support remains optional and lexical
   behavior remains complete. The verified-recipe resolver is an empty internal port until 12G.
9. **Receipt and replay behavior.** Every terminal result is appended through the Slice 12D store.
   The query fingerprint is a domain-separated hash of the bounded search/inspection trace, not raw
   text. Safe evidence records planner kind/identity, rounds and budgets consumed, exact inspected
   references, and safe failure codes; prompts, completions, chain-of-thought, contract bodies, and
   proposal JSON are not stored. Cancellation and timeout map to `unavailable` with distinct codes,
   preserving the already confirmed eight resolution statuses. An equal deterministic rerun may
   return the existing receipt; divergent output under the same idempotency identity conflicts and
   writes nothing. Durable recovery of a lost proposal body is deliberately deferred to the 12F
   public execution/transport confirmation; clients may not execute from a receipt fingerprint
   alone.
10. **Permanent internal identifiers.** Confirm task class
    `system.interaction.planner-step`, response schema name
    `interaction_planner_step_v1`, and trace fingerprint domain
    `dantes-roleplay/interaction-planner-trace/v1`. They are internal contracts, not MCP kinds.
11. **Provider configuration.** The OpenAI adapter is disabled unless the host supplies a secret
    credential through configuration/environment. Credentials never enter SQLite, logs, receipts,
    observations, options projections, or checked-in settings. Unit tests use an in-memory HTTP
    handler; acceptance does not require a billable or network model call. Local planning is also
    disabled unless the existing Ollama provider and the new fixed task class are enabled.
12. **No public or persistence expansion.** Slice 12E adds no route, protocol kind, custom element,
    conversation row/field, receipt table/column, catalog record, recipe, or operation. The existing
    direct query/action clients and operator Codex control-center flow remain unchanged.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Intent interpretation | No D&D rule is interpreted by this slice. | Application adapter and authored catalog descriptions | Generic prompts and C# contain no game vocabulary or formulas. |
| Contract selection | Current active catalog winners are authority. | Application activation, catalog navigation, Slice 12C retrieval | Model references are candidates until exact current rehydration succeeds. |
| Mechanic requirements | Role/component requirements are authored generic contract data. | Mechanics and application execution | The verifier may parse declared generic roles but cannot evaluate rules or effects. |
| Outcomes and state | Catalog JavaScript plus current application state remain authoritative. | Mechanics, actions, ECS, application execution | Slice 12E never runs JavaScript, projects world state, or mutates anything. |

No SRD 5.2.1 locator or Foundry dnd5e implementation is relevant because this slice is entirely
ruleset-neutral. Any application-specific persona or examples belong to a later application adapter,
not this component.

## External provider reference

Official [OpenAI Responses API reference](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)
documents server instructions, maximum output tokens, structured JSON output, explicit tool choice,
and tool lists. Official [GPT-5.6 model guidance](https://developers.openai.com/api/docs/guides/latest-model)
documents `gpt-5.6-luna` and reasoning efforts including `low` and `high`. These documents support
the dedicated stateless schema-only adapter; they do not make model output authoritative.

Repository evidence shows why the existing `src/system/codex-bridge` cannot be reused unchanged:
its app-server process deliberately exposes a read-only repository sandbox and normalizes command,
file, MCP, web-search, network, and approval activity. That is appropriate for the private operator
assistant and ineligible for the no-tools product planner.

## Prerequisite evidence

- [Slice 12B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md) accepts the closed
  authority envelope, product roles, inert proposals, eight statuses, provider-isolation
  requirements, and explicit execution-consent boundary.
- [Slice 12C receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md) accepts current trusted
  feature retrieval, exact/lexical behavior, optional vector fusion, and current-store hydration.
- [Slice 12D receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12D-RECEIPT.md) accepts append-only
  resolution evidence, replay/conflict identity, and fresh authorized redacted receipt reads.
- `IActiveCatalogFeatureSnapshotProvider` owns the immutable current record/trust snapshot;
  `IInteractionFeatureRetriever` owns host-scoped trusted retrieval; neither planner may bypass them.
- `ILocalStructuredCompletionProvider` owns a no-tools schema-bound local completion seam and already
  reports disabled, unavailable, timeout, malformed, schema, and output-budget failures.
- Existing action/application-execution owners remain downstream. They are inspected only to define
  the stop boundary and are not called or changed by this slice.

## Runtime artifacts after confirmation

### Internal contracts

- `InteractionPlannerKind`: `Local` or `Remote`.
- `InteractionPlannerIdentity`: bounded server-derived provider/model/revision/profile/effort
  evidence, distinct from the initiating `InteractionRoleProfile`.
- `InteractionPlannerLimits` and `InteractionPlannerUsage`: closed constants and consumed counters.
- `InteractionPlannerObservation`: canonical bounded intent/scope/trace JSON.
- `InteractionPlannerCommand`: a strict parsed union of search, inspect, propose, or
  model-selectable non-resolution.
- `IInteractionPlanningCompletionProvider`: one schema-only completion method and an attestation;
  it exposes no tools or callbacks.
- `IInteractionProposalVerifier`: the single local/remote/future-submission verification seam.
- `IInteractionPlanner`: internal planning entry point returning result, planner evidence, and
  append/replay/conflict receipt evidence.
- `IVerifiedInteractionRecipeResolver`: returns no recipe in this slice and provides the stable seam
  for 12G without a recipe ID/table.

### Provider adapters

- Local adapter: wraps `ILocalStructuredCompletionProvider` with fixed task class and schema. It
  passes opaque generic scope metadata only and translates provider failures into closed outcomes.
- Remote adapter: calls the OpenAI Responses API through injected `HttpClient`, fixes the host role's
  model/effort, omits all tools, validates response identity/status/size/schema, and fails closed on
  refusals, incomplete responses, malformed payloads, mismatched model/profile, cancellation,
  timeout, or HTTP failure.
- Default/no-provider adapters: return typed `unavailable`; DataAccess remains resolvable without
  Ollama or an OpenAI credential.

### Planning and verification services

- A single coordinator runs both providers, mediates trusted search/inspection, maintains the
  bounded trace, computes its fingerprint, calls the verifier, and appends the terminal receipt.
- A pure proposal parser treats all model JSON as untrusted and never deserializes it directly into
  an already-authoritative `InteractionProposal`.
- A current-contract verifier reconstructs `InteractionContractReference` and
  `InteractionPlanStep` only after exact snapshot and declared-role checks pass.
- No provider adapter references `IActionRunner`, `IApplicationMechanicEvaluator`, ECS mutation,
  operation logging, receipt database internals, catalog persistence, or arbitrary service access.

## Authoritative state and closed input

The coordinator accepts one already host-bound `AuthorizedInteractionEnvelope` and a fresh
`InteractionAuthorizationRequest` for `Plan`. It evaluates the authorization policy again at the
planning boundary and requires exact principal/application/state-space equality with the envelope.
The host selects planner kind and product role; the intent's planner preference may narrow that
choice but cannot select a provider, model, effort, tool policy, endpoint, prompt, or credential.

Authoritative values are always server-derived:

- current application revision/base order and active effective-set fingerprint;
- trusted retrieval lane and current catalog snapshot;
- current record kind/status/version/content fingerprint and exact inspected JSON;
- host state revision, proposal step limit, observation/output limits, and loop limits;
- product role, effective remote model/effort, provider eligibility, and authorization;
- proposal contract references, expected state revision, trace fingerprint, receipt timestamp/ID.

The caller/model may supply only bounded intent text and opaque hints already accepted by Slice
12B, bounded discovery text/filters, inspected-reference claims, step ordering/dependencies, role
bindings, and object input JSON. Effects, outputs, source code, seeds, operation/receipt IDs,
authorization, currentness claims, execution consent, and learning flags are forbidden.

## Planner state machine

1. Re-evaluate `Plan` authorization and compare the exact result with the envelope scope. Denial
   produces `unsafe`; exceptions fail closed and no search/model call occurs.
2. Resolve the empty verified-recipe port. Continue without a recipe in 12E.
3. Create a canonical observation with the authorized intent, scope references, remaining budgets,
   and an empty trace. Enforce UTF-8 byte size before every provider call.
4. Invoke the selected eligible provider once with the fixed system instruction and strict response
   schema. A provider cannot override the requested role/profile or return tool calls.
5. Parse one command with `additionalProperties: false` semantics and enforce remaining limits.
6. For `search`, force the trusted-feature lane and envelope application, call
   `IInteractionFeatureRetriever`, retain at most 12 current hits, deduplicate exact references, and
   append bounded summaries to the next observation. Vector failure remains lexical fallback.
7. For `inspect`, require an exact prior candidate reference, rehydrate it from the current active
   trusted snapshot, compare application/catalog/version/fingerprint/status, and append its bounded
   contract JSON. Any drift terminates `stale`; an untrusted/shadowed/missing record terminates
   `unsafe` or `stale` without exposing it.
8. For `propose`, parse a non-authoritative draft. The common verifier repeats current snapshot
   hydration, reconstructs authoritative references/state revision, validates application scope,
   prior inspection, step DAG/limits, mechanic/action kind, generic declared roles, and object JSON.
   It returns `resolved` with the inert proposal or a typed non-resolution.
9. For a model-selected non-resolution, bound its summary/missing references and accept only
   `needs-input`, `ambiguous`, or `unknown`.
10. If a hard limit, elapsed deadline, cancellation, provider failure, or schema failure occurs,
    map it to the specified typed result. Never retry silently within the same round and never
    switch providers automatically.
11. Hash the canonical trace, append one Slice 12D resolution receipt, and return only the verified
    proposal/result plus safe planner/receipt evidence. Receipt conflict returns conflict evidence
    and never treats the new proposal as accepted.

`Automatic` routing and remote fallback are composed publicly only in Slice 12F. Slice 12E exposes
and tests both internal planner paths independently so neither path gains a privileged verifier.

## Failure, replay, and no-change contract

| Failure | Required result | Required no-change evidence |
| --- | --- | --- |
| Missing/denied/mismatched fresh plan authorization | `unsafe` | No retrieval, provider, proposal, action, or application-state call. |
| Requested local/remote provider disabled or ineligible | `unavailable` | One safe receipt; no fallback provider is silently invoked. |
| Timeout or host cancellation | `unavailable` with distinct timeout/cancel code | No proposal accepted; receipt attempt uses a non-cancelled bounded cleanup token. |
| Malformed JSON, unknown property/command, schema mismatch, refusal, tool call, or oversized output | `unsafe` or `unavailable` per fixed mapping | No contract hydration beyond already completed reads and no mutation. |
| Empty trusted result set | `unsupported` | No model-invented contract reference is accepted. |
| Materially tied unresolved candidates | `ambiguous` | Candidate evidence is bounded and no proposal exists. |
| Missing required role/fact | `needs-input` | Named opaque requirement only; no state scan or action. |
| Forged/uninspected/cross-app/system/untrusted reference | `unsafe` | No proposal or execution consent exists. |
| Changed application/catalog/effective-set/version/fingerprint/state revision | `stale` | Current evidence is rehydrated; no old contract is accepted. |
| Query step or unsupported active contract kind | `unsupported` | No guessed query executor or procedure interpretation. |
| Round/search/inspection/candidate/byte/time budget exhausted | `unavailable` | Limit and usage recorded safely; no extra provider call. |
| Equal receipt replay | Existing receipt returned | No second receipt and no state mutation. |
| Divergent idempotency reuse | Receipt conflict | New proposal is not accepted and no row is added. |

All terminal paths must assert zero changes to application/source/catalog/ECS/world/action/operation/
conversation/recipe state. Receipt append is the only permitted durable write.

## Implementation sequence for the coding AI

1. Read `AGENTS.md`, the required implementation-reading guide, the interaction-orchestration agent
   guide, this document, the Slice 12B–12D receipts, and only the named code owners. Inspect the dirty
   worktree and preserve unrelated edits.
2. Restate the ruleset-neutral boundary, new internal IDs, provider isolation, allowed files, tests,
   and exact stop point. If this document is not `active`, do planning only.
3. Write pure failing tests for command parsing, limits, planner identity versus product role,
   current-reference verification, DAG/role checks, typed status mapping, and trace fingerprints.
4. Implement bounded contracts and the shared verifier first. The verifier must accept only
   non-authoritative drafts and reconstruct authoritative proposal objects itself.
5. Implement the provider-neutral state machine with fakes. Prove local and remote fakes traverse
   identical search/inspect/verify calls and produce the same proposal fingerprint.
6. Add the local adapter using the existing no-tools provider and fixed task class. Do not add any
   orchestration/game reference to the local-AI project.
7. Add the remote Responses adapter using injected HTTP and strict request/response DTOs. Do not add
   the OpenAI credential to files or logs and do not reuse/modify `codex-bridge`.
8. Add fail-closed DI composition and the fixed local task allowlist. Keep both planners disabled
   unless their existing host configuration is valid.
9. Integrate terminal receipt append last. Store only safe bounded evidence and the trace hash; do
   not change the accepted 12D schema.
10. Run focused tests while iterating, then the full shared suite, standalone local-AI suite,
    isolated-output solution build, architecture/guard searches, and `git diff --check`.
11. Inspect every authored artifact, write the short Slice 12E receipt, update roadmap/dependency
    status once, and stop. Do not implement 12F, even if its public callers would be convenient for
    an end-to-end test.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive local | A fake local provider searches, inspects, proposes one active generic mechanic action, passes the common verifier, and appends a resolved receipt. |
| Positive remote | A fake Responses provider produces the same proposal through the same search/inspect/verifier path and fingerprint. |
| Direct remote submission seam | A non-authoritative remote draft enters the same verifier; it cannot bypass prior inspection/current hydration. No public route exists. |
| Deterministic fallback | Exact and lexical discovery work with vectors and completion disabled; stable ties produce `ambiguous`, no hits `unsupported`. |
| Provider failures | Disabled, unavailable, timeout, cancellation, refusal, incomplete HTTP response, malformed JSON, schema mismatch, tool-call output, wrong model, and output limit map exactly. |
| Isolation | Serialized remote requests fix Luna Low/High by role, contain no tool definition/callback/thread carry-over, and reject role/model/effort/prompt/tool/approval overrides. |
| Local independence | Local-AI project has no orchestration, game, catalog, action, ECS, or host dependency and receives only opaque generic prompts/schema. |
| Current authority | Forged, uninspected, stale, shadowed, untrusted, inactive, wrong-version/hash/catalog/effective-set/application/state references all reject. |
| Semantic boundary | Active mechanic/action plus declared required/optional/unknown roles and object input are validated; query/procedure/system execution remains unsupported. |
| Limits | Every hard round/search/inspect/candidate/per-search/observation/output/elapsed/step limit has a boundary test and stops before the next call. |
| Status/receipt | All eight statuses are reachable from appropriate server/model evidence; every terminal path attempts one receipt with safe trace/identity/usage evidence only. |
| Replay | Equal fake output returns the original receipt; divergent output with the same idempotency identity conflicts and is not accepted. |
| No mutation | Spies prove no action, mechanic engine, ECS effect, operation, conversation, recipe, catalog/source/application write, or public dispatch occurs. |
| Compatibility | Existing local assistant, operator Codex bridge, direct query/action, catalog retrieval, receipt reads, MCP, and web tests remain unchanged. |

## Verification commands

- Focused interaction planner, proposal-verifier, local-adapter, remote-HTTP-adapter, receipt,
  component-manifest, and architecture/guard tests.
- Full shared test suite and the standalone `DantesRoleplay.LocalAI.Tests` suite.
- Isolated-output solution build and `git diff --check`.
- Source searches proving no game-specific term in generic additions, no local-AI reverse
  dependency, no provider reference to execution/mutation owners, and no credential in changed files.
- No `roleplay validate catalog`: no catalog artifact may change.
- No protocol walk: no MCP kind, dispatch, description, example, or registration may change.
- No real provider/network call and no normal live-database initialization or migration.

## Completion receipt and exit gate

After verification, write
`receipts/INTERACTION-ORCHESTRATION-SLICE-12E-RECEIPT.md` with the delivered planner/verifier/provider
boundary, isolation evidence, exact test/build results, receipt/no-mutation evidence, and deliberate
exclusions. Mark this document and the 12E dependency row accepted only if every acceptance row
passes. The next leaf remains Slice 12F and requires separate confirmation for public planning,
two-phase execution, application conversations, authorization, idempotency/partial progress, and
the reusable web surface.
