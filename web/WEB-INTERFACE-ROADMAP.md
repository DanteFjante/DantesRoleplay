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
[accepted operator control-center receipts](WEB-CONTROL-CENTER-SLICE-15-RECEIPT.md). [Slice 0 receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) established the
confirmed capability, route, and same-origin foundation; its
[receipt](WEB-CONTROL-CENTER-SLICE-0-RECEIPT.md) records acceptance and the unrelated repository
build/test exceptions. [Slice 1 receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md) delivered the
read-only shell/status presentation; its
[receipt](WEB-CONTROL-CENTER-SLICE-1-RECEIPT.md) records the unrelated repository test exception.
[Slice 2 receipt](WEB-CONTROL-CENTER-SLICE-2-RECEIPT.md) delivers committed event history and exact
operation context; its [receipt](WEB-CONTROL-CENTER-SLICE-2-RECEIPT.md) records full passing
verification. [Slice 3 receipt](WEB-CONTROL-CENTER-SLICE-3-RECEIPT.md) delivers bounded application,
ECS, exact schema/value, and explicitly public catalog exploration; its
[receipt](WEB-CONTROL-CENTER-SLICE-3-RECEIPT.md) records acceptance.
[Slice 4 receipt](WEB-CONTROL-CENTER-SLICE-4-RECEIPT.md) delivers inactive page drafts, exact
revision preview/export, optimistic publish, and immutable rollback; its
[receipt](WEB-CONTROL-CENTER-SLICE-4-RECEIPT.md) records acceptance.
[Slice 5 receipt](WEB-CONTROL-CENTER-SLICE-5-RECEIPT.md) delivers the host-owned local-completion
setting allowlist and redacted read-only panel; its
[receipt](WEB-CONTROL-CENTER-SLICE-5-RECEIPT.md) records acceptance.
[Slice 6 receipt](WEB-CONTROL-CENTER-SLICE-6-RECEIPT.md) delivers audited versioned setting
overrides, reset/rollback history, and restart-only application; its
[receipt](WEB-CONTROL-CENTER-SLICE-6-RECEIPT.md) records acceptance.
[Slice 7 receipt](WEB-CONTROL-CENTER-SLICE-7-RECEIPT.md) delivers durable operator-scoped local
advisory conversations, provider status, exact replay/recovery, and the assistant panel; its
[receipt](WEB-CONTROL-CENTER-SLICE-7-RECEIPT.md) records acceptance and the strict separation from
interaction planning/execution authority.
[Slice 8 receipt](WEB-CONTROL-CENTER-SLICE-8-RECEIPT.md) delivers pinned local Codex app-server
status, durable read-only conversations, bounded streamed output/activity, resume/recovery, and
explicit cancellation; its [receipt](WEB-CONTROL-CENTER-SLICE-8-RECEIPT.md) records acceptance and
the no-approval/no-write/no-network boundary.
[Slice 9 receipt](WEB-CONTROL-CENTER-SLICE-9-RECEIPT.md) delivers explicit, expiring, turn-scoped
Codex command, repository file-change, network, and permission approvals; its
[receipt](WEB-CONTROL-CENTER-SLICE-9-RECEIPT.md) records acceptance of the independent **Sol,
xhigh** gate and the deliberate exclusion of session-wide authority.
[Slice 10 receipt](WEB-CONTROL-CENTER-SLICE-10-RECEIPT.md) delivers a persistent sidebar, one routed
main workspace, and application structure that opens without replacing the control navigation; its
[receipt](WEB-CONTROL-CENTER-SLICE-10-RECEIPT.md) records acceptance of the client-only boundary.
[Slice 11 receipt](WEB-CONTROL-CENTER-SLICE-11-RECEIPT.md) adds the local/private root entry route
for the active control-center page while retaining the direct `/ui` page route and separate MCP
surface; its [receipt](WEB-CONTROL-CENTER-SLICE-11-RECEIPT.md) records acceptance.
[Slice 12 receipt](WEB-CONTROL-CENTER-SLICE-12-RECEIPT.md) makes the root an active home page and
adds direct page navigation from home and Site Editor; its
[receipt](WEB-CONTROL-CENTER-SLICE-12-RECEIPT.md) records acceptance and the synchronized live
page revisions. [Slice 13 receipt](WEB-CONTROL-CENTER-SLICE-13-RECEIPT.md) refreshes the reviewed
local Codex app-server pin and configures the existing development host to use the installed
standalone CLI without altering assistant authority; its
[receipt](WEB-CONTROL-CENTER-SLICE-13-RECEIPT.md) records acceptance.
[Slice 14 receipt](WEB-CONTROL-CENTER-SLICE-14-RECEIPT.md) repairs changed-content preview by
saving an inactive draft before opening the existing isolated revision preview; its
[receipt](WEB-CONTROL-CENTER-SLICE-14-RECEIPT.md) records acceptance.
[Slice 15 receipt](WEB-CONTROL-CENTER-SLICE-15-RECEIPT.md) selects the confirmed Luna model for
new Codex threads and exposes that host-owned selection as status only; its
[receipt](WEB-CONTROL-CENTER-SLICE-15-RECEIPT.md) records acceptance.

## Feature 3 — personal dashboard

[Slice 0 receipt](WEB-PERSONAL-DASHBOARD-SLICE-0-RECEIPT.md) personalizes the existing private
`home` page with the already-selected local outer chat, browser-local notes, and a local clock,
without adding a durable personal-data store or changing any backend route or AI contract; its
[receipt](WEB-PERSONAL-DASHBOARD-SLICE-0-RECEIPT.md) records active live revision 4 and verification.

## Feature 4 — application-aware private workspace

The [application-aware workspace Slice H receipt](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-H-RECEIPT.md)
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

The current owner is the
[React information-hub dependency tree](DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md).
It prioritizes reusable World data, campaign records, party and character views, the current scene,
rules reference, audience-safe knowledge, and scoped map navigation.

The [React server-bundle slice](DND2024-WEB-UI-REACT-SERVER-BUNDLE-SLICE-RECEIPT.md) supersedes
the interim custom-element host correction: `/ui/dnd2024-play` now serves the actual React
information hub and its reviewed assets directly from the DantesRoleplay server. It projects live
World and Campaign records through same-origin authorized reads, while the former iframe, hosted
Site URL, `<dnd2024-workspace>` entry page, and separate `localhost:5173` runtime are absent.
The [legacy cleanup receipt](DND2024-WEB-UI-LEGACY-CLEANUP-SLICE-RECEIPT.md) records retirement of
the old hosted slug and removal of the Sites/Vinext wrapper, superseded custom element, caches, and
completed implementation prose; durable receipts and current React owners remain.
The canonical React source now lives at `src/system/web-interface/dnd2024`; the nested prototype
repository and retired D&D source tree were removed after their live source and durable evidence
were relocated. The exact moved/deleted boundary, recovery evidence, and verification exception are
recorded in the [legacy-source cutover receipt](../ruleset/dnd2024/evidence/DND2024-LEGACY-SOURCE-CUTOVER-RECEIPT.md).

The accepted legacy browser-component slices remain recorded in their completion receipts; their
superseded implementation prose and custom-element source have been removed. The active React
information hub at `src/system/web-interface/dnd2024` owns new player/DM presentation work.
The [Exploration Current View slice](DND2024-EXPLORATION-CURRENT-VIEW-SLICE-1-IMPLEMENTATION.md)
has completed source implementation and awaits feature acceptance. It owns the bounded Current View
tab: exact actor `presence`, authorized observations and people, known exits, optional authorized
imagery, and an explicit unavailable state. Combat and Conversation remain outside that slice until
their campaign-owned selectors exist.
Scoped-map Slice 1 is accepted under its
[receipt](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-1-RECEIPT.md):
World → Region → City navigation over explicit scope links, componentized breadcrumbs, and
independent per-scope coordinate spaces, all fixture-backed.
[Slice 2](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-2-RECEIPT.md) accepts
audience-safe projection: per-layer audience policy, per-audience base variants, features that cannot
outlive their layer, and a missing Player-safe variant that fails closed instead of borrowing the DM
asset. [Slice 5](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-5-RECEIPT.md) adds
authored Location reference views, completing World → Region → City → Location, and
[Slice 7](evidence/dnd2024/DND2024-SCOPED-MAP-VIEWS-SLICE-7-RECEIPT.md) adds campaign
knowledge overlays that annotate World maps while leaving their geography byte-identical. Slice 4
needed no separate prototype deliverable. The two remaining slices — live World/Region maps and
optional reviewed generated imagery — stay blocked in the
[scoped map views dependency tree](DND2024-SCOPED-MAP-VIEWS-DEPENDENCY-TREE.md)
on live state and on approved media provenance. Encounter/Initiative authoring, weapon attack/damage, and an interactive tactical
battle map move to deferred Order 10 and do not block the player-information viewport.

The Rules reference pilot is accepted 
under its [receipt](evidence/dnd2024/DND2024-RULES-REFERENCE-PILOT-SLICE-1-RECEIPT.md). It reads the
fourteen reviewed shared activity records through exact catalog-record requests and provides
search, Action/Reaction filters, readable detail, and SRD 5.2.1 attribution. Broader curated rule
families remain planned and require their own reviewed slice.
