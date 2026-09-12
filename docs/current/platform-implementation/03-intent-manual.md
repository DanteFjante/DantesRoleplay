# 03 — Intent discovery and the operating manual

Status: discovery, manual context, reuse-review context, alternate-intent authoring, publication
refresh and the website/Codex gateway integration are implemented within the boundary below. Other
protocols remain extension points. Discovery creates no runtime authorization or permanent IDs.

## Implemented owner boundary

`InteractionManualContextService` derives read-only context through the existing procedure store,
feature retriever and optional recipe store. It resolves exact current definition ownership through
`IStandingGrantTargetResolver` and checks `IStandingGrantPolicy` with the full invocation host and
`Read/Application` before selection and again before returning selected content. A state-space read
grant does not authorize definition discovery, and definition discovery does not authorize state queries.
It consumes one operation from the shared budget and observes its deadline, and returns
the existing `CompletedComputation` envelope. The packet is inert discovery evidence: it never selects
or executes a write, proves implementation equivalence, or publishes an association. Exact contract,
input, binding and grant validation remain required at execution.

Operational manual selection includes only active procedures. Global categories require an explicit,
copied host allow-list with exact category equality before limits; the host default is `["system"]`,
and an empty list exposes none. This is host-selected orientation, not application authoring authority.
Application content retains the retriever's trust and namespace filtering, then exact target authorization
before exact matching, ranking, limits or disclosure. Every recipe step must pass the same check before
its recipe can influence selection. Unsupported target resolution hides the candidate. Global procedures
retain their actual IDs rather than being fabricated as
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
Compact candidate references retain application, lane, ID, kind, revision and content fingerprint.
Whole catalog and activation generation pins remain host-only for freshness checks; they never enter
the returned packet or its hashes. Denied definitions cannot alter visible alternatives, ranks, fallback
modes or fingerprints. Authorized discovery resolves and filters exact current targets before vector
ranking or result limits. It may query a complete host-built generation, but never rebuilds the full
catalog under a restricted caller grant or sends denied records to the embedding provider during that
request. A missing, stale, unsupported or failed authorized vector view falls back to the already-filtered
lexical view.

The existing activation change reader can invalidate cached catalog snapshots and reject stale
retrieval generations. Derived index failures preserve lexical discovery and cannot gate activation.
Missing or stale vector generations trigger refresh; a successful empty search does not. Hosts share
one `InteractionRetrievalRefreshCoordinator` per derived-index authority across scoped retrievers.
It admits at most 16 active generations and 64 waiters per generation; waiters cancel with their
caller or fall back after five seconds. The owner performs awaited work within its caller lifetime,
pins the requested generation, and releases capacity on failure/cancellation. No background work
captures scoped services. Without host injection, coordination is limited to one retriever instance.
System-manual sections use the same disposable derived index under a distinct lane. The generation
fingerprint covers the host application, exact permitted procedure IDs and revisions, full source and
section fingerprints, normalized embedding text, format and embedding-provider identity. Exact host
category filtering happens before any section text reaches the embedding provider. Repeated lookups and
new scoped service instances reuse an unchanged retained generation; source edits or provider revisions
select a new generation. The procedure store remains authoritative, and every selected source revision
is rechecked before a packet returns. Missing, stale or failed section generations preserve current
lexical section selection.
The accepted context seam is `IInteractionManualContextService` with `InteractionManualContextRequest`.
The coordinator registers it and maps the selected-application website/Codex gateways. Production
standing-grant policy, ownership resolution and publication remain owned by plan 02; discovery
evidence itself does not prove issuance, execution authority or publication.

Candidate reuse preparation implements `IApplicationCandidateReuseReview` with a conservative
structural result and a candidate-bound semantic-review path when that result is insufficient. It
normalizes verified retained text through the existing catalog parser,
requires explicit application Read authority for candidate definitions and active alternatives,
and rechecks exact targets and generation before returning evidence. An exact copy under a different
ID is Invalid: the conservative comparator removes only the top-level ID from otherwise identical
normalized content. Same-ID revisions and nonidentical content require semantic review; this check
does not prove behavioral equivalence. Oversized context, unsupported documents, reusable-task
context without exact review support, missing authority and missing worker evidence remain Unavailable.
Discovery consumes one shared operation; it never creates a separate invocation budget.
Read-only review preparation preserves application-only invocations with an absent state-space/revision
pair, or preserves the caller's complete state pair. Both use the same application-authorized manual
owner and operation ledger; application-only discovery does not grant state execution authority.

The proposed reuse-judgment input freezes full retained text, the full implementation reason,
authorized manual packet and exact alternative contracts. It permits at most 16 documents and
16 alternatives within 64,000 UTF-8 input bytes, and 8,000 UTF-8 output bytes with 500-character
judgment reasons. Nothing is truncated to fit. Strict output checks pin identity and alternative
coverage but do not create an attestation. The host-selected read-only plan 05 worker and plan 04
accounting/worker provenance supply the candidate-bound review path. The authoring owner still
verifies the exact retained judgment and current authority before accepting it. No-hit retrieval, a
nonempty reason, model output or usage counters alone are insufficient.

The additive V2 selected-context contract keeps the full candidate and base activation references
host-only. Its selection, input and manual-result fingerprints derive exclusively from authorized
selected documents, reason, manual and alternatives. The retained owner must separately prove exact
changed-document, sidecar and declared-dependency coverage against the full candidate and base,
and the consuming service must revalidate current Read and Validate authority. A structural
`closureComplete` flag only guards context construction; it is not evidence or permission to dispatch.
Incomplete coverage remains Unavailable. V1 fingerprint semantics are unchanged. The final serialized
provider request has its own size check, including prompts and escaping; fitting the model-input
bound alone does not guarantee that request fits.

Alternate-intent authoring is internal and limited to existing trusted application procedures and
mechanics. It accepts an exact current definition reference, expected candidate revision and replacement
phrase list; active and candidate readers rehydrate retained rows and bytes. Read authority is checked
before retained source preparation, Author is checked by the candidate writer in its SQLite transaction,
and Validate and Activate are checked by their existing lifecycle stages. Up to 32 phrases of 200
characters are trimmed, whitespace-normalized and deduplicated under retrieval matching rules. Selected
source text and replacement text each have a 32,000-byte UTF-8 bound.

The editor changes only the explicit `## Matches` section and rejects ambiguous section/fence layouts.
Publication requires a separate exact structural proof: the same existing ID, kind, version, contract,
requirements and source sidecar; byte-identical authored text outside the editor-owned section; and an
otherwise identical retained generation. It records validation and publication receipts without runtime
samples or model equivalence. The existing application candidate and activation histories retain every
revision. Removing all phrases disables the alternate association while preserving the canonical target
and its implementation; a later revision may restore or replace phrases. Each publication advances the
definition-change feed, so catalog and derived retrieval generations refresh from the authoritative
activation. Existing synchronization/source hash semantics are unchanged.

The authenticated application-candidate gateway exposes the closed
`system.application-candidate.intent-update` operation to the website and Codex tool adapter. Its
payload pins the current application, exact procedure or mechanic revision and fingerprint, expected
candidate revision, and the complete desired phrase set. Gateway selection requires one current grant
with both Read and Author capabilities and reserves the two operations used by source preparation and
candidate authoring. Stable gateway idempotency maps retries to the existing candidate write receipt.
The operation returns that real inert receipt; inspection, validation and activation remain separate
existing capabilities and are the only publication path.

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
