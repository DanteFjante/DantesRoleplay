# D&D code-adoption Slice 11A receipt — damage-mitigation decision

Date: 2026-08-26  
Status: **accepted**

## Accepted boundary

- Selected damage mitigation as the first dependency-ready Slice 11 family.
- Fixed the official SRD 5.2.1 meanings and exact locators for Resistance, Vulnerability, Immunity,
  their ordering/no-stacking rules (PDF p. 17), and Petrified all-damage Resistance (PDF p. 186).
- Verified the official `SRD_CC_v5.2.1.pdf` contains 364 pages and has SHA-256
  `8974902D109D6E63672D7C490BDE9CCF052410503D9CFA768237154FBC5E3D87`.
- Kept current Hit Points, Conditions, condition state-effects, weapon damage, typed effects, and
  application-action transaction owners; no production C# change is needed.
- Chose the archived Feature 15 state/profile IDs for bounded recovery with a corrected exact source
  locator and current result envelopes.
- Required `mechanic.dnd2024.damage.resolve` to compose the existing Condition state-effects owner
  for Petrified rather than duplicating Condition state or validation.
- Reserved HP mutation and mitigation arithmetic for 11C; 11B ends with storage and an effect-free
  defender profile.

## Reference evidence

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` independently confirms
separate mitigation traits, condition-derived Petrified Resistance, and calculation before HP
mutation in `module/data/actor/templates/traits.mjs`,
`module/data/actor/fields/damage-trait-field.mjs`, and `module/documents/actor/actor.mjs`. No Foundry
bytes, assets, IDs, hooks, UI, mutable actor state, or runtime dependency were adopted.

## Deliberate exclusions

No runtime catalog record, schema, mechanic, procedure, test, database, campaign binding, source
profile, public operation, or production C# file changed in 11A. Temporary HP, healing, damage
events, dropping to 0 HP, death saves, concentration, damage adjustments, non-weapon causes,
monster bootstrap, and source-grant tracking remain outside this accepted decision.

