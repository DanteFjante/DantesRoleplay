# D&D code-adoption Slice 10B3B receipt — archived weapon item links

Date: 2026-08-26  
Status: **implemented and verified; acceptance pending user confirmation**

## Delivered

- Recovered the four archived weapon item-definition IDs for Dagger, Flail, Greatsword, and
  Javelin without inventing the missing Battleaxe or Shortbow item IDs.
- Preserved each official weight, held-equipment mode, separate-instance policy, and exact link to
  its already activated Slice 10B3A weapon profile.
- Hash-locked the four archived sources and required exact target equality, complete cohort
  coverage, and an activated profile target.
- Added activated-source, schema, referential-integrity, materialization, and exact burden evidence
  through the existing generic readers.

## Verification

- Weapon item-link transform: 4/4 deterministic targets.
- Focused activated schema/link/materialization/mechanic-consumption test: 1/1.
- Activated D&D suite after Slice 10F joined the source: 80/80.
- Core catalog validation: 144 records valid with 21 existing advisories; no live data touched.
- Repository-wide suite: 1,094/1,094.
- Solution build: succeeded with 0 warnings and 0 errors.
- `git diff --check`: passed with existing line-ending notices only.

## Target hashes

| ID | SHA-256 |
| --- | --- |
| `item.dnd2024.dagger` | `5C7B18DA96A6732A9BD8AA7E50153150C5E4026586C64427661D32962396A820` |
| `item.dnd2024.flail` | `ADC41C45676A64A4267F9AC9DD2147BC89120A0BB370DAFD31E5C68187B4AC40` |
| `item.dnd2024.greatsword` | `C45862C8ADD1317918ECEB3880E1391D4CFED22AC6E10B6AF1DFEDA832003D8E` |
| `item.dnd2024.javelin` | `4D972BC18CA7C9665C4805175ED28AEAD0DF7CA8FCAA11BCF2C15054F855E69C` |

Automatic campaign installation, the two missing weapon item IDs, ammunition, weapon properties,
range, and mastery remain outside this leaf. Final acceptance requires user confirmation.
