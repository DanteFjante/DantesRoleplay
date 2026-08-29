# D&D 2024 character creation CC3A receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC3A implementation](../DND2024-CHARACTER-CREATION-CC3A-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, Character Origin (PDF pp. 19–20) and the four SRD
backgrounds (PDF p. 83)

## Delivered boundary

- Added the immutable `dnd2024.background-creation-profile` owner and complete source declarations
  for Acolyte, Criminal, Sage, and Soldier: abilities, skills, fixed tool or Gaming Set choice,
  exact Origin feat/configuration, starting package contents/currency, and 50 GP alternative.
- Generalized the basic creator from fixed Soldier to any of those four backgrounds without adding
  a D&D rule to C# or changing its five role names/request shape.
- Applied the selected background's fixed skills, Common, fixed background tools, and fixed class
  tools through the existing canonical proficiency components.
- Made deterministic class-skill selection background-aware so all 48 background/class pairings
  preserve the class choice count without duplicate proficiencies.
- Corrected the old ledger meaning: Common is applied as origin-wide state; two Standard-language
  choices remain pending rather than treating all languages as a Soldier benefit.
- Preserved selectable Gaming Sets, Origin-feat behavior, and both background/class equipment
  branches as sorted no-behavior pending entitlements.

## Evidence

- Focused basic-creation/background matrix: 45 passed, 0 failed; the matrix committed and read back
  every 4-background by 12-class combination.
- Complete `Dnd2024AbilityCheckTests`: 247 passed, 0 failed.
- Fresh disposable base-catalog validation: 144 valid records and 21 existing non-blocking
  near-duplicate advisories; no live data touched. The activated D&D test harness separately loaded
  and schema-validated the new application records.
- Full solution: 1,286 shared tests and 21 Local AI tests passed, 0 failed.
- `git diff --check` passed. No protocol walk was required because protocol registration did not
  change.

## Deliberate exclusions

This receipt proves complete SRD background models and exact fixed origin proficiency application,
not source-complete character creation. The caller still cannot choose its two Standard languages,
Soldier Gaming Set, or background/class equipment branch. Origin feats and class features have
identities/declarations but no character grant/behavior owner in this slice. Optional/non-SRD
backgrounds remain separately activatable future extensions rather than core records.
