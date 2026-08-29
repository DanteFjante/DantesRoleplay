# Core-world-time receipt — scoped clock-advance event

Status: **Accepted**  
Implementation: `CLOCK-SCOPED-EVENT-IMPLEMENTATION.md`  
Dependency tree: `CLOCK-SCOPED-EVENT-DEPENDENCY-PLAN.md`

## Delivered boundary

- Declared closed `game.core.world.clock.advanced` catalog event with `worldId` as its E8 entity-payload field.
- Emitted that event after the structural effects of each approved root-clock producer: direct advance, on-foot route travel, ground-conveyance travel, and aerial-conveyance travel.
- Kept declared semantic-event ordering generic: root declared events receive ordinals after the structural proposals in their batch.
- Kept JSON Schema validation generic while separately validating the E8 schema extension.

## Evidence

- Focused clock, travel, clock-reactivity, and catalog-embedding tests: **25 passed**.
- `roleplay validate catalog`: **416 records valid** (86 pre-existing advisory warnings; no live data touched).
- Full repository suite: **803 passed, 0 failed**.

## Deliberate exclusions

No rest episode, scheduler, polling, clock duplicate, campaign logic, D&D rule, recovery, subscription, or MCP-surface change was added. Feature 33 Slice 2 is unblocked but remains a separate unstarted slice.
