# D&D code-adoption Slice 10B1A implementation — schema-faithful adventuring gear

Status: **implemented; acceptance pending confirmation**  
Parent: [Slice 10 static-content design](DND-CODE-ADOPTION-SLICE-10-DESIGN.md), leaf 10B1A  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Adventuring Gear` (PDF pp. 95–100)  
Effort: 6 EP  
Model assignment: `gpt-5.6-luna` medium for entity conversion; `gpt-5.6-terra` high for source and transform review

## Outcome and boundary

Recover nine archived permanent item-definition IDs whose static SRD meaning is representable by the
accepted `dnd2024.item-definition` schema: Backpack, Caltrops, Crowbar, Oil, Pouch, Rations,
Tinderbox, Torch, and Waterskin. The records remain immutable application content and are consumed
through the existing inventory, burden, container-capacity, and transfer mechanics.

This leaf adds no component or mechanic ID, formula, activity behavior, effect, migration, public
operation, live-state write, donor runtime, or archive mutation. Prices and the special behaviors of
Oil, Caltrops, Tinderboxes, and Torches remain outside this static-data leaf.

## Deterministic mapping

The transform accepts only nine hash-locked archived records. It preserves their existing IDs,
schema-compatible static definitions, rational masses, stack policies, and faithful Backpack/Pouch
capacities; replaces the broad historical source locator with an item-specific SRD locator; and
normalizes the display names `Oil, Flask` to `Oil` and `Rations, One Day` to `Rations` to match the
SRD 5.2.1 table. Any source, shape, ID, static value, path, target, or attribution drift fails.

## Explicit quarantine

- `item.dnd2024.hempen-rope-50-foot.v1` is not imported. SRD 5.2.1 identifies `Rope`, its weight,
  and its rules, but does not state the archived 50-foot length or hempen subtype. Reusing that
  record would falsely cite the current SRD.
- `item.dnd2024.quiver.v1` is not imported. The SRD permits 20 Arrows, while the archived
  `permittedItemKinds: ["ammunition"]` would allow Bolts, Bullets, and Needles in the existing
  transfer mechanic. A definition-ID capacity constraint needs a separately confirmed schema change.

## Dependencies, failure, and acceptance

Every target owns one `dnd2024.item-definition` component. Definition entities are immutable source
records; campaign item instances reference their exact IDs. Derived burden and capacity remain
effect-free, and transfer remains atomic. Malformed sources, unsupported extra fields, incorrect SRD
values, duplicates, activation omissions, schema failures, or transform drift fail without changing
live state.

Acceptance requires deterministic transform verification, nine schema validations, activation
retention, representative 30-pound Backpack admission/refusal using the existing transfer mechanic,
nested burden proof, catalog validation, focused/full tests, and final user confirmation.
