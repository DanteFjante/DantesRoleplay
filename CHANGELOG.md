# Changelog

## 2026-09-05

### Fixed

- Accept multiline outer AI messages and tabs while retaining input size and control-character checks.
- Prevent compiled JSON Schemas from accumulating in the global registry across AI, event, and validation requests.
- Load the saved campaign and Player/DM preference on the first page request, avoiding duplicate world loads.
- Keep the current view when required world information fails to load instead of displaying an incomplete map hierarchy.
- Keep a child location from becoming the world map when its parent map image is unavailable.
- Show a retry action when a map image fails to load, and mark maps ready only after their images load.
- Request a fresh Player character projection for GM previews without including GM media or reusing a DM dossier.
- Display character abilities across the full panel, with six columns on wide screens and three on smaller screens.

### Performance

- Reuse successful schema compilations in a bounded least-recently-used cache keyed by exact schema text and profile. Limits are 256 entries, 2 MiB of retained schema text, and 32,000 schema nodes; values and validation results are checked afresh.
- Limit each world load to eight simultaneous requests and reuse bounded entity listings within that load only.
- Separate API read limits from page and media loads, allow 6,000 reads per minute in each group, and return `Retry-After` when throttled. Write and stream limits remain unchanged.

### Changed

- Support up to 500 world chronology entries, with matching server and browser bounds.
- Default local development launches to a GameMaster seat for DM and Player views; `run-mcp-server.ps1 -Role Actor` retains player-only sessions.
- Update catalog manifest versions and include the existing location furnishing component definition.
- Document schema registry lifetime, cache limits, audience-specific projections, and web loading behavior; add regression coverage for these changes.
