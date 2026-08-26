# D&D code-adoption Slice 2A implementation — Features 1–10 archive classification

Status: **accepted 2026-08-25**; [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-2A-RECEIPT.md)
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [D&D code-adoption plan, Slice 2A](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Alignment: **dnd2024-compatible archive classification; no rule adoption**

## Outcome

Classify the archived Features 1–10 playable-session vertical into explicit recovery candidates,
shared prerequisites, test evidence, source-locator evidence, and blockers using the corrected Slice
1 matrix. This identifies what must be revalidated; it does not import or select a recovery cohort.

## Allowed boundary

- Read the corrected Slice 1B matrix, archived Features 1–10 dependency plans, and historical tests.
- Write one deterministic classification generator, report, receipt, and roadmap/plan status update.

## Explicit exclusions

Do not modify catalog, application source, database, runtime code, public operations, or `old-dnd/`.
Do not activate/archive-copy code, choose the first recovery cohort, approve a D&D rule or locator,
resolve kernel compatibility, execute donor code, or create runtime IDs, schemas, mappings,
projections, effects, migrations, or registrations.

## Classification rules

1. Features 1–10 are recovery candidates only when their rows have concrete archived source evidence.
2. Historical test references are evidence, not passing current-kernel tests.
3. Missing official SRD verification, missing capability evidence, and kernel adapter/effect work are
   blockers; no row becomes ready from this report.
4. Shared foundations remain visible in every consuming feature rather than being silently assigned
   to one feature.

## Acceptance and stop

The report must be byte-stable, valid JSON, cite concrete capability keys/archived feature plans,
reconcile its selected keys to the Slice 1B matrix, and change no runtime state. Stop after
classification; Slice 2B classifies later archived feature families.
