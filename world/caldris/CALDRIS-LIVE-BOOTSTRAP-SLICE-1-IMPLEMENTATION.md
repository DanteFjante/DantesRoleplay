# Caldris live bootstrap slice 1 — selectable world and opening region

Status: **accepted**
Owner/roadmap: Campaign C10 composition boundary and application World state
Dependency tree/leaf: C10 R3 cross-root ratification; application ECS typed-effect root
Ruleset alignment: `dnd2024-compatible` authored setting state; no D&D rule is implemented
Source ID and locator: not applicable; this slice creates setting identity and prose only
Outcome: Caldris and The Measure of Mercy are selectable in the D&D 2024 website
Exclusions: actors, quests, encounters, rules, full lore import, current scene, and application binding changes
Allowed files/areas: `world/caldris/` review manifest/tooling and the confirmed live `dnd2024-main` state space
Stop point: verified selector readback for the world, campaign, and seven opening locations

## Confirmed decisions

The user confirmed the permanent IDs `world.caldris` and
`campaign.caldris.measure-of-mercy`, their display names, and creation in live state. The opening
package is additive. No existing campaign, binding, schema, or public protocol kind is changed.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Ruleset selection | D&D 2024 application profile only | active `dnd2024` application | Use installed application component types; add no rules |
| Setting prose | Not an SRD rule | Caldris review documents | Store presentation state only |

## External implementation reference

No Foundry rule implementation is relevant because this slice performs no D&D calculation,
eligibility decision, roll, effect, or character operation.

## Prerequisite evidence

- `WorldCampaignSelector.tsx` consumes the server's live world/campaign selection.
- `game-server-context.js` recognizes an entity with
  `dnd2024.game.core.campaign.root` and groups it by the campaign ID's world segment.
- `ApplicationEcsEffectApplier` is the existing atomic application-state mutation boundary.
- The current public `system.world-state.sync` correctly refuses a new World root, so this slice
  does not weaken that contract.
- The planned C10 composition source is absent from this checkout even though historical receipts
  describe it. Current code is authoritative, so the bootstrap remains a private synchronization
  boundary rather than claiming C10 completion.

## Runtime artifacts

- Reviewed manifest: `runtime/caldris-live-bootstrap-v1.json`.
- Ruleset-neutral one-use runner: `runtime/bootstrap-tool/`.
- Live entities: one World root, one Campaign root, two continents, three opening settlements, and
  two Bramblebridge sites.
- Relationships: campaign in World; campaign references Bramblebridge as the party-visible start.

## Authoritative state and closed input

The manifest fixes the application, state space, IDs, names, component values, containment, and
relationships. The runner resolves installed component versions and schema hashes from the live
registry; the manifest cannot supply them. The runner rejects a state-space/application mismatch,
unknown component type, an unexpected pre-existing target, non-empty expected revision, or a
relationship kind outside the selected application namespace. An exact successful replay returns
the earlier receipts before attempting the effects.

## Behavior, result, and typed effects

The runner creates a consistent SQLite backup, hashes the manifest, derives deterministic preview
and commit operation IDs, performs the exact batch as a dry run, and commits only if the preview is
valid. One transaction owns all entities, components, containments, relationships, and audit state.

## Failure, replay, and rollback contract

Malformed or stale input stops before commit. Any failed effect rolls back the whole batch. A
successful rerun returns the existing deterministic operation receipt rather than creating a
second graph. The backup remains available even if validation fails.

## Implementation sequence

1. Record the closed manifest and generic runner.
2. Build the runner without changing the server surface.
3. Verify live absence and create a consistent backup.
4. Run preview and atomic commit.
5. Read all delivered records through the same web API used by the selector.
6. Record a compact receipt and stop.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Positive | preview valid; commit applied atomically |
| Selector | Caldris contains The Measure of Mercy |
| Boundary | existing Thalorien campaign remains readable |
| Schema | all component values accepted by installed schemas |
| Replay | same commit operation is replay-safe |
| Rollback | effect applier owns one transaction |
| Backup | pre-write SQLite backup exists and opens read-only |
| Surface | MCP capability catalog is unchanged |

## Verification commands

- Build and run the bootstrap runner against the explicit live database and manifest.
- Read every authored entity/component plus application relationships/containments over loopback.
- Read the D&D website context-selection endpoint and confirm both worlds are present.

## Completion receipt and exit gate

Acceptance is recorded in `CALDRIS-LIVE-BOOTSTRAP-SLICE-1-RECEIPT.md`. Stop before importing the
wider cast, lore, quests, chronology, maps, or current-scene state.
