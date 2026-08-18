# Local intent routing and safe action pipelines

## Goal

Reduce the mechanical lookup work a game-master LLM performs so it can spend more context on the
scene, characters, and narration. A local helper may interpret an incoming natural-language intent,
retrieve the likely rules and required context, and return an execution-ready plan.

The helper is an adviser, not an authority: it must never silently choose an outcome, generate an
unreviewed write, or bypass the existing procedure, audit, action-selection, and transaction rules.

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

### 4. Optional Ollama integration

Only after deterministic routing is measured, add an optional host-level Ollama adapter with:

- explicit local endpoint and model configuration; no automatic download or startup;
- health check, timeouts, model/version identification, and a deterministic fallback;
- structured output validated against the route-record schema;
- use limited to intent normalization, candidate re-ranking, and explanation—not final rule choice
  or generation of executable writes;
- audit fields recording that local-model assistance was used, the model identifier, and the
  deterministic candidates it was allowed to rank.

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
Ollama, malformed model output, and deterministic fallback. Confirm a route itself changes no
state; confirm an action sent from a route retains the normal mechanic version, RNG seed, effects,
and audit history. Test vector search against a known retrieval-miss corpus before it can influence
ranking.

## Scope boundaries

This plan does not add another MCP tool, let an LLM execute shell or SQL, make the server stateful,
or replace the game-master's storytelling judgement. It is a route-and-retrieve aid, not an
autonomous game director.
