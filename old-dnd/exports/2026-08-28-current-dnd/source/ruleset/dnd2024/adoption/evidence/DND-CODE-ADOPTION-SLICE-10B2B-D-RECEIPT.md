# D&D code-adoption Slice 10B2B–D receipt — remaining armor table

Date: 2026-08-26  
Status: **accepted**

## Delivered

- Recovered five Medium Armor, four Heavy Armor, and one Shield permanent definition IDs.
- Closed all thirteen SRD Armor-table entries together with Slice 10B2A.
- Preserved exact category, AC/base bonus, Dexterity rule, Strength minimum, Stealth flag, mass,
  don/doff descriptor, and held/worn mode under the accepted item-definition schema.
- Added independently complete hash-locked manifests for Medium, Heavy, and Shield using the reusable
  armor verifier and exact `Equipment > Armor > Armor table (PDF p. 92)` locator.
- Expanded activated-source/schema/runtime evidence across the full table, including worn armor,
  held Shield, and exact 394-pound aggregate burden.

## Verification

- Medium transform: 5/5; Heavy transform: 4/4; Shield transform: 1/1.
- Focused full-table schema/profile/equipment/burden test: 1/1.
- Activated D&D suite: 78/78.
- Core catalog validation: 144 records valid with 21 existing advisories; no live data touched.
- The original repository-wide project run had 1,090 passing tests and one unrelated concurrent
  web-interface failure. The pre-11 acceptance audit later cleared that hold with a clean current
  shared-suite run.

## Target hashes

| Category | ID | SHA-256 |
| --- | --- | --- |
| Medium | `item.dnd2024.hide-armor.v1` | `F51E66941C65045ADE4FBFA35514BFCFC03E8FE32E5E65A154003161AF203440` |
| Medium | `item.dnd2024.chain-shirt.v1` | `8D2C53E6765C8F809F07AC89E72E4299C921533877F351B389FDA9E9D8070FAF` |
| Medium | `item.dnd2024.scale-mail.v1` | `DC121295AC69E3CD285700779E022A6A558ABDA9BE6AB91522BE91293D6667A6` |
| Medium | `item.dnd2024.breastplate.v1` | `63E6EC845507F691D85EFCD8AF4A7185DC7865E24C5A0083C205313EC62E900C` |
| Medium | `item.dnd2024.half-plate-armor.v1` | `A835E5C650BAF2153FFA5BF3D379EC9EAA015DD9748EFFAC0E6D3F335FF1B9E9` |
| Heavy | `item.dnd2024.ring-mail.v1` | `60F9C39FEAC0482D7A8E3E147B7AF289EC95F11CD277A55130C6D4FB9776F9BA` |
| Heavy | `item.dnd2024.chain-mail.v1` | `8CDF13691AEDAC3B88C7346C5B3706B327235C0E6A7622EFDF79824C35C3E937` |
| Heavy | `item.dnd2024.splint-armor.v1` | `BAC0ACEC54BCE53C0C339707649DA6052D6E99630316F302C1A3177DA20DB085` |
| Heavy | `item.dnd2024.plate-armor.v1` | `D7BDB2121B1D6F80C8484BA0E67F8AC10CB85F7FDC594E156A439B79000E9535` |
| Shield | `item.dnd2024.shield.v1` | `3E19718333D9BB0BDB27E4A4814871505A6853DEC03F87540B36E2586A2DCD54` |

Derived Armor Class, training, penalties, Stealth effects, exclusivity, and don/doff execution remain
Parent 11 behavior. The user accepted this completed static-content leaf on 2026-08-26.
