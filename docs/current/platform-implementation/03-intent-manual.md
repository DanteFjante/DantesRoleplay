# 03 — Intent discovery and the operating manual

Status: discovery owner implementation with accepted additive context contracts; dependent publication,
execution and transport integration remain pending. Initial callers are the website and Codex through
MCP; other protocols remain extension points. Discovery creates no runtime authorization or permanent IDs.

## Implemented owner boundary

`InteractionManualContextService` derives read-only context through the existing procedure store,
feature retriever and optional recipe store. It checks trusted invocation authority and state binding
before reading content and again before returning it, consumes the shared budget/deadline, and returns
the existing `CompletedComputation` envelope. The packet is inert discovery evidence: it never selects
or executes a write, proves implementation equivalence, or publishes an association. Exact contract,
input, binding and grant validation remain required at execution.

Operational manual selection includes only active procedures. Global categories require an explicit,
copied host allow-list; an empty list exposes none. Application content retains the retriever's trust
and namespace filtering. Global procedures retain their actual IDs rather than being fabricated as
application features. Historical procedure reads remain available through the existing store.

Manual sections retain heading ancestry, a derived section reference, source revision and content
hash. References depend on field, heading ancestry/occurrence and chunk position, so callers must
retain revision evidence across text edits. Stored synchronization hashes remain verbatim and are
separate from the derived source and match-phrase fingerprints. Match phrases participate in the
manual resolution hash without changing the catalog synchronization hash. Context defaults to 16,000 characters, with a
4,000–24,000 range, eight sections of at most 2,000 characters, eight feature candidates and four
verified recipe candidates. A bounded packet explicitly directs callers to exact source reads;
omitted constraints have not been validated. Result hashes are computed over canonical packet JSON
with `resultFingerprint` replaced by 64 zeroes.
Completion evidence names that final result hash; the separate resolution hash detects source and
candidate drift even when a caller chooses a different output budget.

The existing activation change reader can invalidate cached catalog snapshots and reject stale
retrieval generations. Derived index failures preserve lexical discovery and cannot gate activation.
Missing or stale vector generations trigger refresh; a successful empty search does not. Hosts share
one `InteractionRetrievalRefreshCoordinator` per derived-index authority across scoped retrievers.
It admits at most 16 active generations and 64 waiters per generation; waiters cancel with their
caller or fall back after five seconds. The owner performs awaited work within its caller lifetime,
pins the requested generation, and releases capacity on failure/cancellation. No background work
captures scoped services. Without host injection, coordination is limited to one retriever instance.
The accepted context seam is `IInteractionManualContextService` with `InteractionManualContextRequest`.
Coordinator integration owns constructor registration and MCP/website mapping. Section-level vector identity/index support, standing grants and publication,
candidate-bound equivalence review and end-to-end invocation through plans 01/05/06 remain separate
integration dependencies; discovery tests do not prove those dependent scenarios.

Prerequisite: implement [00 — Shared foundation](00-shared-foundation.md) first and have the coordinator supply its accepted foundation revision and contract baseline. This workstream consumes those shared contracts and does not redefine them independently.

## Outcome and existing owners

Given an intended outcome, the system returns a compact set of relevant manual sections, existing actions or reusable tasks, known inputs, missing information, and applicable constraints. Different ways of requesting the same behavior improve discovery without producing duplicate implementations. Retrieval helps select candidates; execution still validates exact contracts and permissions.

[OrientMcpTool](../../../DantesRoleplay.MCPServer/Mcp/OrientMcpTool.cs) already directs callers to `procedure.system.use` and application feature search. [ProcedureStore](../../../src/system/procedures/persistence/ProcedureStore.cs) retains versioned instructions and constraints, but its global search ranks summary metadata and match phrases lexically. Its default filter excludes archived records only; operational-manual lifecycle selection needs an explicit policy.

[ActivatedApplicationCatalogProvider](../../../src/system/catalog-navigation/persistence/ActivatedApplicationCatalogProvider.cs) includes application procedure instructions, constraints, and match phrases in feature content. [InteractionFeatureRetriever](../../../src/system/interaction-orchestration/persistence/InteractionFeatureRetriever.cs) combines exact, lexical, and optional semantic discovery. Current embedding input is the first 8,000 characters of a combined record, rather than separate manual sections. [Retrieval warmup](../../../DantesRoleplay.MCPServer/InteractionRetrievalWarmup.cs) runs at startup; activation-driven refresh is additional work.

Multiple aliases/match phrases already identify one feature. [InteractionRecipeStore](../../../src/system/interaction-orchestration/persistence/InteractionRecipeStore.cs) reuses a canonical template fingerprint and accumulates evidence from different intents. Capability descriptors reference procedure IDs, but do not bind exact manual and executable revisions together. Reuse these owners rather than introduce another discovery project, action registry, recipe history, or embedding authority.

## Coordination and proposed contracts

Start after the coordinator's shared contract baseline. Discovery requests carry principal, scope, grant reference, relevant definition revision, operation/parent identity, and budgets. Results use outcome, data, commit evidence where applicable, and optional task handle. A context packet adds candidate references, manual section revisions, missing inputs, and resolution evidence; raw fingerprints stay available to the host without requiring verbose model-facing output.

Workstream 02 owns authoring and activation; this workstream owns procedure retrieval, context, associations, and derived indexes. Workstream 01 consumes executable references; 05 consumes worker context; 06 renders discovery. The coordinator owns shared migrations, authorization schema, host wiring, and cross-owner contracts. Agents must coordinate edits to shared interaction-orchestration files.

## Deliverable slices

1. **Make bootstrap lead to bounded operational context.** Preserve `orient` as the small entry point: current scope, how to discover capabilities, and how to retrieve the manual. Extend the existing procedure query path to return relevant sections with stable section references, procedure revision, prerequisites, and links to exact callable contracts. Define active manual selection explicitly; historical reads remain available for inspection. Acceptance: a Codex caller can describe an unfamiliar task and receive sufficient instructions without listing the entire catalog; the website receives equivalent data. Recovery: missing or ambiguous meaning produces a structured unresolved result and bounded next discovery steps, without selecting an uncertain write.

2. **Unify manual retrieval and refresh derived generations.** Include permitted global system procedures alongside application procedures using existing retrieval owners, with scope filtering before content disclosure. Derive bounded sections from instructions and constraints; preserve headings, parent context, and source revision. Extend embedding identity with section, content, model, format, and association revision evidence. Derive refresh from versioned activation/change receipts; publish complete validated generations. Embedding availability cannot block authoritative changes. Reuse existing embedding-provider abstractions and implementations. Acceptance: revised content becomes discoverable without restarting; retiring content removes it from operational candidates. Recovery: rebuild from authoritative records, retain exact/lexical retrieval while embeddings are unavailable, and reject stale vector hits.

3. **Author multiple intent associations without duplicating targets.** First complete existing `Matches`/alias authoring, including the procedure handler's missing match-phrase field and retrieval invalidation when phrases change. Add richer association metadata only for contextual alternatives, reusable input bindings, or multi-step task targets: intent text, target references, conditions, evidence, lifecycle, and revision. Support many intents per target and several qualified alternatives per intent. Observed evidence remains a candidate until bindings validate and standing author/activate grants permit publication. Acceptance: a paraphrase improves discovery of the same action/recipe; overlapping phrases remain explicit alternatives. Recovery: disable or revise the association while preserving its implementation and provenance.

4. **Bind reuse decisions to current contracts.** Combine exact/lexical/semantic candidates with canonical recipe structure and existing mechanic anti-sprawl checks. Before authoring a new action, record whether an existing action, parameter binding, revision, or composition satisfies the need. Semantic similarity alone cannot prove equivalence or authorize execution. Bind selected manual sections, associations, and executable revisions in resolution evidence; revalidate inputs, scope, grants, and current compatibility before execution. Acceptance: manual drift, changed parameters, stale bindings, or revoked authority forces refreshed resolution; a valid reused implementation retains its identity. Recovery: preserve the unresolved candidate and explain which reference requires repair.

5. **Integrate compact results with Codex and the website.** Connect existing MCP discovery and authoring surfaces to the shared context service; expose the same service through workstream 06. Supply workstream 05 with a bounded manual/context packet rather than all tools and documentation. Acceptance: both callers discover and invoke the same action from different intents, explain the selection, and avoid another implementation. Measure context volume and resolution quality separately from embedding availability. Recovery: offer exact manual reads and candidate inspection when compact resolution fails.

Verify each slice against observable outcomes, then run the repository's required acceptance checks and protocol walk for changed MCP/registration surfaces. Do not create a separate test inventory or promise semantic coverage of arbitrary documents in this workstream.
