# DND2024-CLASS-CORE-IDENTITY-REPAIR V1 completion receipt

Status: **complete**
Implementation document: `DND2024-CLASS-CORE-IDENTITY-REPAIR-IMPLEMENTATION.md`
Source: `source.dnd2024.srd-5.2.1`, the twelve class Core Traits tables on PDF pp. 28, 31, 36, 41, 47, 49, 53, 57, 61, 64, 70, and 77

## Delivered boundary

- Corrected the class-key extraction bug that caused every class to receive the universal fallback
  of Strength, d8, and an empty progression suffix.
- Replaced all twelve known class placeholders with exact primary-ability and Hit Die vocabulary
  references plus the matching existing `dnd2024.class-progression.<class>` reference.
- Added a guarded repair mode that recognizes only the old placeholder signature and exact focused
  assertions covering all twelve classes.

## Verification

| Check | Result |
| --- | --- |
| SRD/Core Traits matrix | 12 of 12 exact primary-ability lists and Hit Dice |
| Progression references | 12 of 12 resolve to the matching existing class progression |
| Converter replay | completed with no divergent record |
| Normalization replay | 0 files would change |
| Unresolved reference candidates | reduced from 736 to 735 |
| `npm test` | 52 passed, 0 failed |
| `git diff --check -- prototype/dnd2024` | passed |

## Deliberate exclusions

The current component records the source-listed ability references but has no operator field to
distinguish Fighter's Strength-or-Dexterity choice from conjunctive multi-ability declarations.
This receipt does not accept that missing execution semantic, nor saving throws, skills,
proficiencies, equipment, spellcasting, class features, or level progression contents.
