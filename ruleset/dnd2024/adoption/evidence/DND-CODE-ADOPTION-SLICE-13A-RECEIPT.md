# D&D code-adoption Slice 13A receipt — retained-use inventory

Date: 2026-08-27  
Status: **accepted**

## Accepted inventory

- Every one of the **737** tracked `old-dnd/` files is recorded with normalized path, byte length,
  SHA-256, extension, and retention class in
  `adoption/evidence/retained-archive-inventory-13a.json`.
- Total retained size: **3,614,833 bytes**. Aggregate archive SHA-256:
  `E1AAFB069019CA45201AB92D06568840B9A7EC92EA52153CDBE3CB186AA073FF`.
- Classes: 420 historical catalog files, 124 compiled-adapter source files, 112 plans/evidence,
  50 historical tests, 27 character-source files, 2 archive metadata files, and 2 root documents.
- Two independent reports were byte-identical; report SHA-256:
  `D606B66FA1F13C8A2E1288E1B90737B1A0ABCF3552BE5055C077718CE25E0A6C`.

## Consumer result

- Runtime/build/catalog/production-source consumers: **0**.
- Non-archive development consumers: **46**—13 adoption fixtures/contracts, 4 adoption tools,
  1 compiled packaging/provenance test, 10 durable evidence artifacts, and 18 documents. This
  includes current untracked Slice 13 worktree files so the report remains correct after commit.
- Accepted transformation manifests still verify **43** exact archive source files; every declared
  SHA-256 matches.
- Blocking development consumers: **28**. Deletion readiness is therefore `false`.
- Disposition: **retain**. Archive writes: **none**.

## Stop boundary

No archive file, source registration, active catalog record, project, migration, or live database
changed. Slice 13B decides disposition from this inventory; it has no implicit removal authority.
