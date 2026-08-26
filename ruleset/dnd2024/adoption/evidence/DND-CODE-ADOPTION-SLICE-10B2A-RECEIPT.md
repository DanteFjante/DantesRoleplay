# D&D code-adoption Slice 10B2A receipt — light armor definitions

Date: 2026-08-26  
Status: **implemented and verified; acceptance pending user confirmation**  
Boundary: Parent 10 / armor-and-shields / light-armor leaf

## Delivered

- Recovered the existing Padded Armor, Leather Armor, and Studded Leather Armor permanent IDs as
  immutable activated application content.
- Preserved the accepted item-definition schema and the exact SRD light category, base Armor Class,
  full Dexterity rule, Stealth disadvantage, rational mass, one-minute don/doff timing, and worn
  equipment mode.
- Added a reusable category-complete armor transform verifier. It rejects missing/extra category
  members, stale inputs, source/shape/value drift, duplicates, target drift, and attribution drift.
- Corrected the broad archived locator to `Equipment > Armor > Armor table (PDF p. 92)` and recorded
  the required SRD 5.2.1 attribution/change indication.
- Added activated-source, schema, exact-profile, existing equipment-reader, and aggregate burden
  evidence without adding derived Armor Class behavior or changing generic/runtime contracts.

## Verification

- Light-armor transform — passed, 3/3 hash-locked deterministic targets.
- Focused schema/profile/equipment/burden test — passed, 1/1.
- Full activated D&D suite — passed, 78/78.
- Core catalog validation — passed, 144 records with the existing 21 advisory warnings; no live
  data was touched.
- Main test project — passed, 1,091/1,091 while intentionally suppressing unrelated project builds.
  The count includes three concurrently added unrelated tests since the preceding leaf's run.
- Prior content-transformation, currency, adoption-contract, conformance, and local-AI gates remain
  green from Slice 10B1A in the same logical delivery turn.
- Repository solution build remains blocked by the unrelated untracked
  `ControlSystemCapabilityExplorer.cs` missing the web namespace for `StatusCodes`; this leaf does
  not edit that concurrently owned file.

## Target hashes

| ID | SHA-256 |
| --- | --- |
| `item.dnd2024.padded-armor.v1` | `8521BBB99AF4FD9BF106491D842EA1BBBF2CBF8BE68A257F719D7A8652A0605C` |
| `item.dnd2024.leather-armor.v1` | `FD7F12119E638DB81CEAE4C231DD666D9F285B6B822514855DCB43A700231E5D` |
| `item.dnd2024.studded-leather-armor.v1` | `88F38543C5184EDD88164B1FB1D930D35234D76C6E952EC3F5F486A1CE58749B` |

## Deliberate exclusions and next gate

Prices, training eligibility, Dexterity calculation, derived Armor Class, Stealth application, and
don/doff timing behavior remain later mechanic work. Medium armor, heavy armor, and Shield are
separate complete-category leaves that can reuse the verifier. Final Slice 10B2A acceptance requires
user confirmation.
