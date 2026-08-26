# D&D code-adoption Slice 1B implementation — exact donor/SRD/Foundry evidence

Status: **accepted after corrective review 2026-08-25**; [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-1B-RECEIPT.md)
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [D&D code-adoption plan, Slice 1B](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Alignment: **dnd2024-compatible evidence inventory; no rule adoption**

## Outcome and boundary

Enrich Slice 1A only with exact pinned donor files and Foundry reference files whose explicit match
tokens occur in a capability ID. Record repository, commit, tree, exact file, and Git blob in the
source inventory. Unmatched capabilities remain explicitly unmatched rather than receiving a broad
directory guess. Foundry remains `adopted: false`; archived SRD locators remain unverified.

This slice does not execute or install donor/Foundry code, import assets, approve semantic parity,
resolve conflicts, choose a cohort, change runtime/catalog/database/archive state, or create IDs,
mappings, projections, effects, migrations, or registrations.

## Acceptance and stop

Output must be byte-stable and schema-valid. Every populated external path must be an exact file in
the pinned tree; no default mapping is permitted; unmatched rows are preserved; SRD verification is
never inferred. Stop before conflict/gap classification and hand off to Slice 1C.
