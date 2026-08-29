# D&D 2024 web UI migration Slice 1 receipt — reference-first page shell

Status: **implemented; feature acceptance pending**
Implementation: [migration Slice 1](DND2024-WEB-UI-MIGRATION-SLICE-1-IMPLEMENTATION.md)
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Ruleset alignment: **dnd2024-compatible**

## Delivered boundary

The authored `dnd2024-play` source page now treats its existing live D&D workspace as the primary
game reference surface. It has a responsive game-table frame, a concise reference-first heading,
and an explicit loading fallback. The existing navigation, workspace module, application module,
and workspace-to-conversation state-space binding are unchanged.

The scoped D&D conversation remains available as an **Optional help** companion. Its copy now
distinguishes it from ordinary data viewing: character, campaign, scene, combat, and player-safe
knowledge remain the responsibility of the current data-backed workspace.

No prototype fixture record, React dependency, D&D rule, map/world/Rules tab, DM visibility switch,
route, browser storage entry, backend state read, write, catalog artifact, database record, or page
activation was added. The user's existing unrelated prototype-integration worktree changes were
left untouched.

## Evidence

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~WebInterfaceTests"` passed: **89/89**.
- `dotnet build DantesRoleplay.slnx --no-restore` passed with **0 warnings** and **0 errors**.
- `git diff --check` passed for the tracked migration changes.
- The focused page source contract now verifies the unchanged live custom elements plus the new
  reference-first framing and secondary-help label.

## Deliberate stop

The changed HTML is a reviewed source-page revision only. Publishing it would change the runtime
SQLite page revision, so it remains unactivated. The next migration slice should be a separately
confirmed live page revision export/review/publish, or an independently owned World/Map or DM
audience contract—not a copy of prototype fixture behavior.
