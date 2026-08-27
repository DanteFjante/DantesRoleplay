# D&D code-adoption Slice 11G implementation — Temporary HP damage absorption

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), Temporary HP/healing 11G  
Ruleset alignment: `dnd2024-owned`  
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing > Temporary
Hit Points > Lose Temporary Hit Points First` (PDF p. 18)  
Outcome: revise confirmed weapon-damage application to consume an optional valid Temporary HP buffer
after mitigation and before actual HP in the same root transaction.  
Exclusions: non-weapon damage, damage events, 0-HP/death consequences, Long Rest expiry, healing,
concentration, migrations, public operations, and production C#.  
Allowed files/areas: this document; existing weapon-damage apply contract/script/procedure; focused
`Dnd2024AbilityCheckTests`; Parent 11 status and 11G receipt.  
Stop point: one mitigated weapon-damage instance atomically changes only optional Temporary HP and HP.

## Ordering and state contract

Keep the accepted child graph: weapon roll first supplies raw typed damage and defender resolution
supplies Immunity/Resistance/Vulnerability. After final mitigated damage is known, validate the
target's optional `dnd2024.temporary-hit-points` state. Absorb
`min(finalDamage, temporaryBefore)`, remove an exhausted buffer or set the positive remainder, and
apply only the leftover amount to current HP. Preserve HP maximum/source and clamp current at zero.

The result retains existing mitigation fields and adds `temporaryBefore`, `temporaryAfter`,
`temporaryAbsorbed`, `hitPointDamage`, and `overkill`. These are derived audit values, never caller
authority. Zero mitigated damage touches neither component.

## Typed effects and transaction

Effects are ordered buffer-first and HP-second:

1. optional `component.set` or `component.remove` for a consumed buffer; then
2. optional complete HP `component.set` for leftover damage.

Both effects are proposals to the existing generic application action batch. Validation, stale
checks, atomic commit, audit, operation-key replay, and rollback remain kernel-owned. A corrupt
present buffer rejects the root before either effect, including when mitigation would otherwise make
damage zero.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Buffer absent | current accepted weapon-damage behavior remains identical |
| Partial absorption | buffer removed, remainder changes HP, two effects in one root |
| Exact absorption | buffer removed, HP unchanged |
| Retained buffer | positive remainder set, HP unchanged |
| Mitigation order | absorption uses post-Immunity/Resistance/Vulnerability damage |
| Overkill | calculated only after buffer absorption and HP clamp |
| Corrupt buffer | failure before any buffer or HP change |
| Replay | two-effect root applies once |
| Regression | existing mitigation/no-profile behavior remains valid |

## Verification commands

- `node --check` for the revised script;
- focused tests filtered to weapon damage, Temporary HP, and healing;
- complete `Dnd2024AbilityCheckTests`;
- catalog validation, solution build, full shared tests, and Slice 11-scoped `git diff --check`.

No MCP protocol walk is required because no MCP surface or dependency registration changes.
