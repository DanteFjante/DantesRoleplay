# D&D code-adoption Slice 10B2A implementation — light armor definitions

Status: **implemented; acceptance pending confirmation**  
Parent: [Slice 10 static-content design](DND-CODE-ADOPTION-SLICE-10-DESIGN.md), leaf 10B2A  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, `Equipment > Armor > Armor table` (PDF p. 92)  
Effort: 3 EP  
Model assignment: `gpt-5.6-luna` medium for conversion; `gpt-5.6-terra` high for source review

## Outcome and boundary

Recover the existing Padded Armor, Leather Armor, and Studded Leather Armor permanent definition
IDs as immutable application content. Preserve the current `dnd2024.item-definition` schema and the
official category, base Armor Class, Dexterity rule, Stealth disadvantage, weight, don/doff timing,
and worn-equipment eligibility.

This leaf adds no schema, component/mechanic ID, armor-price field, derived Armor Class rule,
training rule, effect, migration, public operation, live-state write, or archive mutation. Armor
calculation and training behavior remain Parent 11 work; this leaf proves only exact static data and
compatibility with existing equipment/burden mechanics.

## Mapping and failure

The reusable armor-cohort verifier accepts only a hash-locked, category-complete archive subset. It
permits the single declared relocation change from the historical broad `Equipment > Armor` locator
to `Equipment > Armor > Armor table (PDF p. 92)`. IDs, names, component shape, profile values,
weights, target paths, attribution, and deterministic target data must otherwise remain exact.

Malformed or incomplete category manifests, stale hashes, unexpected fields, duplicates, schema
failures, activation omissions, target drift, or incorrect attribution fail without changing live
state. Acceptance requires all three static profiles to match the official table, schema and
activation proof, worn-equipment and 31-pound aggregate burden proof, regression validation, and
final user confirmation.
