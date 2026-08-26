# D&D code-adoption Slice 10B3A implementation — reduced weapon profiles

Status: **implemented; acceptance pending confirmation**  
Parent: [Slice 10 static-content design](DND-CODE-ADOPTION-SLICE-10-DESIGN.md), leaf 10B3A  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Weapons > Weapons table` (PDF p. 91)  
Effort: 6 EP  
Model assignment: `gpt-5.6-sol` high

## Outcome and boundary

Recover six archived permanent weapon-profile IDs as immutable, activated content: Battleaxe,
Dagger, Flail, Greatsword, Javelin, and Shortbow. Transform each archived rich record into the exact
accepted `dnd2024.weapon-profile` shape consumed by current attack and damage mechanics: category,
melee/ranged kind, permitted attack abilities, base damage dice/type, and source reference.

The transform deliberately removes properties, ranges, ammunition subtype, versatile damage, and
mastery because the current accepted schema and mechanics do not represent or consume them. Those
fields are not silently approximated; their exact removed key set is recorded per source record and
remains Parent 11 work.

This leaf adds no ID or schema, item-definition link, property/mastery mechanic, range eligibility,
ammunition consumption, price/mass field, effect, migration, public surface, live-state write, or
archive mutation.

## Determinism and acceptance

The transform hash-locks all six source envelopes, requires the complete approved ID set, selects
only the accepted closed profile fields, verifies the exact declared discarded keys, and compares
the generated target. Any source, key, value, ID, path, attribution, schema, activation, or target
drift fails without changing live state.

Acceptance requires official Weapons-table verification, six deterministic targets, activated schema
validity, exact reduced profiles, representative execution by existing weapon attack/damage readers,
regression validation, and final user confirmation.
