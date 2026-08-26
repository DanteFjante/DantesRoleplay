# D&D code-adoption Slice 1C implementation — normalized conflict and gap report

Status: **accepted after corrective review 2026-08-25**; [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-1C-RECEIPT.md)
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [D&D code-adoption plan, Slice 1C](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Alignment: **dnd2024-compatible inventory analysis; no rule adoption**

## Outcome and boundary

Classify exact active/archive matches, active-only rows, archive-only gaps, and content-hash conflicts
from the normalized manifest capability rows. Report test, dependency, SRD, donor, and Foundry review
coverage without choosing or approving an implementation cohort.

Overlap requires the same manifest kind, ID, and version; equality additionally requires the same
content hash. A gap requires a missing active owner and a concrete archive candidate. Title similarity
is never used as ownership evidence.

This slice changes no runtime/catalog/database/archive state, adapts no JavaScript, verifies no D&D
rule, creates no permanent IDs/effects, and marks no capability ready or accepted for production.

## Acceptance and stop

The report must be byte-stable, valid JSON, reconcile every matrix row exactly once, and reference
concrete capability keys. Stop after the report and hand off to Slice 2A classification.
