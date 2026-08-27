# D&D code-adoption Slice 10B3B implementation — archived weapon item links

Status: **accepted**
Parent: [Slice 10 static-content design](DND-CODE-ADOPTION-SLICE-10-DESIGN.md), leaf 10B3B  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Weapons > Weapons table` (PDF p. 91)  
Effort: 4 EP  
Model assignment: `gpt-5.6-sol` high

## Outcome and boundary

Recover the four existing archived weapon item-definition IDs—Dagger, Flail, Greatsword, and
Javelin—and link each immutable physical definition to its already activated Slice 10B3A weapon
profile. Preserve official weight, held eligibility, separate-instance policy, and exact profile ID.

The archive contains no Battleaxe or Shortbow item-definition IDs, so this leaf does not invent them.
It adds no schema, property/mastery behavior, ammunition, range, price, action, effect, migration,
public surface, live-state write, or archive mutation.

## Determinism and acceptance

The transform requires the exact four hash-locked archived envelopes, exact one-to-one profile links,
complete target coverage, and activated target profiles. Any identity, mass, link, schema, path,
attribution, or target drift fails without changing live state.

Acceptance requires deterministic transform verification, four schema-valid activated definitions,
referential integrity to activated profiles, representative held equipment and exact burden proof,
regression validation, and final user confirmation.
