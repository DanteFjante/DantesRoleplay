# D&D code-adoption Slice 11G receipt — Temporary HP damage absorption

Date: 2026-08-26  
Status: **accepted**

## Delivered boundary

- Declared optional `dnd2024.temporary-hit-points` on the weapon-damage target role.
- Applied damage mitigation before buffer absorption and absorbed Temporary HP before actual HP.
- Removed an exact/exhausted buffer, set a positive remainder, and applied leftover damage to HP in
  one existing generic root transaction.
- Added derived buffer split, HP damage, and post-buffer overkill result fields.
- Preserved the buffer-absent behavior and all accepted mitigation ordering.
- Rejected corrupt present buffer state before any proposed effect.

## Verification

- Revised JavaScript passed `node --check`.
- Focused weapon/Temporary HP/healing tests: **7 passed, 0 failed**.
- Complete `Dnd2024AbilityCheckTests`: **91 passed, 0 failed**.
- Partial, exact, retained, resistant, overkill, corrupt-state, two-effect atomic commit, and replay
  branches are directly covered.
- Generic application effect-batch acceptance remains the rollback owner; this leaf adds no effect
  kind or transaction path.

Family-wide build, catalog, full regression, attribution, compatibility, and scoped diff evidence
is owned by active acceptance leaf 11H.
