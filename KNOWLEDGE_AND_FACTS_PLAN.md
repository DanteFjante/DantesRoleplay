# Knowledge and facts evolution plan

Status: **Slices 1–6C complete; a loopback development bridge is available, while production authentication remains blocked by one named external policy owner**  
Last updated: 2026-08-21

Implementation evidence: [Sol infrastructure receipt](KNOWLEDGE_AND_FACTS-SOL-INFRASTRUCTURE-RECEIPT.md).  
Slice 1 evidence: [knowledge Slice 1 receipt](KNOWLEDGE_AND_FACTS-SLICE-1-RECEIPT.md).  
Slice 2 evidence: [knowledge Slice 2 receipt](KNOWLEDGE_AND_FACTS-SLICE-2-RECEIPT.md).  
Slice 3 proposed semantics: [time, contradiction, and supersession packet](KNOWLEDGE_AND_FACTS-SLICE-3-CONFIRMATION.md).
Slice 3 evidence: [knowledge Slice 3 receipt](KNOWLEDGE_AND_FACTS-SLICE-3-RECEIPT.md).
Slice 4 evidence: [knowledge Slice 4 receipt](KNOWLEDGE_AND_FACTS-SLICE-4-RECEIPT.md).
Slice 5 evidence: [knowledge Slice 5 receipt](KNOWLEDGE_AND_FACTS-SLICE-5-RECEIPT.md).
Slice 5B evidence: [knowledge Slice 5B receipt](KNOWLEDGE_AND_FACTS-SLICE-5B-RECEIPT.md).
Slice 5C evidence: [knowledge Slice 5C receipt](KNOWLEDGE_AND_FACTS-SLICE-5C-RECEIPT.md).
Slice 6 readiness: [authorization contract and Terra handoff](KNOWLEDGE_AND_FACTS-SLICE-6-READINESS.md).
Slice 6 core evidence: [authorization and perspective-safe retrieval receipt](KNOWLEDGE_AND_FACTS-SLICE-6-RECEIPT.md).
Local temporary access: [development knowledge access](DEVELOPMENT_KNOWLEDGE_ACCESS.md).
Approved semantics: [Sol semantic packet](KNOWLEDGE_AND_FACTS-SOL-SEMANTIC-CONFIRMATION.md).

## Goal

Store a large body of world facts while keeping three questions independent:

1. **What is asserted, claimed, inferred, or evidenced?**
2. **Who knows or believes it, and how did they learn it?**
3. **Who is allowed to retrieve it through this application?**

The first is world truth and lore. The second is durable world state about knowers. The third is
authorization. They interact, but none is a substitute for another.

This plan evolves the verified World Feature 4 fact/rumour/secret/clue model. It does not reinterpret
the existing `visibility` field as access control, and it does not make vector similarity authoritative.

The intended local deployment is now confirmed: Ollama at its configured loopback endpoint,
`qwen3-embedding:4b` for embeddings, and `qwen3:8b` for bounded background reasoning, query
normalization, reranking, answer synthesis, and later tool-route proposals. These are separate roles;
the 8B completion model does not replace the embedding model.

## Core decision: dimensions, not a growing list of combined types

“Global”, “regional”, “faction”, “secret”, and “learned after an interaction” are not mutually
exclusive types. One proposition can be a regional secret, believed by one outsider after an
interaction, doubted by another, and later become public. Store these as separate dimensions.

### 1. Epistemic kind — what sort of statement is this?

| Kind | Meaning | Current owner |
| --- | --- | --- |
| Asserted truth | Canonical world state the game treats as true. | `game.core.world.fact` |
| Claim / rumour | A proposition whose truth is unresolved, confirmed, or disproved. | `game.core.world.rumour` |
| Hidden truth | Canonical truth deliberately concealed. Long term, “hidden” is sensitivity rather than a different truth shape. | `game.core.world.secret` |
| Evidence / clue | An observable record or object supporting or contradicting another proposition. | `game.core.world.clue` |
| Belief | What a knower accepts, including mistaken beliefs. | New knowledge-state layer, not a new copy of world truth |
| Hypothesis / question | A live inference that has not become an asserted truth. | Later extension when a consumer needs it |
| Rule / generalization | Stable lore such as “the sun is hot” or “silver harms these spirits”. | Asserted truth with a classification facet |
| Prediction / intention | A statement about a possible future, never presented as settled current truth. | Later extension; preserve modality explicitly |

### 2. Subject-matter classification — what is the fact about?

Use searchable facets rather than separate component definitions for every category:

- state or property;
- event or history;
- identity;
- relationship or allegiance;
- location or route;
- capability or weakness;
- rule, custom, law, taboo, or procedure;
- quantity or measurement;
- intention, plan, or prediction;
- explicit negative fact, where absence must be asserted rather than inferred.

These facets help filtering and ranking but do not decide truth.

### 3. Dissemination baseline — who normally knows it?

The baseline can target one or more scope entities:

- the whole world (common knowledge);
- a region or settlement;
- a faction, culture, religion, profession, language community, or institution;
- a party;
- named actors;
- nobody by default (discoverable or tightly held information).

Region and faction are ordinary scope entities connected to the knowledge record. Do not copy
region/faction IDs into prose or arrays inside the fact.

### 4. Individual epistemic state — what does this knower think?

An explicit state connects a knower to one knowledge record:

| State | Meaning |
| --- | --- |
| `known` | The knower has accepted, recallable knowledge. |
| `familiar` | The knower recognizes the subject but lacks the full proposition. |
| `suspected` | The knower considers it plausible but unverified. |
| `believed` | The knower accepts a claim; it may be false in canonical world state. |
| `doubted` | The knower has encountered the claim but does not accept it. |
| `disbelieved` | The knower affirmatively believes the claim is false. |
| `unknown` | Explicit exception overriding an inherited global/region/faction baseline. |

Forgetting is a transition to `unknown` with recorded cause/time, rather than a second simultaneous
state. The event ledger preserves the prior state.

### 5. Sensitivity and authorization

Sensitivity describes intended handling: open, discreet, confidential, or secret. It does not grant
or deny database access. A true player-safe response still depends on the authenticated audience
policy already identified by Campaign Feature 5.

Secrets may have explicit holders, compartments, and cover stories. “Nobody knows” must mean no
in-world knower is recorded; the trusted GM/system can still retrieve the canonical hidden truth.

### 6. Time, provenance, and dependency

Every proposition needs enough structure to distinguish:

- current, historical, superseded, conditional, and future-facing statements;
- authored source, observation, interaction, document, event, or inference;
- support, contradiction, derivation, and supersession links;
- world scope and subjects it is about.

Do not overwrite history when a current fact stops being true. Close its validity interval or
supersede it, then record the replacement. Derived facts should name their authoritative inputs and
be recomputed rather than becoming an independent source of truth.

## Recommended storage shape

The existing knowledge entity remains the canonical proposition/evidence record. New information is
added around it through relationships and, where lifecycle/provenance is rich, small state entities.

```text
world/region/faction/party/actor ── dissemination baseline ──▶ knowledge record
actor/party                    ── explicit epistemic state ──▶ knowledge record
knowledge record               ── about ─────────────────────▶ subject entity
clue/evidence                  ── supports/contradicts ──────▶ knowledge record
knowledge record               ── supersedes/depends-on ─────▶ knowledge record
acquisition record             ── knower/fact/source ─────────▶ actor, knowledge, event/interaction
```

### Common knowledge and exceptions

For “the sun is hot”, store one world-level baseline, not one `known` row per actor. If Edda does not
know it, store one explicit `unknown` override from Edda to that fact. Effective knowledge is resolved
in this order:

1. explicit current actor state;
2. durable personal acquisition;
3. applicable party/faction/community baseline;
4. applicable location/region baseline;
5. world baseline;
6. otherwise unknown.

A response may expose a derived `hasExceptions` flag for convenience. The authoritative data must be
the exception records themselves; a stored boolean alone cannot answer who is excepted and can drift
out of sync.

### Regional and faction knowledge

A region/faction baseline means a current member or resident normally knows the proposition. An
explicit actor state overrides it. The owning contract must choose one of two semantics per baseline:

- **current-scope:** knowledge is inherited only while the actor is a current member/resident;
- **taught-on-entry:** joining or an interaction creates a durable personal acquisition.

This prevents silently deciding that a former faction member either forgets everything or remembers
everything.

### Learned through interaction

An accepted interaction/discovery creates one acquisition with:

- knower;
- knowledge record;
- resulting epistemic state;
- source event/interaction identity;
- world time when available;
- method such as observed, told, read, inferred, taught, or recalled.

It does not store the whole dialogue transcript. Replaying the same source must not create duplicate
knowledge or move a stronger state backward. Corrections and forgetting are explicit transitions.

### False, contested, and incomplete knowledge

Do not create “false facts” as canonical truths. Store the proposition as a claim/rumour and record
that an actor believes it. Different actors can then know, believe, doubt, or reject the same claim.
Contradictory claims can coexist and point to the same subject; canonical truth may remain unresolved.

Partial knowledge should either use `familiar` or point to a deliberately coarser proposition. Never
reveal a secret summary and attempt to hide only a few words after retrieval.

## Proposed vocabulary boundary

The following names describe the intended responsibilities but are **not confirmed permanent IDs**:

| Proposed responsibility | Shape |
| --- | --- |
| Knowledge classification | Companion component on a fact/rumour/secret/clue: subject-matter facet, modality, validity, sensitivity. |
| Dissemination baseline | Directed relationship from world/region/faction/community/party/actor to a knowledge entity, with closed inheritance semantics. |
| Explicit epistemic state | Directed relationship or state entity connecting one knower to one knowledge entity, with a closed state and precedence over baselines. |
| Acquisition | Entity used only when source, time, method, replay identity, or lifecycle must be independently inspected. |
| `contradicts`, `supersedes`, `depends-on` | Directed knowledge-to-knowledge relationships with closed endpoint and same-world rules. |

Before implementation, confirm whether simple current epistemic state lives in relationship data or
always gets an entity. Prefer relationship data for scale; introduce acquisition entities only for
auditable learning events. Confirm all permanent names and schemas together.

## Search and retrieval architecture

Facts can become numerous, so retrieval should be hybrid from its first public knowledge-search
slice:

```text
authenticated perspective and world scope
  → permitted/effectively-known candidate IDs
  → metadata filters
  → SQLite FTS5 lexical candidates + vector-similarity candidates
  → deterministic rank fusion
  → hydrate canonical entities/relationships
  → final authorization and state recheck
```

### Search document

Index one atomic knowledge record per document. Suggested derived fields are:

- stable knowledge entity ID and world ID;
- epistemic kind, subject facets, status, sensitivity, and validity;
- canonical summary plus separately authored aliases/search terms;
- subject entity names and stable IDs;
- provenance/source labels safe for the resolved perspective;
- embedding model ID, model version, content hash, and indexed timestamp.

Do not embed whole sessions, raw event payloads, or mixed public-and-secret documents. Keep facts
atomic enough that retrieving one document never leaks a neighboring secret.

### SQLite first

Keep the canonical entities/components/relationships/event ledger in SQLite. Add:

- an FTS5 projection for exact names, uncommon terms, and explainable lexical matches;
- a replaceable vector projection, preferably `sqlite-vec` while the project remains single-machine;
- a projection/rebuild worker that derives both indexes from canonical world state.

The index is disposable: it may be rebuilt from canonical rows, uses content hashes to update only
changed documents, and never becomes the authority for truth, knowledge, or permission. If the
embedding model changes, rebuild under the new model version; do not compare vectors from different
models.

### Confirmed Ollama embedding profile

The current machine has `qwen3-embedding:4b` installed. Ollama reports embedding capability and a
2,560-element output; a local batch request to `/api/embed` returned two 2,560-element vectors. Use
the following initial configuration, with values supplied through ordinary host configuration rather
than hard-coded in game mechanics:

```text
provider: ollama
endpoint: http://localhost:11434
embeddingModel: qwen3-embedding:4b
dimensions: 2560
distance: cosine
queryModel: qwen3:8b
```

At startup the provider checks endpoint health, exact configured model availability, embedding
capability, and returned dimensions. Persist the configured name, Ollama-reported model identity or
manifest digest, vector dimensions, and canonical search-document hash with every index generation.
If any identity/dimension changes, mark the affected generation stale and rebuild it; never insert a
new vector into an incompatible generation.

Call `/api/embed` with arrays for bounded backfill batches and a single string for interactive query
embedding. Use the same canonical text builder and model for indexing and querying. Ollama returns
normalized vectors, but the provider must validate finite values and the exact dimension before
storage. Timeouts, an unavailable server, or malformed vectors fall back to FTS and never block play.

`sqlite-vec` is the selected first provider because it keeps vectors in SQLite and supports `vec0`
virtual tables. The Sol spike proved the pinned Windows x64 DLL, extension loading through the
repository's SQLite provider, persistent generation/world-partitioned indexes, backup/restore, and
clean failure when the native extension is absent. The binary is still an explicit deployment input,
not a committed artifact. `IKnowledgeVectorIndex` remains provider-neutral so the implementation can
be replaced without changing world or query contracts.

### Security and ranking invariants

- Resolve the caller/perspective before search. A request parameter alone never grants a GM or actor
  viewpoint.
- Filter to allowed/effectively-known IDs before content is returned or reranked. Recheck after
  hydration to close stale-index races.
- Prefer a local embedding model for hidden lore. Sending secret text to a remote embedding API
  requires an explicit deployment/privacy decision.
- Vector distance returns candidates only. It never confirms a rumour, resolves a contradiction,
  reveals a clue, or proves that an actor knows something.
- Combine lexical and vector ranks with a deterministic method such as reciprocal-rank fusion, then
  use stable ID as the final tie-breaker.
- Exact ID lookup remains available to an authorized trusted GM even if semantic search misses.
- Record model/version and enough rank diagnostics for tests and debugging without logging hidden
  text into a lower-trust channel.

## Local fact query and tool orchestration

`qwen3:8b` is the standard local reasoning profile. It may reduce work performed by the story model,
but it remains an untrusted adviser outside the mechanics sandbox. There are three deliberately
different execution modes.

### Mode A — host-built RAG, first implementation

The host resolves the authenticated/trusted perspective, runs hybrid knowledge search, hydrates a
small authorized fact set, and sends only that bounded set to `qwen3:8b`. The model returns a strict
schema containing:

- normalized question and optional closed metadata filters;
- selected fact IDs from the supplied candidates only;
- answer statements, each citing supporting fact IDs and their epistemic kind;
- unresolved or contradictory points;
- `unknown` when the supplied evidence is insufficient.

The model receives no tools in this mode. It cannot ask for a hidden record, invent an ID, or turn a
rumour/belief into canonical truth. The host validates every cited ID and fact classification before
returning the answer.

### Mode B — bounded read-only fact agent, later implementation

Ollama supports multi-turn tool calling, so a later profile may give `qwen3:8b` a small host-owned
allowlist rather than the complete MCP surface:

```text
knowledge.search   semantic/lexical search in the already-bound perspective and world
knowledge.get      hydrate up to a fixed number of returned knowledge IDs
entity.get         read approved subjects named by returned facts
procedure.get      read an exact governing procedure selected by the host/router
workflow.get       inspect an exact registered workflow candidate
```

Perspective, campaign, world, and authorization are injected by the host and are not model-selectable
arguments. The loop has a fixed request count, wall-clock deadline, result-size budget, no shell/SQL/
network tools, no arbitrary IDs, and no commit capability. Every result is schema-validated. The final
answer must cite hydrated fact IDs and stop with `unknown` on exhausted budget or insufficient proof.

This mode is an explicit future revision to `LOCAL_INTENT_ROUTING_PLAN.md`, whose current safe first
version intentionally gives Ollama no tools. Mode A ships before Mode B and remains the fallback.

### Mode C — tool-chain and write orchestration

The 8B model may propose a typed route; it never executes an arbitrary chain of MCP calls. Its route
result is closed data:

```text
goal
requiredReads[]
candidateProcedureIds[]
candidateMechanicIds[]
candidateWorkflowId?       exact registered workflow only
missingFacts[]
proposedActionPayload?     one schema-valid action proposal, never auto-committed
confidence: exact | strong | ambiguous | none
```

The host performs permitted reads and may iterate Mode B. At the first write boundary it stops and
returns the validated proposal to the trusted story model/human caller. Multi-step writes use only a
registered, versioned, closed workflow from `EXECUTABLE_WORKFLOW_PLAN.md`; the local model may select
an eligible workflow candidate but cannot invent steps, branches, SQL, effects, or nested commits.
The workflow runner—not Ollama—owns validation, transactionality, events, rollback, and audit.

### Background task allocation

| Work | Model/runtime | Authority boundary |
| --- | --- | --- |
| Initial/backfill/query embeddings | `qwen3-embedding:4b` via `/api/embed` | Derived index only; never authors text or state. |
| Query normalization and candidate reranking | `qwen3:8b`, structured output | May rank supplied authorized candidates only. |
| Fact answer synthesis | `qwen3:8b`, Mode A then B | Every assertion cites hydrated fact IDs; no unsupported answer. |
| Alias/tag/duplicate/contradiction suggestions | `qwen3:8b` background proposal | Suggestions are reviewable and do not change canonical facts automatically. |
| Tool-route proposal | `qwen3:8b`, Mode C | Stops before writes; only registered workflow IDs or one validated action payload. |
| Mechanical outcome, visibility decision, transaction, event | deterministic engine | Never delegated to Ollama. |

Run embedding backfill and 8B background proposals through separate bounded queues with concurrency,
timeout, cancellation, retry, and stale-input checks. Interactive play has priority. A job records
model identity, input fingerprint, safe output summary, status, and fallback; it does not retain
chain-of-thought or an unbounded prompt. Recheck the source fingerprint before accepting any
background result so an old job cannot annotate or index revised knowledge.

## Implementation slices

### Slice 0 — semantic confirmation and compatibility map

Confirm the dimensions, state precedence, group inheritance modes, validity semantics, proposed
permanent IDs, and whether `secret` remains a distinct compatibility component. Map every existing
Feature 4 fixture to the new model without changing current bytes.

**Exit:** examples for global-with-exception, regional, faction, interaction-learned, secret,
mistaken belief, historical, and superseded facts all have one unambiguous representation.

### Slice 1 — baseline and explicit knowledge state

Add classification/baseline/state contracts and governed recording/correction paths. Extend focused
fixture validation for scope, endpoints, precedence, duplicates, and cross-world faults. Add a fixed
trusted-GM effective-knowledge reader; do not claim player authorization.

**Implemented 2026-08-21:** `game.core.world.knowledge.classification`,
`game.core.world.knowledge.baseline`, and `game.core.world.knowledge.state`; the trusted-host
`IKnowledgeStateCoordinator`; and compatible world/region/faction/exception fixtures. The reader
resolves explicit actor state, faction baseline, containing-region baseline, world baseline, then
derived `unknown`. It remains internal to the host and is not authorization or an MCP query surface.

**Exit:** a global fact is inherited by all fixture actors except one explicit `unknown` override;
regional and faction facts resolve correctly for insiders/outsiders; existing Feature 4 behavior is
unchanged.

### Slice 2 — acquisition through interaction

Add the acquisition record and one semantic knowledge-learned event only after an actual interaction
owner exists. Make learning atomic with the interaction consequence, idempotent on replay, and
monotonic unless a correction explicitly authorizes a downgrade.

**Exit:** one interaction teaches one actor a previously unknown fact, persists source/method,
survives restart, and changes no other actor or underlying truth.

**Implemented 2026-08-21:** `game.core.world.interaction` is the minimal accepted/void durable
source owner. `game.core.world.knowledge.acquisition` records closed learning method/result state
and connects to exactly one world, knower, knowledge record, and source interaction. The trusted
host `IKnowledgeAcquisitionCoordinator` creates a new accepted interaction and all acquisitions in
one SQLite transaction, treats `(source, knower, knowledge)` as replay-safe, and only strengthens
the current explicit state. This remains an internal host boundary; it does not infer interactions
from the event ledger, create a generic interaction engine, or expose player/MCP retrieval.

### Slice 3 — time, contradiction, and supersession

Add validity and proposition-to-proposition links. Publish deterministic current-as-of and history
reads. Preserve old propositions rather than rewriting them.

**Exit:** current, historical, disputed, false-belief, and superseded cases return distinct and
truthful projections.

**Semantic discovery complete 2026-08-21:** the existing root-owned monotonic world clock is the
only valid in-world time authority. The proposed Slice 3 contract uses a separate validity companion
component plus same-world/same-subject contradiction and supersession links; it preserves existing
records as atemporal until explicitly initialized.

**Implemented 2026-08-21:** `game.core.world.knowledge.validity` stores an optional inclusive/
exclusive root-clock interval without changing existing knowledge payloads. The trusted-host
`IKnowledgeTimelineCoordinator` provides bounded as-of projections, canonical contradiction links,
and same-subject, adjacent-interval supersession. Existing Feature 4 records remain atemporal until
explicitly initialized; no actor state, authorization, MCP surface, or search behavior changes.

### Slice 4 — lexical search projection

Add a knowledge-specific FTS5 projection and bounded trusted-GM search surface with world/kind/time/
subject filters. Prove incremental update and full rebuild produce identical results.

**Exit:** exact terms, names, aliases, and subject filters retrieve many facts deterministically;
stale, archived, wrong-world, and disallowed records are handled by contract.

**Implemented 2026-08-21:** `IKnowledgeLexicalIndex` is a replaceable derived-index boundary and
the SQLite host uses its bundled FTS5 capability to project trusted-GM knowledge documents. The
projection is rebuildable per world or incrementally upserted, accepts only literal normalized
terms, and applies bounded world/kind/subject/status/time filtering. The trusted
`IKnowledgeLexicalSearchCoordinator` always rechecks lexical candidates against canonical world
records and the Slice 3 as-of timeline before returning a concise hit. No search result is an
authorization decision, and no player/MCP surface exists yet.

### Slice 5 — vector and hybrid search

Use the confirmed `qwen3-embedding:4b` Ollama profile and the now-proven provider-neutral persistent
`sqlite-vec` foundation. Add the canonical text builder, production 2,560-dimension generations,
batch backfill, candidate caps, deterministic fusion, quality evaluation, rebuild tooling, and
FTS-only fallback. Stale-generation and model-identity rejection already exist in the foundation.

**Exit:** a fixed evaluation set demonstrates materially better semantic recall than FTS alone,
with no visibility leak, no canonical writes, deterministic tie behavior, acceptable latency, and
successful backup/restore on the supported Windows runtime.

**Implemented 2026-08-21:** one canonical atomic document source now feeds both FTS5 and embeddings.
The trusted `IKnowledgeHybridSearchCoordinator` supports forced per-world rebuilds, content-hash-aware
synchronization, bounded batch embedding, stable model-identity generations, exact-ID lookup, and
deterministic reciprocal-rank fusion. Every fused candidate is rehydrated and rechecked against
canonical world state and the Slice 3 timeline. Ollama, sqlite-vec, missing generations, or malformed
results fall back to the complete FTS path. The local `qwen3-embedding:4b` profile passed the fixed
semantic comparison set at 2,560 dimensions. The sqlite-vec DLL remains an explicit pinned deployment
input as approved by the infrastructure boundary; no native binary was committed.

### Slice 5B — local fact answering and background proposals

Add the `qwen3:8b` Mode A adapter with strict structured outputs, candidate-only citations, timeout/
budget enforcement, and deterministic fallback. Add background queues for embeddings and optional
alias/tag/duplicate/contradiction proposals. Do not enable tool calling yet.

**Exit:** representative questions produce answers supported only by authorized hydrated fact IDs;
unavailable/malformed/unsupported cases return the deterministic search result or `unknown`; stale
background work cannot write an index row or proposal for revised input.

**Implemented 2026-08-21:** the optional host-only `qwen3:8b` adapter accepts only closed task
classes and schema-bound, non-streaming, non-thinking requests. Mode A hydrates bounded hybrid-search
candidates, treats their contents as untrusted data, and accepts only answers whose selected records,
statement kinds, and citations exactly match that candidate set. Failure returns deterministic
candidates and `unknown`. Separate bounded embedding and proposal queues support priority, retry,
cancellation, safe status, and source-fingerprint stale rejection. Alias, tag, duplicate, and
contradiction outputs are ephemeral review proposals only; they perform no canonical write.

### Slice 5C — bounded read agent and route proposals

After Mode A quality is accepted, revise the local-routing contract and add the read-only Mode B tool
allowlist plus Mode C route schema. Integrate exact registered workflow discovery only after the
executable-workflow foundation is verified.

**Exit:** the agent can resolve a multi-read fact question within fixed budgets and propose—but not
execute—one valid action or registered workflow. Adversarial prompts cannot obtain arbitrary IDs,
hidden facts, SQL/shell/network access, unregistered steps, or a write.

**Implemented 2026-08-21:** internal Mode B uses one schema-bound read-plan call followed by at most
three host-owned, world/filter/time-scoped knowledge searches and one strictly cited answer call.
Candidate data cannot request a further read. Internal Mode C supplies bounded active mechanic and
procedure candidates, permits the model to affirm only the deterministic top mechanic, then rechecks
versions/content hashes and uses the existing projection resolver without invoking the action runner.
The host constructs a proposal solely from caller-supplied roles and input. No executable workflow
registry exists, so workflow proposals remain disabled rather than being guessed.

### Slice 6 — authorized player/character retrieval

After the real authenticated audience-policy dependency is verified, bind the perspective to the
transport principal and expose only effectively known/permitted facts. Audit every other read route
for bypasses.

**Exit:** GM, party, faction member, regional inhabitant, named actor, outsider, and explicit
exception cases all return exactly their allowed result sets; denied callers learn neither secret
content nor hidden-result counts.

**Ready 2026-08-21:** the repository audit confirms there is no authenticated transport principal or
audience-policy implementation today. The Slice 6 handoff defines policy-first GM/actor grants,
campaign/world binding, effective-state rules, perspective-safe output, allowed-ID FTS before limit,
stale-input rechecks, bypass coverage, and Slices 6A–6D. The already-approved first generation
deferred party scope until a party owner exists, so initial acceptance covers actor-derived
world/region/faction knowledge rather than treating descriptive party labels as authority. Terra may
implement 6A–6C now; 6D remains absent until the external policy owner is selected.

## Acceptance invariants

| Area | Required proof |
| --- | --- |
| Truth separation | Changing who knows a proposition never changes whether it is true; confirming truth never silently teaches everyone. |
| Global exception | One explicit unknown actor overrides a world baseline without copying state to every other actor. |
| Scope | Region/faction/community inheritance follows explicit current-scope or taught-on-entry semantics. |
| Interaction | Learning is atomic, sourced, replay-safe, and survives restart. |
| Belief | A person can believe a disproved claim without the system calling that claim canonical truth. |
| Time | Historical and superseded facts remain inspectable; current queries do not return them as current. |
| Secret handling | Descriptive sensitivity is never treated as authorization, and embeddings do not create a bypass. |
| Search | Metadata + FTS + vector results are bounded, stable, hydrated from canonical state, and quality-tested. |
| Rebuild | Deleting and rebuilding derived indexes changes no canonical world row and yields the same indexed corpus. |
| Compatibility | Existing fact/rumour/secret/clue fixtures, reveal, confirm, graph reads, and campaign consumers retain their current meaning until an explicit migration boundary. |

## Decisions needed before implementation

1. Should “secret” remain a first-class truth component for authoring clarity, or become an asserted
   truth plus sensitivity after a reviewed migration?
2. Which groups besides world, region, faction, party, and actor are needed initially: culture,
   profession, religion, institution, language community?
3. For each group baseline, is knowledge current-membership or taught-on-entry?
4. Are `familiar`, `suspected`, `believed`, `doubted`, `disbelieved`, `known`, and `unknown` the right
   initial states, or should the first slice be smaller?
5. Is world time available for acquisition/validity, or should the first slice record event identity
   and real timestamp only?
6. **Resolved for the initial deployment:** embeddings use loopback Ollama with
   `qwen3-embedding:4b`; provider failures fall back without blocking play.
7. **Resolved for the supported development runtime:** use pinned `sqlite-vec` `v0.1.9` Windows
   x86-64 behind `IKnowledgeVectorIndex`. Release bundling and other runtime targets remain separate.

## Implementation-agent handoff

The plan is detailed enough for a coding agent to implement one confirmed slice at a time, not to
implement the entire feature without review. Before assigning Slice 1, approve the concrete answers
to decisions 1–5 and the permanent component/relationship IDs in the linked semantic packet. The
Slice 5 provider and architecture amendment are approved and its infrastructure foundation exists.

Use Terra for bounded slices with already-confirmed schemas, tests, and exit gates. Use Sol for the
initial semantic/ID review, migrations, authorization/leakage review, native-extension integration,
and cross-plan reconciliation. Every assignment must name exactly one slice, its allowed files,
focused tests, catalog/migration requirements, and stop gate. Completion still requires the repository
workflow: focused tests, catalog validation after catalog changes, full suite at feature acceptance,
and a protocol walk only when the public MCP surface or dependency registration changes.

No catalog schema, permanent knowledge ID, migration, or public query surface should be implemented
until the semantic packet is confirmed. The approved optional vector foundation is derived-only and
crosses none of those world-state boundaries.
