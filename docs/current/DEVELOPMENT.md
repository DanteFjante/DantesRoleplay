# Development workflow

Use this guide for code, tests, schemas, and catalog changes. The default reading set is [AGENTS.md](../../AGENTS.md), [README.md](README.md), this guide, and the exact implementation files involved.

## Before editing

1. Search for the existing owner in code, `catalog/`, and focused tests.
2. Decide whether the behavior is generic host behavior or game-specific behavior.
3. Inspect the smallest relevant contract: interface/schema, implementation, registration, and focused tests.
4. Check the working tree and preserve unrelated user changes.

Do not create a dependency tree, implementation plan, handoff, receipt, or status diary as a prerequisite. Keep a temporary plan in the task or issue unless the user asks for a durable document.

## Placement

Put generic storage, validation, sandboxing, typed-effect application, transaction, audit, retrieval, or protocol behavior in C#.

Put ruleset vocabulary, IDs, formulas, eligibility, choices, and outcome branching in catalog data or JavaScript. D&D-specific material belongs under `catalog/applications/dnd2024/` and must not leak into the generic C# kernel.

Schemas define stored component state. Procedures define how a capability is invoked and what context it receives. JavaScript mechanics calculate game-specific results. Tests should assert the boundary as well as the result.

Compile request and catalog JSON Schemas with a fresh `BuildOptions` and `SchemaRegistry` for each
build. JsonSchema.Net registers compiled graphs; its default shared registry retains them for the
process lifetime. Keep local references within that build and preserve built-in dialect lookup.
Never register per-request schemas globally or clear the global registry as a cleanup workaround.

The singleton bounded validator may reuse successful schema compilations by exact schema text and
requested profile. Its least-recently-used cache is capped at 256 entries, 2 MiB of retained schema
text, and 32,000 schema nodes. Each cache miss still builds with its own registry, and each value is
parsed and validated afresh under the existing limits. Invalid compilations, input values, validation
results, authorization decisions, and game-state projections are not cached. Concurrent misses for
one exact schema/profile key share only their in-flight build; distinct keys compile outside the
short LRU lock, and evaluation of one retained schema graph remains serialized. Edited schemas use
new keys immediately; no time-based expiry can serve an older contract for changed contents.

Every repository-authored application mechanic declares a closed `inputSchema` in its requirements.
Capability discovery exposes that schema plus generated valid and invalid examples; the common
descriptor also carries the closed output envelope, owner, lifecycle, roles, authorization,
confirmation, idempotency, stable errors, and structured recovery. Catalog validation compiles the
schemas, validates both examples, and requires every child declaration to pin the exact current
version and content fingerprint. An application that has not begun this migration remains a
warning-only legacy boundary, but once any mechanic in that application adopts authored contracts,
missing schemas block validation. Mechanic JavaScript must still validate exact keys,
state-dependent constraints, and authored content before proposing effects; a schema is
discoverability and structural validation, not permission to invent missing world details.

Application-facing read models follow the same ownership rule. Register a closed query contract
under the application `queries/` tree and point `mechanic-projection` at one exact active,
effect-free JavaScript mechanic. The generic host resolves installed component identities, runs the
sandbox, validates the returned data against the registered output schema, and binds the response
to state-space, resolution, result, and source-revision fingerprints. A website must consume that
read model instead of reproducing ruleset calculations or scanning raw ECS components. Host-bound
audience policy selects which entity may be projected and supplies the frozen player-or-DM
perspective seen by audience-aware JavaScript; mechanic input and model output never select or
upgrade their own audience.

The read-model HTTP endpoint accepts an optional `perspective=player|dm`. An authorized
GameMaster may request a Player projection; an Actor cannot request DM or another actor's
entity. Invalid or repeated perspective values fail before projection. GM Player previews
must request a fresh Player projection and omit ambient-GM media enrichment rather than
reuse a DM dossier.

Before an application planner receives a turn, materialize one bounded task-context pack for the
already-authorized principal, application, state space, session, and audience. The pack may contain
current capability descriptors, executable model-visible read views whose exact roles are already
bound, authorized knowledge candidates, play-record facts and continuity, and recent receipts from
the same session and revision. Every included item carries a canonical reference, revision, and
fingerprint, and the complete pack has its own fingerprint that is recorded in planning evidence.
Keep the active ruleset fingerprint and extension-resolution fingerprint distinct from the derived
navigation fingerprint (which also includes the catalog materializer version). State adoption must
preserve both authority fingerprints; task-context freshness must compare like identities.
Authorize before any retrieval, apply structural scope filters before ranking, rank lexically with
optional vector fusion, rehydrate from the canonical owner, then recheck authorization and source
revisions before returning the pack. Vector results select candidates only; they never establish
truth, permission, audience, or freshness. The `interaction-task-context/v2` envelope declares
closed byte, item, elapsed-time, and per-section budgets and names every budget-driven omission.
Discard lower-priority optional items rather than expanding the planning context; fail closed when
mandatory context cannot fit or the deadline, authorization, catalog, or state revision changes.

When a query follows relationships to related entities, put always-required endpoint state in
`targetComponentIds` and state that is valid only on some endpoints in
`optionalTargetComponentIds`. The host projects optional endpoint components when present, records
their observed revisions for provenance, and never rejects an otherwise complete endpoint merely
because optional state is absent.

Application actions have two entry paths over the same execution owner. Ambiguous intent goes
through interaction planning and explicit proposal execution. A caller that already holds the
exact application, state space, mechanic identity, version and content fingerprint, role bindings,
input object, and idempotency key may use `application.action.execute` directly. That route must
still recheck private authorization, current activation, exact mechanic provenance, and the normal
confirmation gate; it must not add a second consent system or reintroduce unscoped mechanic
selection. The former `action` commit kind is physically retired and must not be advertised,
dispatched, or generated as a direct AI tool.

Read-only mechanics may declare `snapshotObjects`:
exact structural objects assembled solely from their already-authorized projection, with no new
storage read grant. Bindings may select existing roles, input-selected authorized entities,
authorized component references, or a bounded collection of authorized reference records.
The host validates exact object provenance, perspective, output schemas and item/byte limits;
catalog JavaScript still owns formatting, disclosure rules and source-revision-bound paging.
These snapshot collections are not relationship-backed storage collections. Their read revision
remains the authorizing projection's revision, and neither they nor their child mechanics may
propose effects, events or notifications. Item contract v1 remains immutable; partial read views
use v2. Authored recipe/activity source pins require the matching catalog component versions at
the next explicit activation/import boundary; editing catalog files does not migrate live state.

Registered-object writes and object-backed reducers carry their complete observed source evidence
into the generic effect transaction. Check component revisions (including absence), entity revisions,
and exact relationship collections by kind, anchor, and direction after acquiring SQLite's write
reservation and before applying any effect. Empty and nested collections are evidence too; checking
only the edges or components being written does not protect the snapshot used to compute a result.
Successful idempotent replay returns the existing receipt without revalidating later world state.

Collection materialization completes the root (items, nested references, and paging metadata) before
validating its exact registered output schema. Required collections must remain required in authored
schemas. Structural dependencies still validate before their results are consumed; ordinary structural
reads never receive an incomplete root. Expansion stays inside the same bounded read transaction.

Nested object reference arrays may declare `read: ["dm"]` (or another subset of the supported
perspectives). The generic collection reader applies that restriction before retrieving edges or
entity names; excluded arrays remain empty and contribute no hidden entity/relationship revisions.
Omitting `read` preserves the prior contract and fingerprint. Restrictions cannot redefine root
collections or grant access beyond the object's existing audience policy.

Campaign summary v3 uses this batched nested-reference owner for up to 20 participations and
20 actor links. DM bootstrap consumes their exact actor identities/names without per-member HTTP
reads; Player projections retain participation identities only, with an Actor's own identity loaded
through the existing authorized binding. Missing/ambiguous active actor references fail the DM
bootstrap, shared actor identities are deduplicated, and withdrawn participations are excluded.
Participation state is merged as an optional endpoint source but required by the final item schema,
so absent/stale state fails validation instead of filtering a member out. The immutable v1/v2
objects remain available. Activating v3 and publishing its matching browser require the normal
explicit catalog/runtime synchronization boundary; source edits do not upgrade a running game.

Time-coupled application mechanics use one `clock.advance` effect in the same effect batch as all
sibling state changes. Their requirements declare `elapsedTime.mode` as `zero`, `fixed`, `derived`,
or `supplied`; supplied durations name the closed input property and derived durations describe
their catalog-owned source. JavaScript calculates the next coordinate and supplies the typed clock
metadata. The generic kernel validates a positive bounded monotonic delta and a one-step clock
revision, replaces the exact observed component, records `game.core.world.clock.advanced`, and
commits the event, operation receipt, and sibling effects together. A time-coupled mechanic must
not ask its caller to run a separate clock action. Reusing the action execution identity returns
the existing lineage without advancing time or writing another event.

Application mechanics may also declare catalog-owned semantic events. The generic application
transaction validates their registered active type, payload schema, bounded application-state-space
entity references, and reserved structural namespace, then writes them in the same transaction as
the effects and operation receipt. Structural `world.*` events remain kernel-owned; application
mechanics still cannot emit notifications through this path.

Generic `component`, `effects`, and `mechanic` commit kinds are also retired. Application component
types use versioned registration, reviewed world authoring uses `system.world-state.sync`, and AI-
authored mechanic candidates use the governed mechanic sandbox before an explicit catalog export
and activation boundary. The legacy mechanic-action information executor and generic action runner
are not runtime services. Retained tests that exercise records still held in the legacy stores use
a test-only catalog harness; do not register that harness in production.

Do not drop legacy mechanic, event, subscription, component, or ECS storage merely because current
execution uses application catalog snapshots. Physical removal requires zero production callers,
zero live records requiring the path, a verified replacement, and backup/readback evidence. The
legacy-state adoption facility remains available while old state exists. `fixture.legacy.*` catalog
content remains quarantined until its live records and non-fixture authored references have been
exported or migrated; never confuse that prefix with historical migrations, optional compatibility
extensions, or game vocabulary that happens to contain the word legacy.
Export-only compatibility records must not be embedded as fresh-start bootstrap content. Excluding
their build resources preserves catalog export and existing runtime revisions; it is not retirement
of their storage or approval of their namespace.

Reviewed compatibility namespaces are retention boundaries, not new authoring space. The exact
kind/identity/path set in `catalog/compatibility-retention.json` is checked by catalog validation;
new, missing, or moved records fail until the ownership and retirement review is updated explicitly.
Retained procedure prose must not advertise retired calls, match new gameplay requests, or seed
the kernel. Dedicated world-feature test relationships belong in the test fixture and are merged
only into disposable catalog copies; a live world export must never supply their test topology.

Application feature identities need an enabled, reviewed registration for their exact namespace
in `catalog/namespaces/`, including nested mechanic and query prefixes. Catalog browsing alone
does not prove discovery: the trusted retrieval gate deliberately excludes unregistered namespaces.
Synchronize reviewed registration metadata with `roleplay import --namespaces-only`; never relax
the retrieval guard to compensate for a missing registration.

Orientation is a runtime projection, not authored capability prose. It combines only the current
dispatcher-backed MCP descriptors, authorization-scoped in-process capability discovery, the
current private principal decision, the ambient audience binding, and authorized application and
state-space registries. Active families must contain only descriptors the current principal can
call. Exact schemas are reached through the descriptor's structured schema link; callable legacy
routes appear only under deprecated limitations with a registered replacement. Do not add a
hand-maintained capability or limitation inventory to `orient`.

Machine-followable guidance uses structured next actions bound to an active capability id,
capability fingerprint, and input-schema hash. Each action separates values already established by
the response from values the caller still needs, carries one complete schema-valid argument
example, and declares whether it is ready to execute. Human-readable next-step text may remain for
display and compatibility, but it must be derived from the structured action rather than naming a
second route. Construct next actions through the live descriptor catalog so unknown, deprecated,
or schema-incompatible targets fail before a response is returned.

Self-explanation is accepted through a cold-agent conformance walk, not by inspecting internal
types. The test client receives only the live MCP tool schemas and `orient`, then must discover its
authorized application and state space, select a read or action contract, understand required roles
and authored input, route exact work directly and ambiguous work through inert planning, obtain its
audience context, follow structured recovery for missing roles and stale fingerprints, and interpret
the execution receipt. An empty feature search must offer an inert review proposal through a current
descriptor; it must never invent or activate a capability. The private web contract runs the same
role, input, stale-proposal, and receipt scenarios. Ordinary discovery must remain free of retired
legacy capabilities.

Verified recipe replay is a deterministic interaction fast path, not a prompt-per-step workflow.
Before any step commits, recheck the recipe's application revision, effective catalog and
resolution fingerprints, and every referenced active mechanic version and content fingerprint.
Learned templates retain exact action/query kinds, dependency order, versions, fingerprints, and
safe result bindings. Literal mechanic input values are replaced with named JSON-pointer parameters;
role entities and input values never become part of the template identity. Replay binds supplied
roles and declared parameters into one inert proposal, verifies it through the common interaction
gateway, obtains the existing trusted-host confirmation, and lets the normal executor run its
dependency-ordered steps and receipts. If required roles or declared parameters remain unknown,
one read-only AI resolution pass may fill only those missing choices; it must not execute actions.
Replay output records the fallback reason, old per-step AI-call baseline, actual and saved calls,
phase latency, and prompt/output tokens on the immutable recipe-use evidence so the efficiency gain
remains observable after the tool response is gone. Existing value-free templates keep their
canonical identity and may still accept the compatibility per-step input shape until retired.

Recipe memory and mechanic learning are separate lanes. After three distinct successful uses of
the same verified recipe for the same normalized intent, the host may persist one inert mechanic-
opportunity proposal. It cites the successful resolution/execution receipts, exact child versions
and fingerprints, proposed roles and step-scoped input schemas, retained child effect ownership,
match phrases, a bounded overlap scan, and an explicit call-reduction estimate. The proposal has no
mechanic ID, catalog path, source writer, review decision, or activation operation. Existing
deterministic recipe replay already presents one AI tool call, so opportunity estimates must report
zero incremental tool-call savings versus that route; atomic composition, catalog discovery, and a
stable typed contract are separate benefits. Failed, stale, candidate, or retired recipe uses never
produce a proposal.

A reviewed mechanic opportunity may enter the governed mechanic sandbox only through the current
system-capability descriptors. `system.mechanic-sandbox.draft` creates or revises an expiring,
application-scoped SQLite draft with bounded candidate requirements, JavaScript, match phrases,
declared effects and captured scenarios. The host applies strict execution ceilings, the ordinary
string-only Jint boundary, closed effect allowlists, catalog checks, and anti-sprawl checks against
both active catalog mechanics and other current drafts. Deterministic conflicts and failed replays
keep a draft inert; fuzzy overlap remains visible for review. Draft count and revision quotas are
per application. `system.mechanic-sandbox.promote` revalidates the current candidate and records a
separate authorized approval for export review, but still assigns no mechanic ID, writes no file,
changes no schema, runs no migration, and activates nothing. MCP-authored candidates remain in
SQLite until a later explicit export/review synchronization boundary creates reviewed catalog
content.

The private web control center is a projection of the same authorized system-capability catalog
used by MCP and direct AI tools. It discovers live descriptors, renders their input and output
schemas, examples, stable errors, and recovery actions, and submits reads or confirmed writes
through the generic system-task workflow. Interaction context packs, learned recipe evidence,
mechanic opportunities, sandbox validation, anti-sprawl preview, export packages, and lifecycle
operations must therefore be added to their owning capability handlers; do not add a browser-only
payload shape, handwritten capability inventory, or separate approval rule.

Application activation owns the whole-overlay anti-sprawl decision. Preview parses the exact
winning mechanic contracts without executing JavaScript and blocks active pairs with identical
normalized match phrases, overlapping declared effect-component ownership, equivalent child
graphs, or incompatible extension namespace claims. Lexical similarity across authored names,
descriptions, phrases, requirements, and effects produces review candidates only. A trusted review
record under `governance/anti-sprawl/reviews/` names both qualified mechanic IDs and exact authored
content fingerprints plus one disposition: `merge`, `distinct-responsibility`, `replacement`, or
`intentional-override`. Changing either fingerprint expires the review. Only the two coexistence
dispositions allow both mechanics to remain active; merge or replacement must be completed before
activation. Draft overlap remains non-blocking and visible.

## Validation

Run checks in proportion to the change:

| Change | Minimum checks |
| --- | --- |
| C# implementation | Build plus focused tests |
| Catalog records, schemas, procedures, or mechanics | Focused tests plus `.\roleplay.cmd validate catalog` |
| Persistence or transaction behavior | Focused persistence tests and affected integration tests |
| MCP surface or dependency registration | Focused tests plus a protocol walk |
| Feature acceptance or broad refactor | Full solution build and full test suite |

Common commands:

```powershell
dotnet build DantesRoleplay.slnx
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj
.\roleplay.cmd validate catalog
```

Catalog validation uses a fresh disposable database and does not change the live database.

## Visual media

Visual media belongs to its ECS entity through `game.core.media.visual`; maps use the same blob
identity through `game.core.world.map.visual`. SQLite owns the attachment metadata and visibility,
while immutable PNG, JPEG, or WebP bytes live in the adjacent content-addressed `blobs/` store.
Website and AI callers discover media through the owning entity. They must not turn a raw digest
or a repository path into a public URL, because doing so would bypass Player/DM visibility.
The host binds `system_entity_media`, `system_current_location_media`, and
`system_current_location_map` to its trusted seat and returns structured attachments; models and
browsers never choose an audience. Runtime item media overrides the same role inherited from its
definition, while unoverridden illustration and icon roles remain inherited.

The play conversation resolves its persisted current situation's exact location through the same
owner-bound media endpoint. It prefers a setting or scene card, re-resolves it after location
changes and page refreshes, and silently omits unavailable media. Conversation records therefore
retain authoritative entity and situation identities rather than copying blob paths or visibility
metadata into transcript text.

Import reviewed files with the production verification path before changing an entity reference:

```powershell
.\roleplay.cmd import-media <image> [<image> ...] --database <database>
```

The command verifies length, media signature, and SHA-256 during upload, then reopens and rehashes
the finalized bytes. It does not change ECS associations or delete sources. Backups and restores of
a runtime database must include both the SQLite file and its adjacent `blobs/` directory.

## Published web bundles

Server startup applies the kernel and web schema initialization, bootstrap contracts, saved host
settings, and interrupted-conversation recovery before opening the listener. It logs phase timings,
then the actual listener address and openable website/MCP links once the listener is ready.
The default console suppresses SQL command text and routine HTTP traffic while retaining application
information and framework warnings/errors. For an explicit SQL investigation, override
`Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command` to `Information` in host configuration.

Legacy page-identity inspection is an explicit web administration operation. Do not run it during
normal startup: it traverses every retained page revision and asset to produce migration evidence.
Its inspection and reviewed migration paths remain available through web page administration.

The browser supports plain HTTP network origins. Use its shared browser crypto helper for SHA-256
fingerprints and request IDs: these origins lack SubtleCrypto and randomUUID even though secure
random bytes remain available. Preserve exact SHA-256 output with the bundled implementation when
the native hashing API is absent, and exercise campaign loading through a non-loopback HTTP URL.

Web read throttling keeps API reads and page/browser-asset loads in separate bounded allowances.
API polling or catalog traversal must not prevent a user from reloading the UI or its entity-owned
map images. Media content shares the page/asset allowance. Each read group permits 6,000 requests
per minute to accommodate complete world views; writes retain their existing limits. Paths and query
values within each group share its allowance; writes and live streams retain their separate limits.
Fixed-window rejections return `Retry-After` with the existing `WEB_RATE_LIMITED` response.

The D&D browser requests saved view preferences on its initial authorized load rather than loading
Player and then repeating the world load for DM. Each view load limits reads to eight concurrent
requests and reuses bounded entity listings only within that load. A rate-limited load never becomes
a successful partial world/map; the current view remains available.

World History, Lore, Locations/Map, People, Current and context-directory discovery load on demand,
separately from the registered Campaign bootstrap. Deferred views distinguish unloaded, loading,
ready/empty and failed states; changing campaign or perspective aborts their pending reads. The
legacy World adapters follow all continuations inside a 2,000-request per-view safety ceiling. This
ceiling prevents runaway reads; it is not the complete-workload performance acceptance budget.
The shared website has full application authority after admission and offers no role switch.
Obsolete Player preferences and deep links normalize to the full shared view. Optional knowledge
preview requires an explicit observer before it can be reintroduced; non-website Actor knowledge
and media projections retain their existing contracts. See OPERATIONS.md for the admission boundary.
Read-only source probes, a served browser traversal and a matched complete-workload benchmark are
different evidence and must not be reported interchangeably.

The read-only `scripts/sample-browser-baseline.mjs` sampler traverses the canonical Character
sheet, Map, History, Lore, Locations, People, every Factions continuation, the context directory
and Current. `collected` records an observation, not acceptance. `--workload-reference` supplies
an independently API-checked, fixture/runtime/audience-bound record-count/digest reference and
matched full-traversal baseline timings (`baseline.cold/warm.completeWorkloadP50Ms`) with the
same `browser` and `machine` identities; `--server-measurements` supplies production-path measurements keyed by
sample ID. Their shapes are checked in `scripts/complete-workload.mjs`. The sampler does not
generate those independent inputs or derive SQL/allocation counts from HTTP traffic. Missing
inputs, fewer than 20 cold/warm pairs, incomplete records or runtime drift block acceptance;
measured gate violations fail it. All three authorized audience profiles are required for
performance closeout. The aggregate gate checks the bound audience, browser/machine and current
three-script harness fingerprint too; it cannot bypass the per-profile identity checks.
First-ready latency is separate from complete traversal latency. Raw
historical reports are retained; new output defaults to `.tmp/complete-workload/browser.json`.
Browser identity digests do not replace authorization/output parity tests or actual production
SQL, source-read, first-request-after-change and doubled-population instrumentation.

Build an application page bundle from its maintained browser source, then stage it through the
registered ECS page identity. Post an `application/zip` body containing root `index.html` and
assets below `assets/` to
`POST /api/control/web/applications/{applicationId}/pages/{entityId}/bundle-drafts?expectedLatestRevision={revision}`.
Read that immutable revision back through the revision endpoint and compare the HTML and every
asset byte and hash with the release manifest. Only then activate it through the separate `active`
route with the expected current active revision. The routes resolve the versioned content owner
from `system.web.page`; they never accept a raw content-page ID. The previous revision remains an
exact rollback target. Mutable HTML is served with `private, no-store`; content-addressed assets
use a private immutable cache policy, while unhashed assets remain `private, no-store`.

For release acceptance, create a version-2 manifest with `--runtime-target` and `--signing-key`.
The reviewed target pins database connection identity (not mutable database bytes), application,
activation, extension-resolution and page revisions/fingerprints, audience binding, executable
query result/source fingerprints, and action contract fingerprints. Keep the Ed25519 private key
outside source control and pin the public key independently of the manifest. Run
`release:verify-live` with `--trusted-public-key` and `--browser-evidence`; it rejects unsigned or
tampered manifests, runtime or asset drift, cache-policy failures, missing live browser observations,
and changes during verification. Browser evidence is bound to the signed manifest, exact listener,
and seat; source tests alone cannot satisfy the wheel behavior and player/DM dossier checks.

Parameterized release probes include their reviewed `input`, `perspective`, and `campaignId`;
the verifier rejects campaign drift and compares result/source fingerprints for that exact query.
Item releases also declare `expectedRuntime.itemView` with the observer, item IDs, perspectives,
and viewport widths. Browser evidence must cover all three tabs for every declared selection,
match corresponding signed server probes, record return/request-budget/knowledge checks, and
include non-overflowing layout measurements at every declared width. Unavailable tabs cannot
satisfy item release acceptance. Actual Actor verification uses an isolated authorized host with
the reviewed artifacts, preserving the shared live seat.

## Scoped live-change recovery

The browser's change stream belongs to the currently authorized application, state space, campaign
and perspective. Scope replacement closes the old stream and starts fresh cursor reconciliation;
queued frames from retired streams cannot invalidate or populate the new scope. Character and Item
consumers retire their caches on their registered-object notices. Unmatched typed effects still emit
application-scoped compatibility invalidation while legacy query dependencies remain in use; one
matched effect in a transaction never proves coverage of its unmatched siblings.

Scoped live-change recovery uses a durable `system_change_recovery` marker. At normal application
and web schema initialization, SQLite triggers cover all application tables except delivery rows,
operation audit, derived FTS5 indexes (including their internal shadow tables), and SQLite/EF
infrastructure. Unknown virtual-table storage modules leave coverage conservative. Typed ECS transactions acknowledge only their own
ECS mutation delta when durable delivery evidence exists; other writes remain conservative scope
invalidations. Missing marker coverage or schema drift also falls back to scope invalidation.
An older database is not migrated by opening a change feed. Apply migrations through the normal
initialization boundary and retain a pre-migration database backup: downgrading this marker requires
restoring that backup, because removing its table alone would leave triggers referencing it.
Initialization compares exact trigger definitions and repairs missing or altered coverage. An
unchanged schema preserves its existing recovery stamp and triggers without database writes.

## Scheduled provider work

Scheduled AI workers claim at most their available execution capacity, counting exhausted-work
finalization in the same bound. Waiting work remains durable and unclaimed instead of consuming
an unrenewed lease in a local semaphore queue. Immediately before invoking a provider, the executor
rechecks the lease owner, token, attempt and expiry. Running calls retain their existing heartbeat
and transactional result fence; this does not promise exactly-once external provider execution
after process loss or a provider/network failure.

## Changes needing confirmation

Pause for confirmation before introducing permanent IDs, changing schema meaning, adding a migration, changing a public surface, crossing an ownership boundary semantically, or performing a destructive operation that the user has not already authorized.

At completion, report what changed, relevant check results, and any deliberate exclusions. Update current documentation only if a durable rule or operating procedure changed.
