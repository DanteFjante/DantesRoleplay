# D&D 2024 local table DM seat Slice 1 implementation — server-authorized companion view

Status: **implementation complete; feature acceptance pending 2026-08-30**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5
Dependency tree/leaf: `DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, accepted server-issued audience foundation
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable**. This is a local audience and presentation boundary; it defines no game rule.
Outcome: authorize one fixed Game Master seat only for the loopback local game server and render the connected companion in its DM perspective.
Exclusions: remote authorization, browser-selected identity/campaign/role, database mutation or migration, new D&D mechanics/content, fixture fallback, and a player-safe projection for a shared DM response.
Allowed files/areas: local knowledge-seat configuration and policy, audience-context response, focused C# and prototype tests, live context adapter/envelope/type, and this receipt pair.
Stop point: stop once the local companion opens as DM from an ambient Game Master grant and the direct server still denies non-loopback requests.

## Confirmed decisions

- The user explicitly authorized this machine's local table to use the DM view and DM-visible campaign knowledge.
- The Game Master identity is server configuration, not a query parameter, browser setting, or forwarded header.
- The local DM presentation has no player-view toggle because the server may have supplied Game Master-visible data to that local browser.

## Prerequisite evidence

- `KnowledgeAudienceRole.GameMaster` is the existing generic authorization role, and `AuthorizedKnowledgeNotebookReader` already uses it to read the campaign notebook without actor-state filtering.
- `LocalKnowledgeAudiencePolicy` already enforces both host configuration and loopback transport before returning an audience grant.
- `DND2024-WEB-RUNTIME-INTEGRATION-SLICE-1-IMPLEMENTATION.md` establishes that `readGameServerContext` is the sole live companion reader and that its former player-only adapter was an explicit temporary limitation.

## Runtime artifacts

- Revise the local seat snapshot to carry an existing generic `KnowledgeAudienceRole` and accept a Game Master seat with no actor ID.
- Revise `system.audience-context` to return `role: "game-master"` with no actor identifier for that existing role; actor behavior and character-creation behavior remain unchanged.
- Revise the connected prototype envelope to map `game-master` to its existing `dm` presentation. The mapping belongs in the D&D prototype, not the generic C# host.

## Authoritative state and closed input

- `Knowledge:LocalPlayer` in the local host configuration is the only identity/role source.
- The game server resolves campaign, application, and role before the companion receives any data. Browser requests carry no audience selectors.
- A Game Master grant must have no actor ID; an actor grant must have one. Application binding remains required for both, and participation remains required only for actor grants.

## Behavior and failure contract

- A valid loopback Game Master seat returns the bound application, state space, campaign, and `game-master` role. It does not return an actor ID.
- The prototype requests only the returned campaign, then reads the existing server-filtered notebook. It uses a UI-only `Dungeon Master` label rather than fabricating an actor entity.
- Non-loopback, disabled, malformed, wrong-campaign, or mismatched-role seats deny before binding/state reads. Player behavior remains unchanged.
- If context or any required campaign response is missing, malformed, or unavailable, the existing unavailable/denied screen remains; no fixture data is rendered.

## Implementation sequence

1. Extend the existing generic local-seat configuration and audience-context verification for the existing Game Master role.
2. Configure the local development host's one fixed seat as Game Master and adapt the prototype's server-only live reader/envelope.
3. Add focused tests for the Game Master grant/context and the DM live envelope, then rebuild and verify the local URL.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| loopback configured Game Master | bound context, no actor ID, DM envelope, Game Master notebook access |
| loopback configured actor | unchanged actor context and participation check |
| remote/missing peer or malformed seat | denied before state reads |
| client changes URL/body/headers | cannot select audience values because no selector is accepted |
| unavailable/malformed live data | existing unavailable response, no fixture data |

## Verification commands

- `dotnet test --no-restore --filter "FullyQualifiedName~LocalKnowledgeAudienceTests|FullyQualifiedName~SystemAudienceContextToolsTests"`
- `npm test`
- `npm run build`
- local HTTP checks for `/api/audience-context` and `/ui/dnd2024-play`

## Completion receipt and exit gate

Record the exact configured local-only boundary, tests, and browser evidence in `DND2024-LOCAL-TABLE-DM-SEAT-SLICE-1-RECEIPT.md`. Do not add remote identity, a user-selectable seat, or non-live DM fixture content in this slice.

Implementation evidence is recorded in
[`DND2024-LOCAL-TABLE-DM-SEAT-SLICE-1-RECEIPT.md`](DND2024-LOCAL-TABLE-DM-SEAT-SLICE-1-RECEIPT.md).
The runtime boundary is complete; feature acceptance remains a separate confirmation gate.
