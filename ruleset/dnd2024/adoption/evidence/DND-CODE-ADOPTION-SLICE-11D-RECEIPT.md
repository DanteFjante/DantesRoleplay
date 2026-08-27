# D&D code-adoption Slice 11D receipt — damage-mitigation family acceptance

Date: 2026-08-26  
Status: **accepted**

## Accepted family

- `dnd2024.damage-mitigation` is the canonical current-application owner for known base Resistance,
  Immunity, and Vulnerability type memberships, with absent versus known-empty state preserved.
- `mechanic.dnd2024.damage-mitigation.write` is the closed record/correct owner and changes only its
  component through one generic typed effect.
- `mechanic.dnd2024.damage.resolve` is a stateless, effect-free profile that declares and composes the
  existing Condition state-effects dependency for Petrified.
- `mechanic.dnd2024.weapon-damage.apply` composes the profile and applies SRD 5.2.1 Immunity,
  one Resistance floor-halving, then Vulnerability before the existing HP boundary.
- Positive final damage performs one complete HP set; immune/zero damage produces no unnecessary HP
  write. Replay cannot apply a second revision, and invalid/corrupt state changes nothing.
- Current generic C# remains ruleset-neutral. No application source profile, existing campaign,
  live database, migration, public operation, event kind, or production host seam changed.

## Consolidated evidence

- Official SRD 5.2.1 PDF: 364 pages; SHA-256
  `8974902D109D6E63672D7C490BDE9CCF052410503D9CFA768237154FBC5E3D87`; exact damage locator at
  PDF p. 17 and Petrified locator at PDF p. 186.
- Pinned Foundry dnd5e reference reviewed at commit
  `275bed0be4ccfa15e6b3347acccb8da8784726d9`; no code/data/runtime adopted.
- Family acceptance focus, including core-only/optional-extension source profiles: 7/7.
- Complete D&D application tests: 86/86.
- All D&D mechanic JavaScript syntax checks: 54/54.
- Catalog validation: 144 valid records with the same 21 existing advisories; no live data touched.
- Solution build: 0 warnings, 0 errors.
- Shared suite: 1,111/1,111.
- Local-AI suite: 21/21.
- Family artifact/schema read-back: 9/9 plus valid JSON schema document.
- Slice-scoped `git diff --check`: passed with line-ending notices only.

## Deliberate exclusions

This acceptance does not add attack-hit authorization, non-weapon damage, damage adjustments,
temporary HP, healing, damage events, dropping to 0 HP, death saves, concentration, thresholds,
bypasses, source-grant tracking, monster bootstrap, migrations, or archive deletion. Those remain
separate complex-family decisions. Slice 11 as a scheduling parent remains active for other bounded
families; this receipt accepts only the complete damage-mitigation family.

