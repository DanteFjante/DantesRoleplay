# D&D code-adoption Slice 9C implementation — conformance and parent closure

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), donor-gap-filling lane
Dependency tree/leaf: [Slice 9 design](DND-CODE-ADOPTION-SLICE-9-DESIGN.md), leaf 9C
Ruleset alignment: `dnd2024-owned` acceptance of the existing character-sheet calculation;
candidate disposition evidence is development-only
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Character Creation > Step 5: Character
Creation Details > Fill In Numbers` (PDF pp. 21–22) and `Rules Glossary > Passive Perception`
(PDF p. 185)
Outcome: accept the existing stateless character-sheet cohort and close every Slice 9 candidate.
Exclusions: new mechanics, components, schemas, state, effects, public operations, spellcasting,
terrain, multiattack, content, migrations, and live data.
Allowed files/areas: Slice 9 status, closure evidence, acceptance receipt, and owner roadmap status.
Stop point: Parent 9 is accepted with zero unresolved candidate groups; later-owner rows remain
future feature-family work.

## Acceptance boundary

The existing `slice-9-closure.json` binds the exact inventory hash, activated mechanic/procedure
hashes, two adapted symbols, seventeen candidate dispositions, and zero unresolved rows. Existing
focused tests validate the closure file and source vectors. The repository-wide failure hold in the
Slice 9B receipt is removed only because the current clean full-suite run passes.

The user confirmed completion of the remaining slices on 2026-08-26. This closes the already
implemented permanent IDs; it does not authorize a stored projection, a new rule meaning, or moving
deferred candidates into this cohort.

## Verification and result

- Activated D&D plus extension packaging: 85 passed, 0 failed.
- Full shared suite: 1,106 passed, 0 failed; local-AI suite: 21 passed, 0 failed.
- Release build: 0 warnings, 0 errors.
- Catalog validation: 144 valid core records with 21 existing advisories; no live data touched.
- Candidate closure: 17 classified groups and zero unresolved groups.
- Repository diff check: passed with existing line-ending notices only.

Acceptance is recorded in
[`adoption/evidence/DND-CODE-ADOPTION-SLICE-9C-RECEIPT.md`](adoption/evidence/DND-CODE-ADOPTION-SLICE-9C-RECEIPT.md).
