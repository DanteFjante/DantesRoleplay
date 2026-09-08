# Test suite improvement slices

This is the implementation plan requested on 2026-09-08, revised after repository inspection,
three complete .NET runs, browser runs, focused setup traces and isolated experiments. It is
written for an implementation agent working one numbered slice at a time. The evidence section
distinguishes measured costs, demonstrated coverage gaps, cleanup candidates and remaining work.

The outcome is substantially faster execution, independently runnable test domains, reliable
selection of affected tests, and physical removal of coverage that no longer protects a current
or retained compatibility requirement. Reducing the reported test count is not a success metric.

## How to execute this plan

Read `AGENTS.md`, the current entry page, and this document for a slice implementation. Consult
the Validation section of `DEVELOPMENT.md` when applying the existing acceptance requirements;
otherwise read only the implementation, contracts, and tests named by the selected slice.
Do not preload the system-audit plan, its evidence, other cleanup plans, world workspaces,
exports, or `_to_delete/`.

Implement the slices in order. Each slice ends with a coherent, reviewable change and the
verification listed below. Use focused tests during iteration. Until slice 11 changes the
working agreement, the existing full-suite requirement for completed feature acceptance remains
in force. This plan does not inherit the unrelated system-audit unattended execution exception.

For each slice, report its delivered boundary, changed tests/harnesses, relevant commands and
results, measured comparison where applicable, and unresolved work in the task response. Put
generated inventories, timings, and traces in ignored local output or CI artifacts. Do not add
per-slice receipts, status diaries, or a second planning document. Keep unrelated working-tree
changes intact. Test cleanup does not authorize changes to live databases, authored game
semantics, production migrations, public protocols, or compatibility retention policy.

Commit each completed slice separately. The commit subject must name its slice number and outcome;
the commit body must include a short `Changelog:` list plus the verification performed and any
deliberate exclusions or unresolved measurement limitations. Stage only that slice's files and
preserve unrelated work in the shared checkout.

## Evidence collected on 2026-09-08

The audit used a separate source snapshot because other tasks were editing and testing this
checkout. It captured 5,729 input files and their SHA-256 hashes, based on HEAD
`4a975dd892faa0b06f27a0d426f44cfc6f665436` plus the working-tree inputs recorded in the manifest.
The manifest hash is `C331F2C1F86B20C3D24673E1B4772044CE311F9AEFB8ED0FDBC8B7FB896A158F`.
These are dated snapshot results, not an assertion that today's changing checkout is identical.

Environment: Windows 11, Ryzen 9 5900X, 12 cores/24 logical processors, approximately 32 GiB RAM,
.NET SDK 10.0.400/runtime 10.0.11, Debug x64, existing xUnit settings, no coverage collection.
Browser tests used Node 24.19.0 with existing installed dependencies through a junction and the
captured lockfile. Default PATH resolves Node 20.3.0, below the package's declared >=22.13.0;
the future runner must report this clearly. The dependency directory itself was not snapshotted.

Local raw evidence is under `.tmp/test-suite/evidence-20260908/`: `summary.json`, full TRX files,
per-case/per-file inventories, phase traces, experiment scripts, source manifest and process
observations. The summary identifies the original temporary source snapshot used by the scripts.
These generated files are ignored local artifacts; attach them to a review if the implementation
agent runs elsewhere. The decision-relevant evidence is retained here.

| Measurement | Result | Interpretation |
| --- | --- | --- |
| Full .NET run 1 | 2,129 passed; 518.97 s | No competing test run observed; execution command includes runner startup, excludes build |
| Full .NET run 2 | 2,129 passed; 734.11 s | Another task's full tests/build overlapped; contention diagnostic |
| Full .NET run 3 | 2,129 passed; 707.05 s | Another task's full tests overlapped; contention diagnostic |
| Fresh snapshot solution build | 16.95 s, success | Existing package caches; not a cold-machine restore measurement |
| Incremental solution build | 10.44 s, success | One observation, with other work active; not an edit/build benchmark distribution |
| D&D Node tests, three fresh processes | 341 passed each; median 1.56 s, range 1.54–21.74 s | First invocation was much slower; do not hide it or attribute the cause without evidence |
| Mounted browser tests, three fresh processes | 102 passed each; median 13.18 s, range 12.46–14.71 s | Existing loaders and actual mounted tests |
| Generic browser tests, three fresh processes | 5 passed each; median 0.27 s, range 0.23–0.46 s | Separate generic transport/state coverage |

All ordinary runs had zero failed/skipped cases. Source inventory found 187 test files and 1,614
Fact/Theory declarations, including conditional protocol source. List-tests printed 2,117 entries;
actual execution expanded to 2,129 cases, so declaration/list line counts must not substitute for
the executed inventory. An opt-in protocol build/list discovered 10 additional facts; source
declares eight active and two already skipped. Their bodies were not executed. Normal inclusion
was rebuilt afterward. These are separate from the ordinary totals.

There is **not yet a three-run uncontended full-suite baseline**. Do not calculate a clean median
from the three .NET runs above. Source isolation protected inputs; it did not isolate machine
resources. Obtain matched quiet-machine repetitions before final full-workload speed claims.

The provisional ownership inventory accounts for all 2,129 executed .NET cases:

| Domain | Cases | Sum of overlapping case durations in run 1 |
| --- | ---: | ---: |
| `game-world` | 117 | 2,569 s |
| `dnd2024` | 581 | 1,741 s |
| `catalog` | 197 | 863 s |
| `state` | 306 | 252 s |
| `automation` | 236 | 224 s |
| `kernel` | 193 | 62 s |
| `web` | 209 | 60 s |
| `interaction` | 162 | 42 s |
| `host` | 73 | 23 s |
| `execution` | 55 | 23 s |

The duration column includes parallel overlap and shared-preparation waits; it is neither wall
time nor CPU time. Ownership here was assigned at class level to locate costs. Mixed classes
such as architectural/host guards and item-input tests still need per-invariant classification
and consumer mapping in slices 2–4.

The longest class spans were `Dnd2024InventoryAndProgressionTests` (149 cases, 486 s),
`Dnd2024CoreMechanicsTests` (90, 408 s), `Dnd2024CharacterCreationAndRestTests` (116, 380 s),
and `CatalogWorldFeature6Tests` (5, 315 s). The 486-second D&D class occupies most of the
519-second run. This is stronger prioritization evidence than deleting the single slowest test
and assuming its entire duration comes off the parallel suite.

Focused diagnostic traces established these preparation costs:

- Ten D&D harnesses caused 20 materializations. Materialization totaled 24.72 s, including
  20.45 s reading catalog bytes and 1.19 s registering objects. The eager calls alone took
  12.82 s. Ten database clones totaled 51.8 ms. One template build took 29.58 s. These nested
  spans overlap and come from an instrumented run under contention; do not add them together.
- `CatalogWorldFeature5Tests` passed three cases in a 43.74-second process. It created three
  databases, including one for a pure local-contract check, and performed two full imports
  totaling 21.19 s. Copies, reads, assertions and cleanup account for additional unseparated time.
- The 20,000-record `Repeated_equivalent_maximum_catalog_preparation_profile` took 242.99 s in
  full run 1. Its focused traced process took 134.84 s: 113.98 s creating files, 125.54 s for the
  whole test body, and 7.25 s deleting files. The materialization calls totaled about 11.50 s.
  The stopwatch printed inside its three-call loop omits most of the actual test cost.
- The natural-one/twenty search made 72 mechanic evaluations. Trace output identified useful
  fixed seeds; preserve roll-value checks when replacing that search in slice 7.

Three fresh-process trials also demonstrated that selective execution is practical before any
test deletion. Draft class groups ran all 73 host cases in a median 18.02 s (17.33–19.42), all 209
.NET web cases in 16.47 s (16.02–17.28), and all 162 interaction cases in 7.21 s (6.91–7.54).
Each trial passed. These exclude build, browser groups and unproven consumer additions. They
support a domain runner; they do not certify an affected selector or a 60-second D&D domain.

Controlled source experiments produced stronger evidence for specific changes:

| Experiment | Observed result | Plan consequence |
| --- | --- | --- |
| Remove only the eager `BuildFeatureSnapshot` in `DndHarness.CreateAsync`; keep assertions unchanged | Ten cases passed in all three runs before and after. Median fell from 32.83 s (32.47–33.42) to 27.26 s (25.67–28.46), a 16.95% reduction. A broader 90-case core-mechanics run also passed in 99.64 s. | Start slice 6 with this narrowly demonstrated waste. The percentage applies to this sample, not the full suite. Full-domain acceptance remains required. |
| Put the same forbidden `attack` identifier in current and project-local kernel source paths | `GuardTests.The_kernel_contains_no_game_vocabulary` passed with the probe in `src/system/building-blocks/domain/`, but failed with it in `DantesRoleplay/`. | Repair moved-source enumeration in slice 1. A fast passing guard currently misses a real ownership path. |
| Deliberately retain compiled schema graphs in a static collection | Both the original repeated-compilation test and existing weak-reference eviction test passed. Changing the retention test to 2,000 distinct schema titles made it fail with 13,638,816 retained bytes. The distinct-schema test passed once the injected leak was removed. | Repair compilation/eviction coverage in slice 1 before increasing shared schema reuse. This demonstrates a test gap, not a leak in the current production validator. |
| Omit the mechanic description from `ContentHash.ForMechanic` | Both named mechanic fingerprint matrices failed at their expected fingerprint assertions. | This supports the overlap identified by source review. Preserve the unique distinct-variant assertion before consolidating in slice 8. One fault alone is not proof of complete equivalence. |
| Replace the natural-one/twenty search with seed 7 and seed 36 and explicit roll assertions | The test passed using two evaluations, preserving the modifier-versus-natural-roll success/failure checks. | Use these verified seeds in slice 7, rechecking against current mechanic inputs. |

All experimental source edits were restored byte-for-byte; all 5,729 captured input hashes
matched afterward. Temporary probes/helpers were removed and the normal test build restored.
One positive-control build failed when capability-layout validation received an unexpanded
`src/system/**/*.cs` glob. The restoration build and a recorded unchanged retry passed. Its
cause is unresolved and its log is retained; it does not establish a failure in the main checkout.

Every ordinary case was timed, and costly/suspicious candidate groups were inspected against
their implementation. This was not an assertion-by-assertion adjudication of all 2,129 cases.
Slice 8 contains concrete starting candidates and the required complete domain review. No
production optimization or test deletion was applied by this audit. Live databases, external
providers, served-browser acceptance and deployment were outside its scope.

## Success criteria and measurement

Use these as initial engineering targets, to be evaluated against slice 0:

| Measure | Target |
| --- | --- |
| Typical affected development run, after an incremental build | Median at most 60 seconds for representative single-domain edits |
| Complete retained test workload | At least 50% lower elapsed time than the original workload, on the same machine and configuration |
| Selection correctness | Every test classified; every executable source change covered by an explicit route or a conservative fallback |
| Test deletion | Every removed test has an explicit obsolete or redundant disposition with evidence |
| Reliability | No unexplained failures, skipped coverage, shared-state leaks, or hidden retries introduced |

The speed targets are goals, not measured claims. Slice 0 must establish whether an absolute
target is realistic and identify the expensive domains. Report an unmet target honestly; do not
silently relax it or call the performance objective complete. Any revised target needs an
explicit evidence-based explanation in the task.

Measure both end-to-end time, including required build/preparation, and execution-only time.
Separate fresh-process startup from reuse within a process; a second `dotnet test` invocation
does not retain process-local templates. Record configuration, SDK/Node versions, CPU count,
concurrency settings, build state, and whether instrumentation was enabled. Compare at least
three normal, uninstrumented runs per final measurement and report median and range. Diagnostic
runs locate costs but do not supply the final performance numbers.

Maintain three distinct comparisons: affected selection, all correctness tests, and the complete
retained workload including performance checks. Keep optional protocol/browser acceptance
separate and compare it on equal terms when it is required. Moving a benchmark into another
command does not reduce complete-workload time. Report actual execution savings, approved
deletions, and avoided unrelated tests separately. Do not add overlapping per-test durations
and present the sum as parallel elapsed time.

## Domain and run model

Paths in this document are relative to the repository root. A bare test/helper filename refers
to `DantesRoleplay.Tests/` unless an explicit capability directory is given. Proposed runner/map
paths do not exist yet; their creation belongs to the specified implementation slice.

Start with logical domains inside the existing .NET test assembly. They enable selective execution
without immediately changing project references or source ownership. They do not avoid compiling
the shared test project; slice 10 addresses build cost only if measurement justifies it.

Use exactly one primary `Domain` and one `Kind` per .NET test. In xUnit, use ordinary traits,
for example `[Trait("Domain", "dnd2024")]` and `[Trait("Kind", "integration")]`. Avoid conflicting
class/method trait values; split a mixed class or annotate its methods consistently. Optional
`Boundary` traits identify existing consumer seams for selection, not new production identities.
The existing VSTest runner supports trait filters. See the
[VSTest filter reference](https://github.com/microsoft/vstest/blob/main/docs/filter.md).

Kinds are `unit`, `integration`, `migration`, `performance`, and `protocol`. Compatibility is
an existing behavior classification within an owning domain, not an excuse to omit tests.
Node and mounted UI tests use explicit file membership in the same domain map; they do not
need a replacement JavaScript test framework.

| Domain | Primary responsibility and starting owners |
| --- | --- |
| `kernel` | `building-blocks`, `schema-validation`, generic sandbox/mechanics and their resource limits |
| `state` | `state`, `ecs`, `ecs-effects`, `snapshots`, `sqlite-hosting`, component/state-space administration, edges, effects/transactions, projections, events, operations/audit, blob storage and entity media |
| `catalog` | `catalog`, `catalog-tools`, `catalog-navigation`, procedures, source/application registries, preview/activation, registry administration, legacy-state adoption and generic import/export compatibility |
| `execution` | Application execution, application preview/execution boundaries, play recording and generic snapshot/reducer integration; application-preview implementation itself remains catalog-owned |
| `interaction` | Interaction orchestration, deterministic retrieval, knowledge and their audience/authorization boundaries |
| `automation` | Trigger scheduling, local AI, Codex bridge, assistant/system conversations, system tasks/capabilities, feedback and host settings |
| `host` | MCP protocol, private authorization, information/orientation, runtime paths, bootstrap/registration and host adapter tests |
| `web` | Generic web interface tests, browser transport/state/UI tests, and mounted application website tests |
| `game-world` | Catalog-owned generic world behavior and Trail Survival tests, including retained world-fixture execution |
| `dnd2024` | D&D JavaScript mechanics, application objects/read views, item projections, rules contracts and D&D integration tests |

This table assigns primary ownership, not all consumers. Root test files must be assigned by
what they assert: a D&D item projection belongs to `dnd2024`, while its HTTP adapter belongs to
`web` with a D&D boundary dependency. `system-audit` tests are classified by the owner measured,
usually `state`, with `Kind=performance`. Any owner not yet having tests still needs a source
route. Slice 2 resolves every file explicitly; there must be no silent miscellaneous bucket.

Expose these run modes through a small repository test entry point:

| Mode | Meaning |
| --- | --- |
| `domain <name>` | All correctness tests in that domain, including its migrations, plus explicitly registered consumer boundary tests |
| `affected` | Union of domains/boundaries selected from changed files, with reasons and a conservative fallback |
| `correctness` | All domains' unit, integration and migration tests, plus both ordinary browser test groups |
| `performance` | All performance/memory checks in controlled isolated runs |
| `protocol` | Existing opt-in complete-host walk, built with its existing inclusion property |
| `all` | Correctness plus performance; protocol and served-browser acceptance remain explicit additions when applicable |
| `list` / `explain` | Show selected tests/files, reasons, additional validations and fallback without executing test bodies |

No tests are silently removed from the unfiltered existing command. The new entry point makes
scope explicit. Full correctness includes retained compatibility and migrations. External-provider
and live-browser work must retain their existing isolation/explicit runtime requirements.

## Slice 0 — Establish the baseline and candidate inventory

**Depends on:** nothing. **Changes:** measurement support only; no deletion or test restructuring.

**Start with:** `DantesRoleplay.Tests/DantesRoleplay.Tests.csproj`, `Directory.Build.props`,
`Directory.Build.targets`, `SqliteFixture.cs`, `CatalogTestTemplate.cs`, `Dnd2024TestBase.cs`,
`src/system/web-interface/dnd2024/package.json`, and the slow owners discovered by timing.

Use the dated evidence above as the starting point rather than repeating the initial broad scan.
The outstanding baseline work is three matched full runs without competing tests and refreshing
the snapshot inventory when current inputs differ. Keep the completed diagnostic and fault
results unless the underlying implementation changed; do not claim their timings apply to a new
checkout automatically. The final semantic disposition of every domain belongs to slice 8.

1. Capture the checkout state. Discover the runnable .NET tests, including theory cases, and
   the Node/mounted test files. Record the optional protocol inventory separately. Source-level
   `[Fact]` counts are not executable case counts.
2. Build once, then capture a full .NET TRX result, the generic browser test and both D&D browser
   groups. Record failed/skipped cases without deleting or disabling them to establish a green
   baseline.
3. Collect the three-run reference described above. Time build, discovery/startup, setup,
   execution, and teardown separately where the runner permits. Avoid competing full runs.
4. Rank slow cases, classes, domains, serialized collections and shared preparation paths.
   Instrument only the dominant harness paths if runner timing cannot separate setup from work.
   Start with database construction, catalog copying/import, activation/materialization, schema
   compilation, provider/host construction and disposal. Count work as well as elapsed time.
5. Build an ignored working inventory with test identity, owner, kind, invariant, setup path,
   duration, and candidate disposition: keep, optimize, move, consolidate, obsolete, or redundant.
   The last two remain candidates until slice 8 validates them.

Initial commands, run from the repository root:

```powershell
dotnet build DantesRoleplay.slnx
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --list-tests
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-build --logger "trx;LogFileName=baseline.trx" --results-directory .tmp/test-suite/baseline
npm --prefix src/system/web-interface/dnd2024 run test:node
npm --prefix src/system/web-interface/dnd2024 run test:mounted
node --test src/system/web-interface/tests/browser/system-client.test.mjs
```

Use distinct output directories for repeated runs under the ignored `.tmp/test-suite/` path.
Avoid coverage collection in normal timing runs. A missing dependency or current failure is a reported
baseline limitation, not evidence that the affected tests are obsolete.
If repository inputs change during a comparison, discard the mismatched measurements and repeat
against a stable checkout. Do not stop or modify another person's running work to obtain a baseline.

**Done when:** exact discovered coverage and failures are accounted for; runtime is ranked;
the largest setup costs and representative single-domain changes are identified. No speedup
is claimed from this slice.

## Slice 1 — Repair coverage guards before expanding reuse or deleting tests

**Depends on:** 0. **Changes:** architecture and schema-retention tests; no production semantics.

**Start with:** `GuardTests.cs`, `Directory.Build.targets`, the core/DataAccess/LocalAI project
compile includes, `src/system/schema-validation/tests/SchemaRetentionTests.cs`,
`SchemaCacheTests.cs` in that same directory, and the validator's current cache implementation.

1. Make architectural source enumeration cover the actual compiled capability owners as well
   as project-local source. Audit the related LocalAI and MCP source guards for the same moved-
   source assumption. Reuse the build's ownership contract; do not scan tests, generated output,
   application JavaScript or historical directories as generic kernel source. Missing expected
   ownership must fail rather than silently producing an empty scan.
2. Add a focused scanner check using disposable source fixtures in both current and project-local
   layouts. A forbidden identifier in a current generic owner must fail. Preserve comment/string
   handling and the existing forbidden vocabulary; this is not permission to weaken the rule.
3. Separate repeated validation/cache-hit behavior from repeated distinct schema compilation and
   eviction. Exercise more distinct schemas than the bounded cache can retain. Keep valid/invalid
   value assertions, local schema registries and nonparallel heap measurement.
4. Ensure the retention check observes compiled graph lifetime. The existing eviction test holds
   a weak reference to `SchemaCompilationResult`; collection of that wrapper alone does not prove
   that its underlying schema graph is collectible. Preserve LRU and size-bound checks too.
5. Verify both repairs using temporary faults in an isolated source copy: current-owner forbidden
   vocabulary and deliberate static retention of compiled schema graphs. Each repaired test must
   fail for its intended reason and pass after the fault is removed. Do not commit fault hooks.

**Verification:** focused architectural guards, schema correctness/cache tests, isolated memory
checks, positive-control failures and the existing acceptance requirements.

**Done when:** the current source layout and actual compilation/eviction paths are protected.
These tests are repaired because their invariants remain important; they are not deleted as old.

## Slice 2 — Classify every test and validate ownership

**Depends on:** 1. **Changes:** traits, one domain map, and selection integrity checks.

**Start with:** all test classes in `DantesRoleplay.Tests/` and `src/system/*/tests/`, plus
the generic browser tests and `dnd2024/test/` tree. Preserve capability-local source placement.

1. Add `Domain`/`Kind` metadata using the model above. Assign mixed root integration files by
   invariant and add consumer boundaries where needed. Classify expensive timing/memory tests
   by behavior, not merely by a slow baseline result.
2. Put domain names, source roots, consumer routes, shared harness dependencies and browser file
   membership in one machine-readable map, proposed at `scripts/testing/domains.json`. Use
   explicit exceptions for mixed folders; do not infer ownership from test-name substrings alone.
3. Add a small integrity check that finds missing/unknown/ambiguous metadata and mismatches
   between the domain map and compiled tests. Validate browser test file membership too.
   Runner discovery is the authority for case identity; inspecting attributes may validate
   classification, but cannot replace executing/discovering dynamic theory data.
4. Verify each domain filter selects cases and the union equals the original correctness
   inventory. Track method/case renames explicitly. Keep protocol's conditional compilation in
   the separate inventory. No test should disappear because it has been moved or classified.

**Verification:** build, ownership integrity checks, domain discovery/union comparison, and the
existing correctness run. Metadata-only changes must preserve outcomes and case membership.

**Done when:** every existing test has exactly one primary owner and kind; unknown source and
test paths are detectable; no semantics or assertions have changed.

## Slice 3 — Deliver domain commands and explicit expensive runs

**Depends on:** 2. **Changes:** test entry point, not a new test platform.

**Start with:** the domain map, existing test project, browser `package.json`, and
`DantesRoleplay.Tests/ProtocolWalkTests.cs`. A root `test.cmd` invoking a small
`scripts/testing/run.mjs` is the preferred implementation: Node is already required by this
repository and this avoids requiring users to change PowerShell execution policy.

1. Implement the modes above, configuration selection, an explicit build/no-build option,
   and stable nonzero exit codes for failures. Help and examples must match implemented flags.
2. Build required .NET inputs once per invocation, then execute selected tests with `--no-build`.
   Run the union of selected .NET traits in one process when possible so shared templates are
   reused. Do not launch one `dotnet test` process per domain by default.
3. Forward argument arrays to child processes safely; do not concatenate untrusted paths or
   refs into shell command strings. Resolve commands from the repository root and retain output.
4. Run Node and mounted files with their existing loaders and test APIs. Preserve generic
   `src/system/web-interface/tests/browser/` coverage as well as D&D browser coverage. Never
   import and execute all files merely to discover a selection.
5. Make zero tests in a requested nonempty domain a failure, even if the underlying runner
   returns success. Distinguish an explained documentation-only selection from a broken filter.
6. Isolate performance/memory checks from other workloads. Preserve their actual assertions,
   sample sizes and nonparallel behavior. `all` must still execute them and report total time.
   Split `ActivatedApplicationCatalogTests.Repeated_equivalent_maximum_catalog_preparation_profile`
   into small correctness fixtures for reuse, activation changes, authority separation,
   unpublished access, registration drift and byte drift, plus the retained 20,000-record
   stress case. The existing test mixes both kinds. Global allocation counters in this test
   and maximum-page projection profiles are process-wide; run their measurements without
   competing tests. Do not mistake the few seconds printed inside the materializer loop for
   the end-to-end cost of creating and deleting its files.
7. Build protocol mode with `-p:IncludeProtocolWalkTests=true` and the existing protocol filter.
   Prevent reuse of a binary built under the wrong inclusion/configuration settings; rebuild
   with the normal property state before a later ordinary run, or use verified isolated output.

Proposed interface, to become executable in this slice:

```text
test.cmd domain dnd2024
test.cmd domain state
test.cmd correctness
test.cmd performance
test.cmd protocol
test.cmd all
test.cmd list --domain web
```

**Verification:** exercise argument handling, failing/zero-match filters, build failures, browser
failures and exit propagation. Compare actual result identities for `correctness` plus
`performance` with the preclassification inventory. Run protocol once to verify the new launcher
and property handling. Do not alter production protocol registration.

**Done when:** each domain is independently runnable, the complete retained workload is reachable,
and exclusions are visible. Keep `dotnet test` usable for direct focused work.

## Slice 4 — Select affected domains and consumers from changes

**Depends on:** 3. **Changes:** an explainable conservative selector.

**Start with:** `scripts/testing/domains.json`, the runner, actual production references, catalog
contracts and representative edits from slice 0. Do not create a separate dependency diary.

1. Implement `affected --base <ref>` for an explicit comparison base and `affected --working-tree`
   for tracked staged/unstaged changes plus untracked relevant files. Define base mode as changes
   from the merge base through HEAD plus working-tree changes. Handle additions, deletions,
   renames, spaces and missing refs; inspect both old and new paths on renames. Do not guess a
   branch name. Missing/unresolvable comparison evidence must fail or choose full correctness.
2. Map changed production/catalog files to their owners and transitive registered consumers.
   Include changed test files themselves. Shared fixture changes select every consumer of that
   fixture. A deleted path still has an owner through its previous path or a broad fallback.
3. Seed conservative routes from the following table. Narrow a route only after identifying
   the actual boundary tests that protect its omitted consumers.
4. Emit a compact explanation before execution: changed paths, owner selections, consumer
   selections, fallback reasons and additional required checks. Deduplicate tests in unions.
5. Unknown executable/catalog/test paths select full correctness. Shared build/package/test
   configuration and selector changes select full correctness. Pure prose edits may skip tests;
   catalog procedure Markdown is executable input and must never be treated as ordinary docs.
6. Add meaningful selector tests for the routes, unknown paths, multiple owners, renames,
   deletions, untracked files, shared fixtures, absent base and zero-match errors. Verify results
   against discovery, not just against the selector's own generated strings.

| Changed area | Initial selection and extra checks |
| --- | --- |
| D&D mechanic/contract/object/component | `dnd2024`, catalog validation, affected execution and web contract boundaries; select full consumer domains until their boundary sets are explicit |
| Generic game-world catalog content | `game-world`, dependent D&D and execution boundaries, catalog validation |
| Sandbox or generic schema validator | Full correctness; changed resource/memory behavior also runs relevant performance checks |
| ECS, transactions, generic projections, database model or migrations | Full correctness; migration tests always included; relevant performance checks for measured storage/query paths |
| Catalog import/activation/source resolution | `catalog`, `execution`, and dependent interaction/game/web boundaries; run catalog validation for authored catalog changes |
| Interaction/retrieval/knowledge | `interaction` plus host, automation and web adapter boundaries |
| Scheduler/provider/task implementation | `automation` plus applicable host/web capability boundaries |
| Browser-only component or presentation code | Affected web file groups and mounted tests; package typecheck/build for TS/TSX or bundling changes; widen for shared transport/schema changes |
| HTTP/MCP adapter, DI registration or private authorization | Owning host/web domain plus consumer boundaries; MCP surface or DI changes require protocol walk; shared authorization changes select full correctness |
| Shared .NET test fixture or browser test support | Every declared consumer; fall back to full correctness if the consumer set cannot be proved |

For catalog changes, build the current tools before `.\roleplay.cmd validate catalog`: the
existing wrapper only builds a missing executable and can otherwise run an older build. The
runner must execute required additional checks, or exit with an explicit incomplete-verification
result; printing a reminder is not equivalent to running a required check.

**Verification:** exercise representative file changes without modifying live state; compare
selected membership with the expected domain/boundary inventory; run selected and full correctness
on the same checkout at acceptance. A passing selected run alone cannot prove routing completeness.

**Done when:** ordinary edits can run their affected domains with an inspectable reason for every
selection, and uncertainty widens coverage rather than silently dropping it.

## Slice 5 — Reuse database and catalog preparation consistently

**Depends on:** 4. **Changes:** generic/test-only fixture preparation and its consumers.

**Start with:** `SqliteFixture.cs`, `CatalogTestTemplate.cs`, `CatalogTestTemplateTests.cs`,
`CatalogWorldFeature4Tests.cs`, `CatalogWorldFeature5Tests.cs`, the other measured world-feature
tests, and persistence tests that call `EnsureCreated` without testing schema creation.

1. Remove eager database construction from pure tests. Create fixtures only where used, or
   move pure cases into a class without database-owning fields.
   Start with the pure methods in `ContentHashTests`, `BootstrapContractTests`,
   `CatalogNamespaceTests`, `CatalogCoverageTests`, and world-feature contract checks. A
   test-local validator is a separate candidate for replacement by canonical-schema coverage;
   removing its database fixture alone does not establish the assertion's value.
2. Extend the existing cloning approach rather than adding another competing fixture system.
   If repeated empty-schema creation is expensive, build an immutable empty-schema template
   once and clone it. Preserve explicit fresh-schema and migration paths.
3. Replace repeated full catalog copies/imports in ordinary behavior tests with private clones
   of `CatalogTestTemplate`. Start with world features 4, 5, 6, 8 and 10, whose setup repeatedly
   imports identical repository inputs. Compare importer options and installed store types
   before sharing a template. Preserve fresh copies/imports when the operation, input change,
   import result, rejection, export, corruption or synchronization boundary is the invariant.
   A method named `Fresh_import_*` is not automatically exempt: several post-import content
   assertions can inspect private clones of one real fresh import with identical inputs and
   options. Keep direct fresh-import assertions on the importer and its diagnostics.
4. Avoid keeping unused filesystem copies alive after preparation. Keep fixture-only relationship
   restoration in disposable test inputs, never in authored/live world content.
5. Synchronize access to the template's SQLite connection during copying. Every test receives
   its own writable connection, context and mutable services. Do not share EF contexts, writable
   databases or transaction state. Preserve schemas/triggers on clones.
6. Extend existing isolation verification to simultaneous clone requests, mutation isolation and
   disposal. Keep templates process-local and immutable; do not introduce a persistent on-disk
   cache with uncertain invalidation.

**Verification:** template isolation tests, affected catalog/world/storage domains, fresh import
and migration tests, and matched timing of the expensive converted classes. Measure template
build count, clone count, import count and retained memory where useful.

**Done when:** repeated preparation is removed from converted consumers, fresh-path coverage
remains intact, and measured savings are reported without shared-state dependence.

## Slice 6 — Finish D&D harness preparation reuse

**Depends on:** 5. **Changes:** D&D test harness; production behavior remains unchanged.

**Start with:** `Dnd2024TestBase.cs` around `TemplateAsync`, `CreateAsync` and `BuildTemplateAsync`,
`SnapshotObjectTestHarness.cs`, and the existing activated catalog materializer/provider and
schema cache contracts in `src/system/catalog-navigation/` and `src/system/schema-validation/`.

1. Measure the remaining per-test work after database cloning: `BuildFeatureSnapshot`, object
   registration, catalog parsing and schema compilation. The materializer currently reads catalog
   bytes and registers objects before consulting its optional preparation cache; simply wiring
   that cache is not proof those costs have disappeared.
   Begin with the demonstrated duplicate: `CreateAsync` eagerly calls `BuildFeatureSnapshot`,
   then the newly constructed provider materializes again on its first lookup. Establish one
   materialization per ordinary harness before attempting broader cross-test snapshot reuse.
   Apply the audited removal experiment only after checking current callers and running the
   whole D&D domain; the audit's focused comparison is not full-domain acceptance.
2. Prepare registered object state and immutable catalog inputs once per exact source/extension
   set where they are invariant. Reuse existing abstractions for prepared catalog access. Never
   retain services bound to the template's EF context inside a cloned test harness.
3. Preserve distinct templates for different source/extension sets and transaction-failure modes
   where needed. A failure-injection option may share immutable inputs but must retain its own
   participant behavior. Synchronize clone access to the shared template connection.
4. Reuse bounded successful schema compilations only under the existing exact-text/profile and
   isolation rules. A shared validator is allowed only if its thread-safety and cache behavior
   support it; tests measuring cache freshness/retention must receive their own instance.
5. Keep a clearly named fresh preparation path for tests that change sources, activations,
   registrations, schema versions or fingerprints. Changed source bytes must be visible to those
   tests; do not substitute a prepared snapshot in a freshness assertion.
6. Keep representative integration tests on the normal production materialization path. Compare
   prepared and fresh harness behavior for reads, writes, provenance, stale input, extensions,
   rollback and idempotent replay. Do not introduce test-only shortcuts into production DI.

**Verification:** D&D domain, object/projection integration and existing catalog freshness/cache
tests; isolation and prepared/fresh parity checks; matched harness timing and memory comparison.
Run catalog validation if any authored catalog input changes, which is not expected here.

**Done when:** converted ordinary tests avoid repeat preparation, freshness tests still detect
drift, and no production checks were weakened to make test setup faster.

## Slice 7 — Exercise rule matrices through smaller real-mechanic fixtures

**Depends on:** 6. **Changes:** D&D/world rule tests and a minimal shared test harness if needed.

**Start with:** `Dnd2024CoreMechanicsTests.cs`, `Dnd2024InventoryAndProgressionTests.cs`,
`Dnd2024CharacterCreationAndRestTests.cs`, `Dnd2024ExplorationAndHazardTests.cs`,
`Dnd2024MovedMechanicTests.cs`, and `src/system/mechanics/tests/SandboxTests.cs`.

1. Identify cases whose invariant is solely JavaScript input validation, calculation, output
   shape or proposed effects. Reuse the repository's existing direct `JintMechanicEngine` pattern
   to execute the real authored source with the smallest complete projection and fixed seed.
2. Keep game rules in catalog JavaScript. C# fixtures supply inputs and assert known outputs;
   they must not implement a second version of the rules to compute expected values.
3. Convert one rule family at a time. Move broad valid/invalid input matrices to this smaller
   fixture. Preserve integration coverage for role/component resolution, registered-object
   provenance, authorization/audience, transaction rollback, concurrency, idempotency and drift.
   Stateful corruption tests stay integrated when storage behavior is the invariant.
4. Replace the runtime seed search for natural one/twenty, and other measured search loops,
   with explicit seeds plus assertions confirming the selected outcomes. Retain broader sampling
   only where randomness or distribution is itself the requirement.
   The audit observed natural one at seed 7 and natural twenty at seed 36 in
   `Raw_check_has_no_natural_one_or_twenty_override`; its search executed 72 evaluations.
   Recheck those outcomes against the current mechanic, then use two evaluations while retaining
   the assertions that the high modifier succeeds on one and the low modifier fails on twenty.
5. Consolidate assertions about the same execution result: output, proposed/applied effects,
   receipt and unchanged state may belong together. Do not concatenate unrelated stateful
   scenarios, or assume a theory avoids setup per row. Preserve identifiable failing cases.
6. Remove old expensive duplicates as each replacement is proved, using slice 8's deletion
   rules immediately. Do not leave both versions indefinitely and call that an optimization.

**Verification:** converted direct rule tests, retained integration tests for those families,
full affected domains and matched family timing. For a representative conversion, introduce a
temporary wrong output/effect in an isolated copy and verify the replacement fails, then restore
the copy. Do not mutate the shared authored catalog during concurrent tests.

**Done when:** detailed rules still execute actual JavaScript, integration boundaries remain
covered, old redundant cases are removed, and elapsed time improves for the converted families.

## Slice 8 — Remove obsolete and redundant tests across all domains

**Depends on:** 7. **Changes:** test deletion/consolidation and removal of unused test helpers.

**Start with:** the slice 0 inventory. Review every domain, prioritizing its slowest candidates.
Inspect `CatalogWorldFeature*Tests`, `Dnd2024*RepairTests`, `Dnd2024MovedMechanicTests`,
catalog contract/coverage tests, host/web registration tests, and repeated adapter tests as
candidates, not as a preapproved deletion list. Review browser source-text tests alongside mounted
and behavioral tests. Keep compatibility retention and adoption tests in the review.

Execute this slice in bounded passes, one domain per implementation task, in this order:
`kernel`, `state`, `catalog`, `execution`, `interaction`, `automation`, `host`, `web`,
`game-world`, `dnd2024`. A request such as "implement slice 8 for catalog" executes that pass
and its verification. Reuse the task/PR deletion mapping from completed passes instead of
re-reviewing them. The full slice is complete only after every domain has a disposition.
For `web`, explicitly include generic browser, D&D Node and mounted coverage.

Begin with these inspected candidates. They are bounded review instructions, not permission to
delete an entire similarly named class. Most cleanup here saves little runtime by itself; the
large expected savings come from preparation and rule-matrix changes in earlier slices.

| Candidate and exact owner | Evidence and required disposition |
| --- | --- |
| `ContentHashTests.Every_authored_mechanic_field_moves_the_fingerprint` and `BootstrapRuleTests.The_fingerprint_moves_when_any_authored_field_changes` in `src/system/building-blocks/tests/` and `src/system/catalog/tests/` respectively | `MechanicFile.ContentHash` delegates its eight authored fields to the same canonical function. Consolidate the repeated field matrix only after preserving the direct test's distinct-variant assertion in the wrapper test. Keep file-to-store parity and backfill tests, which exercise persistence. |
| `ContentHashTests.Every_authored_contract_field_moves_the_fingerprint` and `ProcedureFileHashTests.Editing_any_authored_field_changes_the_fingerprint` in the building-blocks and catalog test owners | Compare all seven fields and transfer the unique distinct-variant assertion. Retain excluded `Matches`, field-boundary and normalization behavior; these are not interchangeable assertions. |
| `ItemViewContractTests.Approved_drafts_compile_in_the_real_host_and_enforce_closed_input` | It compiles `docs/current/item-view-contracts/*.draft.json`. Replace draft approval as the source of confidence with canonical authored query contracts. Transfer closed-input, malicious observer-field and nullable-context cases. Keep generic `ApplicationReadModelInput` cases in this mixed class. |
| `ItemRecipeAssociationTests` versus `ItemRecipesProjectionTests` | The association class contains test-local `Targets`/`Linked` traversal; projection tests exercise actual catalog JavaScript. Transfer useful grouping/link cases to `Separate_groups_use_exact_links_deduplicate_and_do_not_require_owned_materials` and related production-projection tests before deleting duplicate traversal. Keep generic hydration exclusion, observer knowledge, ambiguity and revision checks. |
| `src/system/web-interface/dnd2024/test/website-feature-contracts.test.js` | Fixed W01/W10 slice labels, proposal counts and historical prose in `contracts/website-feature-contracts.json` have no production consumers in the inspected source. Remove milestone-only assertions after confirming current references. Preserve the same file's checks of actual authored query/object contracts. Do not delete the file wholesale. |
| Two skipped methods in `ProtocolWalkTests`: `A_session_can_navigate_catalog_branches_over_the_public_protocol` and `Reading_a_contract_and_citing_it_is_visible_in_the_audit` | Their setup requires retired `procedure`/`mechanic` commit kinds. They already contribute no ordinary runtime cost. Remove the unreachable old scenarios after mapping any still-current navigation and read-evidence auditing invariant to current wire coverage. Preserve the eight other optional protocol facts. |
| `BootstrapContractTests.The_entry_contract_that_orient_points_at_is_seeded` versus `Every_contract_the_surface_names_is_seeded` | Seed membership overlaps, but orientation independently references `procedure.system.use`. Retain that direct reference check or move it into the survivor. `The_entry_contract_names_every_kind_the_surface_serves` checks the opposite direction and remains distinct. |
| World-feature test-local validators such as `CatalogWorldFeature5Tests.Closed_clock_contract_rejects_wrong_shape_and_bounds` | Compare each invalid input with canonical schema validation. Replace tests that only confirm a private duplicate validator once current-schema coverage preserves every relevant boundary. Keep intended fixture-integrity checks where they protect something different. |
| Legacy mechanic, export retention, adoption and migration tests | Retain: fresh-start exclusion, exportability, malformed retention rejection, transactional adoption and historical-schema migration are distinct current requirements. No live compatibility retirement was established in this audit. |
| Scheduler leases/wakeups, host route inventories, web startup and endpoint authorization | Retain the inspected boundaries. Lease fencing, capacity, route metadata, migration composition and authorization/output behavior are different assertions. The reviewed scheduler polling helper uses 10 ms intervals; widespread long sleeps were not established as a dominant cost. |

Apply this disposition table to each candidate:

| Disposition | Required evidence and action |
| --- | --- |
| Obsolete | The asserted behavior/path is retired in current implementation and is not a retained compatibility, migration, export, recovery or public contract. Remove the test and its unreferenced test-only helpers/fixtures. |
| Redundant | Name the surviving test(s), compare the actual assertions and preconditions, and show they catch the same regression at the required boundary. Move any unique assertion before deleting the duplicate. |
| Misplaced/expensive | The invariant remains useful but can be proved more cheaply. Move it to its owner/lower layer; preserve representative integration evidence. |
| Retained | The test protects a distinct active or compatibility invariant. Keep it even if old, slow, similarly named or sharing code coverage. |

1. Compare assertions, inputs, roles, audience, state, transaction boundary and failure behavior.
   Identical line coverage, similar names, passing for a long time or no recent failure history
   is not enough to prove redundancy. Do not impose a deletion percentage.
2. Check tests of test-local validators: preserve intentional fixture-integrity checks, but remove
   circular tests that only prove a duplicate implementation's assumptions when real contract
   coverage supersedes them. A source-text check can be replaced by behavioral coverage only
   if its architectural/packaging invariant is also preserved.
3. For legacy-harness candidates, read current production callers and retained contracts.
   `CatalogMechanicTestHarness` is test-only, but legacy storage/export/adoption remains a current
   requirement. Do not delete its sole coverage because it is absent from production DI. If
   retirement cannot be established without live evidence, retain the test and report why;
   this slice performs no live-state retirement or export operation.
4. Remove adjudicated tests physically, along with unused test helpers, fixture files and project
   entries. Do not replace them with `Skip`, an ignored category, a permanently excluded filter
   or a blanket weaker assertion. Update ownership and discovery membership deliberately.
5. Keep a concise removed-test to invariant/replacement mapping in the task/PR description.
   For high-consequence replacements, demonstrate a representative temporary fault is detected
   by the survivor. Use focused coverage only as supporting evidence, not the deletion criterion.
6. Work through all ten domains and both browser groups. Report domains reviewed with no safe
   deletions too. Remaining ambiguous candidates are retained with a reason, not silently dropped.

**Verification:** each affected domain and consumer boundary, selection/discovery integrity,
catalog validation for any authored changes, then full correctness and retained performance
checks for feature acceptance. Compare final membership against the baseline plus the explicit
addition/removal/rename mapping; every disappearance must have a disposition.

**Done when:** all domains have been reviewed, all evidenced obsolete/redundant candidates have
been removed, and distinct useful coverage remains. Unresolved candidates are named as retained.

## Slice 9 — Reduce serialized work and tune concurrency

**Depends on:** 8. **Changes:** test class organization, runner limits, measured browser setup.

**Start with:** remaining slow classes, especially `src/system/web-interface/tests/WebInterfaceTests.cs`,
the large D&D classes, `src/system/system-audit/tests/SystemAuditBaselineFixtureTests.cs`,
`src/system/schema-validation/tests/SchemaRetentionTests.cs`,
and browser test support/loader files identified by the baseline.

1. Split oversized serial classes by coherent owner behavior. Preserve immutable template reuse
   across the new classes and retain isolation for each writable database. Moving methods into
   different partial files of the same class does not create parallel execution groups.
   Prioritize the D&D inventory/progression, core mechanics, and character-creation/rest classes.
   Their first-run spans were approximately 486, 408 and 380 seconds. The small isolated
   system-audit and schema-retention checks accounted for about 6.7 seconds of case time;
   reorganizing those checks alone cannot address the dominant serial tail.
2. Review existing collections and locks. Keep isolation for global heap measurements, timing,
   environment variables, filesystem state and non-thread-safe resources. Do not place unrelated
   classes in one collection merely to share a fixture.
3. Benchmark a small bounded concurrency matrix on the same retained workload, for example
   2, 4 and 8 workers where supported by the host. Record peak memory and the serial tail as well
   as elapsed time. Avoid competing .NET, Node and mounted test pools all consuming the entire CPU.
4. Keep the conservative xUnit algorithm unless a measured comparison justifies another setting.
   xUnit v2 parallelizes collections, normally one per class, rather than methods within one
   class. Its aggressive setting changes scheduling, not that ownership rule. See the
   [xUnit parallel execution reference](https://xunit.net/docs/running-tests-in-parallel).
5. Reduce browser setup only where measured: reuse immutable fixture data, avoid rebuilding
   bundles in each ordinary test, and reset DOM/global/module state per test. Keep mounted UI and
   transport tests that protect distinct behavior. Do not replace them wholesale with source scans.
   Verify flags against the installed Node version and its
   [test runner documentation](https://nodejs.org/api/test.html).
6. Repeat the touched concurrency-sensitive groups with different execution orders where the
   runner supports this, or explicit reordered batches otherwise. Failures must be fixed, not
   hidden with retries. Re-run the full workload only after selecting the winning configuration.

**Verification:** discovery parity after class moves, isolation checks, concurrency-sensitive
groups, browser groups and matched final concurrency comparison. Performance tests execute alone.

**Done when:** settings and class boundaries have measured justification; extra concurrency does
not compromise isolation or memory stability; no cases disappear on class splits.

## Slice 10 — Remove build/startup overhead where measurement justifies it

**Depends on:** 9. **Changes:** runner/build configuration; a project split is conditional.

**Start with:** measured end-to-end profiles, the solution/test project, `Directory.Build.targets`,
and browser typecheck/build scripts. Domain filtering already exists and remains the interface.

1. Eliminate duplicate builds/restores within one run. Let the normal incremental build establish
   freshness, then use `--no-build` for that run. An explicit no-build option must explain its
   assumption; never infer freshness merely from an executable's existence or latest test result.
2. Preserve one test process for selected domain unions where template reuse pays off. Avoid
   moving expensive preparation into discovery. Reuse installed dependencies when lockfiles
   permit; do not reinstall packages on every selection.
3. Measure several representative single-domain edit/build/test cycles. If compilation/startup
   is less than 30% of elapsed feedback time, or feedback targets are already met, retain the
   single assembly and report the evidence. Do not add projects for organizational appearance.
4. If build/startup is at least 30% and prevents the feedback target, pilot one narrow kernel
   test project with only the production references its tests require. Compare identical edits
   against the existing layout, including discovery, restore and repeated template costs.
5. Retain the pilot only if it improves median end-to-end time by at least 20% for its selected
   workflow without slowing the complete workload beyond measurement noise. Otherwise revert
   the pilot within the slice and retain the simpler configuration.
6. A retained split must update `DantesRoleplay.slnx`, compile includes and
   `Directory.Build.targets`: every capability test source must belong to exactly one declared
   test assembly. Update runner discovery/union checks and minimal test-support references.
   Do not move production code, expose internals publicly, or share database-bound fixtures
   across processes to make the split compile.

**Verification:** fresh and incremental builds, domain and complete-workload discovery parity,
source ownership validation, protocol inclusion behavior, and matched end-to-end measurements.

**Done when:** redundant build work is removed and the project-layout decision has evidence.
Keeping one assembly is an acceptable result of this slice; inventing speedup evidence is not.

## Slice 11 — Verify the complete outcome and adopt the development policy

**Depends on:** 10. **Changes:** final fixes and durable test workflow guidance.

1. Run final inventories and reconcile every baseline test against retained, replaced, renamed,
   added or removed coverage. Check .NET theory cases and browser membership, including protocol's
   conditional build. Run the current full acceptance requirements before changing the guide.
2. Repeat the three matched measurements for affected examples, all correctness and the complete
   retained workload. Include build, setup and all expensive groups in the appropriate totals.
   Run protocol and served-browser checks when their existing triggers apply; do not conflate
   source/mounted tests with live acceptance evidence.
3. Verify representative boundary failures are caught by the selected domains and confirm broad
   infrastructure/unknown changes take the conservative path. Recheck that newly added test and
   source files cannot silently escape selection.
4. Replace the Validation guidance in `DEVELOPMENT.md` with the verified commands and policy
   below, and make the corresponding targeted edit in `AGENTS.md`. Keep its entry-page route
   short. Preserve catalog validation and protocol triggers. The policy change is part of this
   slice's reviewable result, not something earlier slices may assume already applies.
5. Record the final speed comparison and deletion mapping in the task/PR response. Keep durable
   domain routing in the machine-readable map and operating instructions in the current guide.
   Do not turn this plan into an ever-growing timing/status ledger.

Proposed final development policy:

| Situation | Required verification |
| --- | --- |
| Iterating inside a known owner | Build affected inputs; focused or affected tests; required catalog/protocol additions still apply |
| Accepting an owner-local feature with unchanged shared contracts | Complete correctness coverage for that domain plus its registered consumer boundaries and any existing feature-specific acceptance checks |
| Changing shared schemas, storage, transactions, authorization, build/test selection, public contracts, or unresolved ownership | Full correctness plus applicable migration, performance, protocol and feature-specific acceptance checks |
| Accepting a broad refactor, multi-domain milestone or release | Full build and `all`, with protocol/live-browser checks when applicable |
| Changing an explicitly measured performance/memory path | Relevant isolated performance checks in addition to correctness |

An owner-local acceptance run must contain the whole owning domain, not just the few tests edited.
If consumer coverage is incomplete, use full correctness. Full acceptance remains available and
mandatory at the broader boundaries above. A periodic CI full run may supplement this policy if
CI exists; this plan does not create an automation or rely on future CI to cover omissions.

**Done when:** selection and retained coverage are accounted for, required checks pass, runtime
results support the reported gains, deletion evidence is complete, and the new policy matches
working commands. If the speed target remains unmet, identify the remaining measured cost and
leave the performance objective open rather than claiming the whole effort is complete.
