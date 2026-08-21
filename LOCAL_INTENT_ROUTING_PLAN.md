# Local intent routing and safe action pipelines

Status: **The internal standard-profile read chain and action-proposal foundation are implemented;
public query integration, workflow routing, and player authorization remain pending.**

## Goal

Reduce the mechanical lookup work a game-master LLM performs so it can spend more context on the
scene, characters, and narration. A local helper may interpret an incoming natural-language intent,
retrieve the likely rules and required context, and return an execution-ready plan.

The helper is an adviser, not an authority: it must never silently choose an outcome, generate an
unreviewed write, or bypass the existing procedure, audit, action-selection, and transaction rules.

The detailed consumer plan for a remote story model submitting semantic intents which the backend
processes serially is [Story plan orchestration](storytelling/story-plan-orchestration/STORY_PLAN_ORCHESTRATION_PLAN.md). It consumes this
router without turning routing into a generic workflow engine.

## Non-negotiable safety boundary

- Game-changing work remains an existing `commit(kind: "action")` or typed `commit(kind:
  "effects")` call. The kernel records its operation, rule version, seed, and effects as it does
  today.
- The router may execute read-only lookups internally. It returns proposed calls, candidates,
  missing facts, and reasons; it does not execute arbitrary commits.
- A mechanic never calls Ollama, a vector store, the shell, or the network. Local-model and
  retrieval work stays in the host's routing layer, outside the JavaScript sandbox.
- A failure, unavailable local model, or weak retrieval result falls back to deterministic
  category and full-text search. It never fabricates a matching rule.

## Local-model runtime profiles

The host chooses a named profile through configuration. It never chooses the largest installed
model automatically, downloads a model, or silently changes profiles after an error. A profile is
part of the route/audit evidence so a result can be reproduced and performance can be compared.

| Profile | Suggested model | Permitted work | When it says “unknown” |
| --- | --- | --- | --- |
| off | none | Deterministic category, exact-id, and FTS retrieval only | Always use deterministic result |
| micro | qwen3:0.6b | Classify a request into a fixed task family; normalize a short search phrase | Fall back to FTS or return the request to the story model |
| light | qwen3:1.7b | Micro work plus extraction of IDs/roles already present in supplied structured context | Fall back to FTS or return named missing facts |
| standard | qwen3:8b | Normalize intent, re-rank supplied candidates, and prepare a schema-bound read-only route record | Return the deterministic candidates and ask the story model to choose |
| strong-local | qwen3:14b | A bounded second opinion on ambiguous route plans after standard fails validation or reports ambiguity | Return no proposed write; leave the decision to the story model |

The suggested models are defaults, not part of the wire contract. The configuration names the
actual Ollama model and allowed task classes, so a self-hosted deployment can substitute another
model only after it passes the same evaluation suite.

The Raspberry Pi profile should normally be micro, optionally light on a well-provisioned device.
It keeps embeddings disabled, uses FTS plus confirmed procedure aliases, sends tiny contexts, makes
one request at a time, and treats “unknown” as a correct answer. It must remain useful with off,
because a slow local model is never a required dependency for play.

### Request contract and resource budget

Every local-model request is non-streaming, has thinking disabled, temperature zero or near zero,
an explicit JSON Schema response format, a small output-token cap, a hard timeout, and a maximum
prompt size. The host independently parses and validates the returned JSON; a schema-shaped answer
is still untrusted data.

The adapter records the selected profile, configured model, model reported by Ollama, elapsed/load
time, input/output token counts when supplied, timeout/validation outcome, and fallback path. Do
not record a reasoning trace or raw unbounded prompt. The model gets no Ollama tool definitions:
the host performs all allowed reads itself and offers the model only the resulting bounded data.

Desktop profiles may retain their selected model briefly to avoid repeated load latency. Resource-
constrained profiles unload it after each request or use a short keep-alive. Embedding workloads
must be separately configured and run in small low-priority batches; they must not evict the
interactive routing model during a play session.

## Phased delivery

### 1. Observe before automating

Add no new runtime dependency. Use the existing operation history to measure intent searches,
candidate counts, unmatched actions, incorrect rule selections, and the number of host calls per
resolved action. Do not build a router until those records show that the current path is a material
burden.

### 2. Deterministic routing plan

Add a `query(kind: "route")` semantic operation behind the existing `query` tool, governed by a
new `procedure.system.intent-routing` contract.

Inputs: natural-language intent, optional actor IDs, ruleset scope, optional category paths, and
known scene facts.

Output: a typed, read-only route record containing:

- ranked procedure and mechanic candidates, with the matching evidence;
- required entity roles and component definitions;
- missing information as named questions;
- an execution-ready `commit(kind: "action")` payload only when all prerequisites are known;
- a confidence tier and deterministic fallback explanation.

The router performs metadata filtering, hierarchical category filtering when available, and the
existing full-text matcher. The game-master LLM either answers named missing facts or sends the
returned action payload unchanged.

### 3. Stateless interaction pipeline

When a rule needs player input, use the planned stateless `ctx.ask` protocol: return named
questions, then rerun the same action from the beginning with an `answers` object. Do not introduce
resume tokens or server-held workflow state.

The router can include read-only steps in its response, but a pipeline must stop before every
write boundary. This preserves explicit approval, dry-run requirements, and a complete audit.

### 4. Optional Ollama integration and profile evaluation

Only after deterministic routing is measured, add an optional host-level Ollama adapter and begin
with the off and light profiles. Add standard only after it shows a measured quality benefit;
reserve strong-local as an explicit escalation profile, never the routine default.

- explicit local endpoint and model configuration; no automatic download or startup;
- health check, model availability check, timeouts, model/version identification, concurrency
  limit, context/output limits, and a deterministic fallback;
- thinking disabled and a schema-constrained, non-streaming output validated against the
  route-record schema;
- use limited to intent normalization, candidate re-ranking, and explanation—not final rule choice
  or generation of executable writes;
- audit fields recording that local-model assistance was used, the model identifier, and the
  deterministic candidates it was allowed to rank.

Build a local evaluation runner that executes the same retrieval/routing corpus under off, micro,
light, standard, and strong-local, measuring schema-valid response rate, correct procedure recall,
false-positive rate, “unknown” quality, p50/p95 latency, prompt size, and fallback rate. A lower
profile is accepted only for the task classes where it meets its stated quality gate; it is not
judged against tasks it is forbidden to perform.

**Internal foundation implemented 2026-08-21:** the `standard` `qwen3:8b` profile now has closed
structured task classes for bounded knowledge read planning/answering and existing-action route
selection. The host executes the read allowlist, validates citations and model identity, constructs
route payloads from caller values, rechecks content hashes, and stops before every write. This does
not yet add `query(kind: "route")`, install the planned procedure IDs, or expose the feature through
MCP; those changes belong to the later public integration boundary.

### 5. Vector retrieval only when its trigger fires

Keep the current SQLite plus full-text approach until evidence meets an existing architecture
trigger: roughly 150 procedure contracts, roughly 200 mechanics, or logged retrieval misses for
rules known to exist.

At that point, prefer a local SQLite-compatible vector extension so campaign portability remains
one file. Index record ID, version, scope, category path, source-field fingerprint, embedding model
identifier, and embedding. Combine metadata filters, full-text search, and vector similarity;
return candidates only. Vector distance must never directly choose, create, or execute a rule.

### 6. Reconsider write pipelines last

Do not add a generic `commit(kind: "pipeline")`. The existing action runner already performs an
atomic rule-and-effects transaction, which is the safe multi-step write primitive.

Only if measured sessions show a recurring, well-defined sequence outside one action should the
project design a typed workflow operation. It must use a closed step vocabulary, validate all
preconditions before the first write, have one audit root plus child records, and fail without
partial world state. Arbitrary chained commits remain out of scope.

## Required contracts and tests

Create and verify these contracts before their corresponding feature work:

- `procedure.system.intent-routing`
- `procedure.system.local-model-routing`
- `procedure.system.semantic-retrieval`
- `procedure.system.typed-workflows` only if Phase 6 is approved

Test routing with known exact matches, ambiguous matches, no match, missing role data, unavailable
Ollama, malformed model output, timeout, wrong model, context/output-budget breach, and
deterministic fallback. Run each test against every enabled profile and assert that micro and light
cannot return command plans outside their permitted task families. Confirm a route itself changes
no state; confirm an action sent from a route retains the normal mechanic version, RNG seed,
effects, and audit history. Test vector search against a known retrieval-miss corpus before it can
influence ranking.

## Scope boundaries

This plan does not add another MCP tool, let an LLM execute shell or SQL, make the server stateful,
or replace the game-master's storytelling judgement. It is a route-and-retrieve aid, not an
autonomous game director. Running a low-capability local model is an optional acceleration, never
a correctness requirement.
