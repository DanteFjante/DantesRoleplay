# Interaction orchestration Slice 13F implementation — combined adaptive-AI acceptance

Status: **accepted by user confirmation on 2026-08-26**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Slice 13F](INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

Prove the accepted Slices 13A–13E operate as one bounded application workflow: an explicitly
selected local or remote outer provider creates a safe task agenda; every batch attempts inner
resolution first; eligible non-resolution receives at most one outer fallback; exact query/action
proposals require separate confirmation and produce truthful receipts; explicitly opted-in,
completely successful value-free outer routes can become verified guidance for a later inner
request; and every provider-disabled, vector-disabled, replay, failure, privacy, application-
isolation, and fresh-state boundary remains closed.

This is an acceptance slice, not a new capability slice. It may add consolidated ruleset-neutral
tests or make the smallest correction to a concrete regression in an already accepted invariant.
It may not introduce a new permanent ID, configuration key, model task/schema, database meaning,
migration, protocol kind/tool/verb, route/request field, recipe format, authorization policy,
provider fallback, application record, catalog mechanic, or game rule.

Allowed files/areas: this document; existing interaction/provider/query/agenda/recipe/execution/web
tests and at most one consolidated Slice 13 acceptance test; the smallest existing production owner
only if accepted behavior is demonstrably broken; system/component documentation if evidence has
drifted; the Slice 13F receipt and concise owner/dependency status. Validation uses disposable test
databases and must not initialize, migrate, import, or mutate the normal host database.

Stop when the complete matrix, build, catalog, migration, protocol, privacy, dependency, and full-
suite gates are green and a receipt requests completed-feature acceptance. Do not begin query-
recipe generalization, durable agendas, multi-user/public hosting, or another platform feature.

Model: **Sol xhigh** for combined evidence review and any regression correction.

## Accepted authority

The user's continuation after the Slice 13E receipt records completed-feature acceptance for 13E
and commencement of this final bounded audit. Slices 13A–13E contracts and receipts are authoritative
for behavior. Runtime code, catalog contracts, tests, and durable receipts are implementation
evidence; prospective prose cannot widen an accepted boundary.

- 13A owns explicit local/remote outer selection, separate immutable outer identity, schema-only
  adapters, and no silent provider/network fallback.
- 13B owns one correlated inner-first attempt and at most one eligible typed outer fallback.
- 13C owns application query contracts, typed result bindings, bounded query receipt exposure, and
  exact replay.
- 13D owns strict intent-level agendas, sequential process-local work-batch progress, fresh planning,
  separate confirmation per batch, deterministic pause/replacement, and no background loop.
- 13E owns explicit value-free outer-fallback learning, deterministic append-only promotion, safe
  later route guidance, and query/input/result-value exclusion from recipes.

No new semantic or public confirmation is required because 13F adds no runtime contract. A finding
that requires such a change is a blocker and must receive its own confirmed implementation slice.

## Review method

1. Map every acceptance row below to current focused tests and receipts. Add consolidated coverage
   only where a cross-slice handoff is otherwise asserted indirectly.
2. Exercise both selected provider kinds using their fixed adapters/fakes with network calls disabled.
   Prove selection never crosses to the other provider and disabled configurations stop safely.
3. Exercise the conversation state machine through direct inner success, eligible inner-to-outer
   fallback, multi-task/multi-batch confirmation, failure pause, replacement, and exact learning
   intent retention.
4. Exercise query-to-action result binding, execution replay, operation linkage, value-free outer
   fallback promotion, second-request direct reuse/guidance, stale authority, and poisoning rejection.
5. Audit application/principal/state-space/session/conversation/delegation scope, generic dependency
   direction, model prompts/observations, filesystem/transcript privacy, and absence of game terms.
6. Run focused filters, disposable catalog validation, pending-model check, protocol walk, build,
   full shared/local-AI suites, and diff checks from the same final worktree.

## Acceptance matrix

| Area | Required combined proof |
| --- | --- |
| Inner success | Local and remote selected outer flows create an agenda, attempt inner first, retain one inert proposal, execute only after exact confirmation, narrate safe receipt truth, and never call outer fallback. |
| Outer fallback | Inner unknown/unsupported/unavailable returns one safe correlated receipt; outer receives that evidence once, uses its selected planner only, and cannot recurse or execute directly. |
| Learning and second use | Explicitly confirmed successful outer action route creates/replays one value-free verified recipe; later inner planning directly rebinds current hints or receives one safe route hint and still searches/inspects/verifies. |
| Query/action | Application-owned read-only query output is schema checked, safely exposed or binding-only, structurally bound without coercion, and action execution/replay stays exact; query-bearing routes never enter v1 recipes. |
| Task/batch progression | Ordered dependencies and multi-batch tasks plan at most one fresh next proposal after each receipt; each proposal needs separate consent; failure/partial/cancel/needs-input pauses and blocks without cross-batch rollback. |
| Provider selection | Local and remote outer adapters share closed no-tools schemas and immutable roles; disabled, ambiguous, identity-drift, non-loopback, or malformed providers fail without silent network/provider fallback. |
| Replay and authority | Equal plan/execute/learning/review retries do not repeat work or revisions; conflicts, stale app/activation/contracts, forged fingerprints, and missing operation provenance remain inert and receipted. |
| Isolation and privacy | Principal/application/state space/session/conversation/delegation boundaries cannot cross; no binding-only output, old entity/input/query value, path, credential, raw model transcript, prompt, code/effect, or unauthorized receipt leaks through agenda/guidance/results. |
| Retrieval parity | Exact/lexical retrieval remains complete without embeddings/vectors; vector-disabled/corrupt/stale state cannot change authority or suppress lexical fallback. |
| Generic architecture | Local AI and interaction orchestration contain no application/ruleset knowledge or reverse dependency on game/application C#; C# remains generic and JavaScript/catalog owners retain rule behavior. |
| Surface compatibility | Existing three verbs, orchestration kinds, request/response schemas, private web routes, MCP security boundary, direct action paths, and local-disabled operation remain unchanged except accepted 13A–13E fields/tasks/codes. |
| Repository acceptance | Focused matrix, catalog, migration drift, protocol walk, build, full shared/local-AI suites, static scans, and diff checks pass together with no live host data touched. |

Every negative row must prove no unauthorized mutation and retain the safe receipt/audit evidence
required by its accepted owner. Model narration or agenda memory never establishes success.

## Failure and correction policy

- A failing test is diagnosed against the accepted owner. The smallest correction is allowed only
  when it restores an already confirmed invariant without changing public/storage meaning.
- A flaky or concurrent-work failure is rerun only after identifying its owner; the receipt records
  the final reproducible result and any unrelated blocker rather than hiding it.
- Any required new ID, migration, recipe/query format, public member, authorization decision,
  provider network policy, or ruleset behavior stops 13F for a separately confirmed slice.
- Validation failure must not cause normal database migration/import, provider network calls, or
  application state mutation.

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~InteractionOuterProvider|FullyQualifiedName~InteractionTaskAgenda|FullyQualifiedName~ApplicationConversation|FullyQualifiedName~InteractionQuery|FullyQualifiedName~InteractionRecipe|FullyQualifiedName~InteractionExecutionCoordinator|FullyQualifiedName~InteractionOrchestrationAcceptance"
dotnet run --project DantesRoleplay.Tools --no-build -- validate catalog
dotnet ef migrations has-pending-model-changes --project DantesRoleplay.DataAccess --no-build
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:IncludeProtocolWalkTests=true --filter "FullyQualifiedName~ProtocolWalkTests"
git diff --check
```

Also perform static searches for game vocabulary, forbidden application/game-adapter compile
wildcards, reverse local-AI dependencies, provider/tool/prompt authority drift, prior entity/input/
query values in recipes/guidance, unexpected migrations/surfaces, and additional orchestration
mutation paths.

## Completion receipt and exit gate

Write `platform/interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-13F-RECEIPT.md`
with the mapped combined evidence, focused/full counts, any minimal corrections, deliberate
exclusions, no-live-data statement, and confirmation reference. Mark Slice 13 complete only after
the user confirms completed-feature acceptance. Stop afterward.
