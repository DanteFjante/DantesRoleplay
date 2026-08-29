# D&D 2024 web UI migration Slice 1 implementation — reference-first page shell

Status: **implemented; feature acceptance pending**
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 5
Dependency tree/leaf: [D&D 2024 web UI dependency plan](DND2024-WEB-UI-DEPENDENCY-PLAN.md), accepted C5 presentation composition and the existing authored `dnd2024-play` page
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. This is an HTML composition and styling change; it does not interpret a D&D rule.
Outcome: migrate the prototype's information-first game-table framing into the current D&D page source while keeping the current data-backed workspace, navigation, and application conversation components intact.
Exclusions: DM/Player audience policy or switch, new browser components, fixture data, World/Map/Rules tabs, visual attachments, routes, state reads, writes, page activation, database changes, and catalog changes.
Allowed files/areas: this implementation document and receipt, `src/system/web-interface/examples/dnd2024-play/index.html`, and focused `WebInterfaceTests` assertions.
Stop point: stop after the authored page source makes the data-backed workspace visually primary and focused verification succeeds. Do not publish or activate a page revision.

## Confirmed decisions

- The user's request to migrate the page and continue authorizes this presentation-only first slice.
- The current D&D workspace remains the source of every displayed game value. The React prototype's fixture records are not migrated.
- The existing application conversation remains available as a secondary companion; it is not positioned as the way to inspect ordinary game state.
- No new permanent ID, schema, route, state, public surface, or visibility decision is introduced.

## Prerequisite evidence

- [Slice 7B receipt](DND2024-WEB-UI-SLICE-7B-RECEIPT.md) accepted the Character/Campaign/Combat presentation composition in `<dnd2024-workspace>`.
- [Slice 7C receipt](DND2024-WEB-UI-SLICE-7C-RECEIPT.md) accepted the current-place and scene-people views.
- [combined 7D2–7D3 receipt](DND2024-WEB-UI-SLICE-7D2-7D3-RECEIPT.md) accepted the audience-safe player notebook.
- `dnd2024-play/index.html` already composes the exact current D&D workspace, shared navigation, and scoped conversation surface.

## Authoritative state and behavior

The browser continues to load all game data through the existing `<dnd2024-workspace application-id="dnd2024">` owner. The source page adds only static wrappers, labels, and CSS. It does not create browser persistence, a request, an effect, a transaction, or a display-only substitute for unavailable game data.

The workspace is framed as the main reading surface. The existing scoped conversation is visually and semantically secondary, with clear wording that it is optional help rather than a replacement for the table's direct data views. Existing progress binding from workspace to conversation remains unchanged.

## Failure and compatibility contract

- The existing loading fallback remains visible before the workspace is ready.
- Any current workspace loading, empty, unavailable, denied, action, or refresh behavior remains owned by its existing component.
- Page composition makes no network request or mutation and has no replay, stale-state, or rollback behavior of its own.
- The page does not claim DM access, hidden knowledge, maps, imagery, or world records that lack a current authoritative owner.

## Implementation sequence

1. Restyle and reframe the authored `dnd2024-play` page around the existing data-backed workspace.
2. Preserve every existing custom-element binding and workspace-progress listener.
3. Strengthen the focused page-source test to protect the reference-first composition boundary.
4. Run the page-relevant test and solution build, then record a concise receipt.

## Acceptance matrix

| Case | Required evidence |
| --- | --- |
| Primary data view | The page has a reference-first game-table shell containing the unchanged D&D workspace element. |
| Optional help | The existing application conversation remains scoped to `dnd2024` and is presented as a secondary companion. |
| Loading | The existing game-table loading fallback remains present. |
| Compatibility | Navigation, workspace module, application module, conversation module, and the progress listener remain in source. |
| Boundary | No fixture state, React dependency, D&D calculation, request, write, storage, map, image, or audience policy is added. |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~WebInterfaceTests"`
- `dotnet build DantesRoleplay.slnx --no-restore`
- `git diff --check -- web/DND2024-WEB-UI-MIGRATION-SLICE-1-IMPLEMENTATION.md src/system/web-interface/examples/dnd2024-play/index.html src/system/web-interface/tests/WebInterfaceTests.cs`

## Completion receipt and exit gate

Record source/test/build evidence in `DND2024-WEB-UI-MIGRATION-SLICE-1-RECEIPT.md`. Stop before page publishing, DM visibility, a world/map owner, or runtime editing.
