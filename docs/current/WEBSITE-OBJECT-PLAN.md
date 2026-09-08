# Website audit and object-backed application plan

The follow-up [Website reassessment and remediation plan](WEBSITE-REMEDIATION-PLAN.md) evaluates W00–W11 against the implementation and live public website. It records reopened acceptance gaps, the user's shared-website access requirement, and repair slices R00–R14. Read it for subsequent remediation; this original document retains the initial migration design.

## Decision and scope

Use registered application objects consistently across the D&D application and website. Keep the existing React UI and generic C# infrastructure; migrate one complete feature at a time. The largest opportunity is replacing browser-side record assembly with bounded, authorized queries—not replacing React or creating another database.

This is the user-requested audit and proposed implementation plan, dated 2026-09-08, against checkout `c2007c03`. No implementation slice, database synchronization, application activation, or website publication was performed for this audit. Proposed object names below are conceptual boundaries, not newly registered permanent IDs. This document does not extend the unattended authorization granted specifically to `SYSTEM-AUDIT.md`.

The intended outcome is a working, easier-to-use website whose components and game mechanics share well-defined data contracts, without making browser state authoritative or moving D&D rules into C#.

## Evidence and limits

- Source inspection covered the website entry point, hub, feature components, data clients, legacy loaders, registered objects and queries, object read/write infrastructure, mechanic object inputs, activation source verification, and release verification tooling.
- The current catalog contains **14 object version documents representing 10 distinct object IDs**, and 21 query documents. Objects already cover Campaign summary, Factions, Character dossier records, Item instance/definition/recipe/activity records, and Rest inputs. This is partial adoption, not an absent object system.
- The website already has **73 component `.tsx` files**. Important concentration points remain: `game-server-context.js` (2,641 lines), `connected-hub-envelope.ts` (1,429), `DndInformationHub.tsx` (793), `hub-types.ts` (1,095), and `styles.css` (6,056). Line counts identify ownership pressure, not bugs by themselves.
- Checks rerun for this audit: **304/304 Node tests**, **90/90 mounted React tests**, and **TypeScript check passed**, using Node 24.19. No full .NET suite, production build, or live acceptance run was performed for this document-only audit.
- The browser could not connect to `https://localhost:5144/ui/dnd2024-play` (`ERR_CONNECTION_REFUSED`); no `DantesRoleplay.MCPServer` process was present when checked. Consequently this is not a completed visual, responsive-layout, or live interaction audit.
- Earlier diagnostics in this task observed a separate deployment problem: application readiness returned `SOURCE_FILE_DRIFT`, and served page revision 54 used an older bundle than the checkout build. Restarting the host did not reconcile the active catalog. These are earlier observations to recheck, not a claim about a currently running server.

Relevant commands executed from `src/system/web-interface/dnd2024`, using the explicit Node 24 executable:

```text
node --test "test/*.test.js"
node --import ./test/support/register-css-module-loader.mjs --import tsx --test "test/mounted/*.test.tsx"
node node_modules/typescript/bin/tsc --noEmit
```

## Audit findings

| Priority | Finding and evidence | Required change |
| --- | --- | --- |
| P0 | A successful build or restart does not establish a usable release. The [activated catalog provider](../../src/system/catalog-navigation/persistence/ActivatedApplicationCatalogProvider.cs) rereads allowed source files and rejects changed length/hash with `SOURCE_FILE_DRIFT` (around line 393). One changed active source can make the application catalog unavailable. Website revisions are activated separately. | Recover a matched host/catalog/state/bundle release first. Use a stable release source directory instead of pointing the running activation at files being edited. Verify readiness, exact served bytes, and real interactions together. |
| P1 | Deferred screens still assemble data in the browser. [The loader](../../src/system/web-interface/dnd2024/src/server/game-server-context.js) scans entity directories for context selection (1039), locations (1132), people/holdings (1772), and campaign structure (1919). Location loading issues separate containment/component/anchor/media reads per location. Some directory scans allow up to 100,000 candidates; the deferred section guard permits up to 2,000 requests. These are safety ceilings, not efficiency targets. | Replace each production path with a scoped query and bounded pages. Do not count deferred fan-out as removed work, or hide it behind a single HTTP call that still scans everything in C#. |
| P1 | Some relationships are inferred from identifier spelling. Examples include `campaignWorldId`, `campaignChildType`, world directory prefixes, and `isLocationEntity` in the same loader. | Resolve campaign/world membership and record kinds from existing declared components, relationships, and application mappings. If the model truly lacks a needed relation, propose that specific schema/data migration rather than preserving browser naming assumptions. |
| P1 | Data passes through several overlapping forms: connected server envelope, converted hub envelope, cached source envelope, and feature state. [The host entry point](../../src/system/web-interface/dnd2024/src/server-host/main.tsx) keeps `characterSources`, merges deferred patches, then reconverts them; [the hub](../../src/system/web-interface/dnd2024/src/components/DndInformationHub.tsx) coordinates feature loading and selection. Existing cancellation tests help, but extending this arrangement increases coupling. | Give each resource a typed query/cache owner. Keep only scope and navigation in the shell; let features consume independent resource results. Remove old adapters as their last production consumers migrate. |
| P1 | Object adoption is narrow. [BrowserObjectQueryState](../../src/system/web-interface/dnd2024/src/data/browser-object-state.ts) owns Campaign and Factions only. Character and Item also use registered record objects within mechanic projections, while World, Party, and Inventory do not yet have the proposed reusable feature contracts. | Extend the established object/query model through vertical migrations. Distinguish shared structural records from audience-specific read models and complete mechanic inputs. |
| P1 | Safe generic writes exist, but ordinary campaign/character/inventory views are mostly read-only. [Object write transport](../../DantesRoleplay.MCPServer/ApplicationReadModelWebEndpoint.cs) currently requires a GM seat and an active object-projection query with a collection. [The write service](../../src/system/projection-materialization/persistence/ApplicationObjectWriteService.cs) already supports declared paths, revision checking, idempotency, typed effects, and committed output. Browser edit reducer states do not constitute an integrated editing feature. | Add a complete, narrow editing workflow, starting with an already-declared Campaign field. Do not assume arbitrary object roots or Player writes are supported. Keep game actions on mechanic execution paths. |
| P2 | Shared browser infrastructure is not yet a general multi-component resource layer. [ViewReadClient](../../src/system/web-interface/dnd2024/src/data/view-read-client.ts) deliberately has one active request per instance, so every new load aborts its previous load. [Object consumer routing](../../src/system/web-interface/dnd2024/src/data/scoped-change-stream.ts) is a hand-maintained list. | Preserve current safety properties while introducing per-resource in-flight sharing, subscriptions, bounded retention, and explicit dependencies. Do not reuse one single-active client for several independently loading widgets. This is an extensibility issue, not evidence of a current cross-audience leak. |
| P2 | Navigation is only partly addressable: Item/Inventory already use history-aware routes, while many hub selections are local state. [MainNavigation](../../src/system/web-interface/dnd2024/src/components/MainNavigation.tsx) also displays a chapter progress bar at a hard-coded 100%. | Extend stable routes and Back/Forward behavior across features. Preserve selection/filter/scroll context. Remove the progress bar unless an authoritative, meaningful progress value exists. |
| P2 | Components and feature-level lazy loading already exist, but large shared styles and orchestration files make changes difficult to isolate. Character styles correctly load through `ItemWorkspaceFeature`, which also renders Party; their removal from the global entry is not itself a missing-style defect. | Extract by feature ownership, reuse existing components, and verify direct navigation to every lazy feature. Keep shared design tokens and primitives small; avoid a wholesale visual rewrite without live usability evidence. |

## Target model: objects are contracts, not duplicate state

The relevant layers should remain distinct:

```text
Live SQLite: entities, components, containment, relationships, operations
                         |
          Generic authorization + registered object mappings
                         |
       +-----------------+--------------------+
       |                                      |
Bounded audience-specific queries     Complete authorized mechanic inputs
       |                                      |
Typed browser resource cache          Catalog JavaScript rules
       |                                      |
Reusable React feature components     Typed effects + transaction + audit
       |                                      |
       +-- edit intent / game action ----------+
                         |
            Committed result + change notification
```

- **SQLite remains live game-state authority.** The development catalog owns authored schemas, mappings, fixtures, and JavaScript. Export live edits before editing those same file-backed records.
- **A Party object need not create a Party entity.** The existing campaign participation structure may already express the roster. Introduce a persisted party identity only if independent parties, shared ownership, or party lifecycle genuinely require it.
- **A World object must not contain the whole world.** Use a small summary plus separately requested location, map, people, faction, and knowledge pages.
- **A Character object must not eagerly contain every detail.** Share stable identity/record contracts, then request overview, calculated sheet, inventory, and knowledge according to the screen or mechanic's needs.
- **An Inventory object is usually a view of ownership/containment and item state**, not a second mutable inventory JSON document. Keep item instances distinct from catalog item definitions. Shared party storage requires explicit ownership/access semantics, not an assumed browser convention.
- **Not everything belongs in an object projection.** Keep existing blob/media delivery, operation history, change streams, rule search, and conversation protocols where their owners already fit. Compose their results at a feature boundary when necessary.

### Proposed object and query boundaries

Reuse existing IDs and queries where their meanings fit. Create new versions for changed contracts; do not rewrite immutable historical versions. The following labels describe responsibilities, not final IDs.

| Boundary | Reuse and authoritative inputs | Website consumer | Mechanic reuse and exclusions |
| --- | --- | --- | --- |
| Campaign summary / details | Existing Campaign summary v3; campaign root, participations, and existing campaign continuity records | Campaign header, overview, chapter/session/pursuit pages | Small campaign context for actions; do not include all history or every character sheet |
| Party roster | Existing Campaign participation graph, participant status, permitted actor references; extract/share only where it removes real duplication | Party list, character picker, campaign roster editor | Complete eligible-member context when an action needs the party; never treat a UI page as the complete roster |
| World summary / context directory | Existing world and campaign records plus verified mappings; explicitly assess gaps in world membership | World/campaign picker and overview | World identity, clock, and policy contexts only as declared |
| Location / map views | Existing location, containment, map hierarchy, anchor, and authorized media contracts | Location detail, map canvas, breadcrumbs, map/list alternative | Location/route context for movement; preserve coordinate-space and exact-reference validation |
| Character summary / sheet | Existing Character dossier records and sheet/dossier queries; character components and declared references | Roster summary, overview, sheet, features | Reusable records plus calculated projections; HP, derived AC, eligibility, etc. remain rule-owned |
| Inventory / container contents | Existing containment, item instance and definition-link records, quantity/equipment state, inventory authorization | Inventory tree, wallet, container detail, item selector | Complete bounded item subset required by the action; never calculate costs or transfers from an incomplete visible page |
| Item details / recipes / uses | Existing Item objects, actor-scoped queries, known recipes/activities, approved media | Keep existing Item workspace and three-tab interface | Reuse exact records and selected-item context; preserve discovery and hidden-property protections |
| People / factions / knowledge | Existing Faction page object, knowledge policy, world records; separate DM directory from Actor-visible knowledge | People/Factions/Lore lists and detail panes | Explicit authorized social/faction context; do not turn DM records into Player objects by hiding controls |
| Current scene / play context | Existing current-scene, encounter-board, conversation, resume, and related queries | Current View, play panel, board workshop | Reuse mechanic-owned scene context; keep board proposals distinct from accepted game state |

### Capabilities to check before extending the host

The engine already provides nested exact-version object references, bounded relationship collections, source-bound continuation, declared reverse mappings, and committed change notifications. Do not build a parallel repository/ORM or a C# `DndCharacter` domain model.

There are important limits:

1. [Snapshot object materialization](../../src/system/application-execution/persistence/ApplicationMechanicSnapshotObjects.cs) uses only the previously authorized snapshot and rejects objects with relationships or collections. A graph-shaped World/Inventory object cannot simply be inserted into that path. Start with its existing authorized record subsets; add generic snapshot capabilities only when a tested requirement demonstrates the need.
2. [Object-based reducers](../../src/system/application-execution/persistence/ApplicationMechanicObjectProjectionResolver.cs) support exact registered object roles and reject incomplete collections. Rest begin is an existing D&D example. UI pagination must not weaken this completeness rule.
3. Registered relationship collections do not establish that recursive containment paging is already supported in the required form. Inventory design must verify the existing `includeContents` snapshot path and collection materializer before choosing an implementation. Any extension belongs in the generic owner, with cycles, depth, completeness, revisions, and authorization tested.
4. Current object HTTP writes are GM-only and collection-query based. Singleton edits or Player editing require an explicit authorization/transport design; do not expose them incidentally during component extraction.
5. A structural source object and a computed rule result are not interchangeable. Share input contracts; retain the JavaScript query/reducer that owns derived values and outcome decisions.

## Website component and data design

Use the existing React/TypeScript/Vite setup. Adopt a feature structure incrementally, moving code when a migrated feature gives it a clear owner:

```text
src/
  app/                 scope, navigation, shell, readiness/error boundary
  data/                transport, contract validation, resource cache, change stream
  components/shared/   existing reusable fields, panels, media, lists, async states
  features/
    campaign/          resource hooks, feature container, components, tests, styles
    party/
    character/
    inventory/
    items/
    world/
    play/
```

This is an ownership direction, not a mandatory mass rename. Existing MapCanvas, CharacterShell, CharacterSheet, InventoryTree, Item workspace, and campaign components should be retained unless a specific defect justifies replacement.

### Resource ownership

- Scope includes application, state space, authorized seat/observer, perspective, campaign, and binding lifetime. A resource key also includes query/object version, resolution fingerprint, root/role IDs, normalized input, and continuation where relevant.
- Read this identity from server-issued context. A route selects within authorization; it does not confer permission.
- Keep one bounded in-memory resource owner with per-key in-flight deduplication. Independent widgets must not cancel each other; scope replacement must cancel all old reads and fence late completions. A consumer leaving must not cancel a shared read still needed by another consumer.
- Validate responses at the transport boundary. Reuse catalog schemas and generate compatible TypeScript types/validators where practical. Keep small presentation adapters local to a feature; do not duplicate rule calculations in those adapters.
- Preserve server source-revision evidence separately from any client content hash. A browser hash is not an optimistic-concurrency token. Profile redundant serialization before removing it.
- Store navigation preferences and unfinished drafts separately from server results. Do not persist private response bodies to local storage or turn a normalized browser store into game-state authority.
- Refresh only affected visible resources when a trusted notice supplies sufficient scope. Current notices may identify an object type rather than an entity; invalidate conservatively within the authorized scope unless the protocol explicitly supports finer targeting. Unknown notices, reconnect, and lost history must retain broad recovery.
- Keep empty, unloaded, loading, stale, forbidden, incompatible, and transport-error states distinct. Missing authorization or incomplete data is not an empty list.

### Components and usability

- The shell owns scope, navigation, and layout. Feature containers own resource selection and actions. Presentational components take explicit data/callback props and do not discover game state by fetching ECS records.
- Use reusable `AsyncPanel`, paged list, record picker, media panel, field editor, and save/conflict state patterns where existing components overlap. Rich map/sheet/item interfaces should remain purpose-built; do not reduce the entire website to generic JSON forms.
- Make World, Campaign, Party, Character, Inventory, and Item routes stable and preserve Back/Forward, selected container, search/filter, scroll, and focus. Reuse the existing Item return-context pattern.
- Load a selected character overview without unnecessarily loading unrelated inventory/recipe/activity detail. Preserve completeness for any totals shown; label unavailable totals rather than presenting partial sums.
- Keep the selected campaign, world, character, and Player/DM perspective visible. Explain that GM Player preview is not a real Actor seat; retain unavailable states where no legitimate observer exists.
- Provide local retry and clear recovery instructions for unavailable application/bundle versions. The website must never silently activate catalogs, import data, or grant broader access to repair itself.
- Preserve keyboard operation, focus after navigation/save/errors, accessible names, reduced-motion behavior, useful narrow-screen layouts, and a list alternative to the map. Verify these in a real browser, not only mounted tests.
- Keep feature styles with their lazy owner and shared tokens in one place. Test direct entry and error/retry for each lazy feature to catch missing CSS or chunks.

## Editing and mechanism authoring

Use two explicit paths:

**Declared field edits:** small, authorized record changes such as an already-declared Campaign premise edit. Send the exact resource identity, expected source revision, validated change set, and one idempotency key for the logical attempt. Apply only declared reverse mappings. Accept the committed/rematerialized result as truth. On a conflict, keep the user's draft and offer refresh/review; do not automatically overwrite another edit. Retrying an uncertain response uses the same idempotency key.

**Game actions:** equip, use, transfer, spend, rest, advance, and similar actions go through the existing prepare/execute mechanism workflow with declared object inputs and rule-owned validation. A writable mapping must not bypass equipment eligibility, inventory capacity, ownership, resource costs, or another game's invariant. Render only actions actually provided by the application; do not invent missing mechanics for the UI.

Campaign participation edits also need semantic review: an allowed relationship operation alone does not establish that all roster lifecycle rules are satisfied.

For easier mechanic development:

- Reuse exact structural object definitions and role bindings; keep each mechanic's declaration small and specific.
- Follow the existing Rest reducer and Character/Item snapshot integrations before introducing new abstractions.
- Produce a minimal copyable example and focused fixture in the owning catalog/test locations, showing declared inputs, `ctx.objects`, validation, and typed effects.
- Test old/new read outputs or action effects for parity, missing/optional records, source conflicts, transaction rollback, and deterministic execution. Exclude intentional behavior changes from a structural migration unless separately agreed.
- Never let a mechanic fetch arbitrary browser state, assume a UI page is complete, or read an undeclared wider graph.

## Implementation slices

Implement in order, with one coherent commit per slice and a commit body containing its changelog, verification, and deliberate exclusions. A slice is not complete just because contracts or replacement components exist: its intended production path must use them, with superseded reads removed or explicitly retained for unmigrated features.

### W00 — Recover and establish a trustworthy live baseline

- Recheck the actual listener, process/build, readiness, bound campaign, active application/extension resolution, state-space compatibility, and served page/assets.
- Back up the live database using supported tooling. Export and review live-only or live-edited records before any overlapping catalog import. Earlier reconciliation found live differences; rerun the comparison instead of assuming that old list is exhaustive.
- Validate the reviewed release in an isolated runtime/database copy. Separate authored catalog activation, state migration/import, and website publication; none is a substitute for the others.
- Activate compatible sources and publish the matching website through existing workflows. Prefer a stable versioned source directory using existing allowed-root/source registration facilities. Keep the previous source directory and page revision available for rollback; do not weaken hash verification.
- Use the existing [release manifest](../../src/system/web-interface/dnd2024/scripts/create-release-manifest.mjs) and [live verifier](../../src/system/web-interface/dnd2024/scripts/verify-live-release.mjs), including runtime and browser evidence.
- **Acceptance:** real DM and Actor journeys work; GM Player preview stays correctly restricted; readiness and served bytes match the reviewed release. No runtime mutation is authorized merely by this planning document.

### W01 — Freeze feature contracts and acceptance fixtures

- Inventory each visible feature's current query, browser assembly, authority, audience, edit capability, and complete-workload cost. Include installed-content/rules and board routes so they do not regress, without redesigning their protocols.
- Finalize the object boundaries above, selecting existing owners and declaring any genuinely missing relation or generic capability. Resolve whether a distinct party identity is actually needed.
- Define version changes, compatibility order, bounded page sizes, and migration requirements before registering new IDs. Define small/large/unauthorized fixtures and per-feature request, SQL, byte, and memory gates.
- **Acceptance:** every planned screen/action has an authoritative owner and explicit access/completeness contract; no duplicate game-state store is proposed as a shortcut.

### W02 — Establish shared resource and component infrastructure

- Extract shell scope/navigation from data loading; build the per-key resource layer and shared async/edit states by migrating the existing Campaign/Factions clients first.
- Preserve bounded cache behavior, cancellation, exact evidence validation, object notices, reconnect recovery, and page revision checks. Add tests for simultaneous consumers, separate resources, old-scope responses, and failed refreshes.
- **Acceptance:** the production Campaign/Factions paths use the new owner, existing behavior remains intact, and obsolete client wrappers for those paths are removed. This slice must not also redesign the domain.

### W03 — Campaign and Party

- Reuse Campaign summary v3 and extract/share Party roster contracts where useful. Replace Campaign detail and campaign/world selector directory assembly with authorized queries.
- Keep roster summaries cheap, character detail deferred, and participation status explicit. Do not broaden Player visibility beyond the existing contract.
- Add addressable Campaign/Party navigation and remove misleading progress presentation.
- **Acceptance:** 0/1/3/20-member cases, withdrawn participants, actor restrictions, navigation return, source changes, and context switches pass; roster discovery does not grow one request per member. Remove the migrated campaign/context directory reads.

### W04 — Character resources and components

- Split resource delivery by overview/sheet/detail need while reusing dossier record objects and existing calculations. Keep the current full dossier query for callers that legitimately need it.
- Move selected-character loading and state to the Character feature; use shared identity/summary components in Party and selectors.
- **Acceptance:** exact output parity, no new browser rule calculations, scoped media, same-character cache reuse, fast character switching, unavailable/forbidden states, and focused accessibility tests pass. Remove migrated source-envelope conversion branches.

### W05 — Inventory and containers

- Introduce the bounded owner/container contract using existing containment and Item objects. Resolve containment materialization capability explicitly; do not fake inventory paging by slicing a fully loaded tree in the browser.
- Reuse InventoryTree, wallet, Item navigation, and existing Details/Recipes/Uses clients. Keep container expansion, search, totals, and incomplete states coherent.
- **Acceptance:** nested containers, cycles/invalid ownership, depth/size bounds, transfer-induced invalidation, unknown items, and return-to-inventory focus pass. Opening inventory must not eagerly request every item's recipes or uses. New gameplay actions remain outside this read migration.

### W06 — World, locations, and maps

- Add small world/location queries and authorized map projections. Remove ID-spelling membership assumptions and per-location hydration from the production World/Locations path.
- Preserve existing map hierarchy, declared coordinate spaces, exact links, overlays, media authorization, and list navigation. Load visible pages/scopes, not the entire world.
- **Acceptance:** renamed/non-conventional IDs, missing map links, large unrelated catalog data, nested scopes, DM/Actor visibility, direct routes, keyboard/list access, and narrow layouts pass; migrated screens make no raw world-directory scan.

### W07 — People, Factions, Lore, and history

- Keep the registered Faction page path; replace remaining People/Holdings assembly and media fan-out with bounded queries or the appropriate existing read owner.
- Reuse knowledge/chronology authorization and continuation semantics. Separate public/known information from DM-only directories and authoring views.
- **Acceptance:** large pages, stale continuation, empty versus denied, unknown knowledge, restricted media, and observer changes pass. Remove superseded People/Lore/history adapter branches where replacement coverage is complete.

### W08 — Current View and play composition

- Compose existing scene, board, conversation, resume, and action-query results behind typed feature resources. Replace legacy browser reconstruction of encounter/conversation/route state where an authoritative query already exists.
- Keep proposal/review/accept boundaries and operation history. Share object context with existing mechanic declarations where it fits without changing outcomes.
- **Acceptance:** exploration/conversation/combat transitions, no-current-scene states, exact participant/turn data, Player restrictions, board review, and late-response rejection pass. No new browser rule engine remains.

### W09 — First complete mapped-edit workflow

- Deliver an existing permitted Campaign premise edit end-to-end: readable field, draft, validation, submit, pending state, committed result, and conflict recovery.
- Reuse the existing GM write boundary. Explicitly design any later singleton or Player transport extension; do not bundle it into this first edit.
- Add only already-supported game action controls through mechanic execution, if they are included in the approved slice boundary.
- **Acceptance:** stale revision, duplicate submission, uncertain-response retry, no-op, rejection, rollback, unauthorized actor, and notification/refetch behavior pass. Test the actual HTTP route and production UI together, not only the write service/reducer.

### W10 — Expand shared mechanic inputs

- Select one additional existing mechanic with repeated Character/Inventory/Party/World assembly, based on W01 evidence. Migrate it to exact object inputs, using the proven Rest and snapshot examples.
- Add the small authoring example and parity tests. Extend a generic host capability only if the selected mechanic cannot safely use the current contracts; separate any rule behavior change.
- **Acceptance:** same deterministic results/effects, declared complete context, no widened audience reads, preserved optimistic concurrency, and measured context-size/cost improvement or an explicitly justified maintainability benefit. Do not claim all D&D mechanics are migrated from this pilot; list further candidates by demonstrated value.

### W11 — Remove obsolete paths and accept the delivered release

- Delete only adapters, duplicate types/styles, caches, and tests whose production consumers have migrated. Retain legitimate generic endpoints and other callers; do not remove them just because the website stopped using them.
- Run feature acceptance, catalog validation, the full suite, production build, and protocol walk if the MCP surface or dependency registration changed. Measure the complete workload and verify the real browser, not only fixture mounts.
- Deploy the matched release using the W00 process, verify exact manifest/runtime/source/state/page identity, and perform the final live usability/authorization journeys.
- **Acceptance:** no unexplained legacy reads in migrated paths; budgets met; previous compatible release is recoverable; actual served UI works. Report deliberate remaining mechanic/authoring exclusions. A local commit or passing test suite alone is not final release acceptance.

## Measurement and release gates

Measure before/after against the same runtime identity, data fixture, audience, machine/browser, and visible journey. Use the existing baseline tooling; do not claim improvement from source size or isolated microbenchmarks alone.

- Record cold and warm startup separately from the full navigation workload. Include mandatory lazy chunks in first-ready cost and all deferred reads in the complete-workload total.
- Cover Campaign, Party, Character, Inventory, Item tabs, World/map, People/Factions/Lore/history, Current View, and scope switches. Keep DM, real Actor, and GM Player preview as separate results.
- Use at least 20 valid paired samples for reported latency percentiles, with p50/p95, sample count, failures, HTTP/SQL counts, response bytes, materialized records, and retained cache size. An unavailable screen is failure evidence, never a fast successful sample.
- Preserve existing stricter regression gates. Campaign summary already declares a 12-SQL limit; a new query must declare its own bound rather than inheriting a blanket allowance. Proposed page sizes and whole-workload budgets become explicit in W01, not invented performance claims in this audit.
- Test scaling with added unrelated entities and larger worlds/inventories. Bounded output alone is insufficient: ensure selection happens before expensive hydration and that query plans do not scan the full catalog per visible page.
- Require zero unexpected raw directory/record fan-out from each migrated website path. Bounded media transport and intentional change streams must be counted or separately identified, not silently excluded.

Release compatibility must cover host build, application/extension activation, source fingerprints, state-space schema/mapping, and website/query contract versions. If old and new bundles cannot coexist with the same query contracts, use a controlled cutover instead of assuming separate activations are atomic. Retain compatible historical query versions when useful. A rollback involving changed live state requires a tested data recovery procedure; do not restore a backup over newer gameplay without an explicit decision about those changes.

## Completion definition

The plan is fulfilled when the agreed website features run through their intended object/query owners, the components are independently reusable and testable, the selected mechanic reuse is proven, and a compatible release is verified on the actual server. The result must preserve live data, authorization, rule ownership, change recovery, and user workflows.

Not included by default: a new persistence framework; new independent party identities; a visual redesign; a generic all-purpose JSON editor; conversion of every D&D mechanic; new rule behavior; unrestricted Player authoring; or publishing changes during an audit-only request.
