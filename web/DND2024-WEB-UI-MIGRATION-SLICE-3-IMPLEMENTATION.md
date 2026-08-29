# D&D 2024 web UI migration Slice 3 implementation — information-hub presentation

Status: **completed — publication is the separately authorized Slice 4 boundary**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), accepted C5 player viewport composition
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. The slice moves only browser presentation and does not alter a D&D rule.
Outcome: replace the legacy red dossier presentation inside the live workspace with the prototype information hub's green-and-gold theme, sidebar-oriented hierarchy, and World/Campaign/Party/Current/Rules information architecture.
Exclusions: prototype fixtures, React/Next runtime adoption, DM visibility or audience selection, maps, location details beyond existing current-place projection, rules-data expansion, new reads/routes, writes, catalog/game-state change, and page publication.
Allowed files/areas: `dnd2024-workspace.js`, focused web source-contract tests, and this implementation document/receipt.
Stop point: stop after existing panels are reparented under the information-hub navigation and retain their current data/action behavior. Unsupported data surfaces must state their actual availability instead of rendering fixture content.

## Confirmed decisions

- The user clarified that the whole information-hub experience—not merely its outer frame—must be migrated.
- Existing live data is authoritative. The prototype is a visual/component reference, not data to import.
- The local fixed player audience remains unchanged; a non-functional DM switch is forbidden.

## Authoritative state and behavior

The current workspace remains the sole owner of its state selection, data reads, action controls, and panel rendering. This slice changes only closed, disposable presentation keys and DOM composition:

- **World** groups current location, co-present people, and the existing player-safe knowledge notebook.
- **Campaign** retains its registered campaign dossier.
- **Party** contains the current character sheet.
- **Current** contains the accepted encounter and turn-resource panels.
- **Rules** reports that no standalone live rules-reference projection is connected; it exposes no fixture summaries.

The initial view is Party to preserve the existing player-first character behavior. Selection lives only in memory and never changes the selected campaign, actor, audience, or game state.

## Failure and compatibility contract

- Existing empty, unavailable, denied, stale, loading, action review, and server-result states remain unchanged inside their panels.
- View changes make no new request or write.
- The Rules empty state cannot imply implemented content or supply remembered rule text.
- No DM-only content can appear because this slice never expands a request or audience policy.

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~WebInterfaceTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- focused browser smoke at `/ui/dnd2024-play` after a reviewed page/component deployment boundary

## Completion receipt and exit gate

Record presentation mapping and verification in `DND2024-WEB-UI-MIGRATION-SLICE-3-RECEIPT.md`. Stop before publishing the page or adding new data capability.
