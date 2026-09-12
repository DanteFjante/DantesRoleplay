# D&D 2024 upgrade and repair plan

## Purpose and status

This is the execution plan for bringing the D&D 2024 application onto the current generic
platform while preserving its React/Redux/Vite website and its catalog-owned rules. It separates
work that can start now on the website from later mechanic and content repair that needs concrete
fixtures and approved identifiers.

The plan is grounded in baseline `6023d31bd1233bd20d5d061a17736d1674b5ca05`. It is not a claim
that the D&D 2024 rules are complete or correct. It does not authorize a framework rewrite, a
blanket schema relaxation, replacement of the platform's existing activation path, or removal of
retained compatibility records.

## Delivered outcomes

The work is complete when:

1. The existing D&D website is rendered through the shared website composition contract and a
   small D&D-owned mapping to its React components. Page component props remain free-form JSON,
   unknown props are tolerated, and each component declares only the fields it actually requires.
2. One malformed or failed component cannot take down its siblings. The failed component exposes
   a local retry or recovery state and preserves the rest of the page.
3. Reads, actions, and forms use the current generic web application endpoints and composition
   binding envelopes. Typed writes continue through current-authority checks, standing grants,
   confirmation, idempotency, audit, and readback.
4. The shared system navigation discovers and switches among published website applications while
   omitting packages that do not publish website pages. D&D consumes the shared system theme and
   tokens without creating another preference engine.
5. The D&D application's queries, procedures, mechanics, manuals, intent associations, workflows,
   schedules, and observers are adopted only through the platform's existing candidate publication
   and recoverable activation path. Missing records remain unavailable.
6. Every catalog change is rehearsed against a disposable database. Live SQLite authority is
   exported and backed up before the corresponding authored records change, and every activation
   has a tested recovery path.

## Grounded baseline

The canonical authored application is `catalog/applications/dnd2024`. At the baseline it contains
164 mechanic definitions with matching JavaScript, 42 queries, 74 procedures, 183 component
schemas with fixtures, 111 other schemas, 30 object definitions, and 2,487 content records. These
numbers describe authored files, not an assertion that every record is registered or active in a
particular SQLite database.

All 164 canonical mechanics and all 74 canonical procedures are marked active in their authored
definitions. The query set contains 29 mechanic projections and 13 application-object projections;
33 are model-visible and 9 are binding-only. Only 10 queries currently declare an input schema,
and 29 declare an output schema. These are review targets rather than evidence that schemas are
missing: the required contract depends on how each query is exposed.

The focused catalog tests pin the current authored boundary: 164 mechanics, 42 queries, version-1
child pins with SHA-256 fingerprints, reviewed namespaces, and deprecated non-callable legacy
procedures. The generic bounded-schema tests also compile the canonical D&D component schemas.
A recorded disposable inventory run on 2026-09-12 used
`dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj --no-restore -- validate catalog`
at predecessor `9395cd8e` and succeeded with 614 root catalog records and seven pre-existing Trail
Survival input-schema warnings. This evidence must be rerun on the implementation baseline; a
later attempt was blocked by an existing process locking a build output. Neither result proves that
the separately packaged D&D application is registered or activated.

There are 31 duplicate D&D identifiers across the canonical application and older global catalog
layouts: 13 mechanics and 18 procedures. Every duplicate has different bytes. The compatibility
retention manifest deliberately keeps legacy records, and tests require retained procedures to
remain deprecated and non-callable. Retirement therefore requires consumer and provenance
evidence for each record; filename similarity is not sufficient.

The authoritative D&D ECS component schemas are intentionally strict: 182 of 183 reject unknown
top-level properties. The website requirement for free-form JSON applies to presentation component
props. It does not apply to authoritative game-state schemas.

The existing website lives in `src/system/web-interface/dnd2024`. It already uses React 19,
Redux, Vite, lazy views, top-level error handling, defensive data adapters, and object-oriented view
models. Its published feature contract currently describes 14 visible read-only families. A narrow
campaign-premise write adapter and editor exist but are not connected to the published entry point.

The generic page grammar in `DantesRoleplay.Web/Pages/WebComposition.cs`
already keeps its structural envelope bounded while allowing component `props` to contain
arbitrary JSON values. Component definitions already declare `requiredProps`. The implementation
must reuse that contract. The main D&D gap is a component mapping with local failure recovery and
an incremental bridge from generic bindings to existing React components. Workstream 04 may land
an equivalent seam first; the D&D plan must reuse it rather than create a duplicate.

The platform already owns application discovery, read models, action preparation and execution,
recovery, mapped object writes, candidate publication, current-authority validation, manuals,
intent associations, inner workers, scheduling, and observer evaluation. D&D must consume those
owners rather than add parallel registries or execution paths.

No application-owned declarative D&D workflow, durable host schedule, or observer fixture was
found at the baseline. `travel.exposure.schedule` creates a pending ECS schedule entity; it is not
a durable host scheduling registration. A real use case and fixture are required before any of
these capabilities can be considered implemented.

## Non-negotiable boundaries

- C# remains the generic kernel. It must not acquire D&D identifiers, vocabulary, eligibility
  rules, formulas, outcomes, or rule branches.
- D&D behavior belongs in `catalog/applications/dnd2024`, especially its JavaScript mechanics,
  procedures, schemas, and fixtures.
- SQLite is authoritative for registered applications, live state, events, operations,
  notifications, and MCP-only authored content. Export live records before editing the same
  records in files. Import reviewed files only at an explicit synchronization boundary.
- The platform's retained-content candidate publication and recoverable activation path is the
  only activation path. Do not reimplement it in the D&D package.
- Keep the page structure contract bounded. Allow extra fields inside presentation `props` and
  validate only each component's declared minimum. Do not loosen ECS schemas en masse.
- Preserve React, Redux, and Vite. Move existing views behind adapters incrementally.
- Do not create a new public catalog ID, change schema meaning, add a migration, or alter a shared
  public surface without an exact fixture and coordinator review.
- Do not remove legacy records until their source, hash, registration, activation, and consumers
  have been compared with the canonical record.
- Workers use detached worktrees from the coordinator-provided exact master commit and create no
  branches. Compatible lanes with disjoint file ownership may run concurrently. One coordinator
  serializes shared-contract decisions, integration onto master, migrations, and final acceptance.
- Worker checks use disposable databases and disabled providers. Dependent scenarios wait for
  real dependencies; test doubles do not count as acceptance.
- Each worker updates the assigned original-checkout checkpoint at decisions, material progress,
  delivery, and pauses, but never stages or commits original-checkout files. The coordinator alone
  serializes those checkpoint changes.

## Fixed contracts and owners

### Presentation and browser ownership

`DantesRoleplay.Web/Pages/WebComposition.cs` owns the strict page structure, free-form component
props, and `requiredProps`.
`DantesRoleplay.Web/Pages/CompositionPageBindingCoordinator.cs` owns server-side binding
coordination and current-authority revalidation. The browser contract is owned by
`DantesRoleplay.Web/BrowserComponents/system-client.js`,
`DantesRoleplay.Web/BrowserComponents/application-workspace.js`, and
`DantesRoleplay.Web/BrowserComponents/composition-bindings.js`. The active shared website lane
also owns `DantesRoleplay.Web/BrowserComponents/system-navigation.js` and
`DantesRoleplay.Web/BrowserComponents/system-theme.js`.

The generic host project above is separate from the D&D package. D&D integration belongs under
`src/system/web-interface/dnd2024/src`. The following are provisional internal seams for the first
website slice. Reconcile them with workstream 04 before editing; when that workstream has already
landed an equivalent registry, boundary, API adapter, or renderer, extend the landed owner instead
of adding the proposed file:

- `page-components/component-contract.ts` defines the local renderer input and result types. Its
  component key is a website-local key, not a public catalog ID.
- `page-components/component-registry.tsx` maps declared component keys to lazy React renderers and
  their minimum required props.
- `page-components/ComponentBoundary.tsx` isolates load, parse, render, and retry failures per
  component.
- `page-components/CompositionPage.tsx` turns the existing generic page envelope and bindings into
  registered D&D components.
- `server/website-api.ts` is the single D&D adapter over the generic read, prepare, execute,
  recovery, and mapped-write endpoints. It normalizes transport errors but does not decide rules.

Existing page components remain the view owners. Shared `system-navigation` owns the application
switcher; D&D owns only its local views and local route presentation. `DndInformationHub.tsx`
remains the application coordinator until each route has moved behind the shared composition seam;
it should shrink rather than be replaced wholesale. `game-server-context.js` is migrated call by
call into the reconciled API adapter and existing scoped stores, with behavior-preserving tests
around every moved call.

Unknown props must reach the registered component unchanged. Missing required props fail only that
component before render. Malformed optional props are handled by that component's adapter and do
not invalidate siblings. Parse and render failures produce a stable local error state with a retry
that reissues only the failed binding when possible.

### Generic website ownership

Application and page discovery stay in:

- `DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs`
- `DantesRoleplay.Web/Http/WebInterfaceApplicationEndpoints.cs`
- `DantesRoleplay.Web/Pages/WebPublicationDiscovery.cs`
- `DantesRoleplay.Web/Pages/WebPagePublicationService.cs`
- `DantesRoleplay.Web/Http/WebPermissionedPageRouteAdapter.cs`

Shared composition, discovery, transport, DI, or publication changes are coordinator-owned. A
worker that finds a gap must report the current behavior, the smallest contract change, exact
files, compatibility effect, and tests; it must not edit those owners.

Shared `system-navigation` builds multi-application navigation from `/api/web/applications` and the
selected application's published pages. An application appears only when it has at least one
visible website page for the current principal. Packages without website publication never enter
the navigation model. D&D must not add a second discovery registry or application switcher.

`DantesRoleplay.Web/BrowserComponents/system-theme.js` is the sole theme preference engine. It owns
storage key `dantes.system-theme.v1`; defaults to `green-wood` (`Green & Wood`) and also accepts
explicit `system`, `light`, and `dark` preferences; emits
`system-theme-change` with detail `{ preference, resolvedTheme }`; and exports
`initializeSystemTheme`, `getSystemTheme`, `setSystemTheme`, `onSystemThemeChange`, and
`SystemThemeToggle`. D&D consumes that module and the shared CSS tokens. Optional D&D palette
overrides are a future presentation extension and do not require application-theme metadata for
the current lane.

### Catalog and runtime ownership

Canonical D&D definitions remain under `catalog/applications/dnd2024`. Generic persistence and
navigation remain under `src/system/catalog/persistence` and `src/system/catalog-navigation`.
Application registration and activation remain under `src/system/application-registry` and
`src/system/application-activation`.

Candidate closure and runtime validation already have specialized readers for queries, pure and
stateful procedures, and workflows in `src/system/application-activation/persistence` and
`src/system/application-execution/persistence`. Every adopted D&D capability must enter through
those readers and validators with reviewed namespace chains and pinned fingerprints.

Manual and intent integration stays in
`src/system/interaction-orchestration/persistence/InteractionManualContextService.cs`,
`src/system/interaction-orchestration/persistence/ProcedureManualSectionRetriever.cs`,
`src/system/interaction-orchestration/persistence/IntentMatchAssociationService.cs`, and
`src/system/interaction-orchestration/persistence/ProcedureIntentAssociationPreparation.cs`.
Inner work stays in
`src/system/system-task-orchestration`; schedules and observers stay in
`src/system/trigger-scheduling`, particularly
`persistence/SqliteObservationTriggerWorker.cs`, `persistence/SqliteConditionalTriggerWorker.cs`,
and `persistence/ConditionalTriggerPredicatePersistence.cs`. Catalog predicate execution remains
in `src/system/application-execution/persistence/CatalogJavaScriptObserverPredicateAdapter.cs`.
D&D contributes catalog records and JavaScript predicates only after a concrete fixture identifies
the desired behavior.

### Writes and authority

The first form action extends the existing campaign-premise pilot in
`src/system/web-interface/dnd2024/src/server/campaign-premise-write.ts` and
`components/CampaignPremiseEditor.tsx`. It uses the platform's mapped-write or prepared-action
surface, including expected source fingerprint, current selected activation, DM authorization,
idempotency, confirmation where required, audit, and read-after-write. The React component never
writes SQLite or constructs authoritative ECS rows.

No UI field implies authority. The server derives campaign, application, state-space, principal,
and current revision from registered context and rejects stale or mismatched requests.

## Execution order and vertical slices

Each slice ends with a reviewable commit and focused checks. Later slices start only when their
listed dependencies are real on the integration baseline. Website Slices 1–5 describe required
outcomes for the already assigned workstream 04. Before starting one, compare it with workstream
04's integrated and in-flight changes, then implement only the uncovered outcome.

| Slice | Lane | Must already be integrated |
| --- | --- | --- |
| 0 | Current preservation | Generic platform baseline and worktree launcher |
| 1 | Current website | Slice 0 inventory; workstream 04 reconciliation; existing generic composition format |
| 2 | Current website | Uncovered Slice 1 outcome; real generic read and binding endpoints |
| 3 | Current website | Shared `system-navigation` and `system-theme.js` from workstream 04 |
| 4 | Current website | Uncovered Slices 1–2 outcomes; real mapped-write or prepared-action path |
| 5 | Current website | Reconciled Slices 1–4 outcomes, one family at a time |
| 6 | Future catalog integration | Slice 0; real candidate publication, activation, manual, and intent dependencies |
| 7 | Future runtime adoption | Slice 6 plus a named fixture and every real worker/schedule/observer dependency |
| 8 | Future game repair | Slice 0 provenance plus one failing rule/content fixture; Slice 6 before publication |

### Slice 0 — Preserve and inventory live authority

Owner: Astra coordinator. Luna may assist with a read-only comparison.

Record the exact integration commit. In a disposable database, register or locate the D&D source
and application, inspect selected activation, state-space bindings, page revisions, namespace
review status, and retained compatibility rows. Produce a machine-readable comparison during the
task, but do not add a permanent receipt to the repository. Before future live work, export the
same records and create a restorable SQLite backup.

Outcome: canonical files, legacy files, registered rows, and selected revisions can be matched by
ID and fingerprint. Unknown live registrations are explicit. No record is changed.

### Slice 1 — Component runtime and failure isolation

Owner: Terra implementation worker. Luna may perform an independent contract audit. Astra
integrates.

First compare workstream 04's delivered and in-flight files with the provisional D&D seams above.
Add or extend only the missing page-component mapping, composition renderer, and per-component
error boundary. Adapt two existing read-only components with different data shapes. One fixture
includes an unknown prop; another omits a required prop; a third returns malformed optional data
beside a healthy sibling.

Outcome: unknown props survive, required fields fail locally, malformed data cannot take down the
page, and retry is scoped to the failed binding. Existing routes continue to work.

### Slice 2 — Safe API bridge and one read-only page

Owner: Terra. Astra reviews binding and authority behavior.

Extend the website API adapter supplied by workstream 04, using the provisional
`server/website-api.ts` name only if no equivalent owner landed. Migrate one low-coupling page,
preferably Rules or Installed Content, through the shared composition seam. Reuse the generic
binding envelope and current query IDs; do not create a D&D transport. Carry cancellation, denial,
stale selection, recovery, and structured error states to the local component boundary.

Outcome: one published page is wholly driven by generic discovery and bindings, while its current
React component and Redux state continue to render it.

### Slice 3 — Consume shared navigation and theme

Owner: Terra. Luna may perform an independent access and keyboard review.

Integrate D&D with shared `system-navigation`; do not implement application discovery or switching
inside the D&D package. Preserve D&D local routes and show the shared empty state when no website
application is available. Import `system-theme.js`, initialize it once in the shell, and use its
toggle, change event, and shared CSS tokens. Verify focus, labels, contrast, narrow layouts, and
route restoration for `system`, `light`, and `dark`.

Outcome: shared navigation switches among authorized website applications and pages. Nonwebsite
packages remain absent, and D&D follows the single shared theme preference without a second storage
key or theme event.

If shared navigation or theme lacks a necessary behavior, the worker reports an exact contract
proposal. The coordinator owns that proposal, compatibility tests, registration, and DI changes.

### Slice 4 — Typed form/action pilot

Owner: Terra. Astra reviews authorization and recovery.

Connect the campaign-premise editor through the generic write path. Bind fields from the declared
input contract, show validation and confirmation states, submit an idempotency key and expected
fingerprint, recover interrupted results, and refresh the authoritative read model after success.
Exercise denied, stale, duplicate, validation-failed, interrupted, and successful results.

Outcome: the first D&D website form performs a typed, authorized, audited write without application
logic in C# or a D&D-specific write endpoint.

### Slice 5 — Remaining website families

Owner: Terra, one family per commit. Luna may audit fixtures and isolation independently.

Move all remaining contracted page families behind the reconciled component seam in dependency order: simple
read models, list/detail views, route-sensitive views, then encounter and character surfaces. Keep
existing selectors and view models when they already express the behavior. Delete an old adapter
only in the same commit that proves all of its consumers moved.

Outcome: every published D&D page uses the common component runtime, while existing feature
contract coverage stays green. `DndInformationHub.tsx` contains coordination only, and
`game-server-context.js` has no unowned duplicate transport behavior.

### Slice 6 — Candidate publication, manuals, and intent fixtures

Owner: Sol. Astra reviews all identifiers and integrates. Luna may independently inspect authored
closure. Sol may run concurrently with Terra only while their file owners are disjoint.

For one existing mechanic, one existing procedure, and one existing query, build disposable
registration and candidate fixtures that prove exact dependency closure, reviewed namespace
lineage, schema pins, JavaScript availability, and recoverable activation. Add manual and intent
associations only where an existing public ID and authored description establish the meaning.

Outcome: the selected existing capabilities publish and activate through the real platform path,
manual retrieval returns their reviewed sections, intent preparation resolves the same current
capability, and rollback restores the prior selected activation.

### Slice 7 — Inner worker, schedule, and observer adoption

Owner: Sol. Astra owns shared registration and final authority review. This catalog/runtime lane
may run beside a website lane only when file ownership is disjoint.

Start only after a named D&D use case has an input fixture, expected events/effects, retry behavior,
and an approved existing or new public ID. Choose the smallest real case for each capability. A
pending ECS schedule entity alone does not satisfy the durable schedule requirement. Register
catalog JavaScript for rule predicates; keep admission, leases, retries, idempotency, and typed
effect application in the generic host.

Outcome: each implemented path runs through its real generic dependency and survives restart or
replay. Capabilities without an approved fixture remain unavailable and are reported as such.

### Slice 8 — Mechanic and content repair batches

Owner: Sol for rule analysis and implementation. Terra may work concurrently on a disjoint website
family. Luna may check source/fixture consistency. Astra integrates.

Repair one rule family at a time. Begin from a reproducible game-state fixture and an expected
rule outcome, then update the canonical JavaScript/schema/content owner and focused tests. Compare
each touched ID with legacy and live registrations before editing. Schema meaning changes require
a new immutable version and an explicit migration; do not reinterpret stored state in place.

Outcome: each batch fixes named behavior with fixtures and preserves unrelated rules. There is no
aggregate claim of complete D&D 2024 coverage.

## Agent roles and concurrency

- **Astra, medium:** sole coordinator and integrator. Owns architecture, shared-contract proposals,
  DI/registration, transports, migrations, master integration, recovery decisions, and final
  acceptance. Astra uses the expensive model for coordination and shared decisions rather than
  routine file edits.
- **Terra, medium:** primary implementation worker for website slices and bounded mechanical edits.
- **Sol, medium:** implementation worker for catalog/runtime and rule slices. Sol and Terra may run
  concurrently when their declared file owners do not overlap and neither changes a shared
  contract.
- **Luna, low:** optional independent helper for inventory, fixture comparison, accessibility, or a
  focused review when that check materially reduces integration risk. A helper review is not a
  mandatory gate for every slice.

Parallel lanes declare their file lists before editing. The coordinator resolves overlaps first
and serializes shared contracts, original-checkout integration, migrations, and the solution-wide
acceptance. Each worker returns a compact commit, changed files, focused results, dependency status,
unavailable paths, and blockers.

## Starter prompts

Replace `<BASE>` with the exact coordinator-provided master commit. Every prompt assumes a detached
worktree and no branch.

### Terra — component runtime

```text
Work in a new detached worktree from <BASE>; do not create a branch or change the original checkout.
At decisions, material progress, delivery, and pauses, update
C:\repo\DantesRoleplay\docs\current\upgrade\05-dnd2024-plan.md in the original checkout; do not
stage or commit any original-checkout file because the coordinator serializes checkpoints.
Read AGENTS.md, docs/current/README.md, and docs/current/DND2024-UPGRADE-PLAN.md, then inspect only the
D&D website composition owners and focused tests. Reconcile first with the delivered and in-flight
workstream 04 files. Implement only the uncovered Slice 1 outcome: extend the existing component
mapping/composition owner and per-component failure/retry boundary, then adapt two existing
read-only components. Preserve React/Redux/Vite and the strict generic page envelope;
component props remain free-form and ECS schemas remain strict. Do not edit shared contracts, DI,
transports, migrations, catalog IDs, live SQLite, providers, docs/world, PDFs, or _to_delete. Run
`npm run verify` with the package-supported Node runtime. Before editing, report your exact file
list to the coordinator so it can be checked against concurrent lanes. Commit the slice and report
the commit, files, results, dependency status, unavailable paths, and blockers to the coordinator.
```

### Luna — independent component audit

```text
In a separate detached worktree from <BASE>, read the D&D upgrade plan and review Slice 1 without
editing implementation files. At decisions, material progress, delivery, and pauses, update
C:\repo\DantesRoleplay\docs\current\upgrade\05-dnd2024-plan.md in the original checkout; do not
stage or commit any original-checkout file because the coordinator serializes checkpoints. Check
that unknown component props survive, only declared required props gate rendering,
malformed optional data and render exceptions stay local, retry is local, and no authoritative ECS
schema or generic envelope was relaxed. Run only focused checks. Report actionable findings with
file and line references; do not create a branch or commit in the detached review worktree.
```

### Terra — navigation and theme

```text
Work in a detached worktree from the coordinator-provided integrated commit; create no branch.
At decisions, material progress, delivery, and pauses, update
C:\repo\DantesRoleplay\docs\current\upgrade\05-dnd2024-plan.md in the original checkout; do not
stage or commit any original-checkout file because the coordinator serializes checkpoints.
Implement only the uncovered DND2024-UPGRADE-PLAN Slice 3 outcome after reconciling workstream 04.
Consume shared `system-navigation` for application switching and shared `system-theme.js` for the
default `green-wood` and explicit `system`/`light`/`dark` preferences, `dantes.system-theme.v1`
storage, `system-theme-change` event,
toggle, and CSS tokens. Keep D&D local navigation in the React/Redux/Vite shell. Do not add a D&D
discovery registry, application switcher, theme storage/event engine, runtime theme-metadata
requirement, public ID, or generic endpoint edit. If a shared contract is insufficient, stop at an
exact proposal with files and tests. Run the focused D&D tests and `npm run verify` under the
supported Node runtime. Before editing, report your exact file list to the coordinator so it can be
checked against concurrent lanes; commit and report results and blockers.
```

### Terra — typed form pilot

```text
Work in a detached worktree from the coordinator-provided integrated commit; create no branch.
At decisions, material progress, delivery, and pauses, update
C:\repo\DantesRoleplay\docs\current\upgrade\05-dnd2024-plan.md in the original checkout; do not
stage or commit any original-checkout file because the coordinator serializes checkpoints.
Implement DND2024-UPGRADE-PLAN Slice 4 only by connecting the existing campaign-premise editor to
the current generic mapped-write or prepared-action path. Preserve expected fingerprint,
current-authority revalidation, DM authorization, confirmation, idempotency, recovery, audit, and
readback. Add focused tests for denied, stale, duplicate, invalid, interrupted, and successful
outcomes. Do not add a D&D endpoint, game logic to C#, migration, or public ID. Use disposable data
and disabled providers. Before editing, report your exact file list to the coordinator so it can be
checked against concurrent lanes. Commit and report files, results, dependencies, unavailable
paths, and blockers.
```

### Sol — catalog publication fixture

```text
Work in a detached worktree from the coordinator-provided integrated commit; create no branch.
At decisions, material progress, delivery, and pauses, update
C:\repo\DantesRoleplay\docs\current\upgrade\05-dnd2024-plan.md in the original checkout; do not
stage or commit any original-checkout file because the coordinator serializes checkpoints.
Implement DND2024-UPGRADE-PLAN Slice 6 for one existing D&D mechanic, procedure, and query. Use the
existing candidate publication, reviewed namespace, retained-content activation, manual, and intent
owners. Do not invent IDs, create a second registry, edit shared contracts/DI/transports/migrations,
touch live SQLite, or claim unavailable workflows. Run focused catalog and runtime tests with a
disposable database and disabled providers. Before editing, report your exact file list to the
coordinator so it can be checked against concurrent lanes. Commit and report the exact
IDs/fingerprints, files, tests, real dependency status, unavailable paths, and blockers.
```

## Verification strategy

Workers run the smallest checks that exercise their changed boundary. For the website these are
package-local unit/contract tests, TypeScript checking, and a production build under Node 22.13 or
newer with package-local dependencies. For catalog work these are the exact catalog validator,
candidate/runtime tests, and `roleplay validate catalog` against a disposable database. Generic
endpoint tests are run only when the slice uses or changes those endpoints.

Representative focused commands, narrowed further when a slice permits, are:

```powershell
Push-Location src/system/web-interface/dnd2024
npm ci
npm run verify
Pop-Location

dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore `
  --filter "FullyQualifiedName~ApplicationReadModelWebEndpointTests|FullyQualifiedName~ApplicationObjectWriteWebEndpointTests|FullyQualifiedName~CompositionPageBindingCoordinatorTests|FullyQualifiedName~CompositionPageBindingActualIntegrationTests"

dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore `
  --filter "FullyQualifiedName~CatalogValidationTests|FullyQualifiedName~Dnd2024CanonicalCatalogTests|FullyQualifiedName~ApplicationActivationTests|FullyQualifiedName~ApplicationActivationRecoveryTests|FullyQualifiedName~InteractionManualContextServiceTests|FullyQualifiedName~ProcedureIntentAssociationPreparationTests"

dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj --no-restore -- validate catalog
```

The coordinator runs one final acceptance after all integrated slices:

1. Install locked website dependencies with the supported Node runtime, then run the D&D package's
   `verify` command, which includes its production build.
2. Run focused generic tests for application discovery, page publication, composition bindings,
   read models, object writes, authorization, candidate activation, recovery, manuals, intent,
   and any real worker/schedule/observer records added by the work.
3. Run `roleplay validate catalog` with a disposable database and run the solution's full test suite
   once on the final integrated commit.
4. Launch through `scripts/start-platform-worktree.ps1` with a disposable database, disposable
   tests, and providers disabled. When Slice 0 has supplied a disposable D&D source/application
   fixture, register, preview, validate, activate, and recover it through real platform dependencies.
   Without that fixture, report these scenarios unavailable and do not replace them with test data
   that bypasses registration or activation.
5. Exercise the website in a browser: multiple authorized applications, hidden nonwebsite packages,
   direct routes, the shared `system`/`light`/`dark` preference, extra props, missing required props,
   a malformed sibling, query success/denial/staleness/recovery, and the campaign-premise write's confirmation,
   idempotency, audit, and readback.
6. Run the protocol walk only if MCP transport or dependency registration changed.

The final command gate includes exactly one solution-wide run and the worktree-safe host launcher:

```powershell
dotnet test DantesRoleplay.slnx --no-restore
powershell -ExecutionPolicy Bypass -File scripts/start-platform-worktree.ps1
```

The final report distinguishes passing real-dependency scenarios from unavailable paths. A mocked
provider, fake scheduler, synthetic observer, or test-only activation does not satisfy acceptance.

## Live migration and recovery

Before touching a live campaign, export the registered D&D application, source registration,
selected activation, namespace reviews, page publication, state-space bindings, and every catalog
or state record that the slice will edit. Create a file-level SQLite backup and verify that it can
be opened independently. Record fingerprints and revision identifiers in the task output.

Rehearse the exact change against a copy or disposable database first. A presentation-only change
publishes a new page/bundle revision and retains the previous revision for immediate reselection.
A compatible catalog change enters as a candidate, proves complete pinned closure and current
namespace review, activates transactionally, and retains the previous activation for recovery.

If state shape or meaning changes, add a new immutable schema version and a bounded migration that
can validate preconditions, transform in one transaction, audit affected rows, and roll back from
the backup. Never edit stored JSON in place without a version boundary. After activation, compare
record counts and fingerprints, run representative reads and writes, and verify event/audit
continuity before declaring the live cutover complete.

Recovery selects the previous page or application activation when no state migration occurred. A
migrated state rollback restores the verified database backup and corresponding authored revision
together. Do not mix old state with a new schema or restore catalog files without restoring their
registered activation.

## Known unavailable paths and open evidence

- Repository files do not reveal which D&D application/source revisions are registered in a live
  database. Slice 0 must obtain that evidence before live edits.
- No concrete D&D declarative workflow, durable schedule, observer, or inner-worker fixture exists
  yet. Those capabilities remain unavailable until a named use case defines inputs, effects,
  retries, and recovery.
- Any dependency assigned to platform workstream 01 or 03 that is absent from the integrated
  baseline remains unavailable. The D&D lane does not emulate it or accept a test double in its
  place.
- The intended fixes for many D&D rules and content records are unspecified. Each future repair
  needs a failing fixture and expected outcome; this plan does not infer the whole rulebook.
- The current planning environment had Node 20.3 and global TypeScript 5.1.6, while the D&D package
  requires Node 22.13 or newer and TypeScript 5.9. The observed import-attribute typecheck failure is
  an environment prerequisite, not evidence of a source defect. Website acceptance waits for the
  supported package-local toolchain.
- External AI providers are unnecessary for the planned website and catalog checks and remain
  disabled. Any later provider-dependent behavior stays unavailable until exercised with its real
  dependency in an explicitly authorized environment.

Workstream 04 owns the current website implementation. Slices 0 through 5 are its outcome checklist
and must be reconciled against that lane's delivery rather than rerun as a second implementation.
Slices 6 through 8 are future catalog and game repair work gated by real registrations, reviewed
fixtures, and approved identifiers. Completion of the current website lane must not be reported as
completion of D&D 2024 mechanic or content coverage.
