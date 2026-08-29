# Knowledge and facts roadmap

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slices 1–6C complete; production authentication remains externally blocked**
Last reviewed: 2026-08-21

## Boundary

Knowledge keeps three dimensions separate:

1. what the world asserts and whether it is current/contested/superseded;
2. what a knower knows, suspects, believes, doubts, or has learned; and
3. who the application authorizes to retrieve a perspective.

Descriptive visibility/sensitivity is not authorization. Search ranking and model-generated prose
are not truth. Canonical state stays in SQLite; lexical/vector indexes are rebuildable projections.

## Durable model

- Facts, rumours, secrets, and clues keep their existing World Feature 4 meanings.
- Classification, dissemination baseline, individual epistemic state, provenance, acquisition,
  validity intervals, contradiction, and supersession are separate dimensions rather than combined
  “fact types.”
- An explicit individual exception can override a wider baseline without copying a record to every
  actor.
- Learning is an atomic, sourced, replay-safe world change. Changing who knows something never
  changes whether it is true.
- Historical/superseded propositions remain inspectable but are excluded from current-result views.
- Audience/perspective is backend-resolved; callers cannot claim another actor or widen scope.

## Search and local reasoning

- One canonical bounded document projection feeds deterministic SQLite FTS and optional embeddings.
- Exact IDs/terms and filters remain available; vector similarity only ranks authorized candidates.
- The configured development stack uses loopback Ollama, `qwen3-embedding:4b` for embeddings, and
  `qwen3:8b` for bounded normalization/reranking/answer/read-plan work.
- Provider failure falls back without blocking play; no model output commits state by itself.
- `sqlite-vec` is a replaceable derived-index implementation. Rebuilding it changes no canonical
  World record.
- The bounded read agent receives a schema-bound plan and capped results. Tool-route proposals and
  semantic intents still pass through normal owner procedures/actions.

## Evidence index

| Slice | Delivered | Evidence |
| --- | --- | --- |
| Foundation | Local infrastructure and semantic decisions | [infrastructure receipt](KNOWLEDGE_AND_FACTS-SOL-INFRASTRUCTURE-RECEIPT.md), [confirmation](KNOWLEDGE_AND_FACTS-SOL-SEMANTIC-CONFIRMATION.md) |
| 1 | Classification and explicit knowledge state | [receipt](KNOWLEDGE_AND_FACTS-SLICE-1-RECEIPT.md) |
| 2 | Interaction/acquisition | [confirmation](KNOWLEDGE_AND_FACTS-SLICE-2-CONFIRMATION.md), [receipt](KNOWLEDGE_AND_FACTS-SLICE-2-RECEIPT.md) |
| 3 | Time, contradiction, supersession | [confirmation](KNOWLEDGE_AND_FACTS-SLICE-3-CONFIRMATION.md), [receipt](KNOWLEDGE_AND_FACTS-SLICE-3-RECEIPT.md) |
| 4 | Lexical projection/search | [receipt](KNOWLEDGE_AND_FACTS-SLICE-4-RECEIPT.md) |
| 5 | Vector/hybrid retrieval | [receipt](KNOWLEDGE_AND_FACTS-SLICE-5-RECEIPT.md) |
| 5B | Local fact answering/background proposals | [receipt](KNOWLEDGE_AND_FACTS-SLICE-5B-RECEIPT.md) |
| 5C | Bounded read agent and route proposals | [receipt](KNOWLEDGE_AND_FACTS-SLICE-5C-RECEIPT.md) |
| 6 | Perspective-safe authorized retrieval core | [readiness](KNOWLEDGE_AND_FACTS-SLICE-6-READINESS.md), [receipt](KNOWLEDGE_AND_FACTS-SLICE-6-RECEIPT.md) |

## Acceptance invariants

- Truth, belief, acquisition, sensitivity, and authorization cannot silently change one another.
- Scope inheritance follows explicit owner semantics; current membership is not guessed as taught
  knowledge.
- Hidden or unauthorized candidates are filtered before model/vector exposure and never leak through
  counts, snippets, explanations, or fallback paths.
- Search results are bounded, stable, hydrated from canonical state, and source/version traceable.
- Rebuilding derived indexes yields the same corpus without canonical writes.
- Existing World knowledge fixtures/actions and campaign consumers retain meaning unless an explicit
  migration is approved.
- Failed providers, malformed plans, stale generations, and unauthorized perspective requests fail
  closed without blocking ordinary deterministic retrieval.

## Remaining boundary

Development may use the documented loopback bridge in
[DEVELOPMENT_KNOWLEDGE_ACCESS.md](../DEVELOPMENT_KNOWLEDGE_ACCESS.md). Production/player exposure
remains blocked until a real authenticated principal/audience policy owner is selected and verified.
Do not reinterpret profile data, visibility labels, caller arguments, or model claims as authority.

Any future write orchestration, new epistemic vocabulary, schema migration, public query kind, or
external model/provider crosses a fresh confirmation boundary and needs its own plan and receipt.
