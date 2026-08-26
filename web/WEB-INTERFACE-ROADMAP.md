# Web interface roadmap

Status: **Feature 1 complete — Feature 2 complete (Slices 0–15 accepted)**
Last updated: 2026-08-24

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
