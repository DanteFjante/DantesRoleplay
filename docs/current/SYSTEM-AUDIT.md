# System audit: redundancy, obsolete files, and performance

**Audit date:** 6 September 2026. **Source baseline:** `017ed8fec13a54b22b50ef6fa14f04b1cf414c09`.

**Purpose:** a backlog for investigation and later repairs. This document records findings; it does not authorize deletion, migration, changed public contracts, or feature acceptance. No implementation, catalog, configuration, or live game-state changes were made during this audit.

## What deserves attention first

The largest confirmed user-facing cost is loading the connected D&D hub. Two ordinary DM loads made **2,096 HTTP requests each** and took **12.48 and 13.00 seconds**. The knowledge endpoint accounted for **9.39 and 9.45 seconds** of those loads. These measurements use the production loader against the running local server, rather than a simulated response fixture.

The clearest repository cleanup is **678 tracked build-output files occupying 261.00 MiB** in five old verification directories. These include obsolete assembly copies and third-party binaries. The clearest runtime storage opportunity is repeated web assets: **591 stored asset rows contain 184.13 MiB of bytes, while their 179 distinct content hashes represent 26.51 MiB**. The remaining **157.62 MiB** is repeated payload by stored hash. A storage redesign could preserve every page revision while sharing identical bytes; deleting revision history is a separate decision.

There are also real sources of future regressions: unconsumed pagination cursors, three copies of the item response-envelope checks, excluded former-host sources and tests, and large mixed-purpose files. Some apparent duplication is deliberate: 86 compatibility catalog records have explicit retention requirements, and several large frontend modules are still used by tests.

## Scope and evidence standard

The pass inventoried **6,272 tracked files** within the allowed scope, including binaries and authored assets by metadata. It mechanically scanned **1,060 code/style/script files, approximately 206,800 lines**, excluding migration-generated code, generated runtime validators, and build directories from the code comparison. It inspected project configuration, registration, ownership, reference paths, pagination, caching, request orchestration, persistence, validation, AI scheduling, and release checks. The solution has seven C# projects and one game-content project; `src/system` has 46 capability directories.

This was a broad inventory with targeted source inspection, not a manual review of every line. Exact-file comparison normalized line endings; the additional duplicate-block screen compared whitespace-normalized 40-line windows at 10-line intervals. Neither method proves that all semantic duplication has been found. Frontend reachability was checked from `src/server-host/main.tsx` through literal static and dynamic imports, then candidate names were searched independently. Reflection, runtime registrations, historical migrations, and database-held references need separate retirement proof.

Excluded: `_to_delete/`, world-building documents/media under `docs/world/`, rulebook references under `docs/pdfs/`, prior generated evidence, dependency caches, and unrelated private configuration. Public/source asset filenames and sizes were inventoried; artwork was not evaluated. Historical Word plans were identified by path only and were not reopened. No network load test, external package-advisory search, heap dump, CPU profile, full test suite, or full catalog validation was performed. Package versions have not been classified as obsolete merely because a newer release might exist.

Evidence labels:

- **Measured:** reproduced or counted in this pass. Timing observations are samples, not percentiles.
- **Static:** supported by source, references, or evaluated build configuration. Runtime impact may still need measurement.
- **Candidate:** a plausible improvement requiring ownership or profiling evidence before a fix.
- **Retained:** an intentional compatibility, test, recovery, or audit boundary; not deletion-ready.

Priority means suggested order of investigation: **P1** affects current usability or completeness; **P2** warrants planned engineering; **P3** is maintenance after higher-impact work. Finding labels are document references, not new runtime IDs. Every finding below starts **Open for study**, except entries explicitly classified as retained.

## Findings register

| ID | Priority | Category | Finding | Evidence |
| --- | --- | --- | --- | --- |
| P01 | P1 | Performance | Hub bootstrap loads whole-world detail before the selected view is ready | Measured + static |
| P02 | P1 | Performance | Knowledge builds the whole projection, then hydrates selected records again | Measured endpoint; static cause |
| P03 | P2 | Performance | Chronology scans every entity for a component | Measured endpoint; static cause |
| P04 | P2 | Performance | Active catalog materialization rereads and parses source per service scope | Static |
| P05 | P2 | Performance | Role-constraint checks load complete schema history and constrained state | Static |
| P06 | P2 | Performance | Schema compilation and same-schema validation have serialization points | Candidate; bounded cache confirmed |
| P07 | P2 | Performance | Play-record writes share one synchronous process-wide gate | Static |
| P08 | P2 | Performance | Scheduled AI jobs run serially within each polling batch | Static |
| P09 | P2 | Storage | Page revisions repeat identical asset bytes | Measured |
| P10 | P2 | Storage | Activation document evidence and indexes are a major growing allocation | Measured; redesign candidate |
| P11 | P2 | Performance assurance | Initial-JavaScript budget excludes the time and requests needed for a usable hub | Static + measured |
| P12 | P3 | Redundancy/performance | Location cache cannot reuse data across ordinary hub reloads | Static + measured repeated traffic |
| C01 | P1 | Completeness | Some directory/relationship reads ignore continuation cursors | Static |
| O01 | P2 | Obsolete outputs | Old build directories and binaries remain tracked | Measured + static |
| O02 | P2 | Obsolete source | Fourteen source/test files are deliberately excluded from compilation | Evaluated build + static |
| O03 | P3 | Obsolete UI | Two unreferenced frontend components retain matching CSS | Static |
| O04 | P3 | Asset ownership | Fixture assets and other source media need an owner/retirement inventory | Static; candidate |
| O05 | P2 | Obsolete data candidate | A second tracked database can be mistaken for the live store | Measured + static; candidate |
| O06 | P3 | Obsolete documentation | Entry-page status is dated and does not describe this checkout | Static |
| R01 | P3 | Retained redundancy | Fifteen old mechanic path pairs remain alongside the application catalog | Static; retained |
| R02 | P3 | Test organization | Fixture world/projection code lives under the production source directory | Static; retained by tests |
| R03 | P2 | Redundancy | Item clients repeat envelope and request-validation logic | Static |
| R04 | P3 | Maintainability | Several files combine many owners and change paths | Measured size; refactoring candidate |

## Performance and storage findings

### P01 — Whole-world work blocks hub bootstrap

**Owner:** D&D web loader and generic web read surfaces.

**Evidence:** `src/system/web-interface/dnd2024/src/server-host/main.tsx:52` calls `readGameServerContext` before projecting the ready hub. In `src/server/game-server-context.js:2353`, bootstrap waits for knowledge and chronology together with campaign state; at `2461` it loads the location directory and campaign structure; at `2498` it also loads the DM world directory. `readRawLocationDirectory` at `1133` lists all entities and makes four requests per location. `readWorldDirectory` at `1913` adds containment, people, and faction reads. Paths in this paragraph after the first full path are relative to the same web project.

The measured DM request breakdown was 27 entity-list, 782 component, 583 media, 518 containment, 180 relationship, and six other requests. It delivered 259 locations, 124 people, and 35 factions even with character details deferred. The existing eight-request concurrency limit worked: peak concurrency was eight. It limits pressure but does not reduce total work.

**Impact:** all these reads delay the initial usable hub and are repeated for a refresh or perspective reload. Increasing concurrency or rate limits alone would transfer more pressure to the server.

**Later pass:** load the minimum authorized campaign shell first; request world subsections when opened. Investigate a generic, audience-bound batched directory/component/media projection, reusing existing ECS search and media facilities where their contracts fit. Keep C# generic and let catalog declarations select game-specific meaning.

**Acceptance:** compare the same DM and actor workflows at the same database revision; measure request counts, first usable view, bytes, cancellation, and freshness. Require complete faction/location results, no hidden-content exposure, and preserved existing-view behavior on failure. Establish an agreed latency/request budget before implementation; this audit does not invent an accepted production SLA.

### P02 — Knowledge projection and hydration repeat substantial work

**Owner:** authorized knowledge notebook and canonical source.

**Evidence:** `DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs:1122` calls the notebook reader. `src/system/knowledge/persistence/ApplicationKnowledgeCanonicalSource.cs:87` pages through every entity; `ProjectAsync` at `129` reads each candidate's component pages before knowing whether it is a knowledge record. `ReadDocumentAsync` at `111` reloads the state-space relationships for each document. `AuthorizedKnowledgeNotebookReader.cs:95` then hydrates every selected document and, for actors, rechecks effective knowledge individually. `ApplicationKnowledgeEffectiveStateResolver.cs:26` loads relationships and containments and computes graph evidence on each invocation.

The knowledge HTTP response took **9,391.70 ms** and **9,450.88 ms** in the two DM samples and contained **214,318 bytes** each time. This is measured endpoint latency; the share attributable to individual SQL statements or hashing has not been profiled. The notebook limit applies after world projection, so it does not bound discovery cost.

**Later pass:** preselect records by the declared component types; index relationships once per read; hydrate and revalidate selected records in a bounded batch or consistent read snapshot. Preserve the actor-state, source-revision, world, participation, and familiar/unknown disclosure checks. Simply removing the second validation would weaken the existing boundary.

**Acceptance:** SQL-command counts and allocation measurements at increasing entity/knowledge sizes; stale-state and cross-actor tests; same authorized output and revisions; repeat live timing under both GM and actual actor seats. The Player preview measurement below does not establish actor notebook performance.

### P03 — Chronology performs a full entity scan

**Owner:** chronology projection endpoint.

**Evidence:** `DantesRoleplay.MCPServer/WorldChronologyWebEndpoint.cs:115` loads graph edges; at `146` it pages every entity and at `153` asks for the chronology component on each. It stops at 10,000 entities. The two DM endpoint samples took **962.03 ms** and **856.09 ms**, with **234,374 response bytes**.

**Later pass:** investigate declared component-type discovery using the existing generic search store, then fetch the required graph evidence in a batch. Consider cursor-based chronology delivery if users do not need every event immediately. A change to the public response requires its own reviewed contract.

**Acceptance:** identical calendar, visibility, world-membership and subject filtering; ordering and completeness at the scan boundary; increasing unrelated catalog entities should not proportionally increase chronology component lookups.

### P04 — Catalog materialization repeats filesystem and parsing work

**Owner:** activated catalog navigation.

**Evidence:** `src/system/catalog-navigation/persistence/ActivatedApplicationCatalogProvider.cs:36` builds a snapshot by enumerating active winners, checking source registrations, reading text, and parsing records. `ReadText` at `230` reads and hashes file bytes. The provider's cache at `340` is instance-local; `src/system/catalog-navigation/hosting/CatalogNavigationComponentRegistration.cs:16` registers the materializer and provider as scoped services. Thus a new request scope starts without the prior scope's materialized snapshot. `src/system/application-activation/persistence/ActivatedApplicationDocumentReader.cs:51` independently reads/hashes exact active documents.

**Impact:** a candidate source of repeated disk I/O, parsing, allocation and hashing for catalog/read-model traffic. No causal timing or cache-hit rate was measured here.

**Later pass:** instrument materialization count, file bytes and elapsed time per request. Design shared immutable caching only if invalidation still catches source-file drift, source-registration changes, activation changes and allowed-root changes. Keying solely on activation revision would not preserve the current file-drift check.

**Acceptance:** warm-request allocation/I/O reduction, bounded retention, and existing file-drift/activation tests. Do not turn a scoped provider into an unbounded singleton or share request-specific authorization state.

### P05 — Role constraints have a whole-state validation cost

**Owner:** generic ECS writes and role constraints.

**Evidence:** `DantesRoleplay.DataAccess/Ecs/SqliteEcsRoleConstraintValidator.cs:24` loads eligible type IDs and all their schema versions, parses policies, and, when constraints apply, loads all enabled entities and all state-space components before validating. This is a correctness boundary inside the write transaction, not accidental dead code.

**Later pass:** measure write duration and SQL/allocation counts with 1,000, 10,000 and larger entity sets. Consider caching immutable parsed policies separately from mutable state, and validating affected constraint sets with complete indexes. Keep cross-entity uniqueness/cardinality checks transactionally complete.

**Acceptance:** concurrent-write, enable/disable, uniqueness and multi-effect tests must still assert the same invariants. No live write workload was generated in this audit, and no observed slow write is claimed.

### P06 — Schema validation has explicit lock contention candidates

**Owner:** bounded schema validator.

**Evidence:** `src/system/schema-validation/persistence/BoundedJsonSchemaValidator.cs:45` holds the cache lock while compiling a miss. At `182`, evaluations sharing a retained schema serialize on that schema object. Registration is singleton in `hosting/SchemaValidationComponentRegistration.cs:10`.

**Existing protection:** retention is bounded to 256 schemas, 2 MiB of accounted text and 32,000 nodes; compilation uses a dedicated schema registry. This pass found no basis to repeat an old claim of an unbounded schema cache or a proven memory leak.

**Later pass:** profile lock-wait time under mixed schema misses and concurrent same-schema validation. Assess per-key compilation coordination or safe evaluator instances only after establishing the library's thread-safety requirements.

**Acceptance:** schema equivalence, resource limits, malformed-input handling, bounded cache eviction and concurrent correctness; measured improvement rather than removal of safety locks on speculation.

### P07 — Play recording synchronizes every conversation write

**Owner:** durable play recording.

**Evidence:** `src/system/play-recording/persistence/ApplicationPlayRecordStore.cs:13` declares a static `SemaphoreSlim(1,1)`. `ResumeOrCreate`, `AppendMessage`, `AppendNarrative`, and other writes call synchronous `Wait()` and synchronous database operations; the gate is shared across store instances and conversations.

**Impact:** potential head-of-line blocking and blocked request threads when several conversations write. SQLite's own single-writer behavior remains relevant; removing this gate is not automatically a throughput improvement.

**Later pass:** measure waiting and transaction duration on a disposable database with concurrent conversations. Evaluate asynchronous waiting and narrower synchronization while preserving conversation creation uniqueness, ordered messages, revision checks and atomic narrative/truth commits.

**Acceptance:** concurrency and idempotency tests plus latency/queue measurements. No live conversations were written during the audit.

### P08 — Scheduled AI batches are serial

**Owner:** scheduled AI task worker.

**Evidence:** `src/system/trigger-scheduling/hosting/ScheduledAiTaskTools.cs:158` polls every two seconds; `RunBatchAsync` at `180` selects at most eight unread notifications and awaits each AI task before processing the next. A slow provider response can delay later jobs and the next poll. The outer polling catch is silent, although per-job failures are audited inside the batch.

**Later pass:** add queue age, provider duration and worker-failure visibility before deciding on bounded parallelism or per-provider isolation. Review claiming/recovery separately from scheduling throughput; this pass did not test crash recovery or claim lost jobs.

**Acceptance:** bounded provider concurrency, deterministic claims and idempotent processing, cancellation, audit evidence, and continued absence of self-approved model writes. Provider calls were not exercised by this audit.

### P09 — Identical page assets are stored repeatedly

**Owner:** web-content storage and revision lifecycle.

**Evidence:** `DantesRoleplay.Web/Storage/WebPageStore.cs:427` creates new asset rows carrying full bytes for each page revision. `WebContentDbContext.cs:55` makes revision/path unique; content hash is recorded but is not a shared byte-store key. Read-only SQLite aggregation found 591 rows, 179 unique hashes, 193,073,083 total payload bytes, and 27,800,180 bytes when grouped by hash. Ninety-five hash groups repeat; 412 additional rows account for 165,272,903 repeated bytes.

**Later pass:** design shared immutable asset storage referenced by revision/path. Preserve asset media types, immutable URLs, private cache policy, exact hash verification, rollback and old-page resolution. Compare with the existing blob subsystem, but its accepted media types and authorization contract do not automatically fit arbitrary HTML/JS/CSS assets.

**Acceptance:** all retained page revisions read back byte-for-byte, backups restore, and signed release verification passes. The 157.62 MiB estimate is repeated payload, not promised filesystem recovery: indexes, SQLite free pages, WAL and a migration strategy affect actual size. This is a migration candidate, not approval to delete history.

### P10 — Activation history has significant metadata/index growth

**Owner:** application activation persistence.

**Evidence:** the live `system_application_activation_document` table contains **159,426 rows**. SQLite `dbstat` attributes **53,911,552 bytes** to the table, **21,835,776 bytes** to its logical-identity index, and **3,801,088 bytes** to its primary-key index: **75.86 MiB combined**. Mapping starts at `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs:740`; each activation retains its own document evidence.

**Later pass:** measure growth per activation and lookup plans. Study deduplicated immutable manifest records, compact identity keys, or reviewed archival policy. This evidence supports historical resolution and recovery; similar rows across revisions are not automatically obsolete.

**Acceptance:** exact activation fingerprints, winner ordering, rollback, source-drift detection and historical operation resolution remain valid. No activation records were changed or a migration drafted here.

### P11 — The current budget does not measure a usable page

**Owner:** web performance checks and release verification.

**Evidence:** `src/system/web-interface/dnd2024/vite.server.config.ts:16` enforces a 90,000-byte gzip initial-JavaScript limit. `scripts/bundle-budget.mjs:5` follows static chunk imports only. `src/server-host/main.tsx:57` dynamically imports the data loader and connected-envelope projector before a ready view; the hub is also lazy. These are required for normal use despite being outside the initial chunk graph. The measured network work in P01 can therefore coexist with a passing bundle budget.

**Later pass:** keep the initial budget, add first-ready-view dependency accounting and a read-only end-to-end request/latency regression check. Measure CSS, mandatory lazy chunks, JSON, and browser parse/render time separately. Existing performance marks are useful but `src/observability/performance.js:11` records each mark only once per page lifetime, so repeated switches need interaction-scoped measurements.

**Acceptance:** deliberately delayed knowledge and excessive-request fixtures fail the new gate; normal actor and DM flows pass an agreed budget. Do not label all lazy loading or generated validators as waste: they serve real feature and validation boundaries.

### P12 — The location cache has a narrower lifetime than its TTL suggests

**Owner:** D&D hub loader.

**Evidence:** `src/system/web-interface/dnd2024/src/server/game-server-context.js:1259` uses a WeakMap keyed by `options.fetchImpl`. `readGameServerContext` at `2261` creates a fresh `createHubReadScope` and `scope.fetch` for each load. Ordinary subsequent loads therefore use another cache key even within the ten-second TTL. Both measured DM loads made the same 2,096 requests. The listing cache inside `hub-read-scope.js` correctly reused 27 list pages within a load.

**Later pass:** either describe/implement this as a within-load memoization or establish a deliberately bounded cross-load cache. Any broader reuse must bind principal, audience, campaign, source revision and invalidation; the current narrow scope protects against cross-view disclosure.

**Acceptance:** prove reuse where intended, eviction and cancellation, and immediate retirement on audience or source changes. Treat this as overlap with P01, not a separate promise of another full request-count reduction.

## Completeness and obsolete-file findings

### C01 — Pagination remains incomplete in adjacent loaders

**Owner:** D&D world and campaign loaders.

**Evidence:** `src/system/web-interface/dnd2024/src/server/game-server-context.js:1970` requests one containment page per location with `limit=100` and does not consume `nextCursor`. Faction relationships at `2039` similarly pass one page to `relationshipTargetIds` at `1297`, which only processes `items`. `readCampaignStructure` at `2081` stops when its candidate map reaches 100. `readRawLocationDirectory` at `1133` breaks on malformed/failed pages and treats repeated cursors as completion; the outer scope catches some transport failures, but not every malformed-success case.

**Impact:** a location with more than 100 contained records, a faction with more than 100 links of one kind, or a sufficiently large campaign can silently present an incomplete result. The faction fix at the baseline commit repairs the entity-discovery cutoff; it does not repair these separate loops. No missing live records from these remaining boundaries were demonstrated in this audit.

**Later pass:** inventory every list-consuming helper, consume bounded advancing cursors, or propagate explicit partial/unavailable state. Avoid converting malformed data into a credible empty list.

**Acceptance:** focused fixtures at 99/100/101 records, repeated cursors, oversized pages, malformed JSON, and failures after a valid first page; verify no partial result is presented as complete. Keep this distinct from a performance-only batching change.

### O01 — Tracked build output is the largest clear repository cleanup

**Owner:** solution maintenance and repository hygiene.

| Tracked directory | Files | Bytes | Approx. MiB |
| --- | ---: | ---: | ---: |
| `DantesRoleplay.Tests/.codex-build/` | 139 | 58,417,027 | 55.71 |
| `DantesRoleplay.Tests/.codex-obj/` | 8 | 554,130 | 0.53 |
| `DantesRoleplay.Tests/bin-slice2/` | 470 | 168,801,714 | 160.98 |
| `DantesRoleplay.Tools/.codex-build/` | 53 | 45,612,166 | 43.50 |
| `DantesRoleplay.Tools/.codex-obj/` | 8 | 296,186 | 0.28 |
| **Total** | **678** | **273,681,223** | **261.00** |

These are tracked files, not just ignored local disk usage. Examples include `DantesRoleplay.RuleAccess.dll`, old MCP executables, test-platform assemblies and native SQLite runtimes. `Directory.Build.targets:5` already excludes `.codex-build` and `.codex-obj` from source/resource inputs. The solution has no RuleAccess project. Existing ordinary `bin/` and `obj/` ignore patterns do not cover every alternate output directory, and ignores alone do not untrack files.

**Later pass:** verify no supported launcher or recovery procedure depends on these exact paths, retain any necessary evidence outside the source tree, remove reviewed outputs from tracking, and add precise ignore rules. Build from source in a fresh checkout before accepting removal. Removing tracked files from the current revision does not reclaim old Git-history size; history rewriting is a separate, unapproved operation.

### O02 — Former-host sources and tests remain outside the build

**Owner:** MCP composition, main test assembly, former game adapters.

`DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj:25` explicitly excludes:

- `DevelopmentKnowledgeAudience.cs`
- `KnowledgeBackgroundWorker.cs`
- `StoryPlanWorker.cs`

`DantesRoleplay.Tests/DantesRoleplay.Tests.csproj:62` unconditionally excludes these nine files in that project directory:

- `DevelopmentKnowledgeAudienceTests.cs`
- `CatalogWorldFeature7Tests.cs`
- `CatalogWorldFeature9Tests.cs`
- `CategoryToolTests.cs`
- `InformationTests.cs`
- `ProcedureBoundActionVerifierTests.cs`
- `SessionFeature4Tests.cs`
- `SqliteVecExtensionProbeTests.cs`
- `VerbToolTests.cs`

It also excludes `src/system/events-and-notifications/tests/SubscriptionStoreTests.cs` and `src/system/snapshots/tests/SnapshotFeature1Tests.cs`. These **14 files** exist but do not run in the normal build/test configuration. Fresh MSBuild evaluation produced 61 server compile items and 171 test compile items; those counts are files, not test cases.

**Separate intentional exception:** `DantesRoleplay.Tests/ProtocolWalkTests.cs` is opt-in through `IncludeProtocolWalkTests=true`. Its normal exclusion is not evidence of obsolescence.

**Later pass:** classify each excluded file as superseded, behavior still needing migration, or an explicit retained compatibility specimen. Map its assertions to current owners before deletion. Do not re-enable them as a group: their old dependencies and contracts were intentionally removed from the generic host.

**Acceptance:** a file-by-file disposition with replacement test names; current behavior remains covered and no supported feature is inferred from a source file that never compiles.

### O03 — Two unreferenced UI components and their CSS

**Owner:** D&D web presentation.

Candidates: `src/system/web-interface/dnd2024/src/components/HubUnavailable.tsx` (16 lines) and `ServerCampaignConnected.tsx` (76 lines). Neither appears in the production import graph or another source/test reference; independent name searches found their definitions only. Their `.hub-unavailable` and `.server-campaign` styles remain at `src/styles.css:466` onward. The current entry point uses `BootstrapShell`, `RulesOnlyHub` and `DndInformationHub`.

**Later pass:** verify no external fixture imports them, then remove or relocate them and only their unused selectors. These are strong source-removal candidates, but their unreachability already keeps their component JavaScript out of the production module graph.

**Acceptance:** typecheck, mounted unavailable/connected states, build, and CSS selector/reference check. Do not claim a large runtime performance gain from deleting 92 unreachable lines.

### O04 — Media and fixture assets need explicit ownership

**Owner:** web fixtures, authored media, publication tooling.

The D&D web `public/` directory has **38 tracked files totaling 14,635,427 bytes (13.96 MiB)**. `vite.server.config.ts:12` sets `publicDir: false`, so they are not copied wholesale into the server build. Several filenames are still referenced by the fixture world (`src/server/hub-source.js`) and fixture tests. Treat the group as fixture/source assets, not automatically dead runtime assets.

Additional tracked source media exists under `DantesRoleplay.Web/BrowserComponents/MapImages/` and `BrowserComponents/Media/`, including both `caldris-eredane.png` and `caldris-eredane-v2.png`. No reference to the searched path names was found in the inspected code/catalog text. This does **not** exclude database-held, historical-page, external tooling, or excluded world-workspace references. Content similarity and live blob provenance were not compared.

**Later pass:** create a source-asset inventory with fixture, live publication, immutable blob, and historical reference owners. Relocate fixture-only assets if helpful; retire versions only after that inventory proves they are unused. Do not delete an image because its name has a version suffix.

**Acceptance:** reproducible fixture/browser tests, asset-hash and page readback, and no broken live or historical image URL.

### O05 — A second tracked database has an unclear purpose

**Owner:** runtime startup, development setup and catalog tooling.

`data/dantesroleplay.db` is tracked and **2,150,400 bytes**. Read-only inspection found 134 tables, zero operations and zero web pages. The current server uses the separate `DantesRoleplay.MCPServer/data/dantesroleplay.db`, which had 143 tables, 5,772 operations and three web pages at inspection. The stores are not interchangeable copies.

`DantesRoleplay.MCPServer/Program.cs:22` defaults the database below the content root. `run-mcp-server.ps1` supplies the MCPServer working directory; tooling help also points to the MCP server database. The root database has not been proved to be an intentionally maintained seed, and no supported consumer was established during this pass.

**Later pass:** identify its creation/consumer history and either document an explicit seed contract or retire it. Make database selection visible in startup diagnostics and verify launch behavior from different working directories. Do not import, overwrite, or merge these databases based on filename similarity.

**Acceptance:** fresh setup remains reproducible, the intended runtime binding is unchanged, and any retained seed has an owner and refresh policy.

### O06 — Dated entry status can mislead future work

**Owner:** current documentation.

`docs/current/README.md` says its status was last checked on **2026-08-31**, lists 438 catalog records and mentions old full-suite acceptance failures. Those are explicitly dated claims; this audit did not run current catalog validation or the full suite and therefore does not replace them with guessed numbers. The same entry point now routes to the completed item dossier, so the historical status block is easy to confuse with the present state.

**Later pass:** either refresh the status from a defined current verification run or remove volatile counters from the entry guide and keep results in a dated verification record. Old Word plans outside `docs/current/` were not assessed and should not be deleted based on this finding.

**Acceptance:** the entry guide clearly distinguishes current invariants from dated verification results and still routes readers to one relevant topic.

## Redundancy and maintainability findings

### R01 — Retained duplicate mechanics require governed retirement

**Owner:** catalog maintenance and compatibility persistence.

There are **15 paired JavaScript paths** under `catalog/mechanics/dnd2024/mechanic/` and `catalog/mechanics/mechanic/dnd2024/`. Their suffixes are:

`armor-class/write`, `character-level/record`, `check/ability`, `dice`, `encounter-initiative-order`, `hit-points/write`, `initiative/roll`, `saving-throw-proficiencies/record`, `saving-throw`, `skill-proficiencies/record`, `weapon-attack`, `weapon-damage/apply`, `weapon-damage/roll`, `weapon-proficiencies/write`, and `weapon-profile/write`.

The two `dice.js` files are identical after line-ending normalization. The block comparison also found shared code in armor-class, initiative ordering, hit-points, skills and weapon-attack files. The application catalog is a further, current authored owner; similar code does not make historical qualified identities interchangeable.

`catalog/compatibility-retention.json` enumerates **86 retained records** and explicitly classifies them as migration-only. `src/system/catalog/persistence/CatalogCompatibilityRetention.cs:54` fails validation for unexpected additions or missing retained identities. `DantesRoleplay/DantesRoleplay.csproj` excludes the legacy D&D mechanic trees and the lock-picking rehearsal from generic bootstrap resources.

**Disposition:** retained redundancy, not deletion-ready. Follow the manifest's retirement condition: reviewed live export, reference/history/source dependency checks, replacement contract tests, and database-plus-blob backup/readback. Do not merge IDs or change archived behavior just to satisfy a duplicate-code score.

### R02 — Test-only projection code sits beside production loaders

**Owner:** web tests and frontend source organization.

The import graph reaches 97 non-generated project modules from the production entry point; generated validators, styles and external packages are outside that count. It does not reach `src/system/web-interface/dnd2024/src/server/hub-source.js` (1,958 lines), `hub-envelope.js` (651 lines), or `audience-policy.js`. These have concrete consumers in mounted tests, visual fixtures, and audience/envelope tests; they are not unused files. The actual connected path uses `game-server-context.js` and `connected-hub-envelope.ts`.

**Impact:** placement makes fixture world data and fixture authorization look like a second production implementation. Future tests can accidentally validate fixture behavior while missing the connected loader.

**Later pass:** move the test-only sources to a clearly named fixture/support owner and identify which assertions must also cover the connected path. Preserve existing fixtures until replacements exist. The disabled public asset copy means these files/assets are not evidence of a production bundle leak.

**Acceptance:** all imports updated, audience and mounted behavior preserved, and representative connected-path tests remain independent of fixture projection.

### R03 — Item response boundaries are implemented three times

**Owner:** item read clients.

`src/system/web-interface/dnd2024/src/server/item-view-client.ts:29`, `item-recipes-client.ts:24`, and `item-uses-client.ts` each construct the read-model URL, map HTTP status, verify the same nine envelope keys and fingerprints, bind observer/item/perspective, enforce a 65,536-byte data ceiling, and produce similar ready results. They already share `item-read-response.ts` for a bounded body and `ViewReadClient` for request lifecycle. Recipe/use pagination and item media checks are deliberately distinct.

**Later pass:** extract only the invariant envelope transport/verification into a small typed helper, passing the exact contract and feature validator. Keep pagination advancement, revision pinning, media URL validation and feature-specific data checks with their owners. Avoid a loosely configured framework that makes an authorization check optional.

**Acceptance:** the existing forged-envelope, actor/perspective mismatch, malformed-byte, stale-cursor, denied, cancellation and media tests still fail closed for all three tabs. Generated validator equality is already checked by mounted item tests; no missing generation gate is claimed.

### R04 — Large files concentrate unrelated change risks

**Owner:** respective capability owners; size alone is not proof of waste.

| File | Lines at baseline | Suggested study boundary |
| --- | ---: | --- |
| `src/system/web-interface/dnd2024/src/styles.css` | 6,155 | View-specific styles; audit old selectors before moving them |
| `DantesRoleplay.DataAccess/DantesRoleplayDbContext.cs` | 3,188 | Mapping/configuration grouped by capability; preserve migration model |
| `src/system/web-interface/dnd2024/src/server/game-server-context.js` | 2,638 | Campaign, knowledge, locations, people/factions and combat reads |
| `DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs` | 1,868 | Route families with common security registration preserved |
| `DantesRoleplay.Web/Interactions/SystemWorkspaceElement.cs` | 1,448 | Embedded presentation and interaction ownership |
| `src/system/web-interface/dnd2024/src/server/connected-hub-envelope.ts` | 1,368 | Bounded projection sections |
| `src/system/web-interface/dnd2024/src/state.js` | 1,233 | State validation versus view selection |

**Later pass:** split only along established ownership boundaries when touching these areas for a real fix. Maintain single implementations and shared contracts; do not copy code into new modules while leaving old owners active. The linked `src/system/*/{domain,persistence,hosting,tests}` arrangement is intentional: those files compile into the top-level projects, so two directory hierarchies do not prove duplicate assemblies.

**Acceptance:** no behavior change, unchanged dependency direction, relevant tests/build, and easy navigation from a capability to its code and tests. This is maintainability work, not a measured runtime speedup.

## Things that should not be removed on this evidence

- Generated item/board validators: production code imports them and tests verify exact regeneration. Their similar generated text and declaration files are not independent hand-maintained implementations.
- EF migrations, designer files and model snapshots: historical repetition supports database evolution; no squashing/migration-baseline study was performed.
- Compatibility catalog records, legacy-state adoption, disabled-identity handling and historical page revisions: explicit runtime/recovery contracts exist.
- Protocol walk tests: intentionally opt-in, not obsolete.
- Fixture projection code and maps: test consumers remain.
- `ApplicationConversationStore`: it has a 128-conversation cap, message/byte limits and two-hour idle expiry; the presence of a `ConcurrentDictionary` is not an unbounded-cache finding.
- The eight-request hub queue, 32-page/2-MiB within-load listing cache and bounded schema cache: observed or inspected protections to preserve during optimization.
- Synchronous compatibility wrappers in `ControlSettingsExplorer`: they exist, but live HTTP endpoints use `ListAsync`/`GetAsync`. Their presence alone does not establish a blocking HTTP hot path.

## Suggested later slices

These are proposed work packages, not approved changes. Keep completeness and disclosure checks independent from performance goals.

| Slice | Findings | Reviewable deliverable | Required proof before acceptance |
| --- | --- | --- | --- |
| A — Reproducible baseline | P01–P03, P11 | Bounded benchmark fixture and actual actor/GM workflow measurements | Database/binding identified; request, SQL, bytes, time and allocation metrics; agreed budgets |
| B — Paging correctness | C01 | Advancing bounded cursors and explicit incomplete states | 99/100/101 and malformed/repeated/failing-page cases; complete output |
| C — Knowledge and chronology reads | P02–P03 | Generic filtered/batched reads with preserved authorization and evidence | Output/revision equivalence, actor secrecy, source changes, query-count scaling |
| D — Hub loading | P01, P11–P12 | Minimal bootstrap and demand-loaded world sections | First usable view, bounded requests, cancellations, actor/DM and large-world browser checks |
| E — Source hygiene | O01–O03, O06, R02 | Reviewed removal/relocation list and precise ignore rules | Fresh checkout build; replacement test ownership; no source or fixture loss |
| F — Media/database ownership | O04–O05 | Asset and database disposition list | Live/historical references and source provenance; no mistaken runtime-store replacement |
| G — Repeated validation/catalog work | P04–P07, R03 | Profile-guided caching/coordination and a narrow shared item envelope helper | Drift, concurrency, bounded memory, transaction and forged-envelope tests |
| H — Durable storage | P09–P10 | Migration proposal preserving immutable revision/history contracts | Backup/restore rehearsal, old revision readback and exact hashes; explicit migration approval |
| I — Background scheduling | P08 | Queue-age visibility and bounded execution proposal | Provider delays, cancellation, claiming and audit behavior on a disposable runtime |
| J — Retained compatibility | R01, remaining O02 | Exact retirement packet for selected identities/files | Governed live export/reference evidence, replacement contracts and retention-gate updates |
| K — Ownership refactoring | R04 | Small owner-aligned splits during related fixes | No duplicate owner, no semantic drift, relevant build/tests |

## Measurement record

**Runtime observed:** `http://localhost:6217`, listener process 55260, application `dnd2024`, state space `dnd2024-main`, campaign `campaign.caldris.measure-of-mercy`, GM seat with no bound actor. Audience policy fingerprint: `DDFD5948036935AAB14C09A26610C91B3B603523C621C9E1607FE6239D66D47E`. Binding fingerprint: `F2FA37273C3F96E5AAB20AA9435DA951E07B54169A3C773416D610923009E071`. Measurements were collected during this audit on 2026-09-06, not taken from earlier release receipts.

The probe imported the checked-out production `readGameServerContext`, set `deferCharacterDetails: true`, used the normal eight-read scope, and wrapped fetch to count actual HTTP responses. Each response body was consumed and reconstructed for the loader, adding some measurement overhead. A 15-second per-request timeout was imposed by the probe. Runs were sequential, with two DM loads followed by a Player preview. They were not cold-start samples and did not measure React layout/paint, perceived readiness, browser caching, or a statistically meaningful percentile.

| Sample | Loader elapsed | HTTP requests | Peak in flight | Response-body bytes | Outcome |
| --- | ---: | ---: | ---: | ---: | --- |
| DM 1 | 12,481.71 ms | 2,096 | 8 | 1,749,324 | Connected; 35 factions, 124 people, 259 locations |
| DM 2 | 12,998.02 ms | 2,096 | 8 | 1,749,324 | Same counts |
| Player preview under GM seat | 2,438.61 ms | 1,086 | 8 | 1,152,819 | Connected; 256 visible locations |

Both DM runs returned 1,998 HTTP 200s and 98 HTTP 404s, with no 429, 5xx or timeout recorded. The 404s include optional-component probes; they are not all errors requiring repair. The selected campaign-root component was fetched twice per load. The Player preview suppresses the GM notebook path and is not an actual actor-seat benchmark. Response-body bytes are uncompressed bytes seen by the probe, not wire bytes including headers or images.

At database inspection, the live database file occupied **292,540,416 bytes (279 MiB)**, with 143 tables, 69 web page revisions and 591 web asset rows. It contained 2,638 entities in the main state space. SQLite reads used `readOnly: true` plus `PRAGMA query_only=ON`; only schema, counts, hashes, lengths and page-space statistics were read for storage analysis. No database update, vacuum, restart, publication, activation or catalog synchronization was performed.

## Reproduction and handoff

The durable evidence is the counts, methods, paths and observations in this document. Optional local raw evidence and audit-only helpers are in ignored `bin/i/system-audit/`; they are not required to understand this backlog and are not committed runtime tooling.

Useful independent checks for the next pass:

```powershell
# Tracked outputs; listing paths does not remove them.
git ls-files -- DantesRoleplay.Tests/.codex-build DantesRoleplay.Tests/.codex-obj DantesRoleplay.Tests/bin-slice2 DantesRoleplay.Tools/.codex-build DantesRoleplay.Tools/.codex-obj

# Evaluate normal compile inputs without building or changing the live server.
dotnet msbuild DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj -nologo -getItem:Compile
dotnet msbuild DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -nologo -getItem:Compile

# Exact browser retirement candidates.
rg -n 'HubUnavailable|ServerCampaignConnected' src/system/web-interface/dnd2024/src src/system/web-interface/dnd2024/test
```

Read-only storage aggregation, after verifying the correct database, can reproduce the payload estimate:

```sql
SELECT COUNT(*) AS rows, SUM(length(Content)) AS bytes,
       COUNT(DISTINCT ContentHash) AS unique_hashes
FROM web_page_asset;

SELECT SUM(bytes) AS distinct_payload_bytes
FROM (
    SELECT ContentHash, MAX(length(Content)) AS bytes
    FROM web_page_asset GROUP BY ContentHash
);

SELECT name, SUM(pgsize) AS bytes
FROM dbstat GROUP BY name ORDER BY bytes DESC LIMIT 12;
```

For each later slice, append a short disposition to the relevant finding: **confirmed scope, implementation commit, validation result, remaining limitation, and retirement status**. Keep original measurements dated so later improvements can be compared without rewriting the baseline. A zero-reference text search alone is not final removal approval.
