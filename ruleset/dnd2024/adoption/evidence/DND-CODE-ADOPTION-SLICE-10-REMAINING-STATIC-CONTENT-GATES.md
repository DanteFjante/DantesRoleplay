# D&D code-adoption Slice 10 — remaining static-content gates

Date: 2026-08-26  
Status: **verified scope audit; Parent 10 remains active**

## Exhausted schema-ready archive cohorts

The archive contains 33 `dnd2024.item-definition` entities. Slice 10 adopted 31 into SRD-faithful
core: all five currencies, nine of eleven adventuring-gear records, all thirteen Armor-table
records, and all four archived weapon item links. Neither remaining record may enter core:

- `item.dnd2024.hempen-rope-50-foot.v1` asserts a 50-foot hempen subtype not supported by SRD 5.2.1.
  It is now accepted only as hash-locked compatibility content under the disabled-by-default
  `dnd2024-extension.legacy-equipment` source; it remains absent from core.
- `item.dnd2024.quiver.v1` encodes capacity for the broad `ammunition` kind, which would accept more
  than the SRD Quiver's Arrow-only capacity. It remains quarantined from both core and extensions.

All six archived `dnd2024.weapon-profile` records are adopted. The only archived class progression
and its five identity-only feature dependencies are adopted by Slice 10F.

## Permanent-ID gates

The archive contains no item-definition IDs for Battleaxe, Shortbow, Arrows, other ammunition, or
tools. The current item schema can represent ammunition but not tools. Adding any of those records
therefore requires confirmation of new permanent IDs; tools additionally require a representation
decision and likely a schema-meaning change.

## Missing-schema gates

These complete archived entity families cannot activate against the current application because a
required component schema is absent:

| Family | Archived records | Missing component owner |
| --- | ---: | --- |
| Species | 9 | `dnd2024.species-profile` |
| Feats | 2 | `dnd2024.feat-profile` |
| Background | 1 | `dnd2024.background.ability-increase-options` |
| Standard-array policy | 1 | `dnd2024.character.ability-assignment-policy` |
| Spells | 3 | `dnd2024.spell-identity`; `dnd2024.spell-resolution-profile` |
| Magic items | 3 | `dnd2024.magic-item-profile` |
| Rest policy | 1 | `dnd2024.rest-policy` |
| SRD source record | 1 | `dnd2024.source` |

Copying only the shared `dnd2024.character.content-definition` portion of species, feats, or the
background would create partial entities with no accepted family-specific meaning or useful
consumer, so those records remain intact in quarantine.

## Non-static archive records

Archived creature, encounter, item-instance, equipment-state, and policy-bearing creature fixtures
are campaign/runtime state or behavior fixtures, not static content to import through Parent 10.
They require their own feature slices and must not be copied into application content merely because
some individual component schemas already exist.

## Parent exit boundary

No additional static archive cohort can be safely adopted into core without a confirmed new ID,
schema, schema-meaning change, or family-specific behavior decision. Optional compatibility
adoption still requires its own exact source/profile review. Parent 10 also continues to defer
automatic materialization into campaign state until that installation policy is separately confirmed.
