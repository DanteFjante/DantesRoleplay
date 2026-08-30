# Feature 28 Slice 4 receipt — immutable Origin-feat identities

Status: **Implemented and accepted**  
Reconciled: 2026-08-21

## Delivered boundary

Slice 4 supplies static, source-cited catalog identity only for the two ratified Origin feats:

- `content.dnd2024.feature.alert.v1`
- `content.dnd2024.feature.savage-attacker.v1`

Both entities carry the existing CH1 `dnd2024.character.content-definition` with `kind: feature`
and the closed `dnd2024.feat-profile`. The profile fixes content key/version, SRD source locator,
`category: origin`, and `repeatable: false`. Its schema forbids benefits, executable payloads,
actor state, choices, prerequisites, and rule prose.

`procedure.mechanic.dnd2024.feat-profile` governs catalog authoring of that static profile. It does
not select/grant a feat or implement Alert initiative behavior, Savage Attacker weapon-damage
rerolls, Heroic Inspiration, a turn use, or any character-creation state.

## Verification

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~CatalogFeature28Slice4Tests" --verbosity minimal` — **3 passed**.
- `roleplay validate catalog` — **387 records validated**; no errors and no live data touched.
  The validator reported 71 existing near-duplicate warnings, none from this static slice.
- `git diff --check` — passed; line-ending notices from unrelated shared-worktree files were
  informational only.

## Deferred

The static identities do not close the Human Soldier Fighter creation path. Alert/Savage Attacker
behavior and Human Heroic Inspiration remain separate owner-planning work. CH3 must not convert
these identities into actor state, a receipt that implies a benefit, or a character-creation grant
until those owners are accepted.
