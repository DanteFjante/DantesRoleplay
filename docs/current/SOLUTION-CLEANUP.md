# Solution cleanup, optimization and simplification opportunities

Reviewed 2026-09-07 against `df0633431ad08e35b744574baa615f6b1ed5a312`.

## Recommendation

Start with unused code, stale examples, duplicate generation/canonicalization code,
and repeated test setup. The largest structural opportunity is to finish the
registered read path and then remove superseded browser assembly—not to merge the
projects or move game logic into C#.

This document records **18 opportunities**, not implementation approval or a new
numbered-slice execution plan. Nothing listed has been implemented. Runtime code,
catalog content, database contents and publication state were left unchanged.

The existing [slice review](SLICE-REVIEW.md) owns correctness defects. Its missing
view data, snapshot checks, notifications, scheduling leases and acceptance gaps
must not be hidden by cosmetic refactoring. References to that review below identify
dependencies rather than duplicate its backlog.

## Evidence and limits

Reviewed project/build definitions, capability ownership, selected production
callers, browser adapters, catalog mechanics and serializers, tests, and static
asset delivery. File sizes below are physical line counts, not estimates of
removable code. Generated migration/model-snapshot output was excluded from the
large-file comparison. Quarantined history, world-building media and rulebook PDFs
were not inspected.

| Concentrated source | Lines | Why it matters |
| --- | ---: | --- |
| D&D `styles.css` | 6,054 | All-feature style ownership and cascade complexity |
| `DantesRoleplayDbContext.cs` | 3,296 | Many capabilities' mappings and invariant guards in one file |
| `game-server-context.js` | 2,857 | Transport, joins, completeness and application read assembly |
| `WebInterfaceEndpoints.cs` | 1,922 | Many route families and response/security adapters |
| `SystemWorkspaceElement.cs` | 1,447 | JavaScript embedded inside a C# string |
| `connected-hub-envelope.ts` | 1,377 | Large second-stage application/UI projection |
| `state.js` | 1,232 | Navigation, filtering and many handwritten shape validators |
| `Dnd2024AbilityCheckTests.cs` | 10,724 | Broad rules coverage and a substantial nested harness |

Fresh diagnostics performed for this review:

- TypeScript with `--noUnusedLocals --noUnusedParameters --incremental false`
  reported **7 unused bindings**. This stricter diagnostic command fails today;
  it is not a failure of the existing normal verification configuration.
- Read-only GETs of three shared component scripts, with decompression disabled
  and `Accept-Encoding: gzip, br`, all returned 200 with no `Content-Encoding`,
  `ETag`, or `Cache-Control`: `system-client.js` 16,568 bytes,
  `system-workspace.js` 70,090 bytes, `application-conversation.js` 11,033 bytes.
- Inspected the preceding review's TRX from this same HEAD: the full suite took
  12 m 23 s; `Dnd2024AbilityCheckTests` contained 381 cases totaling 677.15 seconds
  of reported case durations. A World Feature 13 case took 147.43 seconds and a
  location-primitives case 146.97 seconds. Parallel test durations overlap and
  include contention/waiting; these are targeting signals, not CPU profiles.

The full suite was **not rerun** for this documentation-only review. Its earlier
passing result is recorded in the slice review. No claimed percentage speedup or
line/file saving should be treated as measured until the proposed change is tested.

## Priorities

P1: strongest near-term value. P2: worthwhile bounded refactor. P3: profile or defer
until the owner is being changed. Effort is relative, not a calendar estimate.

| ID | Priority | Opportunity | Effort / risk | Expected effect |
| --- | --- | --- | --- | --- |
| C01 | P1 | Remove confirmed unused bindings; enable the guard | Small / low | Less dead code |
| C02 | P1 | Correct or retire stale examples and architectural comments | Small / low | Less misleading maintenance material |
| C03 | P2 | One schema-validator generation entry point | Small / low–medium | Two generator scripts can become one |
| C04 | P2 | One subscription canonicalization implementation | Small / medium | Less duplicated identity-sensitive logic |
| C05 | P1 | Reuse isolated catalog test templates and focused harnesses | Medium / medium | Less setup code and likely faster feedback |
| C06 | P1 | Finish read migration, then retire superseded browser assembly | Large / high | Largest plausible reduction in reads and application glue |
| C07 | P2 | Reuse one bounded read-model envelope adapter | Medium / medium | Less validation/transport duplication |
| C08 | P2 | Prepare a reusable object-dependency index | Medium / medium | Less work while holding the SQLite writer |
| C09 | P2 | Retain compiled AI tool schemas for the invocation scope | Small–medium / medium | Avoid repeated schema compilation |
| C10 | P3 | Page collections before expensive hydration, where semantics permit | Medium–large / high | Less per-page work |
| C11 | P3 | Reduce repeated active-catalog materialization across request scopes | Large / high | Fewer source reads, parses and registry lookups |
| C12 | P2 | One browser-asset path, with appropriate static-asset caching | Medium / medium | Less mixed-language hosting code and repeat transfer |
| C13 | P2 | Move model configuration beside a few capability owners | Medium / medium | Smaller central DbContext; clearer ownership |
| C14 | P2 | Group HTTP endpoints by existing capability/security boundary | Medium / medium | Smaller router and less repeated adapter code |
| C15 | P2 | Finish source-layout/build convention consolidation | Medium / medium | Fewer placement rules and duplicated build settings |
| C16 | P3 | Make catalog JavaScript reviewable; selectively share authoring helpers | Medium–large / high | Better diffs, potentially less authored duplication |
| C17 | P3 | Consolidate proven duplicate CSS and load feature styles intentionally | Medium / medium | Less style duplication and possibly smaller initial CSS |
| C18 | P3 | Profile repeated sandbox parsing before considering immutable parse reuse | Medium–large / high | Potential CPU saving without shared execution state |

## Opportunities

### C01 — Remove the seven compiler-confirmed unused bindings

In [connected-hub-envelope.ts](../../src/system/web-interface/dnd2024/src/server/connected-hub-envelope.ts#L747),
`options` at line 749 is unused, and `liveMapFeatures` at line 804 is computed but
never read. Five visual fixtures have unused default `React` imports:
`character-fixture.tsx`, `item-details-fixture.tsx`, `item-integration-fixture.tsx`,
`item-recipes-fixture.tsx`, and `item-uses-fixture.tsx` under `test/visual/`.

Remove the unused local/imports. Review the options parameter's callers before
removing that argument; currently accepting it misleadingly suggests an asset-base
option has an effect. Then enable the unused-local/parameter checks in
[tsconfig.json](../../src/system/web-interface/dnd2024/tsconfig.json).

**Verify:** the stricter command and normal web verification both pass. This is a
small cleanup, not a significant performance optimization. Do not extend the
seven-binding result into a claim that other JavaScript is proven dead: `checkJs`
is not enabled, and exported APIs are not exhaustively checked for unused consumers.

### C02 — Retire misleading examples and historical architecture commentary

[DantesRoleplay.MCPServer.http](../../DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.http)
still demonstrates `commit` kinds `component`, `effects`, and `action`. Those are
absent from the current [commit dispatcher](../../DantesRoleplay.MCPServer/Mcp/CommitMcpTool.cs)
and catalog; the supported direct action kind is `application.action.execute`.
Update the scratch client from current descriptors, or remove it if the maintained
protocol tests and current operations guide fully replace its purpose. Do not run
its old examples against the live database as part of cleanup.

The 156-line [server README](../../DantesRoleplay.MCPServer/README.md) retains template
guidance alongside project-specific operations. Reduce it to a pointer after moving
any still-unique useful guidance to its existing current owner. Also correct these
source comments without changing behavior:

- [DbContext](../../DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs#L33) calls
  itself the only type that knows a database exists and cites old architecture sections.
- [Tools project](../../DantesRoleplay.Tools/DantesRoleplay.Tools.csproj) describes
  avoiding Jint through DataAccess, although DataAccess directly references Jint.
- [Program.cs](../../DantesRoleplay.MCPServer/Program.cs) says all registration is in
  one method while additional provider, bridge and web registration surrounds it.

**Verify:** every retained example resolves to a current descriptor, documentation
has one maintained owner, and useful safety/configuration guidance is preserved.

### C03 — Merge the two near-identical validator generators

[generate-item-validator.mjs](../../src/system/web-interface/dnd2024/scripts/generate-item-validator.mjs)
and [generate-board-validator.mjs](../../src/system/web-interface/dnd2024/scripts/generate-board-validator.mjs)
repeat schema loading, AJV standalone compilation, runtime-import rewriting,
output writing and drift checking. Use one small table-driven generator for the
three Item and two Board targets.

Preserve per-target compiler settings: Item deliberately uses `strictTypes: false`;
Board does not. Preserve the Item contract export and existing `--check` coverage.
Generated validators remain derived artifacts; do not hand-edit them or replace
precompilation with an in-browser compiler.

**Verify:** generated semantics and contract fingerprints are unchanged, all targets
have drift tests, and normal CSP-compatible browser builds pass. This can eliminate
one generator file; fewer emitted validator files is not itself a goal because
independent features benefit from independent loading.

### C04 — Share subscription canonicalization, not every SHA-256 call

[SubscriptionFile.cs](../../src/system/catalog/persistence/SubscriptionFile.cs#L35)
and [SubscriptionStore.cs](../../src/system/events-and-notifications/persistence/SubscriptionStore.cs#L218)
separately implement the same sorted top-level JSON-object and normalized ID-array
canonicalization. Both feed the content identity of the same subscription records.
Move that behavior into one narrowly owned implementation used by both paths.

**Verify:** golden content hashes and import/export round trips remain byte-for-byte
compatible, including whitespace, ordering, nested raw JSON and duplicate IDs.
Do not turn this into a solution-wide hashing rewrite: existing hash domains,
case conventions, field ordering and normalization rules intentionally differ.
The existing `ContentHash` owner is a reason to consolidate compatible policy,
not permission to change historical identities.

### C05 — Reduce repeated catalog test setup and improve test ownership

[CatalogWorldFeature13Tests.cs](../../DantesRoleplay.Tests/CatalogWorldFeature13Tests.cs)
repeatedly copies the catalog and imports it into a new SQLite database; Feature
12 and location tests follow similar patterns. This setup is worth measuring
separately from mechanic execution, given the long cases in the existing TRX.

Reuse a small test-only owner for catalog-copy/fixture-relationship setup. For tests
whose subject is gameplay rather than import, clone a once-reviewed initialized
template into a private database. [SqliteFixture.CloneOf](../../DantesRoleplay.Tests/SqliteFixture.cs)
already supports this, and the nested harness in
[Dnd2024AbilityCheckTests.cs](../../DantesRoleplay.Tests/Dnd2024AbilityCheckTests.cs#L8942)
already caches templates: extend that pattern where absent rather than rebuilding
it or claiming the entire suite lacks caching.

Separately organize the 10,724-line AbilityCheck test class into a few actual
capabilities, reusing its harness rather than copying it. Names such as rest,
creation, inventory and social behavior are easier to target than one historical
AbilityCheck owner or numbered World Feature classes.

**Verify:** tests still have isolated writable databases, mutation/rollback tests
remain independent, and representative fresh-import tests continue to import from
scratch. Compare full wall time and setup time; do not sum parallel case times as
a promised saving. Splitting the large test class may add files while reducing
duplicated setup and making failures easier to locate.

### C06 — Remove old read assembly only after completing the replacement

The [2,857-line loader](../../src/system/web-interface/dnd2024/src/server/game-server-context.js),
[1,377-line hub projection](../../src/system/web-interface/dnd2024/src/server/connected-hub-envelope.ts),
[1,232-line state module](../../src/system/web-interface/dnd2024/src/state.js), and
[hub types](../../src/system/web-interface/dnd2024/src/data/hub-types.ts) carry
overlapping transport, structural assembly, shape validation and presentation work.
The registered-object and legacy assembly paths coexist.

Use completed per-feature reads to replace browser-side ECS joins, then delete only
the superseded join/projection branches and obsolete intermediate types. Keep
presentation mapping where it actually serves the UI. Prefer a typed response per
loaded feature over repeatedly reconstructing one giant all-feature envelope.
Migrate touched JavaScript to checked JSDoc or TypeScript incrementally; do not
create another handwritten declaration layer to conceal unchecked implementation.

**Dependency:** slice-review R01/R06/R07/R11. Emptying fields is not an optimization,
and unused new object contracts should be wired up rather than immediately deleted.
**Verify:** authorized record parity, loaded/empty/error distinctions, paging,
character/item regressions, and both first-ready and complete-workload budgets.
This is the strongest plausible code/read reduction, but no safe removable LOC
count has yet been established.

### C07 — Extend the existing envelope adapter across compatible query clients

[item-read-response.ts](../../src/system/web-interface/dnd2024/src/server/item-read-response.ts)
already centralizes bounded body reading, the exact envelope, scope/fingerprints,
status handling and schema validation for Item clients. Similar generic checks are
repeated in [encounter-board.js](../../src/system/web-interface/dnd2024/src/server/encounter-board.js),
[board-draft.ts](../../src/system/web-interface/dnd2024/src/server/board-draft.ts),
and registered query adapters in the hub loader.

Extract only the common read-model transport/envelope contract, with explicit
per-query byte limits, schema validator, allowed statuses and scope checks.
Application-specific board geometry, disclosure and media URL checks stay with
their feature. Keep independent query/cache state; sharing a parser does not mean
sharing private results across audiences.

**Verify:** forged/extra envelope fields, oversized bodies, wrong schema/scope,
stale source revisions, cancellation and authorization failures behave consistently.
Do not merge all clients into a generic “do anything” client or relax a stricter
existing feature contract to the weakest one.

### C08 — Prepare object dependencies outside the per-write scan

[ApplicationObjectChangeTransactionParticipant.cs](../../src/system/projection-materialization/persistence/ApplicationObjectChangeTransactionParticipant.cs#L31)
loads definition IDs, all registered object versions, component inputs and dependency
inputs, then deserializes/contracts and computes consumers inside every staging
operation. Cost grows with retained definitions and versions, not only changed data.

Prepare a bounded immutable dependency index keyed by database/application and exact
registry generation. Reuse it across batches while still producing durable notices
inside the authoritative transaction. Preserve retained-version consumers and the
generic fallback; “latest object only” is not a safe shortcut.

**Verify:** instrument SQL/allocations and writer-held duration with growing object
history; test definition changes, rollback, replay and audience filtering. Resolve
slice-review R04/R05 before using this optimization as evidence that delivery is
correct. Runtime improvement is a hypothesis until those measurements exist.

### C09 — Avoid compiling the same AI tool schema repeatedly

[AiService.cs](../../DantesRoleplay.LocalAI/Services/AiService.cs#L242)
compiles the input schema for each tool invocation; `UniqueTools` also compiles
schemas to validate tool registration but discards the compiled result.

Retain the compiled schema alongside the validated tool definition for the bounded
request/tool-set lifetime. This does not require coupling LocalAI to DataAccess or
turning its independent schema validation into a game-rule owner. A later global
cache would require exact content keys, count/byte bounds and schema-library
concurrency verification; begin with scope-local reuse.

**Verify:** count compilations for repeated tool calls, measure allocations, and
retain malformed-schema, rejected-argument, changed-definition and tool-authorization
tests. Never cache an authorization decision just because its input schema matches.

### C10 — Investigate collection paging before full endpoint hydration

[ProjectionCollectionMaterializer.cs](../../src/system/projection-materialization/persistence/ProjectionCollectionMaterializer.cs#L62)
reads the full bounded candidate and nested-edge sets, hydrates entities/components,
sorts all items, then applies cursor offset and `Take(pageSize)`. Small pages therefore
repeat whole-collection preparation. It is bounded today, not an unbounded scan.

Profile the maximum supported collection across several pages. Where declared sort,
filter and completeness semantics can be preserved, select ordered page identities
in the database and hydrate just those endpoints. Otherwise retain the bounded
implementation or reuse a safe immutable preparation within its established scope.

**Verify:** unchanged stable ordering/tie breaks, total counts, missing-endpoint
behavior, exact schemas and source-revision-bound cursors; edits must stale old
cursors. A page-only fingerprint would change the contract and is not an acceptable
shortcut. Do not increase collection limits merely to hide a poor query plan.

### C11 — Profile and simplify repeated activated-catalog preparation

[ActivatedApplicationCatalogProvider.cs](../../src/system/catalog-navigation/persistence/ActivatedApplicationCatalogProvider.cs#L388)
caches navigators/snapshots only within its
[scoped registration](../../src/system/catalog-navigation/hosting/CatalogNavigationComponentRegistration.cs).
On a new scope the materializer can read/hash source files, parse records, register
object definitions and construct navigation again. The separate
[ActivatedApplicationDocumentReader](../../src/system/application-activation/persistence/ActivatedApplicationDocumentReader.cs)
also performs exact filesystem verification.

First measure repeated equivalent requests with an unchanged activation. Then
consider one immutable, fingerprint-bound prepared catalog snapshot with explicit
invalidation/verification ownership. A more extensive option is serving captured
verified activation bytes through the existing activation owner; that requires a
reviewed storage/authority boundary, not an incidental caching refactor.

**Verify:** changed source files/registrations, activation changes, unpublished apps
and cross-database contexts fail closed. Do not promote the current scoped provider
to singleton: its state and dependencies are scoped and its existing dictionaries
are not a safe process-wide cache. This is a high-risk profile-first opportunity.

### C12 — Standardize browser asset hosting and avoid repeat static transfers

[SystemWorkspaceElement.cs](../../DantesRoleplay.Web/Interactions/SystemWorkspaceElement.cs)
contains 1,447 lines of embedded JavaScript; another 221-line
[ApplicationConversationElement.cs](../../DantesRoleplay.Web/Interactions/ApplicationConversationElement.cs)
uses the same pattern. Other modules already live under `BrowserComponents/` and
are served by [BrowserComponentAssets](../../DantesRoleplay.Web/Interactions/BrowserComponentAssets.cs).
Move the embedded modules into that established asset path, preserving their URLs,
imports and security filters, and remove redundant C# string wrappers.

The current asset reader calls `File.ReadAllTextAsync` per request. The three live
responses measured above total **97,691 bytes**, without explicit compression or
validators. Add a deliberate static-asset policy: conditional requests and bounded
server-side asset reuse, with release-aware invalidation; evaluate compression for
static text. A stable URL needs revalidation, not a year-long immutable lifetime
unless the URL itself becomes content-addressed through an approved release change.

**Verify:** served-byte parity, MIME/CSP/security headers, module imports, asset
changes, conditional requests, and actual wire bytes. Keep private game responses
and event streams out of a blanket caching/compression rule. Moving two C# wrappers
to two JS modules is primarily better ownership/tooling, not a net file reduction.

### C13 — Give large EF model sections explicit capability owners

[DantesRoleplayDbContext.cs](../../DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs)
already has named `Configure...` methods, so there is an existing seam. Trigger
scheduling, play recording, activation and other models need not all occupy one
3,296-line file.

Move a few coherent configuration groups beside their persistence owners, keeping
one DbContext, the same assembly and the same transaction/invariant enforcement.
Leave save-boundary guards visibly invoked by the DbContext. Avoid a separate
configuration class/file for every trivial property or table.

**Verify:** the relational model is unchanged, no pending model differences or
unintended migration appears, and migration/immutability/rollback tests pass.
This improves navigation and merge-conflict isolation; it does not inherently
reduce runtime SQL or total LOC, and may add a small number of useful files.

### C14 — Group HTTP adapters without merging their security policies

[WebInterfaceEndpoints.cs](../../DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs)
mixes publication, content, ECS reads, conversations, AI/control operations,
settings, observations and event streaming across 1,922 lines.
[Program.cs](../../DantesRoleplay.MCPServer/Program.cs) separately repeats endpoint
filter/rate-limit registration for host-specific adapters.

Extract route groups around existing capability owners and reuse small policy-aware
registration helpers. Keep routes requiring observations, private operator writes,
ordinary reads and streams visibly distinct. Use a few cohesive groups, not a file
per route or a universal exception/status mapper that erases feature recovery codes.

**Verify:** route inventory and payloads are identical; unauthorized, remote-access,
confirmation, rate-limit, cancellation and SSE tests cover the same policies.
Run the protocol walk if dependency registration or the MCP-facing surface changes.
This is a maintainability refactor, not evidence of improved request latency.

### C15 — Finish the existing source-layout and build conventions

Capability files under `src/system/*/{domain,persistence,hosting,tests}` compile
through links in the [core](../../DantesRoleplay/DantesRoleplay.csproj),
[DataAccess](../../DantesRoleplay.DataAccess/DantesRoleplay.DataAccess.csproj) and
[test](../../DantesRoleplay.Tests/DantesRoleplay.Tests.csproj) projects, while some
related owners remain physically inside project directories. The linked
`**/domain/*.cs`, `**/hosting/*.cs` and `**/persistence/*.cs` conventions also require
care when adding nested source folders.

Choose the existing capability-first convention for future touched owners and
finish moves in bounded groups while preserving assembly identity. Add a build
check for orphaned or multiply included source files rather than relying on a
contributor to remember which folder compiles into which assembly. Consolidate
repeated common framework/nullability settings in a small shared build-properties
owner if that actually reduces drift; keep publish-only and test architecture
settings explicit. Review shared package versions without removing deliberate
security pins or dependency-direction constraints.

**Verify:** clean checkout builds, identical compiled source inventory/resources,
test discovery, CLI startup and host publish output. Do not merge seven projects
into one merely to reduce file count. Keep small capability registration classes
when they genuinely identify an owner; a giant registration file is not simpler.

### C16 — Improve authored mechanic readability before inventing a shared rules framework

Files such as [rest.begin.js](../../catalog/applications/dnd2024/mechanics/data/dnd2024.mechanic.rest.begin.js)
and [character.level-one-rules.project.js](../../catalog/applications/dnd2024/mechanics/data/dnd2024.mechanic.character.level-one-rules.project.js)
contain dense one-line logic. Closed-object, integer, source/reference and shape
checks recur across mechanics. This makes small semantic changes hard to review,
even when physical line counts look impressively small.

Format mechanics when they are deliberately versioned, and compare truly identical
helpers before sharing them. If enough duplication remains, consider a narrowly
scoped authoring-time helper/generation step that emits self-contained sandbox
scripts, or existing declared mechanic composition where it represents real rule
reuse. Avoid a runtime loader, ambient shared globals, or a new abstraction for
every small predicate. Similar names do not prove identical validation semantics.

**Verify:** deterministic outputs/effects and sandbox limits, updated exact source
fingerprints and dependent contracts, catalog validation and reviewed activation.
Even whitespace changes can alter exact source fingerprints. Do not mass-format
immutable active sources as a supposedly risk-free cleanup. Game-specific helpers
stay in catalog JavaScript; **no D&D rule or eligibility calculation moves to C#**.

### C17 — Consolidate CSS based on actual usage and cascade evidence

[styles.css](../../src/system/web-interface/dnd2024/src/styles.css) contains 6,054
lines spanning many views, including late scoped-map, knowledge-overlay and mobile
selector adjustments. Separate feature styles such as `character-page.css` already
provide a local convention.

Measure used styles for real views/breakpoints, identify exact redundant declarations
within the same cascade context, and consider loading feature-specific styles with
their lazy feature. Keep shared tokens and layout primitives central. Do not label
a selector dead just because its class is composed dynamically or only appears in
an error, print, focus, mobile or unavailable state.

**Verify:** before/after CSS transfer and rendered parity at desktop/mobile widths,
keyboard/focus, overflow, maps/boards and error states. This review has not established
a removable-selector count or a CSS byte-saving estimate. Splitting CSS alone adds
files and does not necessarily reduce total bytes.

### C18 — Profile sandbox parsing, preserving a fresh execution realm

[JintMechanicEngine.cs](../../DantesRoleplay.DataAccess/Mechanics/JintMechanicEngine.cs#L68)
creates a new engine, serializes its payload, evaluates the fixed harness, and
constructs `new Function('ctx', __source)` for each run. Repeated exact mechanics
may spend measurable time parsing harness/source as well as executing rules.

Measure those stages separately before changing anything. If parsing dominates,
investigate bounded reuse of immutable parsed/prepared code supported by the pinned
engine, keyed by exact source and execution configuration. Confirm the engine's
actual API and concurrency behavior before implementing that option.

**Verify:** deterministic RNG, prototype/global isolation, memory/statement/time
limits, cancellation and repeated hostile-script cases. Do not pool live engines,
cache `ctx`, or replace rule execution with C# shortcuts to claim a speedup. Fresh
realms are a safety feature; keep the current implementation if safe reuse does not
show a meaningful measured gain.

## What should not be “cleaned up” casually

- Retained compatibility identities and legacy schemas have live/history owners.
  The previous retention review is not evidence that their files are dead. File
  deletion alone can be undone by export and can strand references.
- Shipped EF migrations, snapshots and generated validators are not removable just
  because they are large or repetitive. Preserve reproducibility and upgrade history.
- The ignored live database and its blobs are runtime data, not disposable build
  output. No database/media deletion or import is proposed here.
- Authorization/disclosure checks, snapshot validation, transaction boundaries,
  audit records, replay checks and resource limits are not redundant “extra code.”
- Caches need exact scope, invalidation and bounds. Do not obtain lower request
  counts by omitting records or suppressing failures.
- Avoid whole-repository formatting, broad folder shuffles, new service layers,
  generic repository wrappers, or removing tests solely to lower LOC/file counts.

## Practical starting order

1. C01–C04: small, well-bounded cleanup with explicit compatibility checks.
2. C05: cheaper, clearer test feedback before larger refactors.
3. Resolve correctness blockers from the slice review, then C06/C07.
4. Measure C08/C09/C12 and implement only demonstrated improvements.
5. Tackle C13–C15 as owner-specific changes; keep C10/C11/C16–C18 profile/review-led.

For each accepted opportunity, record the actual code/file delta, behavior retained,
verification and measured cost change in its commit. A smaller diff and fewer
maintenance decisions are useful outcomes even when the safest design has more files.
