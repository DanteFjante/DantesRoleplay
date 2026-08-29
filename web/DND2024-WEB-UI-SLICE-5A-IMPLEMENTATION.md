# D&D 2024 web UI Slice 5A implementation — registered campaign selection and scoped reads

Status: **accepted 2026-08-27**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md),
Slice 5A read compatibility after Order 5
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable** — this slice reads existing campaign graph state and defines no D&D rule.
Outcome: A game-table page lists registered D&D campaigns at the top, loads the selected campaign's recorded summary, and offers only actors linked through that campaign's canonical participation graph. A registered historical campaign remains viewable even when its state-space binding is stale for current D&D action descriptors.
Exclusions: Campaign-folder persistence or migration; state-space binding changes; D&D rule/calculation changes; action descriptor compatibility; browser inference from names, IDs, paths, or directories; writes; catalog changes; and relationship mutation.
Allowed files/areas: The existing generic application-state read adapter, its focused web tests, the existing D&D workspace asset/tests, this plan, its dependency tree, and its receipt.
Stop point: The D&D page can select the registered Brackenford campaign, load its stored campaign data, and list only canonically linked actors. Current D&D action controls remain unavailable for stale bindings.

## Confirmed decisions

The user's 2026-08-27 instruction confirms the player-visible campaign-selection outcome and the
required read-only web surface. The generic route exposes only exact, application/state-space
scoped outbound relationships, has bounded pagination, and does not expose a D&D-specific server
operation or state model.

## Prerequisite evidence

- `dnd2024-main` is registered to `dnd2024` and contains `campaign.thalorien.brackenford` with an active `dnd2024.game.core.campaign.root` component.
- The campaign procedure defines the canonical empty-data graph: campaign root → participation via `campaign.has-character-participation`, then participation → actor via `campaign.character-participation.for-actor`.
- `IStateSpaceEdgeStore` already owns exact relationship persistence and reads; `ControlStructureExplorer` already verifies the application/state-space boundary for application-page reads.

## Runtime artifacts

- Add one generic, private, read-only application relationship page: `GET /api/applications/{applicationId}/state-spaces/{stateSpaceId}/relationships?fromEntityId=&qualifiedKind=`. It requires the exact registered application/state space and source entity, returns only matching outbound links, and uses a scope-bound cursor.
- Revise `<dnd2024-workspace>` to discover campaign roots from component summaries, follow only the two canonical D&D campaign relationship kinds, and render safe stored campaign information. Its action controls remain gated on the current descriptor and current character components.

## Authoritative state and behavior

SQLite remains authoritative. The browser supplies only selected opaque IDs obtained from bounded
responses. The generic web adapter validates the application/state-space relationship and source
entity before querying the existing edge owner. It returns relationship identity/revision metadata,
not derived campaign membership. The D&D presentation composes the two declared relationship kinds
and their retained legacy-qualified forms (`dnd2024.game.core.campaign.*`) before component
summaries; it never treats a naming convention as a relationship.

If a campaign has no active participation or a linked actor has no current character record, the
page displays the campaign data and explains that no playable current character is available. A
stale descriptor leaves the campaign readable but keeps action controls unavailable. Missing,
wrong-scope, malformed, cursor-stale, or unavailable data is read-only failure with no state change.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Campaign discovery | Registered campaign roots appear in the top selector. |
| Scope | Only exact state-space roots and only actors reached through both canonical links appear. |
| Isolation | Wrong application/state space/source entity fails; no cross-space edge appears. |
| Bounded read | Cursor/limit are validated and tied to application, state-space, source entity, and kind. |
| Stale binding | Campaign facts remain readable; current mechanics never become enabled merely because a campaign was selected. |
| No change | The route and browser component issue no writes. |

## Verification commands

- `node --check src/system/web-interface/DantesRoleplay.Web/BrowserComponents/dnd2024-workspace.js`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter "FullyQualifiedName~WebInterfaceTests" --no-restore`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Record the live read-back and focused test results in `DND2024-WEB-UI-SLICE-5A-RECEIPT.md`. The
directory/registration model remains a separate confirmed feature because it changes campaign
persistence rather than this read projection.
