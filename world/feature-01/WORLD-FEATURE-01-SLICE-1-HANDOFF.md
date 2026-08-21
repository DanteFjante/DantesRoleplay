# World Feature 1 — Slice 1 implementation handoff

Status: **Complete — verified 2026-08-20**
Assignment ID: `world-feature-01-slice-1`
Owning plan: [World Feature 1 dependency plan](WORLD-FEATURE-01-DEPENDENCY-PLAN.md)

## Outcome and boundary

Implement a namespaced topology fixture: one world root, one region, three locations, and two
canonical adjacency edges. Add only component definitions, their schemas, the governing procedure,
fixture files, relationship records, and focused regression coverage. Stop before movement, lore,
factions, campaigns, quests, events, or a mechanic.

## Baseline and required reads

Read `AGENTS.md`, `procedure.system.create-feature`, `procedure.system.verify`,
`procedure.world.model`, `procedure.world.change`, `procedure.world.naming`, World/Lore Slice 1,
and the owning plan. No existing owner was found for the four proposed IDs. Repository catalog
validation is the development gate; do not import into the persistent database in this assignment.

## Allowed artifacts

- `game.core.world.root` and `game.core.world.location` component definition/schema pairs
- `procedure.game.core.world.location` in category `game.core.world.topology`
- Fixture IDs `world.feature-01.fixture`, `region.feature-01.fixture`,
  `location.feature-01.gate`, `.market`, and `.observatory`
- Relationship kind `game.core.world.location.connected-to`
- `catalog/world/relationships.json`, focused `CatalogWorldFeature1Tests`, and receipt/status/plan
  evidence only; do not hand-edit the manifest in repository mode

No C# domain code, migration, MCP surface, mechanic, event type, subscription, or existing Feature
10 fixture may change.

## Closed contract

- Root data is exactly status (`draft|active|archived`), trimmed nonempty summary (1–1,000), and
  visibility (`public|party|gm`).
- Location data is exactly kind (`region|settlement|site|interior`), status, trimmed nonempty
  summary, and visibility.
- The root has no container; the region is contained by it in `region`; all three locations are
  contained by the region in `location`. No component stores a parent or world ID.
- Each adjacency has empty object data and lexical orientation only: gate -> market, then market ->
  observatory. Endpoints must be locations.

## Acceptance and stop gate

The focused fresh-import test proves exact component data, five entities/five components, four
containments, two canonical edges, deterministic second import, and unchanged Feature 10 state. It
must reject invalid component fixture data, cycles, root containment, missing endpoints, self,
reverse/duplicate/non-location/nonempty adjacency cases. Run focused tests, `roleplay validate
catalog`, full tests, and `git diff --check`; record unrelated existing diff-check failures without
repairing them. Evidence is recorded in
[the receipt](WORLD-FEATURE-01-RECEIPT.md). Stop after receipt/evidence. Do not start World
Feature 2.
