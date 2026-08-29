# D&D code-adoption Slice 8G implementation — experience and class progression

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8G  
Prerequisites: accepted character level and 8D immutable content identity  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, Character Creation > Level Advancement and class sections  
Outcome: Recover the two classified progression components and three classified mechanics.  
Exclusions: XP awards/history, campaign policy/authorization, class membership, level-up effects,
Hit Point mutation, feature behavior/resources, fixture imports, migrations, public operations, and
archive deletion.  
Allowed areas: classified progression components/mechanics/procedures, D&D activated-path tests,
and Parent 8 evidence.  
Stop point: XP write/read and immutable class entitlement read pass acceptance.

## Boundary

Experience is one nonnegative safe-integer total with fixed provenance. Its writer uses explicit
`record|correct`; its reader compares that total to the exact next total-character-level threshold
and never mutates level. Class progression is immutable definition state with source-matched class
identity, valid Hit Die/fixed-gain pairs, ascending levels, and canonical entitlement IDs. Its reader
reports identities as unimplemented diagnostics only.

## Acceptance

Acceptance covers XP boundaries, record/correct/replay/failure preservation, absent/invalid reads,
thresholds and level cap, valid/unsupported/mismatched class progression, canonical ordering,
closed input, no effects from readers, preview/activation, syntax, and regressions.
