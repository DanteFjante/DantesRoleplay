# D&D code-adoption Slice 2B implementation — later accepted-feature classification

Status: **accepted 2026-08-25**; [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-2B-RECEIPT.md)
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [D&D code-adoption plan, Slice 2B](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Alignment: **dnd2024-compatible archive classification; no rule adoption**

## Outcome

Classify later archived feature families that have verified or accepted historical evidence, using
their own historical tests and plans to select concrete capability rows. Record planned-only feature
families as excluded from recovery selection. This does not select a first cohort.

## Boundary

Read the corrected Slice 1B matrix, archived feature plans, and matching historical tests. Write
only deterministic classification tooling/evidence and status documents. Do not modify catalog,
runtime, database, public operations, or `old-dnd/`; execute donor code; approve rule meaning;
activate a candidate; or create runtime IDs, projections, effects, migrations, or registrations.

## Classification rules and acceptance

Only later features with archived accepted/verified evidence are classified. Exact capability IDs
referenced by feature-specific tests are direct evidence; their D&D-only dependency closure is
reported separately. A partial historical feature, missing current-kernel proof, unverified SRD
locator, or absent test remains a blocker. Output must be byte-stable, valid JSON, and resolve every
reported key/plan path before stopping for Slice 2C cohort selection.
