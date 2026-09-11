# Field-based website reads and schema-safe ECS writes

Status: implementation and acceptance are in progress. All thirteen structural object identities have append-only v2-profile definitions and migrated exact consumers, with legacy registrations retained. Their earlier release is selected only in the isolated restored game copy, not the live runtime. Parameterized object reads/submission, scoped component events, registered party knowledge, interrupted-request recovery, and the remaining neutral History/Current migration are being integrated and verified in the checkout. Combined release, gameplay-change, restart, rollback and cleanup acceptance remain open. The live runtime remains on browser 75 and its prior host/catalog. Historical slice paragraphs below record earlier boundaries, not current whole-plan acceptance; current test results belong in the task, not cached totals here.

Current boundary: shared display reads retain mandatory scope, subject, transport, body-size and source-freshness checks without output-schema equality. Confirmed Character, Item, Current, Rules, Installed Content and Table projections have Redux owners; Table covers Campaign, World, Map, History/Lore and deferred collections. In-flight coordinators do not retain completed responses. Bounded raw connected-source inputs now live in the same Hub Redux store; their coordinator retains only leases and request metadata. These projection inputs still overlap some derived values and are not a canonical ECS store. Campaign descriptions and collection facets, location/person/faction fields, chronology and notebook neighbors degrade independently with explicit partial coverage. Narrow selection/action/geometry prerequisites remain strict. Portrait absence, denial and temporary unavailability stay distinct. Shared control/AI recovery has focused coverage, including GET-only interrupted-request lookup and exact principal/provider/context binding; combined deployed action/recovery acceptance remains outstanding. No live game import, release cutover or destructive cleanup has occurred.

## 1. Outcome and binding design decisions

The website must consume the fields it uses from authorized objects, independently of the complete shape or schema fingerprint of those objects. Adding an inventory item, changing quantity/equipment, attaching media, or adding an unrelated component/field must not require a website deployment. A display component must not refuse valid fields because a different field is missing, unfamiliar, or invalid.

ECS components remain schema-owned, versioned, and validated by the server. AI and human writes remain authorized, mapped to declared destinations, concurrency-checked, and transactional. A tolerant reader must not become a permissive writer.

These decisions are the intended implementation boundary, not optional suggestions:

1. **No complete domain-object schema gate in the browser.** Remove compile-time output-schema fingerprint equality and whole-object exact-key validation as display acceptance conditions. Do not replace them with a list of accepted schema versions.
2. **Structural objects are compositions, not another game-state schema authority.** A structural object that copies fields/components or follows declared relationships should not require a second hand-authored value schema duplicating its sources. Keep and validate its composition/access/write metadata.
3. **Component schemas and write validation stay strict.** Validate the resulting complete component, not just a patch fragment. Preserve entity/relationship invariants and cross-record rule checks.
4. **Computed results are not the same as copied components.** Catalog JavaScript still owns calculations and rule decisions. Their producing mechanics retain appropriate input/result/effect validation, since computed values have not necessarily passed component-storage validation. The browser still consumes those results field by field.
5. **Reads select current committed data within the authorized active application.** They do not activate the latest checkout, upgrade stored component schemas, select an unreviewed extension, or mix records from inconsistent snapshots.
6. **Writes never silently select a different destination or expected revision.** A stale edit conflicts and is refreshed/reviewed; it does not retry against the newest state automatically.
7. **One normalized, non-authoritative client data owner.** Redux Toolkit plus React Redux, with selectors and separately stored drafts/request state. Migrate existing owners rather than layering another authoritative cache over them. The local character-first implementation introduces these dependencies; it does not complete migration of the other resource families.
8. **No live bulk import to repair display compatibility.** Preserve the portrait and runtime-only game records. Read tolerance must be demonstrated against the existing running data before broader catalog changes are selected.
9. **The game website remains read-only for now.** Following the user's campaign-premise feedback, do not expose premise editing, combat-map upload/acceptance or other game-data mutation controls until explicitly approved. Keep server/MCP/AI write safeguards and their tests; real-change acceptance uses authorized server operations while the website observes. Retained editor fixtures are not permission to enable website writes. Navigation and local display preferences remain available; this does not change separate system administration surfaces.
10. **Player view represents combined party knowledge.** The user clarified this on 2026-09-11: DM sees all authorized campaign information; Player sees the union of knowledge held by the selected campaign's party members, not only an intersection or one chosen character. Resolve membership and knowledge through the authoritative server owners, retain fact identity/provenance, deduplicate shared facts, and exclude outsiders and information unknown to every member. Missing membership/knowledge coverage is not permission to reveal DM data or claim a complete empty result. Partition and invalidate client results by campaign, audience and party membership; restoring the view switch must not restore game-data editing or relax Actor authorization.
11. **Application data is exposed by registration through generic APIs.** The user explicitly rejected application-aware endpoints on 2026-09-11. Reuse the registered read-model/query route; D&D-specific object compositions, relationship vocabulary, eligibility and party-knowledge rules belong in catalog registrations and JavaScript, not HTTP handlers or C# branches. A missing reusable projection capability may require a reviewed generic declaration/runtime extension, but not a dedicated party-knowledge endpoint or an application-specific switch on the legacy notebook route. Preserve legacy private-notebook behavior while the registered party view is developed. Registration is not a grant: scope, audience, bounded reads, source consistency and strict ECS writes remain server-enforced.
12. **Parameterized registered objects and submitted-object diffs use generic vocabulary.** The user approved implementation on 2026-09-11, explicitly correcting that “campaign” is an application term. New query `roleBindings` map opaque declared roles from `route-entity`, validated `input` with a JSON pointer, or `authorized-context` with an opaque trusted key. No generic resolver compares role names to campaign, party, item or other application concepts. Preserve the existing generic GET/PATCH route; the new PATCH alternative is `mode: "object"` with `object`, mutually exclusive with legacy `changes`. Direct MCP discovery/read/submission use the approved generic `system.application-object` and `system.application-object.submit` kinds. Omitted fields preserve state; unchanged read-only fields are ignored; changed read-only fields reject; only declared set/clear destinations may produce effects. Exact writable object/array fields remain atomic. Keep expected sources, component schemas, permissions, idempotency and transactions. Existing mechanics remain a separate declared rule-execution path, not an inferred inverse for computed fields. This paragraph approves the design; it does not assert that its transports are delivered.
13. **Component subscriptions receive committed application-scoped changes.** The user approved extending the existing event pipeline with authoritative before/after component snapshots and immutable application/state-space source context. Legacy events/subscriptions retain their behavior; a legacy empty-scope wildcard must never match an application-state event. Application subscriptions opt into an exact source and use generic entity/component/type filters and registered mechanics. Capture changes transactionally, bound reaction chains, and publish only committed outcomes; no-op, rollback and replay must not deliver duplicate changes. Registered-object browser invalidation is a separate tested delivery surface and is not proof of catalog subscription execution. Do not add game-specific event logic or synthesize application rule outcomes in C#.
14. **Graph materialization and selection are declared, not inferred.** Approved graph snapshots name an opaque root role and bounded relationship/containment steps. The generic host reads one consistent snapshot, enforces aggregate limits, and freezes it before catalog JavaScript runs. A query's neutral `selection` declares a selector query, a target role, a result JSON pointer and explicit selector-role mappings. It may use only independently authorized parent entities. Selector chains are acyclic and bounded to four links; catalog validation and execution must enforce the same bound and recheck evidence at every link. The shared read service, not an individual transport, must recheck selector identity and source evidence around the main read so HTTP, planned queries, task context and direct object reads obey the same declaration. A changed selection must fail stale, never retarget silently. History visibility/calendar rules remain catalog JavaScript. The current source retires `campaignSelection` execution, role-name inference, campaign parameter aliases, and dedicated knowledge/chronology HTTP routes after migrating their active consumers. Retained descriptor serialization and legacy private knowledge/audience owners are historical/security compatibility, not a second active query path. Runtime selection must complete the verified cutover before this source boundary is considered deployed.
15. **Interrupted recovery never replays an unknown write automatically.** Persist only bounded, body-free request identity in session storage before submitting. After reload, recover by a read-only lookup bound to the original principal, scope, provider and exact source context. Missing, stale, malformed, denied or ambiguous evidence cannot authorize a new submission. Keep legacy read-only System retries compatible only when their original durable request identity can be proven; legacy AI history is not interchangeable with a newly captured request context.
16. **Large graph inputs are paged before the JavaScript sandbox.** A registration can declare a directed page step, validated cursor-input field and page size, with explicit endpoint-step filters for dependent branches. The host still reads a bounded consistent source and fingerprints all of it before selecting a page and its relationship/ancestor closure. Off-page changes therefore invalidate continuation; paging never weakens aggregate graph, sandbox memory or statement limits. Unpaged declarations retain their existing representation and fingerprints. Internal source totals may include unauthorized rows: catalog projections must not expose those totals or hidden identities to a filtered audience. Public counts describe admitted data only; an empty filtered page may still have a continuation. This source contract remains subject to the combined release gates below.

JavaScript/TypeScript permits dynamic property access; the current rigidity is explicitly programmed. Rewriting TypeScript into JavaScript, adding `any`, or turning validators into `return true` is not this plan.

## 2. Evidence and reproduction baseline

Inspected checkout: `083d68f2c9ac429bce2ac27efdd2cfbe50d1d33f` plus the existing uncommitted portrait repair and operations-document change. The implementation must start by refreshing this baseline, not resetting the checkout to it.

Read-only live probes at approximately 2026-09-10 20:22 UTC found:

| Observation | Evidence | Consequence |
| --- | --- | --- |
| Published page is revision 75 | `GET /api/control/web/applications/dnd2024/pages/web-page:dnd2024` | This is the failing release baseline, not proof of acceptance. |
| Running catalog is the frozen `r14-8ba97115-source`; host is `recovered-083d68f2-host` | Saved `DantesRoleplay.MCPServer/data/runtime-launch.json` | Editable checkout and active runtime are distinct authorities. Do not modify these release directories in place. |
| Inventory endpoint returns HTTP 200 and both Ganji items with quantity 1 | `GET /api/applications/dnd2024/state-spaces/dnd2024-main/entities/actor.caldris.ganji/read-models/dnd2024.query.inventory-container?perspective=dm` | No inventory rewrite is needed to make these fields display. |
| Live inventory rows have `classification`, `definition`, `equipmentSlots`, `id`, `name`, `order`, `quantity`, `slot`, but no `isContainer` | Same live response | Container disclosure is unavailable from this response, not evidence that the item list is unusable. |
| Live inventory schema hash is `5B8B70D02F040197B44C7957118639DE33C9F0B7E7D288729AC4D39A6BCCC086` | Same response | The response is valid under the active server's contract. |
| Browser expects `47C75C610B22D02B7518A9E3317E9712B344C1D58394363B75C9C6DEAB52426D` | `src/server/inventory-container-contract.js` and readback of page 75 assets | The generic reader rejects it before useful fields reach React. |
| Page 74 contains the live hash; page 75 contains the new hash | Read back immutable revisions 74 and 75 and searched decoded JavaScript assets | The portrait deployment also introduced an unrelated inventory compatibility regression. |
| Character-sheet endpoint returns Ganji successfully | Character-sheet-v2 endpoint, schema hash `415A6CCADA6DA4D44E623B451C11047EE6269C0FFEA9ABE679D3882F8F356A0C` | Sheet success and inventory failure are separate facts. |

The preceding portrait task reported 397 node tests and 156 mounted tests passing. Those were fixture-based checks plus a live portrait check, not a complete live character workflow. They did not establish inventory compatibility. Do not reuse those totals as acceptance evidence for this plan.

### Evidence index: exact owners and relevant symbols

Paths below are relative to the repository. `W` means `src/system/web-interface/dnd2024/`. Inspect the named implementation, not only its file name, before modifying it.

| ID | Owner / symbol inspected | Finding and required treatment |
| --- | --- | --- |
| E01 | `W/src/server/read-model-response.js`, `validateReadModelEnvelope`, `readModelResponse` | Requires exact envelope keys, exact `outputSchemaHash`, and a complete feature validator. Separate transport/scope checks from field consumption. |
| E02 | `W/src/server/game-server-context.js`, `validInventoryContainer`, `validCharacterSheetV2`, `validCharacterDossier`, `readCanonicalInventory` | Handwritten closed domain shapes; one absent `isContainer` rejects all inventory. Split independent fields/features. |
| E03 | `W/src/server/campaign-summary.js`, `validateRegisteredCampaignSummary`, `projectRegisteredPartyReferences` | Bootstrap rejects an entire summary/party for several unrelated shape failures. Keep authority binding strict; isolate display sections. |
| E04 | `W/src/server/item-read-response.ts`, item clients and generated item/encounter validators; `W/scripts/generate-validators.mjs` | The same coupling exists outside Character. Fix generation/import paths as well as individual checks. |
| E05 | `W/src/state.js`, `isCampaignReadModel`, map/history/lore validators; `W/src/server/connected-hub-envelope.ts` | Whole-view projection and validation can reintroduce rejection after transport is fixed. |
| E06 | `W/src/data/resource-store.ts`, `view-read-client.ts`, `object-resources.ts`, `resource-policy.ts` | Bounded custom caches, validator callbacks, contract-hash cache tokens, cancellation and scope handling. Preserve lifecycle protections; remove domain-schema admission. |
| E07 | `W/src/data/hub-object-ui.ts`, `W/src/components/DndInformationHub.tsx`, `W/package.json` | Uses a custom edit reducer and React `useReducer`; no Redux dependency. Do not describe the present implementation as a normalized Redux entity store. |
| E08 | `W/src/components/PartyView.tsx`, `CharacterSectionState.tsx`, `InventoryTree.tsx`, `CharacterIdentitySummary.tsx` | Independent UI components exist, but receive coarse read failures. InventoryTree already branches on container information; error presentation labels failures broadly as character incompatibility. |
| E09 | `src/system/projection-materialization/domain/ApplicationObjectContracts.cs`, `ApplicationObjectDocument.Parse` | Object documents require their own `schema` and exact component/object references, plus useful access, source, relationship, bound, and write declarations. Separate these concerns. |
| E10 | `.../persistence/ProjectionMaterializer.cs`, `Evaluate`, `Select`, `ValidateOutput` | Requires exact component type references; missing mapped paths can throw; structural output is revalidated against the object schema. This is a server-side coupling, not just a browser problem. |
| E11 | `.../persistence/ProjectionCollectionMaterializer.cs`, `ExpandAsync`, `SetDeclaredMetadata` | Expanded objects are schema-validated; pagination metadata placement depends on the output schema. Remove that hidden dependency when removing object value schemas. Preserve bounded traversal and snapshot/cursor behavior. |
| E12 | `.../persistence/SqliteProjectionDefinitionRegistry.cs`, `ValidateInputs`, `ValidateObjectContract`, `ValidateWrites` | Registration uses source/output schemas to validate paths and generate reverse writes. Do not delete schema fields without replacing path/provenance checks. |
| E13 | `.../persistence/ApplicationObjectWriteService.cs`, `WriteAsync` | Validates edit schema, allowed mappings and expected source fingerprint; copies changes into original components; submits complete source/entity/relationship expectations to typed effects. Preserve these protections. |
| E14 | `DantesRoleplay.DataAccess/Ecs/SqliteApplicationScopedEcsStore.cs`, `WriteWithinTransactionAsync` | Checks component ownership/type/revision; validates complete values, including merged results; disallows implicit in-place type-contract replacement. This remains a hard boundary. |
| E15 | `DantesRoleplay.DataAccess/Ecs/ApplicationEcsEffectApplier.cs`, `ApplyAsync` and expectation checks; `SqliteEcsRoleConstraintValidator.cs` | Checks observed components/entities/relationships/containments inside the write transaction, then validates entity constraints. Retain atomic rollback, audit, idempotency and change delivery. |
| E16 | `src/system/interaction-orchestration/hosting/ApplicationReadModelService.cs`, `ObjectProjectionInteractionQueryExecutor.cs` | Object output gets validated again against query output schemas. Mechanic queries separately validate calculations and forbid effects during reads. Split structural output checks from executable-result checks. |
| E17 | `src/system/application-execution/persistence/ApplicationMechanicObjectProjectionResolver.cs`, `ApplicationMechanicSnapshotObjects.cs` | AI reducers use registered object references and complete snapshot evidence; incomplete collections cannot drive a reducer. Flexible display must not weaken this execution boundary. |
| E18 | `DantesRoleplay.MCPServer/ApplicationReadModelWebEndpoint.cs`, `ReadAsync`, `WriteAsync` | Binds roles, application, campaign and perspective from trusted authority; mapped writes are not arbitrary component writes. Retain these gates. |
| E19 | `W/src/data/scoped-change-stream.ts`, `object-change.js`; projection change participant and dependency-index cache | Existing targeted invalidation plus conservative recovery. Extend to normalized records without losing unknown-change recovery. |
| E20 | `W/scripts/create-release-manifest.mjs`, `release-runtime-verification.mjs`, `verify-live-release.mjs`; runtime launcher | Existing exact artifact/runtime verification is valuable. It did not exercise all live UI consumers. Preserve integrity evidence while adding cross-shape behavior checks. |

A bounded authored-catalog inventory found **17 object documents** (including retained versions) and **31 query documents: 4 object-projection and 27 mechanic-projection**. Only campaign-summary versions 2 and 3 declare object writes in this set. Examples of read-only compositions include item instance/definition records; reducer inputs include rest-begin and carrying-capacity objects. Therefore, “objects just update entities” is not a complete description: many objects are read-only, and some are inputs to rule execution.

Refresh these counts during implementation. Enumerate overlays/extensions and all registered-object callers too; the counts above are the inspected D&D directory, not a claim that every system capability was audited exhaustively.

### Query and object coverage ledger

All query names below have the existing `dnd2024.query.` prefix. They were enumerated from actual query documents, not inferred from visible tabs. Every entry must be carried through slice 8 even if it has no direct React screen today. “Mechanic” describes the executor, not proof that every field is calculated: inspect its JavaScript and classify copied versus computed fields before removing producer checks.

| Query suffix(es) | Executor | Read/field boundary to cover |
| --- | --- | --- |
| `actor-context` | Mechanic | Actor/participation binding, identity/presence, HP/conditions independently; actor authorization remains mandatory. |
| `campaign-context` | Mechanic | Campaign/world identities and their display fields independently. |
| `campaign-details` | Mechanic | Chapters, arcs and sessions separately. |
| `campaign-location-visits` | Object | Visits plus explicit pagination metadata. |
| `campaign-resume` | Mechanic | Party, scene, current arc/chapter/session and recap independently; action affordances remain authoritative. |
| `campaign-summary` | Object | Title/premise/goals/tone separately from party identity and metadata; preserve the mapped premise write. |
| `current-scene` | Mechanic | Scene kind, location and conversation/encounter links; unknown kind gets a safe read-only state. |
| `recent-consequences` | Mechanic | Ended sessions, closed chapters, resolved arcs and visits independently, including AI context consumption. |
| `world-campaign-directory` | Object | Authorized selected-world/campaign identity and directory pages. |
| `character-dossier-v1` | Mechanic | Sheet, origin, classes, features, inventory and reference text independently. |
| `character-sheet-v2`, `character-sheet` | Mechanic | Retained and current sheets; primitive/source selectors must not duplicate game calculations. |
| `downtime-status`, `rest-status` | Mechanic | State/progress display independent from validated requirements/completion/next actions. |
| `inventory-container` | Mechanic | Identity rows/quantity/equipment/container capability plus honest completeness. |
| `inventory-wallet` | Mechanic | Denominations and calculated totals; missing totals are not zero. |
| `encounter-board-draft` | Mechanic | Draft display tolerates unrelated metadata; expected board revision and subsequent write commands remain strict. |
| `encounter-board` | Mechanic | Encounter identity, valid geometry, participants and turn dependencies; isolate unsafe board features. |
| `inventory-item-details` | Mechanic | Name, description, properties, media and observer knowledge independently; preserve observer/item binding. |
| `inventory-item-recipes`, `inventory-item-uses` | Mechanic | Read-only descriptions independent from safe executable action/recipe prerequisites. |
| `object-durability` | Mechanic | Durability/basis display; usable/destruction/consequence decisions remain catalog-owned and validated. |
| `travel-status` | Mechanic | Route/traveller/progress display, complete requirements for actions. |
| `hazard-status` | Mechanic | Subject, afflictions and exposures with audience restrictions and safe calculated effects. |
| `unresolved-decisions` | Mechanic | Scene/items for both display and planner; missing required action context must not be guessed. |
| `social-context` | Mechanic | Source actor/audience binding; attitudes independently displayed and not promoted into unverified rule decisions. |
| `faction-directory-page` | Object | Faction fields, references and pagination without whole-row/schema coupling. |
| `world-location-scope`, `world-location-scope-page` | Mechanic | Scope identity, locations and continuation/source consistency. |
| `world-people-holdings`, `world-people-holdings-page` | Mechanic | People/holdings/location references and partial/paged completeness independently. |

The 13 distinct registered-object identities (17 documents with retained versions) are `campaign-location-visits`, `campaign-summary`, `world-campaign-directory`, `carrying-capacity-creature`, `character-dossier-records`, `inventory-item-activity-record`, `inventory-item-definition-records`, `inventory-item-instance-records`, `inventory-item-recipe-record`, `rest-begin-creature`, `rest-begin-policy`, `rest-begin-world`, and `faction-directory-page`, each prefixed `dnd2024.object.`. The rest/carrying objects and retained versions must not be missed by a website-only search.

Additional concrete shared UI files to classify: `DantesRoleplay.Web/BrowserComponents/system-client.js`, `system-workspace.js`, `system-publication.js`, `page-administration.js`, `governance-control-center.js`, `application-workspace.js`, `application-conversation.js`, and `ai-workspace.js`. This ledger identifies their owners; their complete internal behavior has not been claimed as verified by this planning pass.

Direct navigation to central evidence: [browser response gate](../../src/system/web-interface/dnd2024/src/server/read-model-response.js), [inventory reader and validators](../../src/system/web-interface/dnd2024/src/server/game-server-context.js), [custom client resource store](../../src/system/web-interface/dnd2024/src/data/resource-store.ts), [composition materializer](../../src/system/projection-materialization/persistence/ProjectionMaterializer.cs), [mapped writes](../../src/system/projection-materialization/persistence/ApplicationObjectWriteService.cs), [component storage validation](../../DantesRoleplay.DataAccess/Ecs/SqliteApplicationScopedEcsStore.cs), [atomic effects](../../DantesRoleplay.DataAccess/Ecs/ApplicationEcsEffectApplier.cs), [AI reducer snapshot owner](../../src/system/application-execution/persistence/ApplicationMechanicObjectProjectionResolver.cs).

## 3. Architecture to implement

```text
Authorized read
  -> active registered composition / catalog calculation
  -> bounded, coherent ECS snapshot + source evidence
  -> authorized open field data + explicit read/completeness metadata
  -> normalized client store
  -> field selectors
  -> independent React components

UI draft / AI intent
  -> authorized command or declared mapped patch
  -> current source resolution + expected revisions + rule checks
  -> typed ECS effects
  -> complete component schemas + entity/relationship constraints
  -> atomic commit + audit + change notification
  -> invalidate/refetch relevant client records
```

### 3.1 What retains validation

| Boundary | Retain / introduce | Remove / avoid |
| --- | --- | --- |
| Stored component | Registered schema and version, owner, complete resulting value, revision and entity lifecycle | Nothing merely to accommodate a website deployment. |
| Composition definition | Declared roles, allowed sources/relationships, path safety, access, bounds, unique mappings and write allowlist | Independently authored full assembled-value schema. |
| Display read | Authorized scope/subject, sane transport metadata, bounded JSON, snapshot and paging consistency | Browser equality to a build-time domain schema hash; exact domain key count; whole-object version switches. |
| Display selector | Only fields used by that feature; safe text/number/URL/coordinate handling | Whole character/inventory validation disguised as a selector. |
| Mapped edit | Closed command envelope, allowed paths/operations, source mapping, expected versions/revisions, resulting component validation | Treating all returned object properties as writable; PUT of a partial display object. |
| Catalog mechanic / AI action | Input contract, authorized roles, exact executable provenance, rule eligibility, bounded output/effects, complete dependencies and transactional checks | Browser truth or an AI assertion substituting for server authority. |
| Release / persisted evidence | Artifact signatures/hashes, immutable historical references, runtime and audience selection | Using output-schema identity as the ordinary display compatibility gate. |

Component-schema validity alone cannot prove that an AI chose the right entity, was authorized, obeyed game rules, or used fresh state. The retained mapping, rule and transaction checks are mandatory even when every proposed value individually fits a component schema.

### 3.2 Field behavior

- Consume documented field paths and their meanings; ignore unrelated fields. Dynamic typing cannot infer that a renamed field or a changed unit means the same thing. An absent consumed field degrades that feature; do not guess a replacement by fuzzy name or ID prefix.
- Distinguish **unloaded**, **available**, **absent**, **unavailable/invalid for this feature**, and **access denied**. These are client status categories, not proposed permanent catalog IDs. Do not encode all five as `null` or an empty array.
- Use own-property checks. Preserve legitimate `0`, `false`, empty strings where meaningful, and explicit `null`. Never use truthiness as a universal presence test.
- A missing quantity displays “quantity unavailable,” not 0 or 1. A missing item name can use a neutral “Unnamed item” label without inventing a game-state name.
- Missing `isContainer` means unknown capability. Render the item, do not claim it has no contents, and do not launch per-item speculative requests. Show container disclosure only from positively available information or an existing authorized capability.
- A missing/failed portrait leaves initials, independently of the sheet. Malformed map coordinates suppress the affected marker; they do not corrupt coordinate transforms or imply another map scope.
- A row without usable identity must not enter the entity index or be writable. If it is omitted from an indexed collection, display a local partial-data notice; never report a complete collection or fabricated total.
- Additional properties do not authorize raw JSON dumping. Components render explicitly selected fields; unknown fields stay inert. Retain safe media URLs, escaped text, provenance and audience filtering.
- Do not coerce numeric strings into numbers, invent units, treat an unknown enum as an actionable known value, or silently drop a record that a mechanic needs for a decision.

### 3.3 Current-data resolution and source evidence

For display composition, select the currently attached component by its declared qualified identity within the authorized active application. Its stored type version/schema identity is evidence of what was read, not a demand that it equal the browser's or composition's old source-version constant. Resolve its registered schema/owner and preserve its actual source revision. Do not select the highest registered schema version instead of the version actually attached to the entity.

Keep snapshot consistency across batched sources and collections. A source revision/hash is opaque evidence; do not compare hashes lexically for recency. Scope generations, server revisions and cursor contracts determine freshness.

Execution consumers may require particular component meanings and required inputs. They must declare/check those requirements at their own boundary and continue using reviewed executable/mapping versions. A display request cannot set a `skipValidation` flag that reaches an AI action or mapped write.

Do not add an unqualified `latest=true` switch. Existing public read routes should resolve active read compositions by default; historical/admin reads may remain explicitly exact. New protocol metadata or stored contract-profile changes require an explicit reviewed design in slice 2. Do not invent multiple per-feature endpoint families.

### 3.4 Client store and Redux decision

Use one Redux Toolkit store with React Redux selectors for confirmed client data, request state and separately partitioned drafts. Redux's normalized-state guidance fits the intended entity-oriented UI; `createEntityAdapter` provides ID-indexed records and selectors. RTK Query's cache is not automatically a single deduplicated entity store across endpoints, so adopting it alone does not satisfy this architecture. Sources: [normalized state](https://redux.js.org/usage/structuring-reducers/normalizing-state-shape), [entity adapters](https://redux-toolkit.js.org/api/createEntityAdapter), [cache behavior](https://redux-toolkit.js.org/rtk-query/usage/cache-behavior).

Required logical partitions, with final internal names chosen once in slice 5:

- Authorized scope/session generation, including application, state space, campaign and audience/policy binding.
- Entity/component snapshots indexed by scope + entity ID + component identity; actual source revision and coverage retained.
- Query/composition results indexed by resource identity + subject/arguments + scope. Derived values are not mislabelled as stored ECS components.
- Collection membership/order and paging/completeness evidence, separate from entity values.
- Request states and local field diagnostics, without domain-object admission schemas.
- UI selection and unsaved edits, separate from confirmed server data.

Do not reconstruct canonical component objects from display projections unless the server supplies the necessary source identity and coverage. Two audience-filtered projections of the same entity must not be merged into a more privileged object. Scope partitioning is mandatory even where the current shared website admits a full-authority seat.

The existing request coordinator can temporarily remain a transport/in-flight deduplication adapter. It must not remain a second canonical copy of every migrated response. Keep controllers/promises outside Redux serializable state. Preserve bounded memory, request sharing, cancellation, expiry, late-response fences and no persisted private response bodies.

Additional slice-5/6 acceptance case from the browser rehearsal: opening an item from Party currently rebuilds the character header from its identity-only summary, losing the already-loaded portrait while item details remain usable. The current `CharacterWorkspace`/item-route read ownership must be replaced or reconciled so the same authorized actor retains consistent confirmed fields across that navigation. Include this transition in T07 and T20; the initial inventory adapter repair does not resolve it.

## 4. Ordered implementation slices

Each slice must end with an implementation diff, named tests/results, remaining exclusions and a next-slice decision in the task response. Do not create separate permanent receipts. Finish the coherent slice before starting the next; “tests pass” without its stated behavior is not acceptance.

### Slice 0 — Freeze the evidence and protect the running game

Dependencies: none. Implementation actions below start only after plan approval.

Owners: existing launch/release scripts, production backup/export commands, focused web test fixtures.

1. Re-read AGENTS and the routed guide, inspect dirty changes, active page/host/catalog/audience selections and source hashes. Preserve the current portrait modifications and source image; do not reset or overwrite them.
2. Reproduce the actual inventory rejection and sheet/portrait success. Record the precise failing boundary, not only the UI error text.
3. Capture small representative responses as deterministic test fixtures in the existing test owner: inventory with and without `isContainer`, missing optional component, extra component, and an unrelated bad field. Keep private campaign text and media bytes out of generic fixtures; use synthetic identities for retained tests.
4. Before runtime writes, make a production-verified DB backup with matching content-addressed blobs. Rehearse against a separate restored copy; never use the sole recovery copy as a test database. Retain previous host/source/page selections until release gates pass.
5. Enumerate all 31 authored queries, 17 object documents, relevant extension overrides and server/UI consumers. For each, classify structural copy, calculation, transport/control, or write, and list its consumed fields and required safety boundaries. Add the reviewed implementation coverage to this plan instead of relying on recollection.

Exit: reproducible baseline; preservation verified; every registered-object/query path assigned to a slice. No live gameplay mutation has been used as a test.

### Slice 1 — Repair display admission and the live inventory regression

Dependencies: slice 0. This is a compatible browser-first repair; it does not require catalog activation.

Owners: E01, E02, E08, `W/src/data/hub-types.ts`, existing read-model and character tests.

1. Split the shared reader into bounded transport/scope handling and feature field consumption. A response with new domain fields, a different reported output schema hash, or a different domain version remains consumable. Require trusted subject/scope binding and valid mandatory transport metadata; tolerate additive metadata rather than exact envelope key counts.
2. Preserve schema hashes in evidence/diagnostics where useful, but do not use them to reject display data. Do not accept a foreign actor/application/query simply because it has `name` and `quantity`.
3. Remove all-or-nothing inventory row validation. Extract the item identity/list fields independently. Handle unknown container capability without losing rows or guessing contents; keep wallet status independent.
4. Keep the portrait as an independent media resource. A missing media response, sheet section or inventory capability cannot erase otherwise valid independent state.
5. Introduce narrow field-reading helpers only for repeated primitive safety/presence behavior. Selectors can produce field diagnostics and partial status, not just `valid/invalid` for the whole domain.
6. Replace the generic “Character data incompatible” message with precise affected-feature status. Keep 403, stale reads, malformed transport and unavailable fields distinct.
7. Audit every shared-reader call site before changing its return type so a silent `any` cast does not hide new runtime assumptions. Migrate dependent return handling in the same slice where required.

Tests: T01–T08, T12, T17 below; run the full website suite and build. Demonstrate the existing live-format response displays both items and the portrait. No adapter may recognize Ganji specifically.

Exit: both old/new inventory shapes render useful fields; unaffected sheet/media remain usable; no schema-hash allowlist, no disabled authorization and no inventory data rewrite. A separately approved live browser repair may be published at this gate; it is not whole-plan acceptance.

### Slice 2 — Separate composition metadata from redundant value schemas

Dependencies: slices 0–1. This is a contract-meaning change: present the exact descriptor/storage/API diff for confirmation before activating it.

Owners: E09, E12, `VersionedProjectionContracts.cs`, `ApplicationQueryContracts.cs`, catalog parser/validator and capability adapters.

1. Model structural compositions through declared sources, mappings, references, roles, collections, bounds, access and optional write mappings. Stop requiring an independently authored full assembled-value schema for this mode.
2. Keep the composition definition language itself closed and validated. Reject undeclared sources, overlapping/ambiguous target mappings, unsafe JSON pointers, cycles, cross-owner references, elevated access and unbounded traversal. These are not redundant object-value checks.
3. Replace output-schema-dependent mapping checks with direct mapping/source-component checks. Optional mapped fields can be absent at runtime. Validate path syntax and declared destination ownership without requiring every optional path to exist in a sample value.
4. Replace schema-dependent collection metadata discovery (`SetDeclaredMetadata`) with explicit host-owned metadata placement/serialization. Keep actual row data separate from cursor/completeness evidence so future fields cannot overwrite it.
5. Generate discovery descriptions/field provenance from composition mappings and component schemas. Generated documentation must not become another strict runtime assembled-object schema.
6. Inventory every consumer of `OutputSchemaJson`, `OutputSchemaHash`, `GeneratedWriteMappings` and the legacy object schema key. Classify before removal: structural display duplication can retire; component validation, command envelopes, executable results and historical audit identities remain.
7. Decide the minimal backward-compatible descriptor transition once. Retain immutable legacy registrations and receipts. New registrations use the new semantics; do not rewrite old content hashes in place. Avoid a database migration if an additive descriptor transition suffices; if a migration is needed, rehearse its forward and rollback paths explicitly.
8. Do not globally make every query `outputSchema: true`. Queries backed by calculations and system command results have different responsibilities; retain producer-level validation for them.

The transition must be described in one reviewed cross-owner change: parser/registry representation, query discovery, materializer mode, mapping generation, serializer metadata and persisted-history handling. Do not independently invent incompatible switches in each owner. Display tolerance is selected by the server's registered read purpose, never by an untrusted client flag; execution/write code cannot downgrade its guarantees by calling a display helper.

Tests: T09–T11 and existing `ApplicationObjectContractTests`, plus parser/registration persistence round trips and the legacy-history cases in T23.

Exit: no second hand-maintained assembled-value schema is required for a new structural composition, while invalid composition definitions remain rejected. Exact public/profile changes and any new permanent identity have been explicitly reviewed.

#### Approved v2 contract boundary

The user approved continuing this v2 descriptor/storage/API transition on 2026-09-11. The approval covers the documented migration and its tests, not skipping the release gate or bulk-importing live gameplay. Code edits alone do not convert live components, registration history or runtime selection.

| Boundary | Legacy retained behavior | Approved new behavior |
| --- | --- | --- |
| Object descriptor | Implicit `application-object/v1`, required root `schema` | Explicit `"profile":"application-object/v2"`; root `schema` is forbidden. Sources, mappings, references, roles, access, limits and optional narrow write contract remain validated. Missing `schema` alone never selects v2. |
| Collection metadata | V1 discovers supported metadata from its historical schema | Each v2 collection declares `metadata: { totalCount: "/totalCount", complete: "/complete", nextCursor: "/nextCursor" }`, or reviewed disjoint nested pointers. Source data cannot replace these destinations. |
| Registered storage | Existing rows, object contract JSON and fingerprints remain immutable | New rows retain existing non-null schema columns using the host-owned `{"type":"object"}` transport schema. The object profile is stored in `ObjectContractJson`. No database schema migration. |
| Object query descriptor | Required authored `outputSchema` | Explicit v2 object query profile and no authored `outputSchema`; its internal transport schema/hash is host-owned. Profile mismatch between query and object is rejected. Computed/mechanic queries keep their producer schemas. |
| Public read route | Existing authorized route and evidence envelope | Same route/envelope; active v2 object queries select current attached registered component versions by qualified identity. There is no client `latest`/tolerance flag. Hashes remain evidence, not browser admission. |
| Execution and edits | Exact executable/mapping/source identities, component validation and transaction expectations | These remain exact. The general execution interface and supplied snapshots cannot opt into display semantics. New structural objects have no additional authored assembled-value schema. |
| Missing display fields | Legacy retained behavior remains exact | V2 omits absent mapped paths, retaining source revisions and mapping availability evidence. Required source/role/authority failures still reject the affected read. |

The fixed host transport schema hash is `C2C7529D3F9283F0D0D2F1E5E64C28C3300BA89B9AE84F90606F4A3FC54CF51D`. It expresses bounded JSON-object transport, not domain-field compatibility. Historical v1 serialization and hashes must remain byte-compatible.

Implemented local expansion: bounded provenance now resolves non-root v2 dependency mappings and derives collection fields from registered component schemas, excluding host-owned relationship fields. Authorized MCP discovery and AI context attach these descriptions with explicit unavailable/budget diagnostics; retained v1 descriptor serialization remains unchanged. Mixed-version source identities are batched by exact ID/version pairs, with a 512-reference bound and a 100-version paged-collection SQL-budget regression. Four direct queries (`campaign-summary`, `campaign-location-visits`, `world-campaign-directory`, `faction-directory-page`) use new v2 definitions; their legacy bytes/hashes and the campaign premise write contract are retained. With the subsequent nine-identity migration, the authored inventory is now 30 object documents across 13 identities and still 31 queries (4 object, 27 mechanic).

Implemented actual-read expansion: an authorized internal AI read can now request result-side component evidence separately from immutable capability discovery. Declaration-keyed observations resolve the exact attached registered schemas through a bounded batch, and collection evidence describes the returned page rather than off-page candidates. Final-path availability is derived from declared mappings and returned values. Empty collections, unavailable retained-v1 leaf provenance and evidence-budget limits are explicit. Ordinary browser reads keep the identity-only batch; legacy serialization, capability/result/source fingerprints, exact execution snapshots and mapped-write authority are unchanged. Focused tests and the full backend/protocol suite pass for this boundary; this does not migrate the remaining object consumers.

Remaining limits: all thirteen object identities and their updated exact mechanic/snapshot consumers are now activated in the restored runtime only. V2 dependency-root mappings through retained v1 objects may have no generated leaf provenance and must report that limitation rather than inventing evidence. The earlier four-query selection retained its state across a restart and returned authorized collection-source discovery through the production MCP path; the new all-object selection still needs its complete restart/consumer walk. An earlier browser recovered after a sustained outage and received actual quantity/media changes, but historical replay, Current recovery, the broader mutation matrix, all-consumer migration, final release checks and cleanup remain required. The contract approval gate does not certify those unfinished items or authorize live publication before their acceptance checks.

### Slice 3 — Implement flexible structural materialization safely

Dependencies: slice 2. Keep old behavior available for retained exact historical/execution references until their consumers migrate.

Owners: E10, E11, E16, `SqliteProjectionSourceSnapshotReader.cs`, `ProjectionPlanCache.cs`, collection endpoint selector and projection read transaction.

1. For display sources, read the actual attached component version by the declared qualified identity, preserving its registered owner/schema identity and observed revision. Do not make an optional present component disappear merely because its version differs from an old read composition pin.
2. Omit unavailable optional mapped fields with accurate availability evidence instead of throwing for the entire composition. Required authorization/identity/structural dependencies still fail the affected resource; required mechanic inputs are handled at their execution boundary.
3. Preserve source-to-field provenance for directly mapped fields and separate derived data. Keep entity/component revision evidence and relationship/containment collection evidence, including absence.
4. Remove redundant structural output schema validation in root, dependency, expanded collection and object-query paths for the new composition semantics. Keep size/depth/count checks and deterministic assembly.
5. Maintain per-snapshot batching, required/optional endpoint access rules, stable identities, ordered pagination and cursor freshness. Return partial/completeness metadata explicitly; no unauthorized broad raw ECS scan.
6. Refactor plan-cache identity: composition definitions may still have immutable fingerprints, while materialized values depend on actual observed sources. A cache hit cannot resurrect old fields, old authorization, or an old component selection.
7. Preserve query-result and source evidence without making a browser-generated schema identity necessary. Execute registered reads only through supported authoritative owners; no browser access to arbitrary DB tables or raw blob digests.

Tests: T09–T12, T19–T24; focused projection, collection, read endpoint and snapshot suites. Show existing and expanded component schemas compose correctly without duplicated object schema edits.

Exit: flexible compositions read coherent current values; bounds, authorization and complete/partial distinctions still hold. No write path can request the display-only tolerance as an authority bypass.

### Slice 4 — Keep mapped writes and AI execution schema-safe

Dependencies: slice 2; coordinate with slice 3 before either path is enabled for writes.

Owners: E13–E18, component administration, typed effects, mechanic evaluator/action coordinator, existing write tests.

1. Retain a closed operation/request envelope with known paths and operations. Its job is to identify an authorized change, not duplicate an entire object schema. Discover writable paths from reviewed direct source mappings and current source component schemas.
2. Never treat every displayed field as editable. Block calculated, aggregate, ambiguous, aliased-to-multiple-targets, read-only and foreign-entity paths. Keep relationship edits explicit with their own endpoint rules and revisions.
3. Apply only requested fields to fresh server-read component values. Preserve all untouched and hidden properties. Omission is no-op; clearing/deleting/null are explicit operations with component-defined legality. Do not introduce implicit array merge/delete semantics.
4. Validate every complete resulting component against its actual registered schema/version. Schema-valid values must additionally satisfy entity lifecycle, cross-component/relationship constraints and catalog-owned rule eligibility.
5. Preserve exact expected component/entity/relationship/containment snapshots inside the reserved write transaction. Do not use a fresh post-read snapshot to replace the evidence the decision originally observed.
6. Keep atomic multi-component rollback, confirmation gates, idempotent replay/conflict handling, audit and durable change notifications. A readback/display failure after a committed write must be reported as “saved, refresh unavailable,” not “save failed”; never execute a second write to compensate for a response-format problem.
7. AI objects remain read-only snapshots or declared mapped edits, not mutable entity references. AI cannot forge provenance, select a foreign role, write an undeclared component, submit raw reducer state as authority, or opt out of schemas.
8. In `ApplicationMechanicObjectProjectionResolver` and snapshot-object handling, replace redundant copied-object shape checks only where proven redundant. Preserve exact executable/mapping identity, required source requirements, collection completeness, bounded snapshots and all decision dependencies. Rule computations still validate the values they actually use and their typed outputs.
9. Audit the web PATCH path, system capabilities, MCP/direct AI tools, interaction planner, recipe replay and import/authoring path for the same mutation invariants. Do not fix only the browser's campaign-premise writer.

Tests: all existing `ApplicationObjectWriteTests`, `ApplicationObjectReducerSnapshotTests`, `ApplicationObjectWriteWebEndpointTests`, ECS schema/constraint tests and T13–T18. Existing tests explicitly cover hidden-field preservation, undeclared edits, source conflicts, late rollback and replay after later changes; retain their invariant, not merely their names.

Exit: invalid or unauthorized AI edits fail without mutation; valid narrow edits preserve unrelated data; a schema-valid but wrong/rule-invalid edit is still rejected. No generic ruleset branching is moved into C#.

### Slice 5 — Establish the normalized Redux client owner

Dependencies: slice 1; source-provenance-dependent normalization waits for slice 3. Reuse existing lifecycle behavior rather than rewriting it blindly.

Owners: E05–E07, `W/src/server-host/main.tsx`, `resource-state.ts`, `section-state.ts`, affected hooks and UI reducers.

1. Install reviewed compatible Redux Toolkit/React Redux versions and pin the lockfile during implementation. Introduce the single store/provider once. Keep the existing TypeScript build; dynamic values enter as `unknown`, not a fictitious complete character type.
2. Define scope-aware normalized identities and query/collection/draft partitions as in section 3.4. Pass record IDs to connected components and use narrow memoized selectors. Avoid duplicating a full character object in roster, hero, inventory and details caches.
3. Migrate one resource family at a time. Retire canonical response retention in its old `ResourceStore`/`ViewReadClient` owner as it moves; use a temporary read-through adapter, not two writable stores that reconcile by timing.
4. Preserve fields from partial projections only when their source provenance/coverage proves that merge safe. An authoritative removal must clear the old field; a field omitted because it was not requested must not be mistaken for deletion. Without sufficient coverage metadata, keep results query-scoped rather than guessing a merged component.
5. Update collections and their records in one store transaction/action. Treat server snapshot replacement and incremental changes differently. No ghost items from shallow-upserting a complete list after a removal.
6. Fence late responses with request/scope generations. Never overwrite a newer authoritative record with an older response. Reconcile opaque fingerprints by equality and explicit revision evidence, not ordering them.
7. Separate optimistic drafts from confirmed values. Pending edits cannot change authorization, fulfill rule prerequisites or become AI/game authority. On a conflict retain the user's draft for review, refresh current evidence, and require a deliberate retry.
8. Preserve request deduplication, cancellation, byte/entry/age bounds, retry policy and sensitive-data isolation. Clear private data on scope/access loss before rendering. No localStorage persistence of private response bodies.

Tests: T07, T12, T17–T22; migrate existing resource-store, view-read-client, object-resource and mounted hub tests. Verify selector reference stability and that one entity update does not re-render all unrelated records.

Exit: one confirmed data owner per migrated resource, no field-schema admission at store insertion, no stale or cross-audience merge, and explicit handling of partial coverage/removal.

Current implementation boundary: connected Character sheet/details/inventory and Item details/uses/recipes responses have one Redux confirmed owner, with request coordination separated from retained values. Sheet and details remain named query facets, not guessed canonical ECS components; portrait authority is tracked separately so unavailable reads, denial, known absence and eviction have distinct behavior. Item query keys separate observer, scope, item, source revision and continuation offsets. Registry lists, item/recipe definitions and expanded inventory container pages now live in bounded collection facets within the same Hub store; their transport coordinators retain only in-flight requests. Facets without complete ECS provenance must not be merged into guessed component records. Forced scope refresh atomically clears and fences entries; a failed refresh or cache hit cannot renew an older confirmation's freshness. Eviction offers an explicit reload and must not imply an empty registry/container or cause a refetch loop. The standalone presentation-fixture compatibility path retains loader results only when no confirmed owner is supplied; the connected hub always supplies one. This fixture path is not permission to add a second production owner.

Rules and Installed Content now also keep confirmed responses only in scoped Redux partitions. Their prior clients retain in-flight requests, not completed copies. Scope, generation and logical-flight tokens fence sharing, cancellation, late completion and denial; a denied content collection clears its continuation chain. Entry/byte bounds, source-resolution checks, honest partial paging and explicit retry remain enforced. Rules titles/order and content names/classifications are field-local display concerns, not whole-publication admission gates.

Character freshness boundary: the owner now carries explicitly tagged cache-hit provenance through the loader boundary. Matching cached reads finish their request without recommitting a facet or renewing confirmation/media metadata; fresh reads use completion time even when the reader returns the same JavaScript reference. Focused tests cover all three facets, stale scope/generation/request completion, pre-aborted cache reads and cancellation before a pending result reaches Redux. These source changes still require inclusion in the next frozen browser build and acceptance walk.

Current confirmed ownership is now migrated to Redux. `CurrentViewResourceOwner` retains in-flight requests only; its selected scene/query facet has a one-entry, 2 MiB bound, separate request metadata and scope/generation/token fences. Deferred reads no longer merge Current into React's World/Campaign envelope. Cache hits cannot renew freshness; canceled or superseded reads and late bootstrap seeds cannot restore older or denied data. The production reader's explicit authorization failure clears the private scene, while transport failures preserve an existing confirmed value as stale. Optional scene descriptions, routes and observations degrade independently; missing unrelated wrapper fields cannot reject Current, and an unavailable/unplaced scene cannot borrow a World selection. Exact authority, action prerequisites and board geometry remain strict. Deferred source staging uses bounded serialized leases for overlapping Current/World/People/Faction producers. Browser revision 83 displayed the real Bramblebridge scene after browsing World. A later server/catalog cutover exposed a separate lifecycle defect: Redux invalidation can leave React's deferred-ready marker set, suppressing Current reload/retry. That recovery fix and acceptance remain in progress; the migration is not whole-plan acceptance.

Focused rehearsal covers Ganji's overview portrait, two inventory items, zero wallet, item details/recipes/uses navigation with the loaded portrait retained, and cold Inventory and Item links that independently load character/media information. Both rendered portrait images decoded at 1024 by 1536 pixels. The unopened Campaign navigation displays the authoritative chapter fallback instead of persistent loading. Production-path tests on the separate restored runtime reject invalid quantity changes without mutation; earlier replay tests also preserve exactly-once behavior and reject a changed payload. Revision 81's Party Inventory route recovered both items, wallet and portraits after an 82-second actual outage without navigation/reload. Subsequent quantity changes reached both Inventory and already-open Item Details; archiving the media component removed both portraits, and restoring it returned both decoded images. Original values were restored and all 13 preserved game/history tables compared afterward. The pending-change banner remained after reconnect/historical replay until a later media-triggered refresh; this is an explicit unfinished recovery/acknowledgment case, not a passed warning-clearance test. This is not all-view, full-mutation-matrix or release acceptance.

Stream recovery now retains one owned source and timer with a finite eight-replacement budget. CLOSED connections use prompt escalating retries; CONNECTING connections receive longer watchdog deadlines so a stalled native retry cannot indefinitely suppress a fresh connection attempt. Old sources, hidden pages and disposed scopes are fenced. Only the scoped application connection frame declares recovery; native `open` alone does not. Browser-native attempts may continue after the fresh-instance budget is exhausted. Tests cover quick recovery, sustained outage, mixed connection states, exhaustion, real hidden-to-visible recovery and disposal. This does not change replay cursors or claim that every historical pending notice is acknowledged correctly.

Current recovery acceptance on isolated browser 91: a direct Current link loaded Bramblebridge and its decoded 1536×1024 image with no alert. The isolated listener was stopped at 08:41:05 UTC on 2026-09-11 and resumed at 08:41:40. During the outage the already-open page retained the same scene/image and explicitly reported the failed refresh and last confirmed scene. After restart, without a reload, navigation or retry click, the scene remained and all visible alerts/status notices cleared. The 13-table preservation comparison passed afterward. This closes that browser build's cold-load/stale-retention/restart scenario; future API/event host changes still require the final combined release walk.

### Slice 6 — Migrate every website consumer, not only Character

Dependencies: slices 1 and 5; use slice 3 composition improvements where available. Complete the coverage matrix below in rows, with focused tests after each row.

| Area | Primary existing owners under W | Required independent behavior |
| --- | --- | --- |
| Startup/context/party bootstrap | `src/server-host/main.tsx`, `campaign-summary.js`, `connected-hub-envelope.ts`, `DndInformationHub.tsx`, `src/state.js` | Valid authorized shell/subject identities can load without complete narrative or optional directories. Access binding is never guessed from partial content. |
| Party roster, hero and sheet | `PartyView.tsx`, `components/character/*`, `features/character/project-character.ts` | Portrait, identity, abilities, HP, origin, biography and sheet sections consume independently; an unrelated spell/feature problem does not blank the hero. |
| Inventory and wallet | `game-server-context.js`, `InventoryTree.tsx`, `WalletSummary.tsx`, item route helpers | Rows, quantity, equipment, container controls and wallet have independent states. Preserve unknown versus empty and container nesting bounds. |
| Item Details / Uses / Recipes | `item-read-response.ts`, `item-view-client.ts`, `item-uses-client.ts`, `item-recipes-client.ts`, corresponding feature/components | Missing descriptive fields do not erase useful item identity. Missing action prerequisites disable only the affected action; do not guess eligibility or charges. |
| Item and recipe registries | `item-registry.ts`, `recipe-registry.ts`, registry workspaces | Additional record fields do not reject pages. Unsupported kinds can have an explicit generic read-only presentation, not invented actions. |
| Campaign | Campaign readers in `game-server-context.js`, `CampaignView` and section components, `campaign-premise-write.ts` | Premise/goals/quests/log/visits/outcomes/counts independent. Writes retain confirmation and observed evidence; partial text cannot invalidate unrelated chapters. |
| World directories | World location/people/faction readers, `object-resources.ts`, `World*`/`Location*` components | A field/record problem stays local; partial totals and paging stay honest. All continuations remain reachable. |
| Map | Map projectors, `src/state.js`, scoped map workspace/canvas | Map image, scopes, coordinates, markers and labels handled independently; maintain coordinate safety and authorized navigation. |
| History / Lore / Knowledge | Existing world deferred adapters and feature components | Unknown annotation fields tolerated; visibility/observer binding remain server-owned; missing facts are not fabricated. |
| Current view / encounter board | `current-scene` readers, `encounter-board.js`, `board-draft.ts`, Current components | Scene shell independent from optional media/board facts; invalid geometry disables affected board rendering/actions without corrupting state. Draft commands remain validated. |
| Rules / Installed Content | `rules-reference.ts`, `effective-content.ts`, rules/content views | Useful names/descriptions remain visible with unfamiliar optional metadata; discovery status and runnable capability checks remain authoritative. |
| Shared host/control/AI UI | `DantesRoleplay.Web` shared BrowserComponents, control capability explorer, conversation clients | Apply the same display-versus-command classification. Keep protocol/action discriminants, admission, tool request validation and safe output rendering. Do not blindly remove generic control schemas. |

For each row inspect every called validator and projector, including generated modules and `Array.every`/exact-key checks. Lists of declared query IDs and precise field paths belong in the reviewed slice-0 coverage; no row may be declared complete solely because the shared reader changed.

Exit: every row has tests for additive fields, missing optional fields and independent error presentation; no remaining hidden whole-domain gate can invalidate an unrelated view. Preserve deliberate local safety checks and document them in code/tests.

Shared conversation display boundary: the existing custom element now consumes messages, situation text, participants and task progress independently, with bounded message/history retention and honest partial notices. A connection captures its own cancellation signal across deferred client loading; retired requests cannot render after reconnect, concurrent creation is joined, pending sends are not duplicated, and a stale history page cannot overwrite a newer turn. Optional media captions and malformed neighboring attachments do not hide a valid same-owner image. Command controls still require exact declared statuses. Eleven mounted tests exercise the actual shared component source with only its external client import substituted. The shared publication client/navigation/workspace also consume optional titles, names and ordering independently, retain partial metadata and withhold ambiguous identities. Exact enabled/visibility/index status, same-origin publication URLs and canonical page selection remain mandatory. Twenty-two shared publication tests cover the real consumers. These four shared scripts are now in the isolated all-object host, not the live host. Lost-response turn idempotency, ephemeral conversation recovery, actual conversation mutation acceptance and the remaining control/AI/admin consumers are unfinished.

World Locations now requests one authorized hierarchy page rather than eagerly traversing the complete world. Each admitted scope page retains only its scope, children and required map data under the unchanged 512 KiB page limit; root/child navigation and continuations remain reachable. React scope merging preserves separately owned People, Lore overlays and routes, and scope keys include current actor identities. Actual revision-83 browsing opened Caldris and its three children without the prior memory failure. The UI labels level-local counts and search honestly. World still has duplicate completed retention in its resource cache and React envelope; raw connected-source staging remains eight-scope count-bounded, not fully byte-bounded. Those remaining ownership and field-local migrations are explicit next work, not resolved by paged loading.

### Slice 7 — Live updates, invalidation and scope recovery

Dependencies: slices 3, 5 and the relevant slice-6 consumers.

Owners: E19, `ApplicationObjectChangeTransactionParticipant.cs`, `ApplicationObjectDependencyIndexCache.cs`, `ProjectionImpactService.cs`, current change feed and client state owners.

1. Inventory add/remove/quantity/equipment and media edits must invalidate the precise source/collection dependencies and refresh visible consumers without a full page reload. Notify only after commit.
2. Preserve the existing conservative recovery when a write has no fully known object dependency mapping. A recognized change in one component must not suppress an unmatched change in the same transaction.
3. Ensure newly supported component versions and changed composition declarations do not leave dependency caches pinned to old source types or silently omit notifications.
4. On disconnect/reconnect, revalidate active resources and explain stale values while refreshing. Do not show stale private data after authority loss. A cursor advance without a relevant change must not fabricate a data update.
5. Test changes arriving during fetch, navigation, a pending write, and application/campaign changes. Abort/fence obsolete requests; coalesce repeated invalidations without losing the newest update.
6. An unknown component or object notice triggers bounded safe recovery, not a crash, no-op, or unbounded catalog scan. Component removal and entity disable remove corresponding visible state honestly.

Exit: T17–T22 pass in mounted tests and a browser with actual server-side writes on a disposable game copy. A simulated browser dispatch alone is not proof of server-to-browser delivery.

### Slice 8 — Align catalog discovery, AI context and retained contracts

Dependencies: slices 2–4. Do not enable schema-free structural reads on one surface while another silently reimposes the old value schema.

Owners: `ApplicationCapabilityContractAdapter`, `ApplicationQueryContracts`, activated catalog provider/content reader, `ApplicationCapabilityCatalogValidator`, object/query executors, task-context materializer, proposal verifier, receipt store, mechanic snapshot-object owner, relevant system capability adapters.

1. Walk every consumer discovered by the `OutputSchemaJson`/`OutputSchemaHash`/object-contract searches. Mark whether it is display metadata, composition registration, executable input/output, or historical integrity evidence.
2. For structural reads, replace duplicated output-value validation with composition/source evidence and generic envelope safety. Capability discovery explains available components/fields and writable paths from authoritative definitions, rather than inventing a new complete object schema.
3. Preserve exact action/mechanic versions and expected source evidence in proposals, recipes and receipts. Existing historical receipts must still be readable/verifiable; changing display compatibility must not invalidate or rewrite old audits.
4. Update the AI context pack so missing optional source data is explicit. AI receives registered component schema information and read/write provenance where authorized. Missing required action inputs cause a safe refusal/request for information, not guessed defaults.
5. Review source/component definition registration/import behavior separately from display behavior. Continue reviewed activation and any required data migration; never auto-upgrade a live component type because the website discovered a newer definition.
6. Update the catalog validation rules and fixtures for the new structural descriptor. Retire redundant authored object output schemas only after all old callers are accounted for. Retained exact legacy definitions are compatibility history, not a second active schema authority.

Exit: all object read surfaces work under the new semantics; discovery, action execution, recipe replay and retained-history integrity still pass their focused tests and the protocol walk. No blanket schema deletion is permitted.

#### Remaining append-only object/consumer groups

The follow-up audit found four bounded dependency groups among the nine identities beyond the first four direct-query migrations. All four groups are implemented in source, with original object bytes retained and exact mechanic/query pins updated; producer schemas and component schemas are unchanged. Rest's closed schema alternative now has bounded generic field-provenance support without weakening the relationship filter. The combined full backend suite passed 2,219 tests with two pre-existing retired-protocol skips. That run includes the corrected snapshot fixture, which resolves shared Game component schemas under their actual owner instead of the D&D-only folder. Catalog validation passed 600 records with seven unchanged legacy warnings. The immutable source changes only 21 reviewed files and retains nine predecessors, producing 3,641 catalog winners. On the isolated game copy it is now activation 66, with compatible binding 40 covering 2,638 entities and 4,839 components. All thirteen preserved game/history tables remain unchanged apart from the previously permitted quantity/media revision timestamps, whose complete values were restored. Real dossier, sheet, inventory, item details/uses/recipes and Current read paths respond under this selection. This is not all-consumer or live release acceptance. Paths below start at `catalog/applications/dnd2024/`; object names carry the existing `dnd2024.object.` prefix and mechanic names carry `dnd2024.mechanic.`.

| Group | New object versions, retaining old bytes | Exact consumers that must move together |
| --- | --- | --- |
| Carrying | `carrying-capacity-creature` 1 → 2 | `mechanics/data/carrying-capacity.read` Markdown requirements and JavaScript object version/hash checks; `contracts/website-feature-contracts.json` under the website workspace retains exact execution evidence. |
| Dossier | `character-dossier-records` 1 → 2 | Updated `character-dossier-v1.project` snapshot requirement and the query's exact compiled mechanic hash; keep selected catalog version `1` under the existing provider convention and retain immutable stored mechanic history. |
| Items | `inventory-item-definition-records` 2 → 3 before `inventory-item-instance-records` 2 → 3; activity and recipe records 1 → 2 | Compile the definition first, pin its final fingerprint in the new instance object, then move details/uses/recipes mechanic versions and their three query pins. Retain both existing definition/instance versions. |
| Rest | `rest-begin-creature`, `rest-begin-policy`, `rest-begin-world` 1 → 2 together | World collection declares explicit metadata destinations. Only after all three fingerprints exist, move `rest.begin` requirements and its three JavaScript object version/hash checks together. |

Mechanic files in these groups use their full qualified names, for example `mechanics/data/dnd2024.mechanic.carrying-capacity.read.md` and `.js`. Derive new fingerprints from final registered definitions; never paste guessed or partial hashes. Walk upstream child-mechanic pins, query pins, action discovery and exact snapshot fixtures before selecting each new version. Keep mechanic query producer-result schemas unchanged unless separately justified and reviewed.

Observed mechanic-version distinction: selected application-catalog mechanic records currently expose version `1` with an exact compiled content fingerprint, whereas `MechanicStore` appends immutable stored revisions. The new Dossier query therefore keeps catalog projection version `1` and changes only its exact compiled content hash. The added history regression independently reads stored mechanic versions `1` and `2` and verifies their original/new source and compiled fingerprints. Do not invent duplicate version-suffixed mechanic Markdown/script files or silently change the host's version convention to make a migration appear append-only. Retained Carrying and Dossier v1 object JSON files are byte-identical to their original Git blobs.

The restored-copy history audit found all 21 predecessor object-registration rows unchanged, all nine new object rows present, and all 87 stored mechanic-history rows unchanged. All nine retained definition files match the frozen predecessor bytes. All 5,898 operations from the original recovery point, existing activation receipts and existing object-change rows are also unchanged; rehearsal operations append new audit rows rather than rewriting old evidence. These comparisons use read-only SQLite access and do not substitute for action/replay acceptance.

Carrying, dossier and item producers stay on the authorized exact snapshot path in `ApplicationMechanicSnapshotObjects`; rest stays on the exact `objectRoles` path in `ApplicationMechanicObjectProjectionResolver`. Do not substitute display materialization, reread a newer snapshot during execution, or turn the structural transport schema into write authority. Retain rest's distinct-role/policy/clock/HP and complete-collection requirements, item paging/source-revision and optional-field removal behavior, and every typed effect/stale-write invariant.

Acceptance must include the existing object/history, snapshot-object, reducer-snapshot, `Dnd2024ApplicationReadViewTests`, carrying/progression, item details/uses/recipes and rest/creation test owners. Add current-version cases alongside old-version cases; do not replace history coverage. No live catalog cutover or cleanup follows merely from this audit.

### Slice 9 — Release verification, operational checks and cleanup

Dependencies: slices 0–8. Publishing slice 1 early does not waive this final gate.

Owners: E20, current operational/development guides and tests.

1. Run full solution build/tests, catalog validation and the MCP protocol walk because this plan changes shared contracts and execution dependencies. Run website typecheck, node/mounted tests and production build with the supported Node runtime (the desktop default was older than the project's declared minimum during the portrait task).
2. Rehearse activation/migration with exported runtime-only records and matching blobs on a restored disposable database. Verify counts, representative entity/component contents, relationships, blob hashes, action safety and old receipt access—not just SQLite integrity.
3. Build the release from reviewed source. Stage a page draft, read back HTML and every asset/hash, then activate the exact reviewed revision. Preserve previous release artifacts. Never delete active host/source directories as “build files.”
4. Verify the actual local and non-loopback public origins. Exercise all slice-6 rows, including Ganji's Character, Inventory, Biography, portrait and item Details/Uses/Recipes, plus map/world/campaign/current views. Record honest unsupported/unavailable feature states; do not call every fallback success.
5. On the rehearsal host, change inventory and media through real authorized production operations while the browser stays open. Confirm updates, removals, rejected writes, reconnection and continued unrelated rendering.
6. Run the compatibility matrix: old/new component additions and response fields against both an already-open client and a freshly loaded client. No build-time whole-object schema equality may be needed for those reads.
7. Keep runtime/artifact signatures and exact selected host/catalog hashes for operational integrity. Expand readiness/browser acceptance to test consumed fields and flows; do not lower checks merely until “ready” turns green. Ordinary gameplay result changes are not release failures—freshness evidence and stable artifact identity are different things.
8. Verify restart behavior, saved release selection and matching read/write semantics after activation. Roll back a bad page independently when possible. Never restore an old database over later gameplay without explicit recovery approval.
9. Only after successful acceptance remove newly obsolete generated display validators/contract gates, old canonical caches and temporary rehearsal/staging files. Preserve component/action validators, active releases, retained historical definitions and the required recovery point through verification.
10. Update ARCHITECTURE, DEVELOPMENT and OPERATIONS to describe delivered boundaries. In particular, replace guidance demanding complete browser output-schema matching or complete structural-object value schemas; retain their authority, security, transaction and deployment rules. Mark this plan implemented only when every gate is actually met.

Add a repository regression guard that rejects reintroduced build-time domain-schema equality or generated whole-object validators on active display read paths. Keep an explicit, reviewed exclusion set for command/control envelopes, component writes, computed producers and historical evidence; a broad filename exclusion is insufficient. Old exact-mode compatibility code may remain only for identified historical/execution consumers, not as the unnoticed active browser path.

Exit: final checklist below is complete with actual command/browser evidence. Any unverified surface remains an explicit exclusion, and whole-system acceptance remains incomplete.

## 5. Mandatory acceptance matrix

Use these IDs in tests or the task's acceptance report. They are plan/test labels, not new permanent runtime IDs. Parameterize reusable tests across the slice-6 read families; do not test only one specially lenient inventory adapter.

| ID | Scenario | Required assertion |
| --- | --- | --- |
| T01 | Add unrelated top-level/nested fields and unknown component data | Existing selected fields render; no whole-object rejection or authorization expansion. |
| T02 | Report a different output schema hash/domain version while consumed fields remain usable | Rendering is unchanged; metadata retained as evidence, not an admission gate. |
| T03 | Remove `isContainer`, portrait, narrative, or an unrelated optional section | Only dependent controls/content degrade; inventory identities and other sections survive. |
| T04 | Wrong primitive type in one quantity/name/coordinate | Local unavailable state; no guessed value, unsafe calculation or whole-page failure. |
| T05 | Values `0`, `false`, empty string, null, absent | Distinct intended behavior; no truthiness defaults or accidental writes. |
| T06 | Missing/duplicate/malformed row identity | No wrong entity merge/action; local partial-state notice; no false completeness. |
| T07 | Same entity displayed in roster, hero, list and details | One confirmed state owner; updates are consistent; unrelated selectors remain stable. |
| T08 | Invalid image URL/markup/prototype-like keys | No script execution, arbitrary URL trust, prototype mutation or raw unknown-field rendering. |
| T09 | New source component version retains fields but adds a field | Display composition uses actual stored sources without editing a duplicate object schema. |
| T10 | Malformed composition definition: ambiguous mapping, undeclared source, unsafe path, cycle, excess bounds | Registration rejects it even though object value schemas have been removed. |
| T11 | Optional source/path absent; required authority or action dependency absent | Display degrades locally for optional data; unauthorized/unbound or unsafe execution still fails. |
| T12 | Cross-application/state-space/campaign/actor/audience response or cache reuse | Rejected/cleared before display or write; never merged merely because field names match. |
| T13 | AI or UI proposes schema-invalid component changes, including merge removing required data | Complete resulting components rejected; zero partial ECS mutation. |
| T14 | Schema-valid but undeclared, unauthorized, wrong-target, calculated-field or rule-invalid edit | Rejected by mapping/authority/rules; component validity alone is insufficient. |
| T15 | Valid narrow edit with hidden/unrelated stored fields | Only requested field changes; untouched values and relationships preserved byte/semantically as appropriate. |
| T16 | One failure late in a multi-component/relationship transaction | Entire write rolls back; no success/change event for an uncommitted state. |
| T17 | Concurrent changes to observed components, absent sources, entities, relationships or containment | Stale action/edit rejected inside transaction; no lost update. |
| T18 | Retry after timeout or failed post-commit display; repeated/conflicting idempotency key | Exactly one committed operation; conflict rejected; saved state not mistaken for unsaved. |
| T19 | AI reads partial collection but attempts a whole-collection rule decision | Execution refuses incomplete prerequisites; display can remain partially useful. |
| T20 | Late response after source update/navigation/scope switch | Cannot overwrite newer state or repopulate retired private scope. |
| T21 | Add/remove/move/equip/quantity/portrait change through real server operations | Relevant visible components refresh without page deployment/reload; removals leave no ghosts. |
| T22 | Stream disconnect/reconnect, unknown object notice, mixed recognized/unrecognized effects | Safe bounded recovery; no missed invalidation, authority leak or unbounded refetch. |
| T23 | Existing descriptors, saved proposals/receipts and rollback artifacts | Historical evidence still resolves; no silent fingerprint rewrite or deletion. |
| T24 | Large/nested data, pagination, recursion/cycle, malformed/oversized body | Bounds enforced, honest partial/failed resource status, responsive unrelated UI; no unsafe unbounded “dynamic” read. |
| T25 | Clean release, public HTTP origin, restart and all-view browser walk | Correct assets and saved selection; actual user flows render, not just API health checks. |

Add focused producer-result tests for calculated values (HP, movement, money, eligibility, recipe/action outputs). Removing object duplication must not remove validation of untrusted or computed mechanic outputs before they become action prerequisites or effects.

### Minimum test owners and commands

- Existing C# owners: `ApplicationObjectContractTests`, `ProjectionMaterializationTests`, `ApplicationObjectWriteTests`, `ApplicationObjectChangeTests`, `ApplicationObjectReducerSnapshotTests`, `ApplicationSnapshotObjectTests`, `ApplicationReadModelWebEndpointTests`, `ApplicationObjectWriteWebEndpointTests`, plus component schema/role-constraint/effect and affected catalog/interaction tests.
- Existing website owners: `read-model-response`, `game-server-context`, `connected-hub-envelope`, `resource-store`, `web-state`, `campaign-summary`, item/encounter/registry tests and mounted `object-resources`, `website-states`, item workflows, character/media tests. Resolve actual test filenames before adding duplicates.
- Run focused iterations using `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter ...` with discovered test names. At feature acceptance run `dotnet build DantesRoleplay.slnx`, full `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj`, and `./roleplay.cmd validate catalog`; discover/run any additional solution test projects rather than assuming this is the only one.
- In W, use the project-supported Node runtime and run `npm run verify`. Run existing browser/release tooling with the required independently verified inputs, not invented evidence files.
- Source tests, HTTP probes, mounted tests, served-browser behavior and production-path writes prove different things. Report each separately.

## 6. Guardrails for the implementing agent

Do not claim success by doing any of these:

- Removing only the schema hash check while leaving `hasExactKeys`, generated validators, complete-view projectors or cache validators to reject the same data later.
- Replacing exact schemas with several supported-version branches or automatically importing the checkout to make hashes match.
- Turning every schema into `true`/an open bag, disabling component validation, accepting all mapped paths, or replacing failures with fabricated empty/zero values.
- Returning arbitrary ECS fields or media blobs without the existing authorization/source owner.
- Making an AI's Redux state, a display object, or an optimistic draft authoritative.
- Dropping source revision/relationship completeness checks because object output schemas were redundant.
- Assuming actual Redux is already installed, or adding it while keeping competing confirmed-data stores indefinitely.
- Shallow-merging a partial/filtered object into a canonical entity and thereby reviving removed or hidden fields.
- Removing every hash: component schema identity, executable identity, immutable audit integrity, source freshness and browser output-shape compatibility are different uses.
- Changing tests merely to accept missing data. Replacement tests must assert the intended useful rendered fields and unchanged write/security invariants.
- Treating the portrait or the known two-item fixture as sufficient all-view acceptance.
- Executing this broad migration on the live database first, deleting active release directories, or discarding the recovery point before verification.

If a required public contract/migration/new permanent ID decision remains unresolved, stop at that boundary with the exact proposed change and evidence. Do not invent a parallel object store, schema authority, capability family, or fallback API to avoid it. The numbered-slice unattended exception in AGENTS applies to SYSTEM-AUDIT.md, not automatically to this plan.

## 7. Definition of done

- [ ] Current Ganji inventory and portrait display without modifying his records to satisfy the browser.
- [ ] All slice-6 read families accept unrelated additions and degrade missing optional fields locally.
- [ ] Structural object compositions no longer require duplicate authored assembled-value schemas; composition/access/provenance rules remain enforced.
- [ ] Stored ECS components, commands and computed/action boundaries retain appropriate schema and rule validation.
- [ ] One normalized client owner plus field selectors, with safe partial-coverage, deletion, freshness and scope semantics.
- [ ] Narrow mapped writes preserve unrelated state; all stale/unauthorized/invalid/partial-decision cases reject safely.
- [ ] AI discovery, snapshot inputs, action execution, recipes, receipts and relevant system/control surfaces have been audited and tested, not merely the website GET path.
- [ ] Real server-side gameplay changes update an already-open browser without a page deployment.
- [ ] Full suites, catalog validation, protocol walk, public browser traversal, rehearsal and restart/rollback checks pass.
- [ ] Redundant display validators/caches and obsolete documentation are retired without deleting authority/security/history safeguards.

The desired result is **flexible field-based reads above a strict, schema-safe ECS mutation boundary**. Neither side is a substitute for the other.
