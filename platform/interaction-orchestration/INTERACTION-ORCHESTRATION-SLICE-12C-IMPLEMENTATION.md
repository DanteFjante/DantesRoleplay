# Interaction orchestration Slice 12C implementation — trusted feature retrieval

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Interaction orchestration Leaf C and Slice 12C](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md#lowest-ready-leaf)  
Completion evidence: [Slice 12C receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md)  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Add one server-mediated, application-scoped retrieval component that materializes current
effective feature documents from accepted catalog navigation, always provides deterministic exact
and lexical search, and optionally adds a disposable vector/hybrid index without making it state
authority.  
Exclusions: Player/assistant message storage; model/planner calls; provider/tool invocation;
proposal verification; action execution; receipts/recipes; public MCP kinds/routes/web components;
catalog/source/application migrations; changes to game rules or JavaScript mechanics; and live
database changes.  
Allowed files/areas after confirmation: `src/system/interaction-orchestration/component.json`,
`domain/*.cs`, `persistence/*.cs`, `hosting/*.cs`, `tests/*.cs`; a dedicated derived-index SQLite
project/configuration seam if required; focused composition registration; this plan, its receipt,
and concise owner status links. Existing catalog-navigation or local-AI contracts may receive only
the minimal confirmed generic trust/provenance field or port needed to avoid duplicate source
materialization.  
Stop point: Stop after internal retrieval/rebuild contracts and tests prove current trusted
application isolation, lexical fallback, optional-vector behavior, and disposable-index recovery.
Do not add a public caller, planner, receipt, execution path, recipe, or UI.

## Confirmation package

The user confirmed the following initial policy on 2026-08-24.

1. **One corpus source and authority.** `catalog-navigation` remains the only owner of active
   catalog traversal and exact record content. Retrieval consumes an internal snapshot of current
   effective records rather than rescanning files, parsing source roots, or creating another catalog
   representation. It may index only procedure/mechanic metadata already exposed by the active
   catalog materializer; JavaScript sidecars, hidden prompts, raw paths, and arbitrary scanned files
   never enter retrieval text.
2. **Trust lanes.** Materialization retains each winner's existing `trusted`/`untrusted` source
   classification. The initial retrieval API has two host-selected lanes: `trusted-feature` and
   `untrusted-reference`. Only `trusted-feature` results may later become planner contract
   candidates. `untrusted-reference` is an explicitly labeled read-only evidence lane; it cannot
   be mixed into trusted ranking, a proposal, execution, or recipe. The host must choose the lane;
   a caller/model cannot upgrade a document's trust. If a host has no authorized use for untrusted
   reference text, it receives no results rather than silently falling back to trusted content.
3. **Application and publication scope.** Every retrieval request is bound by the host to one
   non-`system` application and one currently published active catalog snapshot. Queries never
   fan out across applications, bases, users, source roots, or state spaces. A missing/inactive/
   unpublished application returns a typed unavailable/unknown result with safe evidence only.
4. **Derived-store location and lifecycle.** Keep all vector/lexical acceleration material in one
   host-configured, separate SQLite file named conceptually the **derived retrieval index**. It is
   outside the authoritative game database, holds no player messages or raw source paths, is safe
   to delete/rebuild, and is never backed up/exported as game state. The host creates it only under
   its configured derived-data directory; a relative path, root directory, or path outside that
   directory is rejected. A deleted/corrupt/locked derived store returns lexical results where
   possible and schedules/permits a rebuild; it never changes catalog/application state.
5. **Derived schema and generations.** The separate database may contain only generic generation,
   document-provenance, lexical, and vector rows. Each generation is keyed by application ID,
   active catalog/activation fingerprint, trust lane, retrieval-format version, and (for vectors)
   the exact embedding provider/model/revision/dimensions. It stores bounded normalized title,
   description, aliases/match phrases, and active catalog contract JSON plus source ID/logical
   identity/content fingerprint/version—not executable JavaScript, raw filesystem paths, messages,
   prompts, effects, or authorization evidence. Replace one generation atomically; a failed rebuild
   leaves the prior derived data disposable and never makes a stale row authoritative.
6. **Search semantics.** Exact qualified-ID lookup wins first. Lexical ranking uses the accepted
   catalog lexical order and remains complete when embeddings, sqlite-vec, or the derived store are
   disabled. Vector search is optional through the existing provider-neutral local-AI embedding port.
   When available, combine the independently bounded lexical and vector candidate lists with stable
   reciprocal-rank fusion (`k = 60`), then deterministic qualified-ID tie breaking. Hybrid search
   always rehydrates every proposed hit from the current catalog snapshot and checks its application,
   lane, version, and fingerprint before returning it. Missing vector capability is a normal
   lexical-only result with safe availability evidence, not an error or a degraded trust check.
7. **Closed request/result and bounds.** Internal requests contain only host-bound application,
   lane, query text (1–256 normalized Unicode characters), optional existing catalog kind/status
   filters, and a limit of 1–50. Results contain one mode (`exact`, `lexical`, `hybrid`, or
   `lexical-fallback`), safe availability/rebuild diagnostics, and bounded current record summaries
   plus exact immutable references/fingerprints. No result includes a source-root path, document
   body beyond the already-authoritative catalog contract, untrusted instructions in a trusted lane,
   state, outcome, effect, planner reasoning, or executable capability.
8. **No newly public surface.** This slice adds only an internal retrieval port. The future local and
   remote planners use it through Slice 12E; an interaction/public search kind remains Slice 12F
   confirmation work. Existing catalog browse/search/inspect routes and three-verb protocol remain
   unchanged.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Feature metadata | No D&D rule is interpreted. | Active catalog navigation | Retrieval handles opaque feature documents only. |
| Rules/mechanics | Existing catalog JavaScript remains authoritative. | Catalog mechanics and application execution | Indexing never includes JavaScript/effects or computes outcomes. |
| Trust/currentness | Effective application source winner and exact catalog record are authoritative. | Application activation/source overlays/catalog navigation | A vector row is disposable and must be rehydrated before a result returns. |

## External implementation reference

No Foundry dnd5e reference applies: this is ruleset-neutral indexing and retrieval infrastructure.

## Prerequisite evidence

- [Slice 12A receipt](../application-kernel/receipts/APPLICATION-KERNEL-SLICE-12A-RECEIPT.md)
  proves one production provider supports direct and remote application-isolated catalog traversal,
  deterministic lexical search, and exact inspection without vectors or local AI.
- [Slice 12B receipt](receipts/INTERACTION-ORCHESTRATION-SLICE-12B-RECEIPT.md) proves the new
  ruleset-neutral owner, closed application authority, trusted provider boundary, typed
  non-resolution, and separate disposable derived-index decision.
- `catalog-navigation` currently materializes active effective procedures/mechanics with exact
  content fingerprints and source logical identity. Source overlays already choose one winner by
  trust/precedence, but its public catalog record does not currently carry the winner's trust.
  This slice must preserve that trust through one minimal internal provenance seam rather than
  infer it from a source ID or filesystem path.
- `local-ai` already owns only generic embedding contracts and has zero repository component
  dependencies. Retrieval may call its `ITextEmbeddingProvider`; local AI does not reference this
  component, catalogs, applications, games, or a derived database.

## Runtime artifacts after confirmation

- Expanded `interaction-orchestration` ownership/dependencies for generic feature retrieval,
  current-record hydration, and disposable derived-index coordination.
- Immutable internal trusted-feature document, corpus/lane, query, hit, mode, availability, and
  generation contracts with explicit bounds and canonical SHA-256 provenance.
- One catalog-navigation provenance seam that returns exact active record trust without exposing a
  raw source root or changing existing public browse/search/inspect contracts.
- A deterministic current-document materializer, exact/lexical retriever, optional embedding/vector
  coordinator, stable hybrid fusion, and a separately configured SQLite derived-index store.
- In-memory and disposable-file test fakes; no authoritative database migration or catalog edit.

## Authoritative state and closed input

The active application catalog snapshot is authoritative for records, record content, source
provenance, active fingerprint, and publication. The source-overlay/activation owner is
authoritative for winner trust. The host supplies application ID, trusted lane, and all access
authority; a caller or model supplies only bounded search text and existing filter values. The
embedding provider supplies vectors only and has no authority to select documents, assign trust,
or return a result. The derived index is a cache and may be discarded at any time.

## Behavior, result, and typed effects

1. Resolve one host-bound application through the accepted active/public catalog provider and read
   one immutable current snapshot with trust provenance.
2. Build a bounded safe retrieval document from each supported current record. Partition documents
   by the authoritative source trust; never let a query select a more trusted lane.
3. Resolve exact qualified ID first; otherwise run deterministic lexical retrieval against the
   current lane/snapshot.
4. If the optional vector provider and matching derived generation are ready, obtain bounded vector
   candidates, fuse them with lexical candidates using the confirmed stable rule, then rehydrate and
   verify each candidate against the snapshot. Otherwise return complete lexical results plus safe
   fallback evidence.
5. Rebuild replaces only the matching disposable generation. It performs no operation/audit/state
   effect and cannot mutate catalog/application/activation records.

This slice owns no authoritative typed effect or transaction. A derived-store replacement is one
local SQLite transaction owned by the derived-index store and is explicitly non-authoritative.

## Failure, replay, and rollback contract

Invalid/oversized query/filter/lane, unknown application, inactive/unpublished catalog, missing
provenance, source/catalog drift, cross-application candidate, wrong lane, malformed content,
embedding identity change, unavailable vector extension, corrupt/locked derived store, stale
generation, duplicate document identity, and candidate fingerprint mismatch produce a typed empty
or lexical-fallback result with no authoritative mutation. An identical snapshot/generation rebuild
is idempotent. A changed source winner, active fingerprint, trust lane, retrieval format, or
embedding identity makes prior derived rows stale. Failed/cancelled writes roll back only the
derived-store transaction; deleting the store is a valid recovery path.

## Implementation sequence after confirmation

1. Add minimal catalog trust provenance and pure retrieval/generation contracts with tests.
2. Materialize current trusted/untrusted records and exact/lexical retrieval through the existing
   catalog provider; prove vector-disabled behavior first.
3. Add disposable SQLite generation storage, optional embedding/vector rebuild, fusion, and
   hydration validation.
4. Add focused isolation/failure/determinism/rebuild tests, run full evidence, write the receipt,
   update owner status, and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | A current trusted record is found by exact ID and lexical intent; optional eligible vectors produce deterministic hybrid results. |
| Fallback | Disabled/unavailable/wrong-identity embeddings or sqlite-vec produce complete lexical results with bounded safe evidence. |
| Trust | An untrusted winner is never returned in `trusted-feature`, never contributes a trusted rank, and cannot be upgraded by a caller/model. |
| Currentness | Changed active winner/fingerprint/version/trust causes index staleness; returned hits rehydrate to the exact current catalog record or are omitted. |
| Isolation | Two active applications, lanes, source IDs, cursors, and derived generations do not cross; a `system` application is invalid. |
| Determinism | Equivalent snapshots, query normalization, rebuild ordering, ranking, fusion ties, and generation keys are byte-stable. |
| Safety | Bounded text only; no JavaScript, raw root/path, prompt, message, effect, state, or game vocabulary enters the generic component. |
| Recovery | Corrupt/deleted/locked derived storage and cancelled rebuild leave authoritative state unchanged and preserve lexical availability when catalog navigation is available. |
| Compatibility | Existing browse/search/inspect, local-AI, three-verb protocol, action, assistant, Codex-control, and web behavior remain unchanged. |

## Verification commands

- Focused interaction-retrieval, catalog-navigation provenance, local-AI boundary, component guard,
  deterministic ranking/fusion, trust-lane, currentness, and derived-store recovery tests.
- Full shared and standalone local-AI test suites.
- Isolated-output solution build with zero warnings/errors and `git diff --check`.
- Catalog validation and protocol walk are not required unless an accidental catalog/public-surface
  change occurs; such a change is outside this slice and must be reverted or separately confirmed.

## Completion receipt and exit gate

After every confirmation-package decision is explicitly confirmed and implementation passes, write
`platform/interaction-orchestration/receipts/INTERACTION-ORCHESTRATION-SLICE-12C-RECEIPT.md`, mark
12C accepted in the master plan/roadmap, and stop. Slice 12D remains a separate implementation turn.
