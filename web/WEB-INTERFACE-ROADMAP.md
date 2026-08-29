# Web interface roadmap

Status: **Features 1–4 accepted selected scope; Feature 5 D&D 2024 workspace in progress**
Last updated: 2026-08-27

## Outcome

Provide a small local web interface whose pages are ordinary user-authored HTML documents. A page
may contain CSS and JavaScript, use browser-native composition, fetch arbitrary JSON component data,
and later subscribe to committed changes. Updating a page must not require compiling or restarting
the host.

## Confirmed boundaries

- `DantesRoleplay.Web` is a ruleset-neutral system project hosted by the existing ASP.NET process.
- SQLite stores append-only HTML page revisions and the active revision pointer.
- Uploaded HTML is trusted executable content. Authentication, isolation, CSP hardening, and
  sandboxing are deliberately deferred.
- A dynamic read endpoint accepts a data type and entity ID. The reserved `entity` type returns a
  generic entity envelope; every other type is an existing component-definition ID and returns
  that component's JSON object without a compile-time response model.
- The web layer reads state through `IWorldStore`. It never reads tables directly and never owns
  game rules or game-state writes.
- HTML itself owns layout and nesting. There is no page-layout JSON vocabulary, SPA framework,
  Node toolchain, frontend build, or server-side component renderer.

## Ordered delivery

| Slice | State | Capability |
| --- | --- | --- |
| 1 | accepted | Versioned HTML upload/serving plus dynamic entity/component JSON reads. |
| 2 | accepted | Versioned ZIP bundles containing `index.html` and revision-scoped static assets. |
| 3 | accepted | SSE invalidation and optional live page-revision notification. |
| 4 | accepted | Local single-user access boundary, trusted-content policy, CSP, and quotas. |
| 5 | accepted | Private Tailscale identity and remote access with MCP excluded. |

## Current implementation owner

[Slice 5 receipt](WEB-INTERFACE-SLICE-5-RECEIPT.md) verifies the selected private remote access
boundary. [Slice 4 receipt](WEB-INTERFACE-SLICE-4-RECEIPT.md) remains its local prerequisite.

## Feature 2 — operator control center

The requested settings, activity, assistant, ECS/contracts, and site-editing panels cross existing
owners and are planned in the
[operator control-center dependency plan](WEB-CONTROL-CENTER-DEPENDENCY-PLAN.md). [Slice 0](WEB-CONTROL-CENTER-SLICE-0-IMPLEMENTATION.md) established the
confirmed capability, route, and same-origin foundation; its
[receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) records acceptance and the unrelated repository
build/test exceptions. [Slice 1](WEB-CONTROL-CENTER-SLICE-1-IMPLEMENTATION.md) delivered the
read-only shell/status presentation; its
[receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md) records the unrelated repository test exception.
[Slice 2](WEB-CONTROL-CENTER-SLICE-2-IMPLEMENTATION.md) delivers committed event history and exact
operation context; its [receipt](WEB-CONTROL-CENTER-SLICE-2-RECEIPT.md) records full passing
verification. [Slice 3](WEB-CONTROL-CENTER-SLICE-3-IMPLEMENTATION.md) delivers bounded application,
ECS, exact schema/value, and explicitly public catalog exploration; its
[receipt](WEB-CONTROL-CENTER-SLICE-3-RECEIPT.md) records acceptance.
[Slice 4](WEB-CONTROL-CENTER-SLICE-4-IMPLEMENTATION.md) delivers inactive page drafts, exact
revision preview/export, optimistic publish, and immutable rollback; its
[receipt](WEB-CONTROL-CENTER-SLICE-4-RECEIPT.md) records acceptance.
[Slice 5](WEB-CONTROL-CENTER-SLICE-5-IMPLEMENTATION.md) delivers the host-owned local-completion
setting allowlist and redacted read-only panel; its
[receipt](WEB-CONTROL-CENTER-SLICE-5-RECEIPT.md) records acceptance.
[Slice 6](WEB-CONTROL-CENTER-SLICE-6-IMPLEMENTATION.md) delivers audited versioned setting
overrides, reset/rollback history, and restart-only application; its
[receipt](WEB-CONTROL-CENTER-SLICE-6-RECEIPT.md) records acceptance.
[Slice 7](WEB-CONTROL-CENTER-SLICE-7-IMPLEMENTATION.md) delivers durable operator-scoped local
advisory conversations, provider status, exact replay/recovery, and the assistant panel; its
[receipt](WEB-CONTROL-CENTER-SLICE-7-RECEIPT.md) records acceptance and the strict separation from
interaction planning/execution authority.
[Slice 8](WEB-CONTROL-CENTER-SLICE-8-IMPLEMENTATION.md) delivers pinned local Codex app-server
status, durable read-only conversations, bounded streamed output/activity, resume/recovery, and
explicit cancellation; its [receipt](WEB-CONTROL-CENTER-SLICE-8-RECEIPT.md) records acceptance and
the no-approval/no-write/no-network boundary.
[Slice 9](WEB-CONTROL-CENTER-SLICE-9-IMPLEMENTATION.md) delivers explicit, expiring, turn-scoped
Codex command, repository file-change, network, and permission approvals; its
[receipt](WEB-CONTROL-CENTER-SLICE-9-RECEIPT.md) records acceptance of the independent **Sol,
xhigh** gate and the deliberate exclusion of session-wide authority.
[Slice 10](WEB-CONTROL-CENTER-SLICE-10-IMPLEMENTATION.md) delivers a persistent sidebar, one routed
main workspace, and application structure that opens without replacing the control navigation; its
[receipt](WEB-CONTROL-CENTER-SLICE-10-RECEIPT.md) records acceptance of the client-only boundary.
[Slice 11](WEB-CONTROL-CENTER-SLICE-11-IMPLEMENTATION.md) adds the local/private root entry route
for the active control-center page while retaining the direct `/ui` page route and separate MCP
surface; its [receipt](WEB-CONTROL-CENTER-SLICE-11-RECEIPT.md) records acceptance.
[Slice 12](WEB-CONTROL-CENTER-SLICE-12-IMPLEMENTATION.md) makes the root an active home page and
adds direct page navigation from home and Site Editor; its
[receipt](WEB-CONTROL-CENTER-SLICE-12-RECEIPT.md) records acceptance and the synchronized live
page revisions. [Slice 13](WEB-CONTROL-CENTER-SLICE-13-IMPLEMENTATION.md) refreshes the reviewed
local Codex app-server pin and configures the existing development host to use the installed
standalone CLI without altering assistant authority; its
[receipt](WEB-CONTROL-CENTER-SLICE-13-RECEIPT.md) records acceptance.
[Slice 14](WEB-CONTROL-CENTER-SLICE-14-IMPLEMENTATION.md) repairs changed-content preview by
saving an inactive draft before opening the existing isolated revision preview; its
[receipt](WEB-CONTROL-CENTER-SLICE-14-RECEIPT.md) records acceptance.
[Slice 15](WEB-CONTROL-CENTER-SLICE-15-IMPLEMENTATION.md) selects the confirmed Luna model for
new Codex threads and exposes that host-owned selection as status only; its
[receipt](WEB-CONTROL-CENTER-SLICE-15-RECEIPT.md) records acceptance.

## Feature 3 — personal dashboard

[Slice 0](WEB-PERSONAL-DASHBOARD-SLICE-0-IMPLEMENTATION.md) personalizes the existing private
`home` page with the already-selected local outer chat, browser-local notes, and a local clock,
without adding a durable personal-data store or changing any backend route or AI contract; its
[receipt](WEB-PERSONAL-DASHBOARD-SLICE-0-RECEIPT.md) records active live revision 4 and verification.

## Feature 4 — application-aware private workspace

The [application-aware workspace dependency plan](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md)
is complete. Slice A's reviewed `dnd2024` and `trail-survival` live registrations, activations, and
initial state spaces are accepted under its
[receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-RECEIPT.md). Slice B's shared browser-native
navigation foundation is accepted under its
[receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-B-RECEIPT.md). Slice C's reusable system-capability
read catalog is accepted under its
[receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-C-RECEIPT.md). Slice D's read-only general system
chat is accepted under its
[receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-D-RECEIPT.md), with exact durable scope, bounded
provenance-bearing context, private routes, and migration evidence.
Confirmed system administration and application-page composition remain in ordered Slices E–H
with separate system/application authority. Slice E's exact task-orchestration artifacts are
[accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-E-RECEIPT.md). Slice F's reusable action and form
components are [accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-F-RECEIPT.md). Slice G's scoped page
composition is [accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-G-RECEIPT.md). Slice H's final
combined acceptance, live activation, and accepted-boundary chat corrections are
[accepted](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-RECEIPT.md).

The user-confirmed application-to-page association is now implemented by
[Application-page association Slice 1](WEB-APPLICATION-PAGE-ASSOCIATION-SLICE-1-IMPLEMENTATION.md):
each registered application has the deterministic direct URL `/ui/<application-id>-play`, with an
authored page taking precedence over a safe generated landing page. Its
[receipt](WEB-APPLICATION-PAGE-ASSOCIATION-SLICE-1-RECEIPT.md) records focused route/navigation
evidence; feature acceptance remains pending.

## Feature 5 — D&D 2024 player and GM workspace

The [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md) inventories the
existing shared web components and current D&D catalog owners, defines a ruleset-neutral
application read/action seam, and now prioritizes a player-information viewport: character sheet,
current place and imagery, remembered player-safe knowledge, and switchable people in the current
scene. Accepted inventory and encounter controls remain available but are secondary to game context.
It deliberately leaves spells, monsters, tactical battle maps, rests, dying, Inspiration use, magic
items, and complete character construction behind their independent D&D gameplay gates.

Order 0 is confirmed. [Slice 1](DND2024-WEB-UI-SLICE-1-IMPLEMENTATION.md) and its
[receipt](DND2024-WEB-UI-SLICE-1-RECEIPT.md) accept the private exact application-state read seam,
reviewed browser-component asset host, `<dnd2024-workspace>` game HUD, and authored
`dnd2024-play` page. The page is not live-activated and has no action/write controls. [Slice 2A](DND2024-WEB-UI-SLICE-2A-IMPLEMENTATION.md) and its
[receipt](DND2024-WEB-UI-SLICE-2A-RECEIPT.md) add the character profile/Size/experience dossier and
selected-encounter Initiative/turn cards without calculations or writes. [Slice 2B](DND2024-WEB-UI-SLICE-2B-IMPLEMENTATION.md)
and its [receipt](DND2024-WEB-UI-SLICE-2B-RECEIPT.md) accept a private paged direct-containment read
plus game-styled direct carried-item cards with exact identity, quantity, equipment state, slot, and
custody revision. [Slice 2C](DND2024-WEB-UI-SLICE-2C-IMPLEMENTATION.md) and its
[receipt](DND2024-WEB-UI-SLICE-2C-RECEIPT.md) now accept explicitly published activated entity
records and exact item-definition facts/provenance on those cards. [Slice 2D](DND2024-WEB-UI-SLICE-2D-IMPLEMENTATION.md)
and its [receipt](DND2024-WEB-UI-SLICE-2D-RECEIPT.md) complete the bounded read-only nested
containment tree with explicit depth, entry, and page cutoffs. Plus/minus mutations, dice/check
execution, inventory actions, and encounter mutations remain planned. [Slice 3](DND2024-WEB-UI-SLICE-3-IMPLEMENTATION.md)
and its [receipt](DND2024-WEB-UI-SLICE-3-RECEIPT.md) accept the ruleset-neutral exact mechanic
descriptor and private prepare/explicit-confirm/execute boundary over the existing interaction,
action, transaction, replay, and receipt owners. No browser control was added: the next action-side
leaf was the generic game-styled entity picker/button/form surface. [Slice 4](DND2024-WEB-UI-SLICE-4-IMPLEMENTATION.md)
and its [receipt](DND2024-WEB-UI-SLICE-4-RECEIPT.md) now accept that reusable review-first control
layer without binding a D&D mechanic or adding a raw JSON form. [Slice 5](DND2024-WEB-UI-SLICE-5-RECEIPT.md)
implements the purpose-built dice, raw ability-check, and saving-throw controls over that layer and
awaits feature-acceptance confirmation; the read-only inventory remainder is complete.
[Slice 5A](DND2024-WEB-UI-SLICE-5A-RECEIPT.md) implements top-level registered campaign selection,
campaign-scoped actor discovery through canonical relationships, and readable legacy campaign state
without enabling stale D&D action controls. Both Slice 5 and Slice 5A are accepted.
[Slice 6A](DND2024-WEB-UI-SLICE-6A-RECEIPT.md) accepts healing and Temporary HP controls. The
[Slice 6B](DND2024-WEB-UI-SLICE-6B-RECEIPT.md) accepts direct-item equip/unequip card controls. The
final [Slice 6C](DND2024-WEB-UI-SLICE-6C-RECEIPT.md) accepts ordinary transfer, stack quantity, and
descriptor-authored item-use controls; administrative bootstrap/move helpers remain outside the
player game UI. [Slice 7A](DND2024-WEB-UI-SLICE-7A-RECEIPT.md) accepts recorded encounter
selection, turn lifecycle, and spendable turn-resource controls. [Slice 7B](DND2024-WEB-UI-SLICE-7B-IMPLEMENTATION.md)
and its [receipt](DND2024-WEB-UI-SLICE-7B-RECEIPT.md) accept the player-first
Character/Campaign/Combat viewport composition. [Slice 7C](DND2024-WEB-UI-SLICE-7C-IMPLEMENTATION.md)
and its [receipt](DND2024-WEB-UI-SLICE-7C-RECEIPT.md) accept the exact direct-parent containment
read and Scene view for current place and co-present people. [Slice 7D0](DND2024-WEB-UI-SLICE-7D0-RECEIPT.md)
accepts the provider-neutral authorized knowledge core in current modular owners. [Slice 7D1](DND2024-WEB-UI-SLICE-7D1-RECEIPT.md)
accepts the fixed loopback Orban player seat, catalog-owned D&D binding, and exact participation
proof. The [combined 7D2–7D3 batch](DND2024-WEB-UI-SLICE-7D2-7D3-RECEIPT.md) now accepts the
reviewed live Orban knowledge state and game-styled, campaign-scoped Knowledge viewport with eleven
safe lore entries, search/filter controls, keyboard navigation, and excluded-secret verification.
Orders 7E–7F retain a display-only known-place map and reviewed location/person imagery as their
independent authority gates close. In the separate information-hub prototype, the scoped map
workspace's [Slice 1](../prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-SLICE-1-IMPLEMENTATION.md)
is accepted under its [receipt](../prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-SLICE-1-RECEIPT.md):
World → Region → City navigation over explicit scope links, componentized breadcrumbs, and
independent per-scope coordinate spaces, all fixture-backed.
[Slice 2](../prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-SLICE-2-IMPLEMENTATION.md) and its
[receipt](../prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-SLICE-2-RECEIPT.md) accept
audience-safe projection: per-layer audience policy, per-audience base variants, features that cannot
outlive their layer, and a missing Player-safe variant that fails closed instead of borrowing the DM
asset. [Slice 5](../prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-SLICE-5-RECEIPT.md) adds
authored Location reference views, completing World → Region → City → Location, and
[Slice 7](../prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-SLICE-7-RECEIPT.md) adds campaign
knowledge overlays that annotate World maps while leaving their geography byte-identical. Slice 4
needed no separate prototype deliverable. The two remaining slices — live World/Region maps and
optional reviewed generated imagery — stay blocked in the
[scoped map views dependency tree](../prototype/dnd2024/planning/DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md)
on live state and on approved media provenance. Encounter/Initiative authoring, weapon attack/damage, and an interactive tactical
battle map move to deferred Order 10 and do not block the player-information viewport.
