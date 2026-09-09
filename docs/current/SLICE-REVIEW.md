# Slice 0–17 quality review

Reviewed 2026-09-07 at `df0633431ad08e35b744574baa615f6b1ed5a312` on `master`.

## Conclusion

The automated gates pass, but the delivered system does **not** satisfy all of the
completion claims in [SYSTEM-AUDIT.md](SYSTEM-AUDIT.md). Twelve open findings below
cover lost view data, stale-write/change-delivery gaps, incomplete migration wiring,
and insufficient acceptance evidence. Passing existing tests is not sufficient to
close these findings.

This is the requested follow-up backlog, not authorization to implement it. No
production code, catalog record, live database, activation, or published page was
changed during this review. Historical slice receipts were not rewritten. Keep the
C# kernel application-neutral when addressing these issues; D&D rules and
application-specific dependency declarations remain in catalog JavaScript/data or
the D&D browser application.

Scope: all numbered slices **0 through 17**, hence 18 commits. Each commit's changes
and message were inspected, followed by focused source/caller/test review and
integration checks at the final HEAD. The 18 historical trees were not each rebuilt.
All 18 messages contain both slice information and a changelog.

## Verification performed

| Check | Result |
| --- | --- |
| Full Release .NET suite | 1,988 passed, zero failures/skips; 12 m 23 s |
| Opt-in protocol walk | 8 passed, 2 existing environment-dependent skips |
| Web `npm run verify`, bundled Node 24.19.0 | TypeScript, 264 Node tests, 78 mounted tests, and production build passed |
| Web gzip budgets | Initial 77,011 bytes; mandatory deferred 42,310; first-ready total 119,321; all-feature total 188,051 |
| `roleplay validate catalog` | 600 records valid; 7 existing Trail Survival capability-input-schema warnings; disposable database |
| Served GM/DM browser | World History displayed zero events despite a ready API containing 251 |
| Read-only production-loader comparison | Minimal and full loaders against the same bound live campaign; results in R01 |
| Disposable adversarial probes | Party lookup failures/scaling, concurrent object-source changes, required collection schema, and mixed tracked/untracked commits |

Commands used from the repository root, except the web command:

```powershell
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --logger "trx;LogFileName=slice-review.trx" --results-directory .tmp/slice-review/test-results
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore -p:IncludeProtocolWalkTests=true --filter FullyQualifiedName~ProtocolWalkTests --logger "trx;LogFileName=slice-review-protocol.trx" --results-directory .tmp/slice-review/test-results
dotnet run --project DantesRoleplay.Tools -c Release --no-build -- validate catalog
# Working directory: src/system/web-interface/dnd2024; use Node 24, not the machine's default Node 20.
npm run verify
```

Local diagnostic helpers and TRX files are under ignored `.tmp/slice-review/`.
The helper commands are `node .tmp/slice-review/bootstrap-probe.mjs --live` and
`dotnet run --project .tmp/slice-review/ReviewProbe.csproj -c Release`. These are
temporary review aids, not committed regression coverage; each finding below
contains enough setup information to turn it into a maintained test.

Live access was limited to browser navigation and read-only requests. The loader
probe rejected non-GET/HEAD requests. No real scheduled provider jobs, live object
edits, migrations, rollback, or additional actor-seat provisioning were performed.
The browser check is not a fresh responsive/accessibility audit. Single diagnostic
loader runs are not a substitute for the plan's 20-pair performance measurements.

## Commit coverage

“No additional finding” means none confirmed in the reviewed change and its tests;
it is not a guarantee that the slice is bug-free.

| Slice | Commit | Boundary reviewed / follow-up |
| --- | --- | --- |
| 0 | `08be3b44` | Imported baseline and frozen workload gates; R12 concerns later comparison against them |
| 1 | `5e190c15` | Complete opaque-cursor pagination and failure semantics; R06 is a later regression of this invariant |
| 2 | `1d1fbdd2` | Registered contracts, exact references, schemas and reverse declarations; R10 |
| 3 | `07c9899f` | Prepared plans, batch reads, snapshot ownership and limits; R10 at the collection integration boundary |
| 4 | `3a308332` | Campaign/Factions objects and active browser loaders; R01, R12 |
| 5 | `43b1b0cb` | Generic knowledge/chronology selection and disclosure checks; APIs work, but R01 prevents their UI consumption |
| 6 | `1c9304c1` | Reverse writes, atomic effects, stale sources and replay; R02 |
| 7 | `f1b2de5d` | Registered-object reducer inputs and effect translation; R03 |
| 8 | `29495588` | Transactional change rows, replay, fallback and browser wiring; R04, R05, R08 |
| 9 | `f7af4693` | Browser query ownership, scope, invalidation and mounted consumers; R04, R08 |
| 10 | `0e075806` | Character/Item contracts, transport consolidation and active callers; R04, R07 |
| 11 | `7fdb2d32` | Generated-output removal, ignores and source/documentation cleanup; no additional finding |
| 12 | `c31f55a6` | Host costs, type filtering, schema reuse, contention and scaling tests; R12 limits the acceptance claim |
| 13 | `d8e961e8` | Runtime database/blob path ownership and corrected exact schema references; no additional finding |
| 14 | `7f0dd5df` | Content/activation deduplication, preserved references and migration tests; no additional finding |
| 15 | `3830123e` | Durable scheduling, claims, leases, retries and bounded execution; R09 |
| 16 | `d93c5aa3` | Compatibility retention policy and its validation; no additional finding; retention was deliberate, not failed deletion |
| 17 | `df063343` | Final runtime wiring, party hydration and acceptance assertions; R01, R06, R11, R12 |

## Open findings

P1 means address before accepting the cleanup as complete. P2 means a correctness,
contract, or performance follow-up that needs a maintained regression test. Every
entry is **Open**. “Reproduced” and “source-confirmed” distinguish execution evidence
from a traced code path; no source-only item is claimed as a reproduced live failure.

### R01 — P1: deferred World/Current data has no corresponding loader

**Slices:** introduced by SC04; still present at SC17. **Evidence:** reproduced in
the served browser and through the production loader.

[main.tsx](../../src/system/web-interface/dnd2024/src/server-host/main.tsx#L79)
enables all three deferral flags and the registered Campaign path.
[game-server-context.js](../../src/system/web-interface/dnd2024/src/server/game-server-context.js#L2431)
then returns connected with empty knowledge/chronology and unavailable current
situation. The hub only receives lazy loaders for Campaign details and Factions,
not the omitted World directories, history, lore, current situation, or context
directory. [DndInformationHub.tsx](../../src/system/web-interface/dnd2024/src/components/DndInformationHub.tsx#L435)
does not complete those reads when their tabs are opened. Refreshing the same
bound campaign repeats the minimal path.

On the same authorized live campaign, with character details deferred in both
cases, `readGameServerContext` returned:

| Value | Current minimal bootstrap | Full loader, Campaign/World deferral disabled |
| --- | --- | --- |
| HTTP reads | 5 | 2,097 |
| Party actors | 1 | 1 |
| Knowledge entries | 0 | 200 |
| Chronology entries | 0 | 251 |
| Location directory | Omitted | 259 |
| World people / factions | Omitted | 124 / 35 |
| Context-selection worlds | 1 fallback | 2 |
| Current situation | Unavailable | Ready |

The live History tab said “0 of 0 events” and “No dated world history available”;
its chronology API independently returned `ready` and 251 entries. This is missing
read wiring, not an empty or unimported database. SC17's claim that places/current
content is unavailable cannot be established from this minimal response.

**Follow-up:** add bounded, authorized per-view loaders and context-directory
discovery; distinguish unloaded, genuinely empty and failed states. Do not simply
restore the 2,097-read eager bootstrap. **Close when:** served GM/Actor/preview
navigation obtains the same authorized records as independent APIs, including all
continuations, with first-ready and complete-workload budgets measured separately.

### R02 — P1: object writes do not atomically enforce their complete read fingerprint

**Slice:** SC06 (`1c9304c1`). **Evidence:** reproduced in a disposable fixture.

[ApplicationObjectWriteService.cs](../../src/system/projection-materialization/persistence/ApplicationObjectWriteService.cs#L63)
compares the requested fingerprint before the effect transaction. It subsequently
builds expectations from components being written and required relationship-edit
endpoints, not all `current.SourceRevisions`. It does not pass the materialized
relationship snapshot into the transaction. See its locator construction and
effect batch at lines 135–198.

Using the existing `ApplicationObjectWriteTests.Fixture`, read the writable object
and submit a premise edit with its exact fingerprint. Wrap the effect applier to
change `member.one`'s read-only status immediately before applying the batch. In a
second case add the `member.two` membership relationship at that point. Both saves
returned `Applied: true`; the only checked component was
`subject.fixture:write-object.primary`. The resulting object contained the changed
status or new membership despite the supposedly exact expected snapshot.

**Impact:** a save based on stale object context is accepted; the advertised full
fingerprint is weaker than its contract. Written-component checks still protect
that component, so this is not a claim that all optimistic concurrency is absent.
**Follow-up:** carry the complete observed source and collection/absence evidence
into the typed-effect transaction and check it there. **Close when:** races on
written and read-only components and relationship addition/removal all reject
atomically, preserving successful replay behavior.

### R03 — P2: reducer relationship snapshots are recorded but not checked as collections

**Slice:** SC07 (`f1b2de5d`). **Evidence:** source-confirmed; no live action performed.

[ApplicationMechanicObjectProjectionResolver.cs](../../src/system/application-execution/persistence/ApplicationMechanicObjectProjectionResolver.cs#L145)
records complete `RelationshipCollections`, but
[ApplicationActionRunner.cs](../../src/system/application-execution/persistence/ApplicationActionRunner.cs#L152)
only sends component and containment expectations to the effect transaction.
Relationship snapshots are consulted at line 368 to choose the expected revision
of an edge being mutated; other observed edges and collection membership/absence
are not guarded.

This does not implement SC07's stated “checks every observed revision” guarantee.
The selected rest action does check its own added edge through its effect; that
does not validate the complete World membership snapshot the reducer received.
Future or expanded reducers can depend on unmodified relationships too.

**Follow-up:** introduce generic complete relationship-collection expectations in
the established transaction owner, without moving rest rules into C#.
**Close when:** a reducer fixture that reads a relationship collection and writes
an unrelated component rejects concurrent edge addition, removal and revision
change, including an initially empty collection.

### R04 — P1: targeted delivery leaves Character and Item views stale

**Slices:** SC08–SC10. **Evidence:** source-confirmed end-to-end event path.

[ApplicationObjectChangeTransactionParticipant.cs](../../src/system/projection-materialization/persistence/ApplicationObjectChangeTransactionParticipant.cs#L51)
only derives targeted consumers from registered objects. Once any objects exist,
unmatched component changes receive a `tracked-no-dependency` marker instead of a
compatibility invalidation. A quantity change does have a registered Item object
consumer, but [main.tsx](../../src/system/web-interface/dnd2024/src/server-host/main.tsx#L325)
only invalidates the Character client for Campaign-summary notices.
[DndInformationHub.tsx](../../src/system/web-interface/dnd2024/src/components/DndInformationHub.tsx#L184)
ignores notices other than Campaign/Factions, and
[ItemWorkspace.tsx](../../src/system/web-interface/dnd2024/src/components/items/ItemWorkspace.tsx#L16)
does not listen to `dnd2024-object-changed` at all.

Thus a typed item-quantity write emits an Item-object notice that no visible Item
consumer handles. Unregistered legacy read dependencies can be suppressed
altogether. The broad pre-migration refresh path no longer guarantees a stale
banner/cache retirement for these views; focus/expiry is not committed-change
delivery and does not itself guarantee that already rendered data updates.

**Follow-up:** register the actual query dependencies and connect their notices to
Character/Item consumers, retaining scoped compatibility invalidation until every
active legacy query is covered. **Close when:** a mounted integration test opens a
dossier/item, commits HP/quantity/equipment changes through typed effects, delivers
the real event, and verifies targeted stale state/refetch without a focus change.

### R05 — P1: a tracked commit masks an untracked commit in the same polling interval

**Slice:** SC08 (`29495588`). **Evidence:** reproduced in disposable SQLite.

[WebChangeFeed.cs](../../DantesRoleplay.Web/Live/WebChangeFeed.cs#L113)
emits `untracked-commit` only when replay returns zero changes. However, replay
returns a cursor checkpoint even when all new rows belong to another audience or
application (lines 169–184). SQLite `data_version` does not identify which commits
are covered by those rows.

Reproduction: connect a scoped player feed; before its next poll, commit one
delivery row for another application, then independently commit an untracked
state-table update. The feed emitted `cursor-advanced`, followed by `keep-alive`,
with no invalidation. The same masking can occur with a same-scope object notice.
Existing tests exercise untracked commits in isolation, not this combination.

**Impact:** the fallback is timing-dependent; direct-store/import/lifecycle writes
can leave consumers stale when another tracked operation happens nearby.
**Follow-up:** cover supported mutations with durable transactional change evidence
or a reliable transactional recovery marker; do not infer complete coverage from
the presence of any delivery row. **Close when:** mixed tracked/untracked writes
in one poll invalidate every affected scope, including filtered/no-dependency rows.

### R06 — P1: failed party hydration becomes a credible empty or partial roster

**Slice:** SC17 (`df063343`), regressing SC01 completeness. **Evidence:** reproduced.

[hydrateRegisteredPartyReferences](../../src/system/web-interface/dnd2024/src/server/game-server-context.js#L1379)
turns a failed/ambiguous actor-relationship read into `null`, filters it away, then
does the same for failed actor entity reads. The caller returns `connected` without
any incomplete marker.

In a valid one-member registered Campaign fixture, returning HTTP 500 for its
actor relationship resulted in four requests, `connected`, and zero party members.
Returning HTTP 500 for its actor entity resulted in five requests, `connected`,
and zero members. Multi-member failures silently produce a partial roster.

**Follow-up:** propagate failed or invalid required joins as an explicit scoped
unavailable/incomplete result rather than filtering them into emptiness.
**Close when:** first/later lookup failures, malformed responses, and ambiguous
links cannot yield a successful complete roster; normal deduplication still works.

### R07 — P2: SC10's Character/Item registered objects are not on the execution path

**Slice:** SC10 (`0e075806`), still open at SC17. **Evidence:** source/caller search.

The new [Character records](../../catalog/applications/dnd2024/objects/character/dnd2024.object.character-dossier-records.json)
and [Item records](../../catalog/applications/dnd2024/objects/item/dnd2024.object.inventory-item-instance-records.json)
are referenced by registration tests and the Item-to-definition object reference,
but not by production query or reducer object-role declarations. The published
[dossier](../../catalog/applications/dnd2024/queries/character/dnd2024.query.character-dossier-v1.json),
[Details](../../catalog/applications/dnd2024/queries/data/dnd2024.query.inventory-item-details.json),
[Recipes](../../catalog/applications/dnd2024/queries/data/dnd2024.query.inventory-item-recipes.json)
and [Uses](../../catalog/applications/dnd2024/queries/data/dnd2024.query.inventory-item-uses.json)
still execute their existing mechanic projections; these new records do not
participate in their read assembly. No registered recipe/use collection was added.

The shared Item envelope adapter and fixture relocation are genuine delivered
improvements. Retaining published query IDs is also correct. Neither demonstrates
the required registered-object migration or improved query/SQL counts. SC10's own
receipt says read assembly is still incremental and request counts are unchanged;
SC17 nonetheless treats the mandatory architecture as finished.

**Follow-up:** route structural read inputs through the exact registered objects,
including bounded recipe/use references, while preserving published envelopes and
JavaScript rules. **Close when:** production-path tests prove these exact objects
are materialized, parity/disclosure/pagination tests pass, and matched read/SQL
counts improve. Registration-only assertions are insufficient.

### R08 — P2: the change subscription remains bound to the initial perspective

**Slices:** SC08/SC09 integration. **Evidence:** source-confirmed lifecycle.

[main.tsx](../../src/system/web-interface/dnd2024/src/server-host/main.tsx#L283)
creates one `EventSource` from `initialEnvelope.audience.perspective`. Changing the
hub perspective replaces its envelope but never replaces this stream. The stream
is only closed on page hide.

Start a GM session in persisted Player preview, then switch to DM and open
Factions: reads use DM authority, but the stream remains player-filtered and cannot
deliver DM-only Faction changes. The opposite switch keeps the broader initial
subscription. This is a scope-lifecycle defect, not evidence that actual private
object payloads were leaked; notices carry metadata rather than game data.

**Follow-up:** make subscription ownership follow the active authorized scope,
closing/reconnecting and resetting/reconciling cursors when that scope changes.
**Close when:** mounted root tests cover both perspective transitions and verify
subsequent private/public notices and cache invalidation against the new scope.

### R09 — P2: claimed jobs can expire while waiting for a worker slot

**Slice:** SC15 (`3830123e`). **Evidence:** source-confirmed timing sequence; no real
provider job was run.

[SqliteScheduledAiTaskWorkStore.cs](../../src/system/trigger-scheduling/persistence/SqliteScheduledAiTaskWorkStore.cs#L12)
claims up to eight jobs with ten-minute leases.
[ScheduledAiTaskTools.cs](../../src/system/trigger-scheduling/hosting/ScheduledAiTaskTools.cs#L195)
allows four concurrent executions, but renewal starts only inside `ExecuteAsync`,
after a job acquires the semaphore. The other four claimed jobs have no heartbeat.

If the first four provider requests run longer than ten minutes while renewing
normally, the waiting leases expire. Another worker can reclaim them; the original
worker later invokes them anyway because the executor does not validate the lease
before calling the provider. Its eventual stale result is rejected, but duplicate
provider work/cost and wasted attempts have already occurred. The existing slow-job
test releases jobs promptly and does not advance across this waiting interval.

**Follow-up:** claim only available capacity, or renew all owned queued leases and
validate ownership before invocation. **Close when:** fake-time, two-worker tests
hold four jobs beyond the lease duration and prove the waiting jobs neither execute
under expired leases nor incur duplicate calls from this local queueing condition.

### R10 — P2: valid required collection schemas fail before collection expansion

**Slices:** SC02/SC03/SC04 contract/materializer integration. **Evidence:** reproduced
with the production registry and materializers in disposable SQLite.

[ProjectionCollectionMaterializer.cs](../../src/system/projection-materialization/persistence/ProjectionCollectionMaterializer.cs#L61)
first invokes the structural materializer using the final object schema; only later
does it add the collection at its target pointer. Structural materialization validates
that schema before returning, so a normally required collection field is absent
at validation time.

Register an object with a mapped string `title`, a declared relationship collection
targeting `/items`, and output schema `required: ["title", "items"]`. Registration
succeeds. Materializing even an empty collection fails with
`Structural projection output fails its exact schema.` Existing objects that omit
top-level collection requirements avoid the failure but weaken schema guarantees.

**Follow-up:** validate the complete expanded output at the correct stage while
still validating inputs/intermediate structural contracts; alternatively reject
unsupported declarations explicitly until that is implemented. **Close when:**
required empty/non-empty/paged collections materialize and genuinely invalid final
outputs still fail their exact schemas. Do not solve this by weakening catalog schemas.

### R11 — P2: the five-read Campaign bootstrap grows as two reads per party member

**Slice:** SC17 (`df063343`). **Evidence:** reproduced with the production loader.

The same [party hydration](../../src/system/web-interface/dnd2024/src/server/game-server-context.js#L1388)
performs one relationship request per active participation and one entity request
per distinct actor. The initial total is `3 + participations + distinct actors`.
Fixtures with one, three, and twenty unique active members performed **5, 9, and
43 requests** respectively. All returned connected with the expected roster.

Consequently even three members exceed the frozen eight-read initial Campaign
gate. The SC17 statement that this path uses five reads only describes the recovered
one-actor campaign. The existing eight-request concurrency limiter controls pressure,
not total request count.

**Follow-up:** declare and batch the actor-reference/name hydration behind the
generic object/read owner, leaving application relationship meaning in the catalog.
**Close when:** realistic and maximum supported rosters meet the initial request
budget with complete results and no per-member browser requests.

### R12 — P1: final acceptance compares an incomplete traversal with the complete baseline

**Slices:** SC04/SC10/SC17 acceptance against SC00. **Evidence:** source-confirmed
measurement mismatch, reinforced by R01/R07/R11 reproductions and caller tracing.

[SC17's receipt](SYSTEM-AUDIT.md#slice-17-sc17--acceptance-and-closeout) compares at
most 33 reads with the 200-request complete-workload ceiling and claims matched
loader improvements. But the
[browser sampler](../../src/system/web-interface/dnd2024/scripts/sample-browser-baseline.mjs#L125)
traverses shell, Party/character, Map and Current; it does not traverse and assert
complete History/Lore/People/Locations/Factions data. A run can be `collected` when
Map is unavailable or a view times out. Collection is a useful observation status,
not successful feature acceptance. R01 proves missing data was treated as
unavailability instead of being restored by deferred reads.

The frozen gates explicitly require the same authorized records as the original
2,096-request workload, no omitted features, and separate first-ready versus
matched-workload timing. The full diagnostic loader still makes 2,097 reads; this
is not a new statistical benchmark, but it exposes the surviving eager workload.
The [SC12 scaling fixture](../../src/system/system-audit/tests/SystemAuditBaselineFixtureTests.cs#L71)
measures generic selection of 419 marker rows. Its one-SQL/zero-source results do
not establish the full workload's 80-SQL gate or actual Campaign/Factions
allocation/latency growth when unrelated population doubles. Earlier focused
object SQL tests remain useful, but cannot substitute for those missing measurements.

**Follow-up:** reopen final acceptance after repairing runtime coverage, and measure
both first-ready and equivalent complete traversal with explicit record/parity
assertions. Preserve existing raw measurements as observations rather than
retroactively relabeling them. **Close when:** required Character/Item wiring is
delivered; real per-view SQL/source/allocation/scaling gates are instrumented; and
20 cold/warm pairs per authorized profile meet the unchanged gates with failures,
unloaded states and genuinely unavailable content distinguished.

## Suggested resolution order

1. Restore correct view/roster completeness (R01, R06), with the missing served-path tests.
2. Fix transactional snapshot and change-delivery correctness (R02–R05, R08).
3. Complete the registered read path and bounded bootstrap (R07, R10, R11), and repair queued lease handling (R09).
4. Rerun equivalent-workload acceptance and update the historical completion claims through an explicit correction (R12).

Implementations should close individual entries with maintained tests and reviewed
commits. Do not import/export live content, retire compatibility identities, or
change application schema meaning merely to make the review green.
