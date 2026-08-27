# D&D code-adoption Parent Slice 13 receipt — retained archive and recovery

Date: 2026-08-27  
Status: **accepted retained scope**

## Delivered boundary

- Exact inventory of all **737** retained archive files, totaling **3,614,833 bytes**, under
  aggregate SHA-256 `E1AAFB069019CA45201AB92D06568840B9A7EC92EA52153CDBE3CB186AA073FF`.
- Exact classification of **46** non-archive references: **0** runtime/build/catalog/production
  consumers and **28** blocking test/tool/fixture/evidence consumers.
- Verification of **43** hash-locked transformation sources.
- Explicit disposition: retain all archive files; remove none.
- Same-worktree release build/catalog/full-suite/recovery acceptance with identical archive hash
  before and after.

## Consolidated acceptance

- Build: **0 warnings, 0 errors**.
- Catalog: **144 valid**, same **21 advisories**, no live data.
- Shared tests: **1,117/1,117**; Local AI: **21/21**.
- Real protocol walk (additional evidence): **6 passed, 2 deliberately skipped**.
- Archive files modified, moved, restored, or deleted: **0**.
- Runtime IDs, schemas, mechanics, migrations, source registrations, donor pins, and public
  operations changed: **0**.

## Adoption-plan exit

All numbered D&D code-adoption parents 0–13 are accepted in their selected scopes. Incomplete
spells, monsters, rest, dying, tactical, progression, and other gameplay families remain owned by
their independent feature gates; they are not pending archive-import work. Any future archive
removal needs a new exact proposal and separate destructive confirmation.
