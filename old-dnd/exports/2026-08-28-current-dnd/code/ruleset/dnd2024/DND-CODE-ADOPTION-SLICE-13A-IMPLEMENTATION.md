# D&D code-adoption Slice 13A implementation — retained-use inventory

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), archive-maintenance lane  
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 13A  
Ruleset alignment: `ruleset-neutral` inventory  
Source ID and locator: not applicable; no rule meaning changes  
Outcome: produce a deterministic inventory of every tracked `old-dnd/` file, aggregate fingerprint,
top-level/type counts, all non-archive consumers, and a deletion-readiness decision.  
Exclusions: archive edits/removal, restored runtime files, catalog/schema changes, source
registration, migrations, and live data.  
Allowed files/areas: this document; `ruleset/dnd2024/adoption/tools/New-RetainedArchiveInventory.ps1`;
one generated evidence report and Slice 13A receipt.  
Stop point: the report is reproducible, internally verified, and classifies deletion as ready or
blocked without changing `old-dnd/`.

## Confirmed decisions and owners

- Git-tracked `old-dnd/` paths and bytes are inventory authority.
- Current project/solution/catalog files are runtime/build-consumer authority.
- Current source/tests/adoption tools/fixtures and durable documents are development-consumer
  authority.
- The archive README remains the recovery-use contract: evidence and recovery material, never an
  authored catalog or build input.
- No new runtime ID, schema, mechanic, transaction, or public surface is introduced.

## Inventory behavior

The tool must use tracked files only, reject a missing/untracked archive root, hash every file, and
derive one stable aggregate SHA-256 from normalized path plus per-file SHA-256 and length. It scans
tracked files outside `old-dnd/` for literal references and classifies each consumer as:

- runtime/build configuration;
- active catalog;
- compiled production source;
- compiled test;
- adoption tool or fixture;
- durable evidence/documentation; or
- other development material.

It records exact source paths from accepted transformation manifests and whether their bytes still
match the declared hashes. Deletion is ready only if runtime/build references are absent and every
development consumer has replacement evidence. This slice supplies no replacement evidence, so
any active consumer blocks deletion.

## Failure and acceptance

Missing files, duplicate tracked paths, path escape, hash mismatch, malformed transformation
manifest, nondeterministic ordering, or an output path below `old-dnd/` fails without a success
report. Repeated runs against unchanged bytes must produce identical JSON.

All tracked files are accounted for, transformation source hashes are valid, archive writes are
zero, and repeated reports are identical. Results are recorded in
`adoption/evidence/DND-CODE-ADOPTION-SLICE-13A-RECEIPT.md`.
