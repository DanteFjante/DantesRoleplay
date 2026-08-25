# Interaction orchestration Slice 13 dependency plan — adaptive local/remote outer AI and bounded task batches

Status: **planning only; semantic and public-shape confirmation required before implementation**
Ruleset alignment: **ruleset-neutral**
Source: **not applicable**
Owner: [Interaction orchestration dependency plan](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md)

## Outcome and non-goals

A player gives one goal to an outer AI supplied by either a local model or a configured remote
model. The outer AI may delegate one intent or a bounded ordered task list to the inner AI. The inner
AI searches verified recipes and current trusted capability contracts for each task and returns
either a closed proposal or a typed non-resolution receipt. A recipe is an optional fast path, not a
prerequisite: a one-off task may be solved through fresh current contract discovery without being
learned. When the inner AI cannot resolve a task, the outer AI may traverse the same server-mediated
contracts, submit its own proposal through the same verifier, execute only after the required
consent, and leave successful value-free route evidence for later inner reuse.

A larger goal may proceed as several bounded runtime **work batches**. Each batch plans and executes
against a fresh authoritative state revision, produces its own receipt, and returns control to the
outer coordinator before another batch begins. A work batch is not a repository implementation
slice, background Codex task, transaction spanning multiple actions, or permission for an unbounded
autonomous loop.

This extension does not let either model invent executable code/effects, bypass application action
owners, approve its own authority, search unrestricted files or entities, silently contact a remote
provider, or create a missing primitive capability merely by recording a recipe.

## Confirmed product direction

- The outer role may use a local LLM as well as a remote LLM.
- Actionable outer turns attempt bounded inner resolution first.
- Inner `unknown` or `unsupported` returns safe search/missing-capability evidence to the outer AI.
- The outer AI may then solve the goal using existing authorized system/application contracts and
  submit the result through the common verifier and executor.
- A completely successful outer fallback records a reusable, value-free route candidate so the
  inner AI can find the route later.
- The inner AI may handle a larger task through bounded work batches. State and contracts are
  rehydrated between batches; committed earlier batches remain truthful history.
- The outer AI may provide a bounded list of intent-level tasks. The task list is an untrusted
  agenda, not executable authority; the inner AI independently resolves and verifies each task.
- A task does not need a recipe. Resolution checks a current verified recipe first and otherwise
  performs fresh trusted contract discovery. Learning remains separately controlled per successful
  novel route rather than creating a recipe for every one-off task.
- Query, action, receipt, learning, local-disabled, and remote-disabled paths remain usable without
  making a model an authority.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Local structured completion | `src/system/local-ai` | verified | Provider-neutral no-tools completion and Ollama adapter are operational. |
| Outer decision and narration schema | `InteractionOuterProtocol` | accepted foundation | Slice 12F outer application surface. |
| Inner and outer planning | `InteractionPlanner` / `InteractionGateway` | accepted foundation | Both use trusted search, inspection, and the common verifier. |
| Typed non-resolution receipts | interaction receipt store | accepted | Unknown, unsupported, unavailable, unsafe, stale, and needs-input are persisted safely. |
| Exact action execution | interaction execution coordinator and application action runner | accepted | Explicit consent, replay protection, partial-progress truth, and operation linkage pass Slice 12H. |
| Candidate/verified recipes | interaction recipe store/resolver | accepted first format | Successful action-only routes may create candidates; explicit review verifies them. |
| Automatic inner-to-outer fallback | outer application conversation coordinator | missing | The current coordinator makes one delegate/direct choice and stops after delegated non-resolution. |
| Query-contract execution | no owner enabled | missing | The current verifier rejects query steps with `QUERY_CONTRACT_UNSUPPORTED`. |
| Query-result binding | no accepted contract | missing | Later step input is currently fixed at planning time. |
| Task-list and multi-batch continuation | no accepted owner | missing | One interaction currently plans one fixed proposal against one state revision. |

## Dependency tree

```text
Adaptive outer/inner goal execution and reusable learning              [planned]
├─ A. Provider-symmetric outer role                                     [ready]
│  ├─ Existing immutable outer decision/narration schemas               [accepted]
│  ├─ Existing local no-tools completion provider                       [accepted]
│  ├─ Local outer adapter with separate outer role/profile              [missing]
│  └─ Explicit host provider selection and no silent remote fallback    [confirmation required]
├─ B. Inner-first fallback coordinator                                  [depends on A]
│  ├─ Delegate actionable intent to inner                               [accepted primitive]
│  ├─ Return typed inner receipt to outer                               [accepted primitive]
│  ├─ Outer server-mediated direct traversal after non-resolution       [missing coordinator]
│  └─ Same verifier/consent/executor for fallback proposal              [accepted primitive]
├─ C. Query contracts and typed result references                       [depends on B]
│  ├─ Ruleset-neutral query executor registry                           [missing]
│  ├─ Exact query output schema/hash and bounded receipt                [missing]
│  ├─ Immutable result-reference binding into later inputs              [missing]
│  └─ No raw hidden value, arbitrary expression, or model coercion      [required]
├─ D. Bounded task lists and runtime work batches                       [depends on B/C]
│  ├─ Goal/task/batch continuation identity and bounds                  [confirmation required]
│  ├─ Intent-only task agenda with earlier-task dependencies            [confirmation required]
│  ├─ Recipe-first then fresh-discovery resolution per task             [ready]
│  ├─ Fresh state/contract rehydration before every batch               [ready]
│  ├─ Stop/needs-input/unknown/failure/partial/cancel semantics          [ready to specify]
│  └─ Consent scope for a dynamically replanned next batch              [confirmation required]
├─ E. Outer-fallback learning                                           [depends on B-D]
│  ├─ Successful receipt -> value-free candidate                        [accepted primitive]
│  ├─ Query/result values excluded or safely parameterized              [missing]
│  ├─ Deterministic automatic-verification policy                       [confirmation required]
│  └─ Current-authority revalidation on every reuse                     [accepted]
└─ F. Application and provider acceptance                               [depends on A-E]
   ├─ Local outer -> inner success -> narration                         [missing]
   ├─ Local outer -> inner unknown -> outer fallback -> learning        [missing]
   ├─ Second request -> inner verified-route reuse                       [missing]
   ├─ Multi-batch state change/replan/stop evidence                      [missing]
   ├─ Local/remote disabled and no-vector parity                         [missing combined proof]
   └─ Generic and application isolation/full repository evidence        [missing]
```

## Ordered implementation slices and model routing

| Slice | Capability | Default model | Exit gate |
| --- | --- | --- | --- |
| 13A | Local outer provider and explicit provider selection | **Terra High**, Sol review | Local and remote outer providers obey the same no-tools schemas, immutable roles, budgets, and safe failure contract; provider choice never silently sends content remotely. |
| 13B | Inner-first typed fallback coordinator | **Sol High** | Every actionable outer turn delegates first; resolved/needs-input/unknown paths return through one correlated state machine; outer fallback uses only server-mediated traversal and the common verifier. |
| 13C | Query-contract execution and typed result references | **Sol High** | Authorized read-only queries have exact output contracts and receipts; later actions may bind declared result paths without copying hidden/unbounded values or allowing model expressions. |
| 13D | Bounded task lists and runtime work batches | **Sol High** | One goal advances through a bounded intent-level task agenda; each task uses recipe-first/fresh discovery and one or more fresh-state batches with exact continuation evidence, explicit consent policy, deterministic stop conditions, and no unbounded autonomous loop. |
| 13E | Outer-fallback recipe generalization and promotion | **Sol High** | Only completely successful validated routes become value-free candidates; the confirmed review/automatic-verification policy makes eligible routes reusable; query outputs and old entity values cannot poison recipes. |
| 13F | Local/remote/application acceptance | **Sol xhigh** | The complete inner-success, outer-fallback, learned second-use, multi-batch, provider-disabled, isolation, replay, catalog, protocol, build, and full-suite matrix passes. |

## Lowest ready leaf

Slice 13A is structurally ready because it reuses the accepted outer schema and provider-neutral
local completion port. It is not implementation-active until provider-selection semantics and the
exact configuration boundary are confirmed. No runtime artifact is authorized by this dependency
plan.

## Task-list contract direction

The outer AI supplies task identity, bounded intent text, ordering/dependencies, and optional safe
context references. It does not supply trusted contract IDs, effects, authorization, state truth,
success claims, or executable expressions. Dependencies may name only earlier tasks and never imply
cross-task rollback.

For each pending task the inner coordinator:

1. rehydrates current application, state-space, principal, and conversation authority;
2. checks for a current verified recipe and otherwise searches/inspects current trusted contracts;
3. returns needs-input/unknown/unsupported evidence or one exact bounded proposal;
4. obtains the confirmed consent required for that proposal and executes one work batch;
5. records task, batch, action/query, and operation receipts before continuing; and
6. re-evaluates whether the task is complete or another bounded batch is required.

Completed tasks remain immutable history. A failed, cancelled, or unresolved task stops or skips
dependent tasks according to the confirmed policy. Independent tasks may continue only when the
task-list policy and consent permit it. The inner AI cannot mark a task successful from narration;
host-verified receipts determine its terminal state.

## Confirmation gates

Before implementation, confirm:

1. whether `local`, `remote`, and `automatic` outer-provider modes exist, and whether `automatic`
   may fall back across the network or must fail without an explicit remote choice;
2. the local outer model/profile and bounds independently of the smaller inner profile;
3. whether mutation consent applies to every dynamically planned work batch or a bounded whole-goal
   authorization may cover later exact proposals;
4. the task-list bounds, dependency/failure policy, lifecycle vocabulary, and whether independent
   tasks may continue after an unrelated task fails;
5. whether goal/task/batch continuation is process-local or durable, including cancellation/retention;
6. the exact closed query contract, typed result-reference, and redaction shape;
7. whether successful outer fallback remains a candidate pending review or may be automatically
   verified by a deterministic non-model policy; and
8. any new permanent IDs, configuration keys, public fields/kinds, persistence tables, migrations,
   or compatibility aliases required by the selected slices.

## Planning receipt

- Runtime artifacts, database records, migrations, public kinds, and configuration keys created: none.
- Existing Slice 12 behavior is unchanged and remains accepted.
- Deliberate stop: dependency planning and owner links only.
