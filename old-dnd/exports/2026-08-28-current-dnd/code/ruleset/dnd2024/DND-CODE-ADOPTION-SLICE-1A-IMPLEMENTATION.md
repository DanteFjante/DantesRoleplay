# D&D code-adoption Slice 1A implementation — active/archive capability inventory

Status: **accepted after corrective review 2026-08-25**; [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-1A-RECEIPT.md)
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [D&D code-adoption plan, Slice 1A](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Alignment: **dnd2024-compatible inventory; no rule adoption**

## Outcome and boundary

Generate one coverage row per authoritative manifest `{kind,id,version}` across the current
`catalog/manifest.json` and `old-dnd/catalog-manifest.pre-archive.json`. Group each capability's
definition, schema or JavaScript companion, exact-ID test references, dependencies, and archived
source-locator evidence. Duplicate capability keys and missing primary evidence fail generation.

This slice may read manifests, catalog artifacts, and tests and may write only Slice 1 development
evidence/tooling. It does not verify D&D rule meaning, execute donor code, classify external reuse,
change catalog/runtime/database/archive state, create permanent IDs, or activate a candidate.

## Classification rules

1. Exact current/pre-archive kind, ID, version, and content-hash matches retain the active owner and
   record a compatible archive candidate.
2. Archive-only rows are unclassified recovery candidates; they are not runtime owners.
3. Related component definitions/schemas and mechanic Markdown/JavaScript are evidence on one row.
4. Tests and dependencies are included only where the exact capability ID occurs in source.
5. Archived SRD locators remain `verified: false`; absent evidence remains absent.

## Acceptance and stop

The generator must be byte-stable, schema-valid, cover every manifest capability exactly once,
group related artifacts, reconcile current/archive hashes, and change no runtime state. Stop before
external donor/reference matching and hand off to Slice 1B.
