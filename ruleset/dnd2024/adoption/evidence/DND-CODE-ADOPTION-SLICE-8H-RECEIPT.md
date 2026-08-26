# D&D code-adoption Slice 8H receipt — seeded dice primitive

Date: 2026-08-25  
Status: **accepted**  
Boundary: Parent 8 / 8H final classified mechanic

## Delivered

- Recovered `mechanic.dnd2024.dice` as a closed, seeded, effect-free dice primitive.
- Added explicit repository safety bounds of 1–100 dice and 2–1,000,000 sides plus safe-integer
  modifier/possible-total checks. Defaults remain 1d20+0.
- Kept all randomness in the generic sandbox RNG while all dice parameters and behavior remain
  D&D catalog JavaScript.

## Verification

- Dice focused activated-path case — passed, 1/1.
- Full activated D&D suite — passed, 59/59.
- All D&D JavaScript syntax checks — passed, 51/51.
- Core catalog validation — passed, 144 records with 21 existing advisory warnings; fresh D&D
  preview/activation passed and no live data was touched.
- Full repository suite — passed, 1,061/1,061 plus 20/20 local-AI tests.

## Evidence and exclusions

Tests prove deterministic same-seed output, explicit dice bounds, default 1d20, closed input, die
ranges, and empty effects/events/notifications. D20-test rules, Advantage/Disadvantage, damage,
tables, hidden rolls, world state, migrations, public operations, live state, archive mutation, and
donor runtime code remain excluded.
