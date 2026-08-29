# D&D 2024 web UI migration Slice 3 receipt — information-hub presentation

Completed: 2026-08-29

## Delivered boundary

- Replaced the live workspace's red dossier presentation with the prototype's dark green and gold reading surface.
- Replaced horizontal view tabs with the prototype-style information-hub sidebar: World, Campaign, Party, Current, and Rules.
- Mapped current authoritative panels without importing prototype fixtures: World contains location, people, and player-safe knowledge; Campaign preserves the campaign dossier; Party is the character sheet; Current is encounter and turn data.
- Added an explicit Rules unavailable state rather than inventing a live rules source.
- Retained existing reads, action controls, loading, unavailable, stale, and denied states.

## Evidence

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js` passed.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~WebInterfaceTests"` passed: 89 tests.
- `dotnet build DantesRoleplay.slnx --no-restore` passed: 0 warnings, 0 errors.
- `git diff --check` passed for the changed web files; Git only reported existing line-ending normalization notices.

## Deliberate exclusions

No fixture data, new server route, map, DM audience, visibility-policy change, rules content, or runtime publication is included in this source slice. Those require their own owners and an explicit deployment boundary.
