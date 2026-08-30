# D&D 2024 World tab slice 20 implementation — audience-safe chronology projection

Status: **accepted in code; live application activation pending**
Owner/roadmap: `WORLD_AND_LORE_PLAN.md`, World tab presentation
Dependency tree/leaf: `web/DND2024-WORLD-TAB-COMPLETION-DEPENDENCY-TREE.md`, C2
Ruleset alignment: **ruleset-neutral** World presentation, compatible with the dnd2024 application
Source ID and locator: not applicable; this slice adds no D&D rule or game outcome
Outcome: project authoritative dated World chronology records into the connected History screen with Player-safe and DM-safe audience shapes
Exclusions: authoring live Thalorien chronology records, editing chronology schemas, imagery, map placement, directory work, biographies, and external deployment
Allowed files/areas: a separately activated dnd2024 chronology binding and resolver contract, a generic chronology HTTP reader and its tests, the dnd2024 connected web adapter and History presentation/tests, this dependency tree, and the completion receipt
Stop point: the local connected page consumes only the chronology projection for History, handles empty or unavailable chronology truthfully, passes the stated verification, and records the explicit activation boundary

## Confirmed decisions

- The user's 2026-08-30 instruction to implement the previously proposed History integration confirms the public-surface and binding-meaning changes below.
- The public route is `GET /api/applications/{applicationId}/campaigns/{campaignId}/chronology?perspective=player|dm`.
- Player projection includes active `public` and `party` records only, uses opaque response-local entry IDs, and omits authoritative record IDs, visibility labels, and linked subjects.
- DM projection includes active `public`, `party`, and `gm` records and may include same-World linked subjects. A Player actor cannot request the DM projection.
- Authorized knowledge remains lore input and is never interpreted as dated history after this slice.
- No permanent component, relationship, procedure, or live World entity ID is added by this slice. A separate application-owned chronology metadata document carries closed vocabulary so the generic host does not hard-code dnd2024 identifiers and the already-activated audience binding does not drift.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| World chronology display | No D&D rule is calculated | `game.core.world.chronology` schema/procedure | Preserve authored dates, precision, text, status, and visibility without rules branching |
| Audience authorization | Application/campaign seat and perspective policy | existing system audience-context and application binding owners | Reuse ambient authorization; callers cannot supply an actor or visibility set |

## External implementation reference

No relevant Foundry dnd5e implementation exists for this repository-specific World chronology and audience-projection boundary. No Foundry code or data is reused.

## Prerequisite evidence

- `world/feature-19/WORLD-FEATURE-19-SLICE-1-RECEIPT.md` verifies the permanent chronology component and relationship owner.
- `catalog/components/game.core.world.chronology.schema.json` owns the closed dated record shape.
- `catalog/procedures/game/core/world/procedure.game.core.world.chronology.md` owns chronology validation and trusted-GM authoring semantics.
- Existing audience-context and authorized-knowledge endpoint tests prove the ambient seat, application binding, campaign participation, and Player/DM perspective boundary reused here.

## Runtime artifacts

- Add a separate application chronology binding document declaring component, property, relationship, clock, and visibility vocabulary. It becomes readable only after an explicit reviewed application activation.
- Add one generic HTTP endpoint at the confirmed route. It returns JSON only and performs no writes.
- Extend the connected web-server envelope with a closed chronology result.
- Make `WorldHistoryEvent.consequence` optional because the authoritative chronology owner has no consequence field.
- No database migration, MCP operation kind, schema ID, mechanic ID, fixture entity, or public TypeScript package API is added.

## Authoritative state and closed input

The route accepts only path `applicationId`, path `campaignId`, and query `perspective`. Ambient seat, role, campaign participation, application binding, state-space ID, World ID, chronology vocabulary, World clock calendar, and visibility eligibility are resolved by the backend. Callers may never supply an actor ID, World ID, component ID, relationship kind, visibility list, subject list, chronology ID, or calendar ID.

The success response is closed:

```json
{
  "status": "ready|empty",
  "perspective": "player|dm",
  "entries": [
    {
      "id": "chronology-1",
      "occurredAtMinute": 123,
      "dateLabel": "authored display date",
      "precision": "exact|approximate|era",
      "title": "authored title",
      "summary": "authored summary",
      "subjects": [{ "id": "canonical subject id", "name": "visible subject name" }]
    }
  ]
}
```

`subjects` is DM-only. The web adapter may derive presentation labels from these fields but may not recover excluded data from knowledge text.

## Behavior, result, and typed effects

1. Resolve and verify the ambient audience context, application binding, and requested campaign.
2. Permit `player` for Player or GM ambient seats; permit `dm` only for a GM seat.
3. Resolve exactly one active campaign-to-World relationship and the active World clock calendar.
4. Read bounded chronology candidates from the bound state space. Require their closed component data, one same-World scope relationship, the bound calendar, empty relationship payloads, and valid same-World linked subjects.
5. Omit archived records. Player filtering admits only `public` and `party`; DM filtering also admits `gm`.
6. Sort by `occurredAtMinute`, then canonical entity ID for deterministic ties; assign opaque ordinal response IDs after filtering.
7. Return `empty` with an empty array when there are no authorized records. Never synthesize events from knowledge.
8. The endpoint is read-only and owns no transaction or typed effect.

The History screen maps each entry to its existing timeline card. It conditionally omits a consequence section when no authoritative consequence exists and distinguishes an empty chronology from filters that match no available events.

## Failure, replay, and rollback contract

- Missing or invalid perspective/path input returns `400` without data.
- Audience denial returns `403`; an application/campaign mismatch remains undisclosed.
- Missing, malformed, ambiguous, cyclic, cross-World, cross-calendar, over-limit, or inconsistent authoritative state fails closed as `503` with no partial entries or secret details.
- Repeated reads of unchanged state return byte-equivalent ordered entry data except ordinary HTTP headers.
- The route is non-mutating, so replay causes no state change. Rollback is removal of the route/binding projection and restoration of the prior web bundle; authoritative chronology records are untouched.
- The browser converts transport or shape failure to `unavailable` with no entries and never falls back to knowledge interpretation.

## Implementation sequence

1. Add and strictly parse the separately activated application-owned chronology binding vocabulary.
2. Add the smallest generic audience-safe chronology reader and focused endpoint tests.
3. Fetch and validate the projection in the connected browser adapter.
4. Replace the History knowledge classification with chronology mapping and add truthful empty presentation/tests.
5. Run focused checks, catalog validation, the web suite/build, and the full .NET acceptance suite where the shared worktree permits it.
6. Publish the verified bundle to the already-local dnd2024 page only after exporting the current live page record, then record a receipt and mark dependency C2 code-verified with activation pending.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Player positive | active public/party events ordered and rendered; no GM event, canonical chronology ID, visibility label, or subject |
| GM positive | active public/party/GM events ordered; valid same-World subjects available |
| Authorization negative | Player requesting `dm` receives `403` before chronology disclosure |
| Scope negative | cross-World subject/scope or wrong calendar fails closed with no entries |
| Shape negative | malformed component, relationship payload, ambiguity, or over-limit returns unavailable/no partial data |
| Empty | no authorized records yields a dedicated no-dated-history message |
| Source separation | history-like knowledge remains lore and does not appear in History |
| Determinism | minute ties use canonical-ID order before opaque IDs are assigned |
| Compatibility | fixture/non-connected History remains valid; consequence cards still render where provided |
| Rollback | prior page revision and exported record remain recoverable |

## Verification commands

- Focused .NET endpoint and binding tests with an isolated output directory.
- `npm test` and `npm run build:server` in `src/system/web-interface/dnd2024`.
- `roleplay validate catalog` because application metadata changes.
- Full `dotnet test DantesRoleplay.sln --no-restore` at feature acceptance; record unrelated shared-worktree blockers precisely.
- No MCP protocol walk: this slice adds an HTTP read route and changes no MCP surface or dependency registration.

## Completion receipt and exit gate

Record accepted behavior, commands/results, publication/export evidence, and deliberate exclusions in `web/evidence/dnd2024/DND2024-WORLD-CHRONOLOGY-PROJECTION-SLICE-20-RECEIPT.md`. Mark C2 code-verified after tests prove Player non-disclosure, DM projection, source separation, deterministic ordering, truthful empty behavior, and local page publication. Do not call the live projection active until the separate chronology document is included in a reviewed application activation.
