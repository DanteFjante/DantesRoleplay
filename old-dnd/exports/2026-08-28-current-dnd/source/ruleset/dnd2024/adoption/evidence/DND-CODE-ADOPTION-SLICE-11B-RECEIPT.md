# D&D code-adoption Slice 11B receipt — mitigation state and defender profile

Date: 2026-08-26  
Status: **accepted**

## Delivered

- Recovered and activated `dnd2024.damage-mitigation` with exact SRD 5.2.1 provenance, closed
  canonical Resistance/Immunity/Vulnerability lists, and explicit unknown-versus-known-empty
  semantics.
- Recovered/adapted `mechanic.dnd2024.damage-mitigation.write` as a closed record/correct owner with
  exactly one typed component effect, replay safety, canonical ordering, and unchanged-state failure.
- Adapted `mechanic.dnd2024.damage.resolve` into an effect-free, dependency-aware defender profile.
  It composes `mechanic.dnd2024.d20-test.state-effects` for Petrified rather than duplicating
  Condition state or validation.
- Added the two governing procedures and current application activation/schema/test coverage.
- Corrected the archived broad damage locator to exact PDF page 17 headings and recorded the
  Petrified dependency at PDF page 186.
- Removed the archived schema `$comment` during verification because the current bounded schema
  profile intentionally rejects that unsupported keyword; its explanation remains in the procedure.

## Verification

- New JavaScript syntax checks: 2/2.
- Focused damage-mitigation tests: 3/3.
- Complete `Dnd2024AbilityCheckTests`: 84/84.
- Catalog validation: 144 records valid with the same 21 existing advisories; no live data touched.
- Solution build: succeeded with 0 warnings and 0 errors.
- Shared suite: 1,109/1,109.
- Local-AI suite: 21/21.
- D&D-scoped `git diff --check`: passed with line-ending notices only.

## Deliberate exclusions

Weapon-damage behavior, mitigation arithmetic, Hit Point mutation changes, temporary HP, healing,
damage events, 0-HP consequences, death saves, concentration, non-weapon causes, source-grant
tracking, migrations, public operations, and production C# remain outside this accepted leaf. No
live campaign or source profile was changed.

