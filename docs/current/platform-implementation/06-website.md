# Website composition and operator interface

Status: implementation plan, not implemented or accepted. The
[coordination plan](../PLATFORM-IMPLEMENTATION.md) defines shared contracts and scheduling; the
coordinator includes the relevant agreement with each assignment. This file owns the website
workstream. Initial integrations are the website and Codex. Application-specific
pages and DND2024 behavior are outside this plan.

Prerequisite: implement [00 — Shared foundation](00-shared-foundation.md) first and have the coordinator supply its accepted foundation revision and contract baseline. This workstream consumes those shared contracts and does not redefine them independently.

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
