# D&D code-adoption Slice 10B3A receipt — reduced weapon profiles

Date: 2026-08-26  
Status: **accepted**

## Delivered

- Recovered six permanent weapon-profile IDs: Battleaxe, Dagger, Flail, Greatsword, Javelin, and
  Shortbow.
- Deterministically selected only the accepted closed combat profile: category, kind, attack
  abilities, base damage, and source reference.
- Hash-locked each rich archived source and recorded the exact discarded key set. Properties,
  ranges, ammunition subtype, versatile damage, and mastery were deferred without approximation.
- Added activated-source, schema, exact-profile, weapon-attack, and weapon-damage evidence using the
  existing stateless combat readers.

## Verification

- Weapon transform: 6/6 deterministic targets.
- Focused activated schema/combat-consumption test: 1/1.
- Activated D&D suite: 79/79.
- Core catalog validation: 144 records valid with 21 existing advisories; no live data touched.
- `git diff --check`: passed with line-ending notices only.
- The repository-wide suite remains externally red at the single concurrent web-interface assertion
  recorded by the Armor receipt; 1,090 unrelated/main tests passed in that run.

## Target hashes

| ID | SHA-256 |
| --- | --- |
| `weapon.dnd2024.battleaxe` | `F69F14C42B2844B072A4B50AC1A229F804A89B95C9C712D757CEEBAF53E925A7` |
| `weapon.dnd2024.dagger` | `A2A22952986A9796807D357B66C6B8988D6BB270EADBF58769B97406D0E99303` |
| `weapon.dnd2024.flail` | `5777BFB1F5BA4C2932E8270EBD21B4AA3DB9ABBF7CC62BCA7C6ACB6AFD8B3196` |
| `weapon.dnd2024.greatsword` | `DFD4029E5E5B46E237F52FCABBFBA1717743100552CE0A1A2CEB7632A1E38066` |
| `weapon.dnd2024.javelin` | `F91CD34BC076C48FBA0781DF2AAE81E9D95AADDDF9289FFD11C40FCC0DB55228` |
| `weapon.dnd2024.shortbow` | `9D7F8E0462B546C3A06FC994B25096A61A2164F9046EB0362B47FEDD5F035104` |

Weapon item-definition links exist for only four of these six archived profiles and remain a separate
cohort. Rich weapon properties and behaviors remain Parent 11. The user accepted this completed
static-content leaf on 2026-08-26.
