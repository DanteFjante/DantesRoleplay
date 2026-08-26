# D&D code-adoption Slice 10F receipt — Fighter levels 1–2 progression identities

Date: 2026-08-26  
Status: **implemented and verified; acceptance pending user confirmation**

## Delivered

- Recovered the archived Fighter class identity and immutable level-1/2 progression using the
  existing `dnd2024.character.content-definition` and `dnd2024.class-progression` owners.
- Recovered the five referenced feature identities: Fighting Style, Second Wind, Weapon Mastery,
  Action Surge, and Tactical Mind.
- Hash-locked all six archived sources and required exact relocated targets, complete cohort
  coverage, source agreement, exact entitlement sets, and closed feature references.
- Proved all six records are activated and schema-valid and that the existing stateless progression
  reader returns exact level entitlements with `unimplemented` behavior and no effects.
- Reviewed pinned Foundry dnd5e Fighter progression as independent engineering evidence; no Foundry
  bytes, UUIDs, descriptions, choices, behavior, or mutation flow were adopted.

## Verification

- Fighter transform: 6/6 deterministic targets.
- Focused activated schema/reference/mechanic-consumption test: 1/1.
- Activated D&D suite: 80/80.
- Core catalog validation: 144 records valid with 21 existing advisories; no live data touched.
- Repository-wide suite: 1,094/1,094.
- Solution build: succeeded with 0 warnings and 0 errors.
- `git diff --check`: passed with existing line-ending notices only.

## Target hashes

| ID | SHA-256 |
| --- | --- |
| `content.dnd2024.class.fighter.v1` | `59D7CDFF2634B7DEC63193CC64C27CB5DE3729A654C699D8473FDD7D95562488` |
| `content.dnd2024.feature.fighter.action-surge.v1` | `B077D255EF643228194E0705901493E599B2C867B3F4FAD588BE5F565035C77A` |
| `content.dnd2024.feature.fighter.fighting-style.v1` | `B6BED0B6A606887D029FBA2FD3924DE6131F6581F0A4B4FF0A6B6793898CD939` |
| `content.dnd2024.feature.fighter.second-wind.v1` | `ACC7E1E28D07A4A6B7C53C1BD2E21055938F4E586C11D6982C077E0E5A5E02A2` |
| `content.dnd2024.feature.fighter.tactical-mind.v1` | `BE3DDEE2FACB46B5ECB2DF29BBF5F885325EE6F8480B6BD5E314C58BF9A2F8FE` |
| `content.dnd2024.feature.fighter.weapon-mastery.v1` | `9B75010D9F901AAF32F6894765306B4917F006ED4D55A719C048D8AF39A41C9D` |

Feature behavior, choices, resources, actor advancement, HP mutation, multiclass handling, later
levels, and automatic campaign installation remain outside this leaf. Final acceptance requires
user confirmation.
