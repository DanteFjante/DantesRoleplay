# D&D code-adoption Slice 8I receipt — contract and disposition closure

Date: 2026-08-25  
Status: **accepted**  
Boundary: Parent 8 / 8I governing contracts, source disposition, and exact inventory closure

## Delivered

- Recovered the seven classified governing procedures that lacked current owners: play, ruleset,
  source-registry, weapon-profile, weapon-attack, weapon-damage-roll, and weapon-damage-apply.
- Recorded `dnd2024.source` as an explicit replacement rather than a second campaign-state owner.
  The generic application-source registry owns immutable source/scan identity; the D&D procedure and
  adoption policy own stable `source.dnd2024.srd-5.2.1` citation and licensing meaning.
- Added machine-checkable closure evidence pinned to the accepted Slice 1B matrix SHA-256. The
  regression derives classified IDs from that matrix and compares them with current catalog owners
  plus the single declared replacement.

## Verification

- Matrix closure regression — passed: 51/51 mechanics, 26/26 component dispositions, and 39/39
  procedures; all unresolved sets empty.
- Full activated D&D suite — passed, 60/60.
- All D&D JavaScript syntax checks — passed, 51/51.
- Combined D&D, application-execution, ECS-effect, and Trail Survival application-seam suite —
  passed, 84/84.
- Solution build — passed with 0 warnings and 0 errors.
- Core catalog validation — passed, 144 records with 21 existing advisory warnings; fresh D&D
  preview/activation passed and no live data was touched.
- Full repository suite — passed, 1,062/1,062 plus 20/20 local-AI tests.
- `git diff --check` — passed with only existing line-ending notices.

## Evidence and exclusions

[Closure evidence](slice-8-closure.json) binds to matrix SHA-256
`2B07ED18F7C55FE116171DD4A70A2746D2B1542B71F15A192598C15418B5666B`. This slice changes no
mechanic behavior, public operation, migration, live state, archive content, static SRD content, or
capability outside Parent 8's accepted matrix. Retained active catalog contracts outside that matrix
are allowed and are not misreported as recovery rows.
