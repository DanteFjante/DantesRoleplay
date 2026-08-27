# D&D code-adoption pre-Slice 11 acceptance

Date: 2026-08-26
Status: **accepted**
Boundary: all delivered adoption work in Slices 0–10 plus optional source-profile Slice 2A

## Review outcome

Every implementation document and completion receipt through Slice 10 is accepted. The review
found no runtime, catalog-content, schema-validation, activation-profile, transformation-hash, or
mechanic-consumption defect.

The review did correct stale evidence:

- the main dependency tree no longer describes accepted D&D mechanics as missing or Slices 7–10
  as planned/blocked;
- Slice 7A3–7D now records completed Sol review plus user acceptance;
- Slice 9's unrelated-worktree hold is explicitly cleared by the current clean full-suite run;
- all seven implemented Parent 10 leaves and Parent 10 itself record final acceptance; and
- older Parent 10 receipts no longer claim that user confirmation is still pending.

Parent 10 remains honest about its selected scope: deferred spell, monster, magic-item,
ammunition/tool, missing-ID, Quiver, and complex-behavior families are not counted as delivered.
The legacy hempen rope remains separately opt-in and never enters SRD-faithful core. Automatic
installation into existing campaigns and archive deletion remain excluded.

## Adoption-tool evidence

- Contract validation: 2 positive documents accepted and all 9 required negative cases rejected.
- Conformance tooling: 4 schemas compiled, 3 source conversions deterministic, equal comparison
  passed, unexpected difference blocked, and declared difference still requires confirmation.
- Projection/dependency mapping: positive mapping passed; 2 schema and 7 semantic negative cases
  rejected; no writes.
- Result/effect allowlist: 2 positive documents passed; 3 schema, 4 semantic, and 3 conversion
  negative cases rejected; no writes.
- Impact/replay/rollback proof: exact mapping/allowlist/result hashes passed one focused proof with
  no writes.
- Generic transformation Stage 5C: deterministic dry-run/candidates passed; hash, schema/mapping
  tool, license, duplicate, normalized-alias, reparse-point, and existing-target hazards rejected.
- All 43 accepted core static targets passed their hash-locked cohort verifiers: 5 currencies,
  9 adventuring-gear records, 13 Armor-table records, 6 weapon profiles, 4 weapon item links, and
  6 Fighter progression/feature identities.

## Runtime and repository evidence

- All 52 activated D&D JavaScript mechanic files passed `node --check`.
- Activated D&D core and optional-profile suite: 85/85 passed.
- Catalog validation: 144 records valid with 21 unchanged advisory overlaps; no live data touched.
- Release solution build: 0 warnings, 0 errors.
- Shared suite: 1,106/1,106 passed.
- Local-AI suite: 21/21 passed.
- D&D-scoped `git diff --check`: passed with existing line-ending notices only. The repository-wide
  check still reports two unrelated interaction-orchestration status lines outside this boundary.

## Stop point

Slices 0–10 are accepted. Slice 11 has not been designed or started in this audit and remains the
next feature-family boundary. `old-dnd/`, live databases, non-empty campaign profiles, and public
protocols were not changed.
