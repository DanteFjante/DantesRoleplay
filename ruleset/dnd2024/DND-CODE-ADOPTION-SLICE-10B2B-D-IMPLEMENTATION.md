# D&D code-adoption Slice 10B2B–D implementation — remaining armor table

Status: **implemented; acceptance pending confirmation**  
Parent: [Slice 10 static-content design](DND-CODE-ADOPTION-SLICE-10-DESIGN.md), leaves 10B2B–10B2D  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Armor > Armor table` (PDF p. 92)  
Effort: 8 EP  
Model assignment: `gpt-5.6-sol` high for bundled implementation and verification

## Outcome and boundary

Recover the five Medium Armor, four Heavy Armor, and one Shield archived permanent definition IDs.
Together with Slice 10B2A, this closes all thirteen entries in the SRD 5.2.1 Armor table under the
accepted `dnd2024.item-definition` schema.

The bundle is coherent because every category shares the same official table, immutable entity
envelope, schema, deterministic relocation transform, activation boundary, and existing equipment
and burden readers. Separate category manifests preserve independent completeness and review.

This bundle adds no new ID or schema, price field, Armor Class derivation, armor-training behavior,
Strength speed penalty, Stealth effect, exclusivity rule, don/doff action, migration, public surface,
live-state write, or archive mutation. Those behaviors remain Parent 11 work.

## Mapping, dependencies, and failure

The reusable armor verifier requires the exact complete ID set for each category. It hash-locks the
archived source, permits only the declared locator replacement from `Equipment > Armor` to the exact
Armor table locator, and compares the complete generated target envelope. Medium profiles preserve
the maximum-2 Dexterity rule; Heavy profiles preserve no Dexterity contribution plus optional
Strength minimums; Shield preserves its +2 bonus and Utilize-action don/doff descriptor.

Missing or extra category members, stale hashes, unexpected fields, wrong profiles, duplicate IDs or
paths, attribution drift, schema failures, activation omissions, or target drift fail without live
state changes. Acceptance requires all ten profiles to match the official table, three deterministic
category reports, activated schema validity, existing held/worn equipment and aggregate-burden proof,
regression validation, and final user confirmation.
