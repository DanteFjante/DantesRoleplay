# D&D code-adoption Slice 2C implementation — first-cohort selection

Status: **accepted 2026-08-25**; [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-2C-RECEIPT.md)
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [D&D code-adoption plan, Slice 2C](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Alignment: **dnd2024-compatible selection evidence; no rule adoption**

## Outcome

Select the first bounded recovery cohort from Slice 2 classifications and explicitly defer every
other candidate. The selected entry is the Feature 1 ability-score/fixed-DC ability-check seam,
with a narrower future probe than the archived whole mechanic: raw ability score, fixed DC, and
kernel-owned seeded RNG only.

## Boundary

Read Slice 1B and Slice 2A/2B evidence. Write only a deterministic selection record, receipt, and
status updates. Do not adapt or activate archived code; verify SRD behavior; create runtime IDs,
schemas, mappings, effects, migrations, or public operations; or modify catalog/database/archive
state.

## Selection rules

1. The first cohort must have a bounded archived plan, concrete artifact paths, historical test
   evidence, and a plausible no-effect probe.
2. The historical whole ability-check mechanic is not approved wholesale because it also references
   level, skills, conditions, and D20 behavior owned by later recovery families.
3. The future Leaf 3 probe must expose only the narrowed raw ability-check view. It must fail on
   undeclared reads rather than silently import the broader archived contract.
4. Features 2–10 and every later family stay deferred pending their own bounded acceptance path.

## Acceptance and stop

The record must be deterministic, reference only existing evidence keys/paths, name dependencies
and exclusions, and make no runtime decision. Stop after selection; Leaf 3A becomes the next planned
test-only adapter mapping slice.
