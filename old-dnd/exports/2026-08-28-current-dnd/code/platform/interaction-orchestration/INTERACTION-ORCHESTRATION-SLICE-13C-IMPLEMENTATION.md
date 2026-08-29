# Interaction orchestration Slice 13C implementation — query contracts and typed result references

Status: **accepted 2026-08-25**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Slice 13C](INTERACTION-ORCHESTRATION-SLICE-13-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**

## Outcome and boundary

An inner or outer planner can find and inspect an application-owned read-only `query` contract,
include it in an inert interaction proposal, and execute it through one generic host registry. A
query resolves only an exact registered structural projection. Its schema-validated bounded output
may be returned to the outer AI when the contract explicitly marks the complete projection as
model-visible, or may remain memory-only and feed declared later-step roles/input through immutable
result bindings. Every execution records enough exact hash/revision evidence to explain and replay
the result without turning a model, Markdown prose, or an application rule into read authority.

Exclusions: arbitrary SQL/file/network reads; JavaScript query code; model expressions,
transformations, coercion, defaults, or output interpretation; field-level redaction; action-result
bindings; mutation from a query executor; task lists/work batches; recipe generalization; automatic
execution/consent; application or D&D-specific query IDs and projections.

Allowed files/areas: application catalog query parser/materialization and validation; structural
projection read contracts; interaction planning/verifier/executor/query registry/receipts; SQLite
interaction persistence and one migration; existing MCP/private-web interaction projections and
focused tests; this document, its receipt, and concise owner status. Stop when one query-only plan
and one query-to-action plan execute with exact receipts and safe outputs. Do not begin Slice 13D.

## Confirmed decisions

The user confirmed this complete package on 2026-08-25:

1. Add `query` as a permanent searchable application catalog kind. Query IDs must be qualified by
   the registered application and cannot use the reserved `system` application prefix.
2. Add the permanent executor kind `projection`. It is a generic host registration that may only
   call the existing read-only structural projection materializer.
3. Add strict application source records under `queries/**/*.json` with exactly: `id`, `category`,
   `name`, `description`, `matches`, `roles`, `executor`, `projection`, `outputSchema`, `exposure`,
   and `status`. `projection` contains exact `qualifiedId`, positive `version`, `contentHash`, and
   `outputSchemaHash`; `roles` contains only descriptions keyed by the exact registered projection
   role names; `exposure` is `model-visible` or `binding-only`. Unknown properties fail validation.
4. Add `resultBindings` to each planner proposal step. Each binding names one earlier query step,
   an RFC 6901 source pointer, and exactly one target: a declared role name or an object input
   pointer. Bindings are immutable structural copies. They cannot read action output, name a
   non-dependency/future step, overwrite static data, address arrays at the target, or execute an
   expression/coercion/default. Role targets require a runtime JSON string.
5. Treat the complete query projection as one privacy boundary: `model-visible` may be persisted
   and returned in full; `binding-only` is never returned or persisted raw. Authors must define a
   separate safe projection instead of requesting field-level runtime redaction.
6. Add a persisted per-query-step receipt containing exact query/schema/result/source-revision
   fingerprints and optional bounded model-visible output. This requires one SQLite migration and
   adds query results to the existing execution outcome/receipt projection. An equal execution
   replay returns persisted safe query results without rerunning queries or actions; a conflicting
   fingerprint fails closed.
7. Keep the current 65,536-byte/32-depth JSON bound, 16 proposal-step bound, 16 dependencies per
   step, and add at most 32 distinct result bindings per step. No new MCP tool/route or authorization
   capability is added; existing interaction planning, execution consent, execution, and receipt
   authorization continue to own access.

## Prerequisite evidence

- [Slice 13B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13B-RECEIPT.md) proves both AI roles
  use current trusted discovery and the common verifier before the existing consent boundary.
- `InteractionProposalVerifier` currently rejects every query with
  `QUERY_CONTRACT_UNSUPPORTED`, leaving a fail-closed seam.
- `IProjectionDefinitionRegistry` owns exact application-qualified projection versions, normalized
  bounded output schemas, schema hashes, content hashes, role sets, and acyclic dependencies.
- `ProjectionMaterializer` is already read-only, application/state-space scoped, bounded to 256
  component reads and depth 16, exact about source component versions, and validates its output
  against the registered schema.
- The interaction receipt store already owns scoped idempotency and append-only resolution/execution
  truth, but its step rows currently carry action operation IDs only; structured query evidence is
  therefore a deliberate migration rather than an evidence-string workaround.

No Foundry or SRD review applies because this slice defines generic host plumbing and no game rule.

## Runtime artifacts

| Artifact | Change | Authority |
| --- | --- | --- |
| Application catalog kind `query` | New permanent public/search kind | Trusted active application source winner |
| Query source shape | New strict `queries/**/*.json` contract | Authored application source plus exact projection cross-check |
| Executor kind `projection` | New permanent generic registry key | Host registration; callers cannot register executors |
| `resultBindings` | New planner/proposal/public projection field | Common parser and verifier |
| Query result projection | New execution outcome/receipt field | Host execution plus persisted receipt |
| Query receipt rows | New table/entity and migration | Interaction receipt transaction |

There is no generic `system.*` query contract in this slice. Application query IDs and their
projection/component dependencies remain application-owned. C# contains only the generic `query`
kind, `projection` executor adapter, structural binding vocabulary, limits, validation, execution,
and receipt plumbing.

## Authoritative state and closed input

The query record is executable only when all of these remain exact at planning and execution:

- trusted active source winner and catalog fingerprint;
- application-qualified query ID, version, content fingerprint, and active status;
- executor kind registered by the host;
- registered projection owner, ID, version, content hash, output schema hash, normalized output
  schema, and exact role-name set;
- application revision, activation/effective-set fingerprint, state-space binding, authorization,
  and the execution proposal fingerprint.

The planner supplies only query/action selection, static role entity IDs, bounded object input,
dependencies, and result-binding declarations. The backend supplies executor registration,
projection registration/schema, state-space/application scope, component source revisions, output
validation, canonical result fingerprints, exposure enforcement, and receipt identity. The model
may never supply a query implementation, raw component locator, SQL/path, schema hash substitute,
source revision, result value, result fingerprint, authorization, or success claim.

Projection queries accept no free-form query input in this slice, so a query step's static `input`
must be `{}`. Its roles are filled by static role bindings and/or earlier query result bindings and
must exactly equal the registered projection role set at execution.

## Behavior, result binding, and transaction ownership

1. Search and inspection include trusted `query` records alongside mechanics/procedures. The
   planner protocol accepts at most the three closed kinds.
2. Proposal verification cross-checks the query record against the current projection registry and
   validates every declared source/target pointer against the exact query output shape where it can
   be decided statically. It retains exact query and output-schema references in the proposal.
3. Execution first checks for an equal persisted execution receipt. Equal replay returns that
   receipt and its model-visible query results; conflict stops before reads or mutation.
4. Steps execute in topological proposal order with the existing stop-on-failure behavior. A query
   executor materializes the exact projection, canonicalizes/validates the output, derives its
   result and ordered source-revision fingerprints, and retains raw output only for the current
   execution.
5. Before each later step, the host applies its bindings in declared order from successful earlier
   query results. The source pointer must exist. A role target receives only a string. An input
   target copies the complete JSON value; its parent object must already exist and its leaf must be
   absent. A root input target is allowed only when the static input is `{}` and the source is an
   object. Duplicate/overlapping targets fail verification.
6. A query marked `model-visible` contributes its complete bounded output to the outcome and
   persisted receipt. A `binding-only` query contributes only hashes/revisions; its raw output is
   discarded after the plan stops or completes.
7. Action execution remains owned by `IApplicationActionRunner` and the ECS/effect transaction.
   Query reads do not join or expand that mutation transaction. The interaction receipt store
   atomically appends the final execution receipt, step rows, and query-result evidence after the
   ordered plan stops. Previously committed action operations remain truthful on partial failure.

The action step operation identity remains derived from the immutable execution/proposal/ordinal.
The submitted proposal fingerprint includes result bindings, while the action runner's existing
operation conflict/replay boundary prevents a second derived input from being committed under the
same operation identity.

## Failure, replay, and no-change contract

| Condition | Result | No-change/evidence guarantee |
| --- | --- | --- |
| Query kind/source shape malformed or projection mismatch | Unsafe/stale proposal | No query read, action, or execution receipt until explicit execution is attempted; resolution receipt remains truthful. |
| Unknown executor, missing projection/component/role, wrong application/state space | Unsupported/stale/failed step | No mutation from the query; later dependent steps are skipped. |
| Unauthorized planning/execution/receipt read | Existing typed denial | No query materialization and no newly exposed output. |
| Output exceeds bounds or fails exact schema | Failed query step | Raw value is discarded; no later binding/action. |
| Missing pointer, wrong role value type, target collision, or forbidden target | Failed verification when static, otherwise failed binding step | No affected action starts. |
| `binding-only` success | Hash/revision receipt only | Raw output is absent from database, API, logs, evidence, and model observation. |
| `model-visible` success | Exact bounded output plus hashes | The output is the authored safe projection; no implicit field redaction. |
| Equal execution replay | Existing persisted receipt/results | No query or action reruns. |
| Conflicting idempotency/proposal/binding fingerprint | Conflict/stale | No query or action starts. |
| Cancellation before action | Cancelled/partial receipt as applicable | No uncommitted mutation; in-memory query values discarded. |
| Receipt write failure after an action commit | Existing operation remains auditable/replayable; execution reports failure | Retry cannot duplicate the action; no query output is silently claimed persisted. |

## Implementation sequence

1. Confirm this document; mark it active. Add contract/parser tests for strict query source records,
   application ownership, projection cross-checks, catalog navigation/search, and source overrides.
2. Add `query` materialization and planner search/inspection support without enabling execution;
   retain the verifier's fail-closed behavior until the query registry is registered.
3. Add the closed query executor registry/result contracts and the projection adapter. Prove it has
   no write/action/file/network dependency and enforces exact scope/schema/bounds.
4. Add `resultBindings` parsing, canonical proposal fingerprinting, verification, structural copy,
   role binding, and negative pointer/ordering/collision tests.
5. Add structured query-result receipt persistence/migration, early execution replay, exposure
   projection, and private web/MCP serialization through the existing interaction endpoints.
6. Integrate ordered query/action execution and focused query-only/query-to-action/partial/cancel
   tests. Run fresh catalog validation, migration/replay tests, build, full suite, and protocol walk
   because the existing MCP result schema/dependency composition changes.
7. Read back all artifacts, write the completion receipt, update the owner/dependency status once,
   and stop before 13D.

## Acceptance matrix

| Area | Required proof |
| --- | --- |
| Positive | Search/inspect/plan/execute one model-visible query; bind an entity-ID string into a later action role; bind a typed value into absent action input; return exact narration plus safe query result. |
| Query-only knowledge | Outer AI receives the complete safe projection and can narrate it; no action/operation is created. |
| Binding-only privacy | A hidden result can drive a declared binding but appears nowhere raw in outcome, receipt, log, evidence, or replay. |
| Negative | Reject malformed query records, unregistered executor/projection, stale hashes/schema/roles, arbitrary input, future/action source, missing pointer, non-string role, overlapping target, expression-like/unknown properties, and wrong scope. |
| Bounds | 16 steps/dependencies, 32 bindings, 65,536-byte/32-depth output, projection depth/read limits, and safe receipt limits fail at their exact boundaries. |
| Determinism | Equal projection values/source revisions produce equal canonical result/revision fingerprints and binding output. |
| Replay | Equal execution returns stored visible results and does not materialize or mutate; conflicting execution fails before work. |
| Partial/rollback | Query failure skips dependants; action failure preserves earlier query evidence and truthful committed operations; injected persistence failure never invents a receipt. |
| Fresh import/override | Query source winners obey registered application directories, trust, precedence, and exact source fingerprint drift checks. |
| Compatibility | Existing mechanic/procedure search, action-only proposals, recipes, receipts, local/remote planning, web flows, and all shared tests remain unchanged. |
| Architecture | Generic C# contains no application ID/rule term; query records/projections/components remain application-owned; query executor has no mutation capability. |
| Surface | Existing interaction endpoints serialize only authorized model-visible output; remote/private boundaries and MCP authorization remain unchanged. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~InteractionQuery|FullyQualifiedName~InteractionPlanning|FullyQualifiedName~InteractionExecution|FullyQualifiedName~ProjectionMaterialization|FullyQualifiedName~ApplicationCatalog"
dotnet run --project DantesRoleplay.Tools -- validate catalog
dotnet build DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.slnx --no-restore
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore -p:IncludeProtocolWalkTests=true --filter "FullyQualifiedName~ProtocolWalkTests"
```

## Completion receipt and exit gate

Write `platform/interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-13C-RECEIPT.md`
with delivered boundary, migration/catalog/protocol evidence, focused/full counts, deliberate
exclusions, and confirmation reference. Mark 13C accepted only after the user confirms completed
feature acceptance. Stop before task lists, work batches, or automatic route promotion.
