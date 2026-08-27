# Interaction orchestration Slice 13 dependency plan — adaptive local/remote outer AI and bounded task batches

Status: **complete — Slices 13A–13F accepted**
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
| Automatic inner-to-outer fallback | outer application conversation coordinator | accepted 13B | One correlated inner attempt precedes one eligible typed outer fallback; neither executes. |
| Query-contract execution | no owner enabled | missing | The current verifier rejects query steps with `QUERY_CONTRACT_UNSUPPORTED`. |
| Query-result binding | no accepted contract | missing | Later step input is currently fixed at planning time. |
| Task-list and multi-batch continuation | no accepted owner | missing | One interaction currently plans one fixed proposal against one state revision. |

## Dependency tree

```text
Adaptive outer/inner goal execution and reusable learning              [planned]
├─ A. Provider-symmetric outer role                                     [accepted 13A]
│  ├─ Existing immutable outer decision/narration schemas               [accepted]
│  ├─ Existing local no-tools completion provider                       [accepted]
│  ├─ Local outer adapter with separate outer role/profile              [accepted]
│  └─ Explicit host provider selection and no silent remote fallback    [accepted]
├─ B. Inner-first fallback coordinator                                  [accepted 13B]
│  ├─ Delegate actionable intent to inner                               [accepted primitive]
│  ├─ Return typed inner receipt to outer                               [accepted primitive]
│  ├─ Outer server-mediated direct traversal after non-resolution       [accepted]
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
| 13A | [Local outer provider and explicit provider selection](INTERACTION-ORCHESTRATION-SLICE-13A-IMPLEMENTATION.md) | **Accepted 2026-08-25**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13A-RECEIPT.md) | Local and remote outer providers obey the same no-tools schemas, immutable roles, budgets, and safe failure contract; provider choice never silently sends content remotely. |
| 13B | [Inner-first typed fallback coordinator](INTERACTION-ORCHESTRATION-SLICE-13B-IMPLEMENTATION.md) | **Accepted 2026-08-25**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13B-RECEIPT.md) | Every actionable outer turn delegates first; resolved/needs-input/unknown paths return through one correlated state machine; outer fallback uses only server-mediated traversal and the common verifier. |
| 13C | [Query-contract execution and typed result references](INTERACTION-ORCHESTRATION-SLICE-13C-IMPLEMENTATION.md) | **Accepted 2026-08-25**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13C-RECEIPT.md) | Authorized read-only queries have exact output contracts and receipts; later actions may bind declared result paths without copying hidden/unbounded values or allowing model expressions. |
| 13D | [Bounded task agendas and fresh-state work batches](INTERACTION-ORCHESTRATION-SLICE-13D-IMPLEMENTATION.md) | **Accepted 2026-08-25**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13D-RECEIPT.md) | One goal advances through a bounded intent-level task agenda; each task uses recipe-first/fresh discovery and one or more fresh-state batches with exact continuation evidence, explicit consent policy, deterministic stop conditions, and no unbounded autonomous loop. |
| 13E | [Safe outer-fallback learning and promotion](INTERACTION-ORCHESTRATION-SLICE-13E-IMPLEMENTATION.md) | **Accepted 2026-08-25**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13E-RECEIPT.md) | Only completely successful correlated action-only routes become value-free candidates; deterministic host verification makes eligible routes reusable, while query outputs and old entity/input values remain excluded. |
| 13F | [Combined adaptive-AI acceptance](INTERACTION-ORCHESTRATION-SLICE-13F-IMPLEMENTATION.md) | **Accepted 2026-08-26**; [receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-13F-RECEIPT.md) | The complete inner-success, outer-fallback, learned second-use, multi-batch, provider-disabled, isolation, replay, catalog, protocol, build, and full-suite matrix passes. |

## Next leaf

Slices 13A–13F and the complete Slice 13 extension are accepted.

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

## Remaining confirmation gates

Provider selection, task batching, and Slice 13E's deterministic automatic-verification principal,
codes, action-only value-free eligibility, and safe route-guidance observation are confirmed. Before
later work, confirm only any new permanent IDs, public/configuration fields, persistence changes,
compatibility aliases, or structural query-recipe format not already covered by Slices 13A–13E.

## Planning and delivery boundary

- Slices 13A–13C have runtime artifacts and accepted evidence linked above.
- Slice 13C has one query-receipt migration and completed catalog/protocol/full-suite evidence.
- Slice 13C added the application-owned `query` catalog kind through existing interaction surfaces;
  it added no MCP tool, route, authorization capability, or game contract.
- Slices 13A–13D have runtime artifacts and accepted evidence linked above.
- Slices 13E–13F have accepted runtime artifacts and evidence; the Slice 13 extension is complete.
