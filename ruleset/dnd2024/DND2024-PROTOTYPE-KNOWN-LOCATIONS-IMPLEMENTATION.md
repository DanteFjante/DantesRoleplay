# D&D 2024 prototype known-locations implementation — authorized location directory

Status: accepted
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `DND2024-PROTOTYPE-SERVER-INTEGRATION-DEPENDENCY-TREE.md` / detailed locations projection
Ruleset alignment: ruleset-neutral
Source ID and locator: not applicable; this slice defines no D&D rule
Outcome: the existing generic knowledge projection additionally returns a player-safe directory of active locations the actor demonstrably knows through currently authorized knowledge.
Exclusions: map data, location IDs, descriptions, visibility metadata, containment, routes, factions, raw graph data, browser filtering, D&D-specific C#, mutations, and fixture fallback.
Allowed files/areas: generic knowledge projection contracts/source/reader, existing generic knowledge web serialization, focused knowledge/web tests, prototype adapter/types/connected view/tests, and this implementation evidence.
Stop point: return only stable location display names plus already-authorized notebook entries; do not add navigation IDs or a location-detail endpoint.

## Confirmed decision

The user authorized continuing the audience-filtered companion projection on 2026-08-29. A location
is player-displayable only when it is active in the bound world and is the explicit subject of at
least one non-familiar knowledge document that the existing actor-aware notebook reader has already
admitted. This is an intentionally conservative discovery rule, not an interpretation of the
location component's descriptive `visibility` field.

## Prerequisite evidence

- `procedure.game.core.world.location` expressly says location visibility is descriptive until an
  authorized audience feature exists.
- `procedure.game.core.world.knowledge` makes the configured audience policy the sole player-safe
  knowledge boundary.
- `KnowledgeApplicationBinding` already carries the generic active-location component/status
  owner, and `ApplicationKnowledgeCanonicalSource` already resolves one knowledge document's
  scoped subject entity.
- `AuthorizedKnowledgeNotebookReader` already performs authorization, binding, participation,
  effective-state, validity, and hydration checks before it emits content.

## Runtime artifacts

The existing `AuthorizedKnowledgeNotebookResult` gains an identity-free `locations` collection.
Each location is `{ name, entries }`; `entries` repeat only the notebook data already allowed in
the same response. Entity IDs, component values, containment, relationship data, routes, and
visibility flags never cross this boundary. The existing `/knowledge` route serializes this field;
no route or D&D-specific host contract is added.

## Authoritative state and closed input

The server determines campaign, actor, bound world, effective knowledge state, knowledge subject,
and active-location status. Callers select none of them. A location qualifies only after the
notebook's existing source/hydration recheck succeeds and only for a non-familiar displayed entry.

## Behavior and failure contract

For every selected notebook document, the generic source records whether its existing subject is
an active location under the application's binding. The notebook reader groups only successful,
non-familiar, player-visible entries by that subject internally; it emits their trimmed subject
names and allowed entries in stable source order. Empty knowledge yields an empty directory.

Any malformed source, stale hydration, inactive/non-location subject, familiar-only entry, denied
audience, unavailable projection, malformed web response, or transport failure yields no location
data. The prototype does not infer a place from prose, visibility, IDs, or graph structure. This
read-only slice creates no effects, transaction, replay token, or rollback work.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Known fact about an active location | one named location with its already-authorized entry |
| Familiar knowledge about a location | no location name or entry disclosure |
| Known fact about a non-location/inactive location | no location entry |
| Unknown/denied/invalid/stale knowledge | no location entry |
| Browser input attempts to select a location | ignored; the adapter has no such input |
| Malformed locations response | prototype exposes no location data |

## Verification

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~KnowledgeCoreTests|FullyQualifiedName~WebInterfaceTests"
node --test test/game-server-context.test.js
npm test
npm run build
```

## Completion receipt and exit gate

Record evidence in `ruleset/dnd2024/evidence/DND2024-PROTOTYPE-KNOWN-LOCATIONS-RECEIPT.md`. Stop
after a safe directory; location detail, routes, maps, and factions require their own projection
contracts.
