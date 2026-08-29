# D&D code-adoption Slice 11C receipt — weapon-damage mitigation behavior

Date: 2026-08-26  
Status: **accepted**

## Delivered

- Extended the existing `mechanic.dnd2024.weapon-damage.apply` dependency graph with exactly one
  fixed-input `mechanic.dnd2024.damage.resolve` child bound from defender to target.
- Preserved the existing closed ability/critical input and kernel-owned deterministic damage roll;
  callers still cannot supply damage or mitigation.
- Applied SRD 5.2.1 ordering before HP mutation: matching Immunity prevents damage, otherwise one
  Resistance floor-halving applies from stored membership and/or Petrified, then matching
  Vulnerability doubles once.
- Reported both stored and Petrified Resistance reasons while proving they halve only once.
- Preserved the existing complete Hit Point component owner and one generic action transaction.
  Positive final damage proposes one `component.set`; zero/immune damage performs no unnecessary
  database write.
- Added focused unmitigated, immune, resistant, vulnerable, combined, Petrified, corrupt-state,
  no-op, and replay coverage. No temporary-HP, event, or 0-HP branch was added.

## Verification

- Focused weapon-damage mitigation plus existing fresh-host combat tests: 3/3.
- Complete `Dnd2024AbilityCheckTests`: 86/86.
- Catalog validation: 144 records valid with the same 21 existing advisories; no live data touched.
- Solution build: succeeded with 0 warnings and 0 errors.
- Shared suite: 1,111/1,111.
- Local-AI suite: 21/21.
- All activated D&D mechanic JavaScript syntax checks: 54/54.
- Slice-scoped `git diff --check`: passed with line-ending notices only.

## Deliberate exclusions

Attack-hit authorization, non-weapon damage, damage adjustments, temporary HP, healing, damage
events, dropping to 0 HP, death saves, concentration, thresholds, bypasses, source grants,
migrations, public operations, and production C# remain outside this accepted leaf. Existing
campaign/source-profile bindings were not changed.

