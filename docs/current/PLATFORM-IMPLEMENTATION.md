# Platform implementation coordination

Status: separate implementation plans prepared for review and agent assignment. No runtime work,
migration, release, or completed feature acceptance is implied by these documents.

[PRODUCT-DIRECTION.md](PRODUCT-DIRECTION.md) records the agreed product and working defaults.
This page coordinates six bounded plans. Plan numbers identify workstreams, not a requirement to
finish every file in numerical order. An assigned agent reads its own plan and exact implementation
owners; it does not need to preload all six plans or historical audits.

Implement [00 — Shared foundation](platform-implementation/00-shared-foundation.md) first. It
publishes the shared code contracts, adapters, conformance fixtures, and agreed source baseline;
the six workstreams then implement against the accepted foundation revision and contract baseline.
All six can develop independent slices, while cross-feature acceptance still waits for its stated
dependencies and coordinator integration.

## Initial scope

Deliver one installation controlled by the operator, with invited users and permissioned AI.
The initial client integrations are the website and Codex through the existing MCP interface.
Keep the inner AI harness, embeddings, dynamic authoring, storage, actions, events, schedules,
observers, and runtime web content in scope. Use existing model/provider abstractions and the
existing Codex adapter; no additional AI provider or external device integration is required.

Other APIs are future extension points. Preserve generic host-service registration, normalized
observation ingress, scoped connection references, and the separation of credentials from authored
code. Do not build a phone bridge, third-party API connector, mobile client, or general integration
marketplace as part of this plan. Existing optional integrations are not removed merely because
they are outside the initial delivery scope.

Application rules, DND2024 authoring, and test inventories are excluded. Relevant verification is
still required for implementation acceptance. The existing checkout contains unrelated changes;
these plans do not authorize overwriting, discarding, or committing those changes wholesale.

## Recommended implementation base

Evolve the existing project, introducing coherent new interfaces and replacing individual
implementations where necessary. Source inspection supports this as the lower-total-work path;
there is no measured calendar estimate or claim that every current subsystem has passed acceptance.

| Option | Assessment for this product |
| --- | --- |
| Extend existing owners | Reuses runtime identity, SQLite state/versioning, effect transactions, catalog activation, retrieval, task/lease infrastructure, MCP, and web publication. Most identified gaps are service integration or additional behavior. Recommended. |
| New project and selectively copy working code | Adds discovery of each copied component's transitive dependencies, resource/build wiring, persisted identity and schema compatibility, host configuration, and transport registration. The new JavaScript/worker/composition features are still required. Likely more total work at present. |
| New thin host referencing existing libraries | Potentially useful later for packaging or naming, but it does not remove the missing platform work. Treat it as a separately justified packaging change. |

Evidence for that assessment includes the current
[core source/resource layout](../../DantesRoleplay/DantesRoleplay.csproj),
[persistence and component compilation](../../DantesRoleplay.DataAccess/DantesRoleplay.DataAccess.csproj),
[combined MCP/web host](../../DantesRoleplay.MCPServer/Program.cs),
[existing Codex provider seam](../../DantesRoleplay.LocalAI/Providers/CodexAiProvider.cs), and
the owner lists in the individual plans. The capability-oriented source folders are already
compiled into existing assemblies; moving a folder alone would not lift all of its contracts,
persistence, embedded resources, and hosting behavior.

Use the first runtime-read/action and authoring slices to check this recommendation against actual
implementation effort. Reconsider an individual subsystem if its dependencies cannot be adapted
without duplicating core state or violating the desired execution model. A clean new directory is
not evidence that a rewrite is cheaper. A whole new project requires demonstrated architectural
incompatibility, not merely missing wrappers or a large change list.

## Workstream ownership

| Plan | Primary boundary | Services consumed from others |
| --- | --- | --- |
| [01: Runtime services](platform-implementation/01-runtime-services.md) | JavaScript host calls, authorized dynamic reads, action composition, execution profiles, prepared-code reuse | Authority/activation from 02; durable services from 04-05 |
| [02: Authoring and activation](platform-implementation/02-authoring-activation.md) | Runtime definition/state authoring, standing grants, validation, schema compatibility, recoverable activation | Runtime descriptors from 01; reuse findings from 03; publication adapter from 06 |
| [03: Intent and manual](platform-implementation/03-intent-manual.md) | Orient/Codex guidance, procedure sections, intent associations, retrieval freshness, reuse evidence | Activation changes from 02; callable contracts from 01/04/05 |
| [04: Jobs and events](platform-implementation/04-jobs-events.md) | Durable script work, task/parent/dependency lifecycle, schedule targets, observers, checkpoints, retries and cancellation | Basic invocation from 01 and authority from 02 |
| [05: Inner AI](platform-implementation/05-inner-ai.md) | Focused worker context and execution, delegation adapter, prerequisite-result mapping and compact results | Invocation from 01, context from 03, lifecycle from 04, authority from 02 |
| [06: Website](platform-implementation/06-website.md) | Runtime component composition, page/data/action bindings, operator presentation | Authoring/reads/actions and task readback from 01-05 |

One coordinator owns cross-workstream contract decisions, shared dependency registration,
solution/project wiring, database migration sequencing, public MCP/HTTP compatibility, and final
integration. A workstream agent proposes changes to a shared file instead of concurrently rewriting
it with another agent. The coordinator assigns the actual file boundary for each active slice.

## Shared contract baseline

Before parallel implementation, settle these conceptual contracts in the existing domain owners.
Their names here describe data requirements; they do not allocate new permanent runtime IDs or
final public method names.

- **Invocation context:** initiating principal, application/state scope, trusted grant reference,
  operation and parent/causation identities, selected definition revision, serializable inputs,
  execution profile, cancellation/deadline, and shared child-work budgets. Authored input cannot
  set or broaden the trusted fields.
- **Operation result:** outcome, structured data, selected revision, relevant read dependencies,
  and either provisional effects, authoritative committed-operation references, or a durable task
  handle. A prepared proposal, a task submission, and a committed action are different outcomes.
- **Authoring candidate:** expected base revisions, affected records and schema meanings,
  dependency/manual references, validation/reuse evidence, requested activation scope, and
  recovery information. Each owner controls its records; the coordinator handles cross-owner
  activation and reconciliation rather than assuming one transaction covers every store.
- **Definition-change notification:** canonical target and revision/fingerprint, changed discovery
  metadata, affected dependency references, and source operation. Derived indexes and prepared
  programs refresh from this signal; authoritative publication does not depend on a model service
  being available.
- **Durable task:** existing task identity plus parent/dependency references, task kind, pinned
  execution inputs/revision, current state, step/checkpoint, attempt/lease fencing, cancellation,
  resource budget, and structured result/receipt references. Reuse the current task owners and
  distinguish the logical task from an individual worker attempt.
- **Discovery packet:** relevant canonical targets, current procedure sections, contracts,
  prerequisites, known input bindings, missing information, and version/source references. The
  host retains exact evidence while the model receives only the useful portion.

Atomic mechanics accumulate proposed effects and commit at the root boundary. Durable workflows
use explicit persisted steps/checkpoints and completion handlers. JavaScript awaiting a task in
memory does not establish crash recovery. Journal accepted service requests/results and correlate
completion to the waiting step; do not automatically replay arbitrary JavaScript side effects or
claim a suspended engine heap is persistent. The exact checkpoint protocol belongs jointly to the
runtime and job owners and must be fixed before their adapters are implemented.

## Delivery order and parallel lanes

1. **Establish the implementation baseline and contracts.** Inspect the current working state,
   identify owned changes, record baseline verification in the task, and settle the shared shapes
   above. Preserve application-independent operation. Do not create a clean worktree from HEAD
   and silently omit relevant uncommitted work. Agent assignments refer to an agreed source state.
2. **Build the first reusable boundaries.** In parallel, deliver plan 01's basic engine/read/action
   contract, plan 02's candidate/grant/activation path, and plan 03's procedure/context selection.
   These slices use the common contracts and existing owners. Integrate a runtime-authored action
   that Codex can discover and use before declaring the foundation connected.
3. **Connect durable execution and the website.** Plan 04 adds durable work over the basic runtime
   boundary. Plan 06 implements content composition/read bindings. Plan 03 completes activation
   refresh and alternate-intent authoring with plan 02. There is no whole-plan circular dependency:
   runtime schedule/AI adapters wait for the service contracts, while basic runtime calls do not.
4. **Connect inner workers and complete service adapters.** Plan 05 uses the agreed task lifecycle
   and context packets. Plans 01 and 06 connect their inner-task/progress adapters through those
   interfaces. Prepared-code optimization can proceed independently once invocation semantics are
   stable, with measurements before and after.
5. **Integrate, recover, and accept the complete platform path.** The coordinator combines the
   workstreams, resolves contract drift, rehearses migrations/recovery, and runs the required
   acceptance checks. Completion requires functioning integration, not six isolated green reports.

Planning agents and implementation agents are roles within this task. Agents may delegate a
bounded independent subtask when a slot is available; the parent remains responsible for review
and delivery. This session currently permits four active agents total, including the coordinator,
so plan for at most three simultaneous workers across the whole tree. Recheck capacity when work
starts rather than treating that number as a permanent product limit.

Agents in this task share the checkout. Use disjoint file ownership and serialize shared edits.
Separate worktrees can be used when they preserve the agreed source state and their integration
cost is justified. Do not give several agents overlapping ownership of the same DbContext,
migration snapshot, service-registration file, or transport envelope. A child agent's work is
reviewed by its parent; the coordinator reviews cross-workstream behavior.

This coding-agent delegation is separate from the product's inner-AI task feature in plan 05.
The latter requires its own runtime implementation and is not delivered by using Codex subagents
to write the software.

## Model and context budget

The user preference is confirmed: each of the six workstream chats may use gpt-6-astra as its
lead for assignments, reviews, and difficult decisions, while gpt-5.6-terra, gpt-5.6-luna, and
gpt-5.6-sol perform implementation according to task difficulty. The coordinator remains the
shared-contract and integration owner; workstream leads use the accepted foundation contracts and
do not independently redesign the shared API. Default implementation work uses gpt-5.6-terra at
medium reasoning. Use gpt-5.6-luna at low or medium for narrow searches, documentation,
mechanical edits, and focused check-result summaries. Use gpt-5.6-sol for harder execution or
transaction work when appropriate. Use gpt-6-astra for shared-contract decisions, difficult
blockers, and reviews of authority, durability, or integration. This roster reflects currently
callable tools, not pricing promises.

When spawning, explicitly choose the model; do not inherit an expensive parent or the whole
conversation. Pass only the assigned slice, exact file owners, fixed contracts, and a short
evidence summary. Smaller agents perform focused checks; the coordinator runs required full
integration checks at acceptance and repeats them when changes or failures justify it. Escalate
ambiguity or repeated failure with evidence instead of speculative retries.

Use at most one additional subagent per workstream by default, and only for an independent useful
task within actual capacity. Six workstream chats may organize six plans in separate worktrees,
but activate slices by dependency waves rather than launching six unrestricted agent trees. Keep
the source baseline, including relevant uncommitted changes. The coordinator remains dormant
between decision and review gates; do not duplicate full code review at every layer. This policy
does not claim exact token savings, guaranteed six-chat concurrency, or change global settings.

## Integration and completion boundary

Demonstrate through website and Codex: create/update runtime definitions and their manual;
discover an existing action through multiple intents; activate a permitted candidate; read data
and invoke actions from JavaScript; register an observer or schedule; execute a durable workflow
that delegates to the inner AI; inspect its recorded result through a composed page. Update an
action/component and verify subsequent work uses the intended active version without a host build.

Include failures that cross boundaries: unavailable embeddings, stale inputs, revoked grants,
failed activation, interrupted workers, repeated delivery, cancelled dependencies, and page
publication failure. Record what committed and what remains pending. Preserve authoritative data,
operation history, blobs, and existing content while derived indexes/caches remain rebuildable.

Run focused verification during each slice and the repository's required full acceptance checks
after integration; run the protocol walk when MCP/dependency registration changes. Do not enumerate
the whole test suite in these plans. Existing AGENTS.md rules still govern implementation; these
planning documents do not import a different plan's unattended-acceptance exception.

Deliver changes and evidence in the task. Keep these documents as implementation plans and update
their contracts when decisions change; do not turn them into execution diaries or receipts.
