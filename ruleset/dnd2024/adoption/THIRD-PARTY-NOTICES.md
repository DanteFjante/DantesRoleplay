# Third-party notices for D&D code adoption

## System Reference Document 5.2.1 static content

This work includes material from the System Reference Document 5.2.1 ("SRD 5.2.1") by Wizards of
the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the
Creative Commons Attribution 4.0 International License, available at
https://creativecommons.org/licenses/by/4.0/legalcode.

Slice 10A reuses five archived currency-definition records. The records were relocated, canonically
formatted, and their historical `Equipment > Currency` locator was changed to the exact SRD heading
`Equipment > Coins > Coin Values (PDF p. 89)`. No SRD prose is reproduced in the records.

Slice 10B1A reuses nine archived adventuring-gear definition records. The records were relocated,
canonically formatted, and given item-specific locators below `Equipment > Adventuring Gear` (PDF
pp. 95–100). The `Oil, Flask` and `Rations, One Day` display names were aligned with the SRD 5.2.1
table. The archived Rope record was excluded because its length/subtype is not stated by SRD 5.2.1,
and the Quiver record was excluded because its kind-level capacity was broader than the SRD's
20-Arrow capacity. No SRD prose is reproduced in the records.

Slice 10B2A reuses three archived light-armor definition records. The records were relocated,
canonically formatted, and their historical broad `Equipment > Armor` locator was changed to the
exact `Equipment > Armor > Armor table (PDF p. 92)` locator. No SRD prose is reproduced in the
records.

Slices 10B2B–10B2D reuse the remaining ten archived Armor-table definition records: five Medium
Armor, four Heavy Armor, and one Shield. They received the same canonical relocation and exact Armor
table locator. No SRD prose is reproduced in the records.

Slice 10B3A reuses six archived weapon-profile records, reduced to the accepted base combat fields.
Weapon properties, ranges, ammunition subtype, versatile damage, and mastery fields were deliberately
removed from these targets and remain deferred rather than approximated. No SRD prose is reproduced.

Slice 10B3B reuses four archived weapon item-definition records for Dagger, Flail, Greatsword, and
Javelin. The records are exact semantic relocations that retain their official weight and link to
the corresponding activated Slice 10B3A weapon profile. No SRD prose is reproduced in the records.

Slice 10F reuses one archived Fighter class-progression record and five archived Fighter feature
identity records for levels 1–2. The records are exact semantic relocations: they identify the
Fighter's Hit Point Die/fixed gain and level-indexed feature identities, but do not reproduce feature
descriptions or implement feature behavior.

## dnd-srd-engine derivation code

The Slice 9 character-sheet derivation adapts ideas and code structure from the following MIT
licensed engine files in `dnd-srd-engine` at commit
`ead852b19b9e45f54f43e193caf4f10aad91a91b`:

- `src/derive/character-view.ts`
- `src/derive/ability-check.ts`

The adaptation was changed to consume DantesRoleplay's closed component views, use the current
D&D 2024 SRD-backed component shapes, return a deterministic read-only result, and exclude the
donor's campaign state, content pack, effect stack, spell slots, armor calculation, RNG, and
persistence architecture. No donor starter-pack content or SRD reference-submodule text is copied.

Copyright (c) 2026 Greg Carr

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
