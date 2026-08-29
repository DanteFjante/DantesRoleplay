# Application-page association Slice 1 receipt — automatic direct application pages

Status: **implemented; acceptance confirmation pending 2026-08-27**
Implementation: [Slice 1 implementation](WEB-APPLICATION-PAGE-ASSOCIATION-SLICE-1-IMPLEMENTATION.md)
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md)
Ruleset alignment: **ruleset-neutral**

## Delivered boundary

Registered applications now have a deterministic direct page URL:
`/ui/<application-id>-play`. Shared `<system-navigation>` uses that URL for every discovered
application instead of opening the control-center hash route.

The existing page route first serves an active authored page. If none exists, it recognizes only an
exact valid `-play` page ID, confirms the derived application exists in the registry, and serves a
safe generated landing page using registered display metadata. Thus D&D keeps its authored
`/ui/dnd2024-play` game viewport, while another registered application such as Trail Survival now
receives a direct landing page at `/ui/trail-survival-play` without a page upload. An unknown or
unregistered derived ID remains 404.

No application registration payload, registry record, page record, page revision, migration,
route, MCP surface, state space, source profile, catalog, game rule, or game-state data changed.
The association is a generic route convention, not persisted ownership and not a D&D special case.

## Evidence

- Focused web tests passed: **89/89**. They prove direct navigation, registered generated landing
  pages, authored-page precedence, invalid/unregistered 404 behavior, and no control-center
  content in a generated page.
- `dotnet build DantesRoleplay.slnx --no-restore` passed with **0 warnings and 0 errors**.
- `git diff --check` found no whitespace error for this change; it reported pre-existing line-ending
  warnings on unrelated worktree files.
- The restarted local host returned HTTP **200** for `/ui/dnd2024-play` with the D&D workspace and
  for `/ui/trail-survival-play` with the generic direct landing page. Its live navigation asset
  contains direct `-play` links and no longer contains control-center application deep links.
  `/ui/not-real-play` returned **404**.

## Deliberate exclusions and exit gate

Persisted custom page ownership, page-upload automation, templates editable through application
registration, creating state spaces, migrating old campaigns, and game-specific page generation
remain separate work. Mark this slice accepted only after user confirmation.
