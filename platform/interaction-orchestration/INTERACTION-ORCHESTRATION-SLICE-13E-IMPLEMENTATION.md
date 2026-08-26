# Interaction orchestration Slice 13E implementation — safe outer-fallback learning and promotion

Status: **accepted 2026-08-25**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Slice 13E](INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

When an inner planning attempt ends with one of the accepted fallback statuses and the correlated
outer planner produces an exact proposal which the player explicitly confirms and executes
successfully, the existing opt-in learning path may create and deterministically verify a
value-free route. On a later semantically matching request, the inner planner can either rebind a
complete set of current caller role hints directly or receive the verified route as bounded current
contract guidance while it discovers the current entities and constructs a new proposal. In both
cases the common verifier, consent reference, executor, and receipts remain mandatory.

This slice deliberately keeps the first reusable format narrow. An automatically verified route
contains only application-owned action contract IDs, versions, hashes, dependency order, and role
slot names. It contains no entity reference, action input value, query input/output, result binding,
state value, prompt, code, effect, path, credential, or model transcript. A proposal containing a
query, result binding, or non-empty input remains successful execution history but is not learned.
This exclusion is the query-value safety policy for Slice 13E; structural query recipes require a
separately confirmed later format and are not silently added here.

Exclusions: learning without the existing explicit `learn` opt-in; model-controlled promotion;
automatic promotion of inner/direct routes; query/result recipe templates; arbitrary input
parameterization; learned code/effects; source/catalog writes; new application IDs; durable task
agendas; background work; changed authorization or consent; a new MCP/HTTP operation; game rules.

Allowed files/areas: interaction recipe contracts, learner, current-authority/provenance validator,
verified recipe resolver and planner observation; interaction receipt read ports/store queries;
existing dependency registration; the existing system-use procedure; focused recipe, planning,
execution, conversation, persistence, protocol, and catalog tests; concise component/owner/receipt
updates. No database entity, table, column, or migration is allowed.

Stop when one explicitly opted-in, correlated, completely successful outer fallback becomes a
verified value-free recipe; a later inner request receives safe current route guidance or direct
role-hint reuse; every ineligible/poisoning/replay/stale case fails closed; and the complete suite is
green. Do not begin the combined Slice 13F acceptance matrix.

Model: **Sol High** for the provenance, promotion, poisoning, privacy, and replay implementation and
review. Terra may perform bounded mechanical edits only after this contract is confirmed, but Sol
must perform acceptance review.

## Confirmed decisions

The user confirmed this complete package on 2026-08-25:

1. **Learning remains explicit.** The existing per-execution `learn` flag remains false by default.
   Automatic verification is considered only when the player explicitly confirms the exact
   proposal with `learn=true`; ordinary execution and agenda continuation create no recipe.
2. **Outer fallback is proven from durable evidence.** The candidate's resolution receipt must use
   the immutable outer role and must correlate to exactly one earlier eligible inner non-resolution
   using the same principal, application, state space, session, conversation, parent delegation,
   and host-created batch idempotency base. Eligible inner statuses remain exactly `unknown`,
   `unsupported`, and `unavailable`. A direct outer plan or caller assertion is insufficient.
3. **Success is closed and complete.** The outer resolution must be `resolved`; the execution must
   be `succeeded`; every step must be `succeeded` or an equal replay with valid operation audit
   linkage; proposal/receipt fingerprints must agree; and current application, activation, and
   contract hashes must still match at promotion time. Partial, failed, cancelled, stale,
   unauthorized, or ambiguous provenance cannot promote.
4. **The eligible template remains value-free action-only v1.** Every proposal step must be an
   application-owned action with `{}` input, no result bindings, and only role bindings whose values
   are discarded. The stored template keeps exact current contract references, dependencies, and
   role-slot names. Query steps and all non-empty inputs return a typed not-created learning result;
   no query result or old entity value reaches recipe storage.
5. **Promotion is deterministic host policy.** After candidate append/replay, a non-model verifier
   reruns current-authority, contract-role, template, and durable provenance validation. If all
   checks pass it invokes the existing append-only review transition using the permanent opaque
   verifier principal `system.interaction.recipe-auto-verifier`, decision `verify`, a fixed bounded
   reason, and a request token derived from the execution receipt. The model cannot select or forge
   this transition.
6. **Manual review remains available.** Inner-planned routes, direct outer routes, older candidates,
   and candidates that fail the outer-fallback eligibility check remain inert `candidate` records
   under the accepted Slice 12G private review policy. Automatic-verification failure never changes
   the successful execution receipt and never retires or replaces a candidate.
7. **Reuse has two safe modes.** A unique current verified match with every role slot present in the
   newly authorized intent keeps the existing deterministic rebind-and-verify fast path. If current
   role hints are incomplete, the planner receives one bounded `verifiedRoute` guidance object
   containing only recipe reference, exact current action references, dependencies, and role-slot
   names. The inner model must still search/inspect current trusted contracts and submit a fresh
   proposal through the common verifier. Guidance is never executable authority.
8. **Ambiguity and staleness fail closed.** Zero or multiple verified matches provide no route
   guidance. Changed application/activation/contract authority marks the matching recipe stale using
   the accepted append-only transition and provides no guidance. Vector-disabled, corrupt, or stale
   indexes preserve the complete lexical path.
9. **Replay is exact.** Repeating the same successful execution/learning request replays candidate
   evidence and the deterministic review without a duplicate revision. Conflicting execution,
   candidate, or review tokens create no promotion. Concurrent equal promotion yields one verified
   revision and an equivalent replay/conflict result without corrupting state.
10. **No new transport or persistence shape.** Reuse the existing recipe tables, statuses,
    `system.interaction-execute`, `system.interaction-recipes`, and
    `system.interaction-recipe-review`. Add no migration, route, MCP kind/tool, request field, recipe
    status, or application/catalog ID. Add the closed result/evidence codes
    `RECIPE_AUTO_VERIFIED`, `RECIPE_AUTO_VERIFICATION_INELIGIBLE`, and
    `RECIPE_AUTO_VERIFICATION_FAILED`; update the existing system-use procedure to explain the
    narrow deterministic exception to manual review.

## Prerequisite evidence

- [Slice 12G receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12G-RECEIPT.md) proves explicit opt-in
  candidate derivation, value-free action templates, append-only review, current-authority recipe
  retrieval, poisoning controls, and exact replay.
- [Slice 13B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13B-RECEIPT.md) proves one durable,
  correlated inner-first attempt and one eligible outer fallback under immutable product roles.
- [Slice 13C receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13C-RECEIPT.md) proves query outputs are
  bounded and typed. Slice 13E does not copy them into recipes and rejects query-bearing templates.
- [Slice 13D receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13D-RECEIPT.md) proves every task batch
  has a host-created correlation/idempotency base, fresh planning, exact per-batch consent, and
  durable resolution/execution receipts before continuation.
- Existing recipe revisions already retain reviewer principal, reason, request token/fingerprint,
  application revision/fingerprint, and effective-set fingerprint. No storage change is required.

No Foundry or SRD review applies because this slice defines generic orchestration policy and no
rule calculation.

## Runtime artifacts after confirmation

| Artifact | Change | Authority |
| --- | --- | --- |
| Outer-fallback evidence validator | New generic durable correlation check | Existing receipt and operation records |
| Deterministic recipe auto-verifier | New internal policy composed after candidate append | Current application/activation/catalog plus durable provenance |
| `system.interaction.recipe-auto-verifier` | Permanent opaque reviewer principal | Host code only; never caller/model input |
| `verifiedRoute` planner guidance | New internal/provider observation member | Current unique verified recipe projection |
| Existing recipe learning result | Reuses existing shape with closed new codes | Learner result; no new request/response member |
| System-use procedure | Clarifies explicit learning and narrow automatic verification | Authored system procedure |

The new reviewer principal and result codes are permanent system identifiers. The guidance member is
part of the closed model observation but is not an MCP/HTTP field. No schema migration, catalog
record ID, application component, mechanic, action, query, or procedure ID is added.

## Authoritative state and safe guidance

The database remains authoritative for resolution/execution receipts, operation links, recipe
candidate evidence, and append-only recipe revisions. Application registration/activation and the
active trusted catalog snapshot remain authoritative for current contract identity. The process-
local agenda may identify the batch to the caller, but it is not trusted by the recipe verifier;
promotion is reconstructed from durable receipt correlation.

The learning caller supplies only the already accepted `learn` flag and exact original learning
intent required by Slice 12G. It cannot supply an automatic-review decision, reviewer principal,
fallback eligibility, status, current hashes, or route guidance. The host derives all of them.

`verifiedRoute` contains no recipe evidence intent text, reviewer identity/reason, prior role value,
prior input, prior query/result, state value, source path, raw contract body, or model transcript. It
is bounded to the one unique current recipe already selected by the accepted lexical/vector
retriever. The planner must obtain full contract bodies only through its existing trusted
search-and-inspect sequence.

## Behavior and transaction ownership

1. Execute the confirmed proposal and persist its existing execution/operation receipts exactly as
   today. Learning remains downstream and cannot change action success.
2. If learning was not requested, return the existing not-requested result. If execution or the v1
   value-free template is ineligible, return the exact not-created result and create no recipe.
3. Append/replay the candidate and evidence under the existing recipe transaction. Candidate
   derivation discards role values and rejects query/input/result-binding material before storage.
4. Ask the new durable evidence validator whether this resolution is a genuine correlated outer
   fallback. If not, leave the candidate inert for existing manual review.
5. If eligible, run the existing independent current-authority/provenance review checks and append
   one `verified` revision with the fixed host verifier identity. Candidate creation and review are
   separate idempotent transactions; a crash between them is recoverable by replaying the exact
   execution/learning request.
6. On a later request, select current verified recipes exactly as today. Complete current role hints
   use direct reconstruction and common verification. Incomplete role hints yield guidance to the
   bounded planner; the recipe never supplies old values.
7. The newly planned proposal still requires exact player confirmation and a new execution receipt.
   A recipe-backed success/failure appends the existing use evidence and cannot automatically repair,
   promote, demote, or reinterpret a recipe.

Each action transaction remains owned by its application/ECS execution owner. Candidate append and
review revision remain separate append-only recipe-store transactions. No transaction spans action
execution and learning; receipts are truthful even if learning or automatic verification fails.

## Failure, replay, and no-change contract

| Condition | Result | No-change/evidence guarantee |
| --- | --- | --- |
| `learn=false` | `LEARNING_NOT_REQUESTED` | No candidate or review. |
| Query step, result binding, or non-empty input | existing unsafe/not-created code | Successful execution remains; no recipe data is written. |
| Missing/invalid inner correlation or direct outer route | `RECIPE_AUTO_VERIFICATION_INELIGIBLE` after candidate creation | Candidate stays inert and manually reviewable. |
| Inner status not unknown/unsupported/unavailable | auto-verification ineligible | No automatic review revision. |
| Partial/failed/cancelled/stale execution or missing operation | learning ineligible | No candidate or automatic review. |
| Current app/activation/contract/provenance mismatch | `RECIPE_AUTO_VERIFICATION_FAILED` or existing stale code | No verified route; stale transition may append when current authority proves change. |
| Equal learning/promotion replay | prior candidate/verified reference | No duplicate evidence or revision. |
| Conflicting or concurrent token | typed conflict | No replacement/corruption; successful execution receipt remains authoritative. |
| Missing role hints on later request | safe route guidance, then fresh planning | No old role/entity value is reconstructed. |
| Multiple recipe matches | ordinary fresh planning without guidance | No ambiguous recipe influences a proposal. |
| Vector unavailable/corrupt | lexical lookup | Same complete safe fallback; no authority write from vector state. |
| Planner ignores/misuses guidance | common verifier result | No execution without a valid current proposal and explicit consent. |

## Implementation sequence for the implementing AI

1. After confirmation, mark this document active. Add pure outer-fallback evidence and automatic-
   verification result contracts/codes with exhaustive closed-value tests. Do not change storage or
   planner behavior until they pass.
2. Add a narrow receipt-owner port and persistence implementation that validates the exact
   inner/outer correlation and complete execution/operation provenance. Interaction orchestration
   must not query another owner's tables directly outside this adapter.
3. Compose a deterministic auto-verifier after existing candidate append/replay. Reuse the existing
   review service and store; derive its principal, fixed reason, and replay token in host code. Prove
   crash-between-transactions recovery and concurrent/equal/conflicting replay.
4. Tighten candidate eligibility so result bindings are explicitly rejected in addition to existing
   query and non-empty-input rejection. Add database readback/static tests proving entity/input/query
   values and transcripts never appear in template/evidence/revision fields.
5. Extend verified recipe lookup with a safe current route-guidance result when role slots cannot be
   directly rebound. Add the one bounded guidance object to planner observation; keep the existing
   trusted search/inspect and common verification requirements.
6. Add end-to-end conversation tests for inner non-resolution, outer fallback, explicit execution
   and learning, automatic verification, and a second inner request using guidance without another
   outer fallback. Cover both explicitly selected local and remote outer providers.
7. Update the existing system-use procedure and component description. Run catalog validation,
   focused tests, migration-model check, build, full suite, protocol walk, privacy/architecture
   scans, and diff/readback validation.
8. Write the Slice 13E receipt, update owner/dependency status once, request completed-feature
   acceptance, and stop before Slice 13F.

Do not add query recipes, a generic templating language, model-generated reviewer identity, silent
learning, automatic execution, a background promoter, a repair loop, a second recipe store, direct
game-state queries, or application-specific C#.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Positive fallback | Inner eligible non-resolution plus correlated outer resolved proposal plus explicitly confirmed successful `learn=true` execution creates/replays one candidate and one verified revision. |
| Second use | A semantically matching later inner request either directly rebinds complete current role hints or receives one value-free route hint, searches/inspects current contracts, and resolves without outer fallback. |
| Opt-in | `learn=false` changes no recipe state; learning cannot be inferred from agenda/provider choice. |
| Correlation | Wrong/missing principal, application, state space, session, conversation, delegation, role, batch key, ordering, or inner status cannot auto-verify. |
| Completeness | Partial/failure/cancellation/stale/unauthorized outcomes and missing operation links create no verified recipe. |
| Poisoning | Entity IDs, action inputs, query inputs/outputs, result bindings, state values, prompts, paths, code/effects, credentials, and transcripts are absent from stored templates and guidance. |
| Query boundary | Query/action and query-only executions remain valid history but return typed not-created learning results and create no recipe. |
| Authority | Promotion rechecks current application, activation, trusted mechanic contracts, role requirements, hashes, and durable provenance immediately before review. |
| Manual compatibility | Inner/direct/older candidates remain inert until existing private review; manual verify/retire and terminal stale/retired rules remain green. |
| Replay/concurrency | Equal candidate/review replay is stable; conflicts and concurrent promotion produce at most one verified revision. |
| Guidance | Guidance is unique/current/bounded and cannot bypass search, inspection, common verification, consent, execution, or receipts. |
| Retrieval | Exact/lexical/vector selection is deterministic; vector-disabled/corrupt/stale cases preserve lexical completeness. |
| Provider parity | Local and remote outer fallback produce the same learning policy; disabled providers do not silently switch or promote. |
| Architecture | Generic C# contains no application/rule vocabulary; no migration, new route/tool/kind, game owner, or catalog authority is introduced. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~InteractionRecipe|FullyQualifiedName~InteractionReceiptStore|FullyQualifiedName~InteractionExecutionCoordinator|FullyQualifiedName~InteractionPlanning"
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~ApplicationConversation"
dotnet run --project DantesRoleplay.Tools --no-build -- validate catalog
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --no-build
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:IncludeProtocolWalkTests=true --filter "FullyQualifiedName~ProtocolWalkTests"
```

Also scan authored/runtime diffs for entity-looking values, input/query/result payloads, prompts,
paths, JavaScript/effects, game vocabulary, unexpected schema/migration changes, new MCP/HTTP
operations, and unrelated concurrent work.

## Completion receipt and exit gate

Write `platform/interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-13E-RECEIPT.md`
with the delivered boundary, correlation/promotion/guidance/poisoning/replay evidence, focused/full
counts, deliberate exclusions, and confirmation reference. Mark 13E accepted only after the user
confirms completed-feature acceptance. Stop before the combined Slice 13F matrix.
