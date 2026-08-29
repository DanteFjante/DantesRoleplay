# D&D 2024 prototype audience-projection receipt

Status: accepted
Implementation document: `DND2024-PROTOTYPE-AUDIENCE-PROJECTION-IMPLEMENTATION.md`
Ruleset alignment: dnd2024-compatible

## Delivered boundary

The prototype's server-only adapter obtains its application and campaign only from the existing
ambient audience context, then reads the existing generic authorized knowledge notebook. It
projects only bounded `text`, `stance`, and `presentationKind` fields into the connected campaign
view. A non-ready, malformed, or transport-failed notebook response yields no entries. The
prototype does not enumerate generic world relationships or use Eldervale as a fallback.

## Evidence

- `npm test` — passed: 121 tests.
- `npm run build` — passed: Vinext production build.
- `dotnet build DantesRoleplay.MCPServer/DantesRoleplay.MCPServer.csproj --no-restore` — passed,
  zero warnings/errors.
- Focused server suite — passed: 31 tests across audience-context and interaction planning/query
  acceptance coverage.
- `git diff --check` — passed.

## Deliberate exclusions

No C# D&D endpoint, component/schema/catalog record, live database write, generic relationship
enumeration, location/map/faction projection, action path, or remote-hosted gateway was added.
