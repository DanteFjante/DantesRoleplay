# D&D 2024 World tab slice 21 implementation — live Thalorien DM chronology

Status: **accepted**
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, World History
Dependency tree/leaf: `web/DND2024-WORLD-TAB-COMPLETION-DEPENDENCY-TREE.md`, dedicated dated World chronology
Ruleset alignment: **ruleset-neutral**
Source ID and locator: **not applicable**; this slice authors campaign World history and does not implement a D&D rule
Outcome: activate the reviewed chronology projection binding and author the 35 existing Thalorien turning points as dedicated DM chronology records so the live DM History tab renders them
Exclusions: Player visibility decisions, new lore, rewriting or deleting knowledge, campaign recaps, rule changes, schema changes, and a knowledge-as-history fallback
Allowed files/areas: this implementation document and receipt; the reviewed D&D application activation; additive live `dnd2024-main` chronology entities and their World-scope relationships; the stale History envelope validator, its focused regression test, and the republished local page bundle
Stop point: the live DM chronology route returns the 35 reviewed entries, the Player route discloses none of them, the History tab renders the DM entries, and readback evidence is recorded

## Confirmed decisions

- The user's repeated request to make the missing DM History visible confirms activation and additive live authoring within this boundary.
- Permanent entity IDs use `chronology.thalorien.dm.` plus a stable title slug.
- Existing authorized knowledge supplies titles and summaries; the accepted presentation table supplies ordering and authored date labels.
- All entries are `gm` visibility in this slice. Player publication remains a separate decision.
- The current valid application preview is activated even though it includes other pending catalog changes; the user chose immediate visibility after that impact was disclosed.

## D&D 5e 2024 alignment

No D&D rule, formula, action, eligibility decision, content definition, or game-specific C# branch is added. Existing generic World chronology and application-activation owners remain authoritative.

## External implementation reference

No relevant Foundry dnd5e implementation exists for repository-specific campaign chronology activation or authoring.

## Prerequisite evidence

- `world/feature-19/WORLD-FEATURE-19-SLICE-1-RECEIPT.md` verifies the chronology component, relationship conventions, and trusted-GM authoring contract.
- `web/evidence/dnd2024/DND2024-WORLD-CHRONOLOGY-PROJECTION-SLICE-20-RECEIPT.md` verifies the route and DM/Player projection code and identifies activation plus live authoring as the remaining boundaries.
- Live readback found the World clock at `world.thalorien`, calendar `lantern-compact-epoch`, and no chronology components.
- The valid application preview contains the separate `world-chronology.json` binding.

## Runtime artifacts

- No new schema, procedure, mechanic, event type, subscription, public route, or host code.
- Thirty-five additive live entities with `dnd2024.game.core.world.chronology` components.
- One empty `dnd2024.game.core.world.chronology.in-world` relationship per entry to `world.thalorien`.

## Authoritative state and closed input

The live `dnd2024-main` World clock owns calendar identity. Existing DM-authorized knowledge owns the reviewed prose. The accepted Thalorien presentation table owns the existing relative-date labels and stable order. The synchronization request supplies only complete components, expected-zero revisions, and empty scope relationships.

## Behavior, result, and typed effects

Each entry is active, DM-visible, scoped to Thalorien, and sorted by a bounded signed minute coordinate. Authored relative-date labels are preserved independently. Each entry requires four effects, so the generic 128-effect ceiling requires two non-overlapping `system.world-state.sync` batches. Both batches must pass dry run before either is committed; each batch creates its entities, components, containments, and scope relationships atomically.

## Failure, replay, and rollback contract

Activation and authoring both use dry-run-first replay-protected commits. Any stale preview, revision collision, schema rejection, calendar mismatch, or relationship rejection leaves that transaction unchanged. Rollback is archival through complete component replacement; no knowledge record is deleted.

## Implementation sequence

1. Dry-run and activate the valid reviewed application preview.
2. Build two non-overlapping closed chronology manifests from the 35 exact existing titles and summaries.
3. Dry-run both manifests, apply both atomic batches, and read them back.
4. Verify DM inclusion, Player non-disclosure, and the live History presentation.
5. Record the completion receipt and stop.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Activation | Chronology binding resolves from the active application |
| DM positive | All 35 active entries return in deterministic order |
| Player negative | No DM entry or subject is returned |
| Scope | Every entry has exactly one empty link to `world.thalorien` |
| Calendar | Every component uses `lantern-compact-epoch` |
| Replay | Repeating either committed payload causes no second write |
| Failure | Stale or malformed requests change nothing |
| UI | DM History renders a nonzero timeline |

## Verification commands

- Application preview and activation dry run/readback through the private protocol surface.
- World synchronization dry run, commit, and entity/component relationship readback.
- DM and Player chronology HTTP reads.
- Focused chronology/web interface tests if repository files require adjustment.

## Completion receipt and exit gate

Record live operation IDs, counts, route results, and deliberate exclusions in `web/evidence/dnd2024/DND2024-THALORIEN-DM-CHRONOLOGY-LIVE-SLICE-21-RECEIPT.md`. Stop after verified DM rendering and Player non-disclosure.
