# Application-aware workspace Slice G receipt — scoped page composition

Status: **accepted**  
Implemented: **2026-08-26**  
Accepted: **2026-08-26**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Implementation boundary: [Slice G implementation](WEB-APPLICATION-AWARE-WORKSPACE-SLICE-G-IMPLEMENTATION.md)

## Delivered boundary

Slice G composes the already accepted shared navigation and chat controls into the authored home,
control-center, and application-page examples without adding a route, custom element, coordinator,
capability, migration, or page/application association.

The green/vine home dashboard retains its clock, browser-local notes, application/state-space
picker, and local outer application chat. It now also includes a visibly separate general system
chat. The system element receives no application, state-space, session, provider, model, route, or
authorization binding. The application element continues to mount only after exact application and
state-space values are returned by the existing bounded structure routes.

The control-center Assistants workspace now hosts the same no-binding general system chat alongside
the existing advisory local and Codex conversations. Existing application deep links still target
`#/applications/{encodedApplicationId}`. That application workspace now offers an application-chat
selector populated only from the selected application's returned state spaces. It mounts the
existing `<application-conversation>` with the exact routed application, selected state space, and
a new bounded browser session-context ID. A selection change removes the prior element and creates
a fresh binding; an application with no state spaces mounts no chat.

The application-page fixture demonstrates one explicitly bound D&D application conversation and
one separate general system chat with no application/state-space attributes. Both accepted module
routes remain available for compatibility.

No composition automatically sends a turn, prepares an action, confirms a proposal, or executes a
system/application write. Creating an application conversation remains ephemeral process state and
the existing server rejects cross-application state-space binding.

## Verification evidence

- `WebInterfaceTests|ApplicationConversationTests`: **105 passed, 0 failed**.
- Broader `WebInterfaceTests|ApplicationConversationTests|GuardTests|CatalogCoverageTests`:
  **122 passed, 0 failed**.
- Extracted system-workspace and application-conversation JavaScript both passed `node --check`.
- `dotnet build DantesRoleplay.slnx --no-restore --nologo`: **0 warnings, 0 errors**.
- Scoped `git diff --check` passed; line-ending notices were informational only.
- A real browser loaded the authored pages from a disposable host database copied from, but never
  written back to, the normal database. The temporary host received only disposable page revisions.
- Home rendered both chats, notes, clock, shared application navigation, and the existing green/vine
  theme. Browser attribute readback showed system chat had no application/state binding while the
  application chat was exactly `dnd2024` / `dnd2024-main` with its own session context.
- Control-center Assistants rendered general system chat with no application, state-space, or
  provider attribute while retaining the existing local/Codex controls.
- Clicking shared navigation opened D&D and Trail Survival application deep links. Attribute
  readback proved exact bindings to `dnd2024-main` and `trail-survival-onboarding`; each workspace
  received a different fresh session-context ID.
- The application-page fixture rendered accessible navigation, application chat, and general system
  chat. Its system element had no application/state binding and its application element carried the
  exact declared application, state-space, and session values.
- The disposable host was stopped and its database copy and SQLite sidecars were removed. The
  normal host database and active live page revisions were not initialized, migrated, or changed.

## Deliberate exclusions and exit gate

New application pages or URL associations, live page activation, automatic turns/actions,
application/system coordinator changes, local-AI changes, game rules, ECS data, catalogs,
migrations, MCP/protocol changes, and combined full-suite/privacy acceptance remain excluded.

Slice G was accepted by the user on 2026-08-26 together with explicit direction to complete Slice H
and the whole feature. This receipt is accepted prerequisite evidence for the final boundary.
