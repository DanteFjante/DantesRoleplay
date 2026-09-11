# Website composition and operator interface

Status: independent composition/read/presentation library and retained draft storage implemented; production publication,
transport integration and dependent platform acceptance remain pending. The
[coordination plan](../PLATFORM-IMPLEMENTATION.md) defines shared contracts and scheduling; the
coordinator includes the relevant agreement with each assignment. This file owns the website
workstream. Initial integrations are the website and Codex. Application-specific
pages and DND2024 behavior are outside this plan.

Prerequisite: implement [00 — Shared foundation](00-shared-foundation.md) first and have the coordinator supply its accepted foundation revision and contract baseline. This workstream consumes those shared contracts and does not redefine them independently.

## Independent implementation boundary

[WebComposition.cs](../../../DantesRoleplay.Web/Pages/WebComposition.cs) parses a closed, page-local
declarative document into an opaque immutable tree. Component IDs/revisions resolve only inside
that document; `generation` is an authored label, not proof of an active database revision. The
coordinator must pin the exact retained document bytes/hash and selected content revision before
production rendering. There is no global component registry; retained content uses the existing
versioned page store and asset payload owner.

The renderer permits escaped text/values, allowlisted elements, conditions, loops, required props,
and caller-scoped slots. It validates references/cycles and rejects duplicate/unknown JSON fields,
unsafe URLs and undeclared bindings. Asset references must occur in the host-supplied selected
revision inventory; render accepts the legacy `/ui/{slug}/` base or the exact retained base
`/ui/{slug}/content/{contentPageId}/revisions/{revision}/`. The current active-asset
route does not pin asset reads to the rendered revision. It must be coordinated before claiming
generation-consistent publication. Action buttons remain disabled until a real dispatcher is attached.

Bounds: 1 MiB document, JSON/node depth 32, 64 component definitions, 4,096 authored nodes,
16 query/action declarations each, 100 items per loop, 16,384 expanded render nodes, and
1,048,576 output characters. Binding/property identifiers are ASCII and at most 80 characters.
`props` is reserved. Missing query data is a render error, never an empty successful result.

[CompositionPagePreview](../../../DantesRoleplay.Web/Pages/CompositionPagePreview.cs) requires exactly
the declared query names. [CompositionQueryMaterializer](../../../DantesRoleplay.Web/Reads/CompositionQueryMaterializer.cs)
accepts only host-selected `ApplicationReadModelInvocationRequest` values sharing one read-only
`InteractionInvocationHost`. It delegates reauthorization, scope checking, exact query contracts,
and budget consumption to the existing real adapter. Reads are sequential, bounded to one page
per binding (1–100 items, opaque cursor at most 1,024 characters), and are never cached by this
consumer. This does not claim all queries share a database snapshot. Any failed read suppresses
render values; original shared outcomes remain available for diagnostics.

[composition-bindings.js](../../../DantesRoleplay.Web/BrowserComponents/composition-bindings.js)
provides result/operator presentation and a controller accepting injected read/dispatch functions.
`bindControls` enables only the host-supplied available binding names, obtains input through the
host callback, and removes listeners/disables controls on disposal; absent input stays recoverable.
It declares no new HTTP endpoint and accepts no principal, grant or state authority. The host
adapter must resolve each binding within its selected generation; browser input is never authority.
Missing adapters/readback sections report unavailable. It separates read/worker output, proposals,
pending tasks, current receipts, prior workflow receipts and recovery identities. Commits refresh
authoritative data; uncertain actions remain fenced without retry. Existing scoped stream events
invalidate reads, reconnect refreshes data, and a page-revision event disposes old controls before
the host reloads compatible content. No task lifecycle or authorization semantics live in this UI.

Example composition payload (draft authoring; production publication remains unavailable):

```json
{
  "formatVersion": 1,
  "generation": "example-generation",
  "queries": [{"name": "records"}],
  "actions": [{"name": "refresh_record"}],
  "components": [
    {"id": "heading", "revision": "1", "requiredProps": ["title"],
     "template": {"kind": "element", "tag": "h2", "children": [{"kind": "value", "path": "props.title"}]}},
    {"id": "command", "revision": "1", "template": {"kind": "element", "tag": "button", "action": "refresh_record",
     "children": [{"kind": "text", "text": "Refresh record"}]}}
  ],
  "root": {"kind": "element", "tag": "main", "children": [
    {"kind": "component", "id": "heading", "revision": "1", "props": {"title": "Records"}},
    {"kind": "each", "items": "records", "as": "record", "children": [{"kind": "value", "path": "record.name"}]},
    {"kind": "component", "id": "command", "revision": "1"},
    {"kind": "component", "id": "heading", "revision": "1", "props": {"title": "Operation results"}}
  ]}
```

`records` is only a local binding name. The coordinator-owned resolver must provide its exact
`InteractionQueryContractReference` (projection ID/version/content hash, schema hash/schema,
exposure/roles), selected application/state revision and trusted caller host. `refresh_record`
similarly requires the real action owner to pin mechanic ID/version/content hash and command
identity. Neither declaration can select authority or advertise availability by itself.

## Retained content and coordinator integration

- The accepted content model adds `ContentFormat` (`html` by default or `composition-v1`),
  `CompositionJson` and `CompositionHash` to the existing bundle/revision/read owners. Legacy
  constructors/defaults and HTML hashes remain compatible; revision summaries expose format/hash
  without duplicating JSON. `WebPageBundleReader` accepts exactly one root `index.html` or
  `composition.json`, plus assets, within the existing ZIP/strict UTF-8 bounds. HTML and composition
  payloads are mutually exclusive. Composition has empty HTML and validated JSON, canonicalized by
  ordinal property ordering (array order/JSON number tokens preserved), with the uppercase SHA-256
  of those retained UTF-8 bytes. Both source and canonical JSON are at most 1 MiB; total retained
  content/assets remain at most 25 MiB. Optional caller hash must match. The accepted coordinator
  migration `20260911175529_RetainedWebCompositionDrafts` installs format/JSON/hash/size constraints.
  It refuses downgrade while composition or unpublished (`ActiveRevision=0`) pages exist.
- `AppendBundleDraftAsync` now accepts `expectedLatestRevision=0` only when the page is absent,
  creating revision 1 with `ActiveRevision=0`. Positive expectations compare exactly against the
  current latest revision. An explicit non-deferred SQLite writer reservation precedes reads and
  writes; a busy reservation returns `PAGE_WRITE_BUSY` without replaying a write. Revision/blob/
  page creation is one web-content transaction. Active reads never fall back to a first draft.
  `GetRevisionAssetAsync` reads the requested retained revision, and requires caller-authorized
  historical/draft access. This creates no public draft asset route. Existing direct HTML editing
  preserves legacy validation semantics and rejects a composition base instead of implicitly
  changing its format. `ActivateRevisionAsync` and `SaveBundleAndActivateAsync` refuse composition
  with `COMPOSITION_ACTIVATION_UNAVAILABLE`; a first HTML draft can activate with expected active 0.
  Validate the exact candidate and binding contracts via plan 02 before any composition active pointer
  advances. Failed candidates retain previous content/assets; reconcile ECS identity and content
  references explicitly without assuming a transaction spans both owners.
- In `WebInterfaceEndpoints.GetPageAsync` and the root path, retain publication discovery and
  existing access filters; branch on the selected revision's content format and invoke
  `CompositionPagePreview`. Supply the verified host, exact selected queries and asset base,
  serve audience-specific results without shared HTML caching, and map missing/denied/incompatible
  reads distinctly. Coordinate revision-pinned asset reads and retention with the same publication.
  The proposed immutable asset base is `/ui/{slug}/content/{contentPageId}/revisions/{revision}/`; acceptance of its access
  semantics and transport remains coordinator-owned. It is not a public draft preview URL.
- Register the read materializer/preview only in the existing coordinator-owned web registration.
  Bind browser actions to the common plan 01 dispatch boundary in
  `WebInterfaceApplicationEndpoints`; preserve stable command identity and reconcile uncertain
  receipts. Plans 02–05 supply authoring/grants, manuals, schedules/observers and child-task readback.
  No proposed downstream contract is consumed before its coordinator freeze.

`WebPageContentReference` preserves the legacy pageId-only reference or requires all four pin
fields: `revision`, `contentFormat`, `contentHash`, and `assetInventoryFingerprint`. It derives
the content hash from actual retained bytes and verifies canonical composition and every asset
payload. The inventory fingerprint is uppercase SHA-256 over UTF-8
`dantes-roleplay/web-page-asset-inventory/v1`, a NUL separator, and compact JSON containing
ordinal-path-sorted `[path, contentType, contentHash, length]` arrays. Asset bodies remain in the
web content store. Metadata updates preserve the whole reference.

The unregistered selection methods in `WebPagePublicationService` capture and recheck the exact
application-publication binding, application generation, entity, component type/revision/value,
and retained content pin. Draft selection names its revision; published selection refuses an
unpinned reference. Literal component/slot rendering needs no synthetic invocation host; query
and action declarations remain unavailable until their actual owners select exact contracts.
The unregistered publication CAS writes only the ECS component reference after content retention.
`ReadCompatibilityPointerAsync` reports disagreement with the separate web `ActiveRevision`;
it neither repairs the pointer nor promises a transaction across the two databases. Legacy
discovery refuses to send pinned content through the old mutable-active HTML route.

Selections and pins are evidence, never permission tokens. The coordinator's `web-page` resource
permission maps to the retained web content page and immutable owning application; publication
entities are current links to it. Schema Read, namespace prefixes, a verified principal, and
legacy private access do not substitute for that resource permission. Page Read/authoring/activation
are Application-scoped; audience queries separately require exact StateSpace Read. Their hosts
must share a principal/application generation and operation/deadline ledger while retaining their
actual distinct scopes and grants. The website does not construct broader query authority.

`WebPageStandingGrantResourceTargetOwner` implements the fixed `web-page` resource-owner seam.
It reads the immutable content-page mapping, verifies the actual owning application generation
and the complete enabled/reviewed namespace registration, and verifies exact retained content and
asset bytes. Its evidence binds the mapping, full content pin and namespace metadata; the shared
grant policy rehydrates that evidence on every use. Exact resource resolution does not require an
ECS publication link. `ResolveCurrentAsync` selects the latest retained revision for inspection or
authoring only; it never selects what a published route serves.

`WebPagePermissionedReader` is an unregistered serving consumer. It accepts a host-created read-only
`InteractionInvocationHost.ForApplication` invocation with no state-space authority, selects the live
ECS publication pin, resolves that exact retained resource, and requires its current Application-scoped
Read grant. It consumes one shared operation and rechecks authority
and the publication before returning HTML or exact revision assets. Failures return no content;
query/action compositions remain unavailable. The coordinator owns host construction, registration,
HTTP routing and response cache policy. This consumer does not provide resource adoption, candidate
acceptance, publication authorization, or mutation receipts. Existing legacy publish/activate and
directory paths refuse pinned references rather than changing or reading the compatibility pointer.

Library and migrated SQLite fixtures demonstrate composition, inert retention, exact historical
asset readback, local transaction rollback and consumer conformance. They do not demonstrate
production composition activation, published composition routes, cross-owner failed-publication recovery,
Codex-to-page action execution, invited-user grants, scheduled jobs or delegated worker completion.
Those scenarios wait for real dependencies and coordinator integration; test doubles are confined
to test projects and cannot satisfy platform acceptance.

## Outcome and existing owners

An authorized caller can publish a page composed from runtime-authored reusable components,
populate it with registered queries, and invoke the same actions used by Codex and JavaScript.
The operator can inspect authored changes, permissions, jobs, and authoritative results through
that website. Ordinary page development requires no compiled application widget or new host route.

Reuse these owners:

- [Page contracts and publication](../../../DantesRoleplay.Web/Pages/WebPagePublicationService.cs):
  ECS publication identity, navigation, and references to separately versioned page content.
- [Content store contract](../../../DantesRoleplay.Web/Storage/IWebPageStore.cs) and
  [implementation](../../../DantesRoleplay.Web/Storage/WebPageStore.cs): draft/active revisions,
  HTML, and durable assets.
- [Web content database](../../../DantesRoleplay.Web/Storage/WebContentDbContext.cs): content
  persistence and its own migration history.
- [Page serving and stream](../../../DantesRoleplay.Web/Http/WebInterfaceEndpoints.cs): root,
  page/assets routes, publication resolution, and scoped server-sent events. Currently the page
  path returns the active HTML directly; it does not compose a runtime component tree on the server.
- [Application dispatch](../../../DantesRoleplay.Web/Http/WebInterfaceApplicationEndpoints.cs) and
  [read adapter](../../../DantesRoleplay.MCPServer/ApplicationReadModelWebEndpoint.cs): existing
  application/query/action boundaries.
- [Live changes](../../../DantesRoleplay.Web/Live/WebChangeFeed.cs) and
  [scope authorizer](../../../DantesRoleplay.MCPServer/WebChangeScopeAuthorizer.cs): notification
  transport and audience filtering, rather than a new website event bus.

The website agent owns component composition, rendering, browser bindings, and operator presentation
in those areas. Shared host registration, new migrations, and endpoint contract changes are
integrated by the coordinator. Other agents supply services; this agent must not reimplement
authorization, query evaluation, activation, or task execution inside page handlers.

## Proposed contract and storage boundary

Extend the existing versioned content bundle with an explicit content/composition format. Preserve
the existing plain-HTML format. A composition declares a root component, references to component
definitions/revisions, slots or children, serializable properties, query bindings, and action
bindings. Runtime component content belongs with the existing content owner; do not introduce a
second global registry of pages or application objects.

The first rendering profile is declarative: templates/components plus bounded iteration,
conditionals, and escaped value substitution. Components cannot perform writes during a GET/render.
Resolve data through registered, authorized queries and pass plain values into the renderer.
Browser events invoke declared actions through the common dispatch boundary. Existing published
JavaScript assets remain available for richer client interactions.

Activation validates component references, recursion/dependency cycles, required inputs, query and
action contracts, asset references, and resource budgets. A render selects one compatible content
generation for its component tree. Cached templates/plans can be shared; audience-specific data or
rendered HTML must not be shared across incompatible scopes. Task progress and action receipts
remain distinct in both the API and the UI.

## Implementation slices

1. **Add versioned composition authoring and validation.** Extend content bundle parsing and
   draft persistence with the composition format and inspectable validation errors. Reuse the
   authoring/activation workflow from plan 02. Preserve the ability to serve existing HTML bundles.
   Outcome: a caller can author and preview a generic page from two reusable components, with
   exact content/dependency references and no application-specific C#.
2. **Render compositions on existing page routes.** Resolve publication identity through the
   existing directory, load active component revisions, materialize bounded authorized query data,
   and render the selected profile. Missing content, forbidden data, and incompatible definitions
   produce distinct readable outcomes. Keep the root/index path and asset behavior usable.
   Outcome: the server returns composed content and a page does not expose another user's data.
3. **Connect runtime data and actions.** Bind controls to the runtime query/action contracts from
   plan 01. Use existing scoped change delivery to invalidate or refresh relevant data and to show
   task progress; read authoritative state again when an operation finishes. Paginate collection
   data and retain reconnect behavior. Outcome: an action created through Codex can be invoked
   from a newly authored page, and its committed result is visible to the permitted audience.
4. **Compose the operator experience.** Add generic views for available capabilities/manuals,
   drafts and activation results, effective grants, schedules/observers, and child-task state.
   Plans 02-05 supply these records and commands. Show recoverable errors, missing inputs,
   cancellation, and the difference between a worker summary and a completed operation. Outcome:
   the user can operate the foundation through a website as well as through Codex.
5. **Verify publication and recovery across owners.** Rehearse an update to a component/query
   contract and a failed candidate publication. Keep content drafts inert until publication can
   reference them. ECS identity and content persistence have different owners; do not assume an
   arbitrary transaction spans both. Outcome: failed publication leaves the prior page usable,
   and reconciliation can identify an incomplete publication without losing assets or identity.

## Dependencies and parallel work

After the coordinator fixes the common contract vocabulary, slices 1-2 can proceed using existing
read interfaces alongside the runtime and retrieval plans. Final activation integration needs
plan 02. Action bindings need plan 01's basic call contract. Job and inner-worker presentation
depends on plans 04-05 readback contracts, but must not block the composition renderer itself.

Plan 03 owns orientation/manual semantics; the coordinator integrates MCP adapters for the
services owned by each workstream. Both clients use the same underlying services and result
semantics. Avoid simultaneous edits to the shared application endpoint file:
the agent owning the active slice supplies the change, and the coordinator integrates other adapters.

## Acceptance and recovery

Demonstrate a generic runtime-authored page at the root or a published page path, component reuse,
authorized data refresh, and a runtime-defined action. Then demonstrate a scheduled/delegated job
whose progress and committed result can be inspected from the page and from Codex. This is a
platform acceptance scenario, not an application test inventory.

Record the baseline for existing plain-HTML serving before the first change. Make persistence
changes additive where possible and let the coordinator sequence content/database migrations.
Reactivating prior compatible content restores presentation; it does not undo actions performed
through that presentation. Use the existing asset/blob retention and recovery mechanisms. This
plan adds no external API connector, mobile client, unrelated-customer tenancy, or deployment service.

## Reuse assessment

The existing host already serves root/pages/assets, resolves runtime publication identity, stores
content revisions, and delivers scoped updates. Composition and common bindings are the missing
features. A new project would still need those features and would additionally need the content,
identity, security, and dispatch integration rebuilt. Reusing these owners is the preferred path;
replace a particular renderer or adapter only if the implementation slice demonstrates that need.
