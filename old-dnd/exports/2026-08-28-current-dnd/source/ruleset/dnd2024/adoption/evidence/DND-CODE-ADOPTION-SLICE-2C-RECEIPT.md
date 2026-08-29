# D&D code-adoption Slice 2C receipt — first-cohort selection

Status: **accepted 2026-08-25**
Implementation: [Slice 2C](../../DND-CODE-ADOPTION-SLICE-2C-IMPLEMENTATION.md)
Selection: [first-cohort-selection-2c.json](first-cohort-selection-2c.json)

Slice 2C selects Feature 1's ability-score/fixed-DC check seam as the first test-only recovery
cohort. Its future operation view is limited to a raw ability score, fixed DC, and kernel-owned
seeded RNG. The archived mechanic is not approved wholesale: skill proficiency, character level,
conditions, donor campaign state, and donor persistence, events, and reducers are explicit
undeclared inputs.

The record retains three archive-only capability candidates and historical-test evidence, while
deferring all 31 other classified feature families. The selection is not an adoption decision and
creates no runtime ID, schema, mapping, operation, effect, migration, or catalog/database/archive
change.

Selection SHA-256: `F7B5161C18EE7448C7245651CE5273177217105D851038EBD0FAFBF24BF401F2`.
Two generations were byte-identical. Every selected key resolves to the Slice 1B matrix, and all
referenced archive paths exist. Official SRD locator verification and current kernel compatibility
remain blockers. Slice 3A is the next planned leaf, subject to that exact source review.
