# D&D code-adoption Slice 11C implementation — apply mitigation to weapon damage

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), damage-mitigation 11C  
Ruleset alignment: `dnd2024-owned`  
Source ID and locators: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing > Damage
Rolls` (PDF p. 16), `Resistance and Vulnerability > No Stacking/Order of Application` and `Immunity`
(PDF p. 17), plus `Rules Glossary > Petrified > Resist Damage` (PDF p. 186)  
Outcome: make the existing weapon-damage action compose the accepted defender profile and apply
Immunity, one Resistance halving, then Vulnerability before its existing Hit Point boundary.  
Exclusions: attack-hit authorization, non-weapon damage, damage adjustments/bonuses/penalties,
temporary HP, healing, damage events, 0-HP consequences, death saves, concentration, thresholds,
bypasses, source grants, migrations, public operations, and production C#.  
Allowed files/areas: this document; existing `mechanic.dnd2024.weapon-damage.apply` contract/script
and governing procedure; focused `Dnd2024AbilityCheckTests` helpers/tests; Parent 11/status/notice and
11C receipt evidence.  
Stop point: replay-safe SRD mitigation in confirmed weapon damage with at most one HP set; no event or
downstream consequence.

## Confirmed decisions

[11A](DND-CODE-ADOPTION-SLICE-11A-IMPLEMENTATION.md) fixed the rule/transaction ordering and
[11B](adoption/evidence/DND-CODE-ADOPTION-SLICE-11B-RECEIPT.md) accepted the profile dependency. This
leaf creates no permanent ID or effect kind. It changes the existing mechanic's declared child graph
and result fields inside the user's approved SRD-faithful core boundary. Existing campaign bindings
are not automatically upgraded.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Raw weapon damage | roll weapon dice and add the selected ability modifier for a confirmed hit | `mechanic.dnd2024.weapon-damage.roll` | inherit the same closed ability/critical input into exactly one damage child; caller never supplies damage |
| Immunity | matching damage is not taken | `mechanic.dnd2024.damage.resolve` profile | final damage is zero and HP is not written |
| Resistance | matching damage is halved, round down; multiple instances apply once | profile stored membership plus Petrified | collect both reasons but halve at most once |
| Vulnerability | matching damage is doubled, after Resistance; multiple instances apply once | profile membership | double at most once after halving; reject unsafe overflow before effects |
| HP | damage reduces current HP no lower than zero | existing `dnd2024.hit-points` owner | preserve maximum/provenance and propose one complete `component.set` only when final damage is positive |

## External implementation reference

Pinned Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/documents/actor/actor.mjs` lines 818–932, independently calculates Immunity, Resistance with
integer truncation, and Vulnerability before `applyDamage` mutates HP. The adopted engineering
lesson is calculation-before-mutation. No Foundry code, mutable actor state, hooks, caller ignores,
healing/temp-HP logic, threshold, bypass, UI, or runtime dependency is adopted.

## Prerequisite evidence

- [11B receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11B-RECEIPT.md).
- Existing weapon-damage roll/application and HP component are accepted through Slice 7C and the
  pre-Slice 11 acceptance gate.
- Existing application composition supports a fixed `{}` child input and role binding
  `defender -> target`.
- Existing typed component effects and application action runner own validation, atomic commit,
  operation-key replay, rollback, and audit.

## Runtime artifacts

No new record is introduced. Revise only:

- `mechanic.dnd2024.weapon-damage.apply` requirements to add exactly one fixed-input
  `mechanic.dnd2024.damage.resolve` child; and
- its JavaScript/procedure to validate the child, calculate final damage, and retain the existing HP
  component owner.

## Authoritative state and closed input

The parent still accepts exactly `{"ability":"str|dex","critical":boolean}` and the same subject,
weapon, and target roles. The damage child inherits this input. The mitigation child receives exactly
`{}` and binds `defender` to the target. HP comes from the target component.

Callers cannot supply damage amount/type, mitigation memberships, known flags, Petrified,
arithmetic flags/reasons, HP delta/result, effects, events, or notifications.

## Behavior, result, and typed effects

Validate both child envelopes against bound roles/input. Start with `rawDamage` from the damage child.
If its type is immune, final damage is zero. Otherwise collect stored Resistance and Petrified as
ordered reasons, halve once with floor when either applies, then double once for matching
Vulnerability. Reject an unsafe integer before effects.

Return the existing test marker plus `rawDamage`, final `damage`, type, immunity flag, Resistance
flag/reasons, Vulnerability flag, HP before/after/maximum, and mitigation child ID. If final damage is
zero, emit no effect. Otherwise propose exactly one complete `component.set` for
`dnd2024.hit-points`. Emit no event or notification and consume no additional randomness.

## Failure, replay, and rollback contract

Missing/extra/mismatched children, malformed child data, wrong roles/input, invalid HP, invalid
profile known-state relationships, noncanonical mitigation lists, source drift, or overflow fails
before effects. Failed evaluation/action leaves HP unchanged. Successful action uses one generic root
transaction; identical operation replay cannot apply another HP revision. No new multi-effect seam is
introduced, so generic injected-failure rollback evidence remains authoritative.

## Implementation sequence

1. Extend the mechanic's declared child graph and procedure boundary.
2. Adapt JavaScript to validate and calculate before the existing HP effect.
3. Add unmitigated, immune, resistant, vulnerable, combined, Petrified, corrupt-profile, no-op, and
   replay tests.
4. Run focused/D&D/catalog/build/full regression and record the receipt.
5. Stop before family-wide 11D acceptance or any excluded damage concern.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Unmitigated | absent profile preserves raw damage and existing HP behavior |
| Immunity | final zero, no HP effect/revision, Vulnerability does not override |
| Resistance | one floor-halving for odd/even raw damage |
| Petrified | Condition-derived all-damage Resistance applies once |
| Two resistance reasons | stored membership plus Petrified reports both but halves once |
| Vulnerability | doubles once after Resistance |
| Boundary zero | zero raw/final damage emits no HP write |
| Corrupt state/child | fails before effect and HP remains unchanged |
| Replay | identical operation applies at most one HP revision |
| Compatibility | no mitigation state remains a valid unmitigated target; no migration/profile change |
| Regression | focused/D&D/catalog/build/full suites pass |

## Verification commands

- `node --check catalog/applications/dnd2024/mechanics/combat/mechanic.dnd2024.weapon-damage.apply.js`;
- focused tests filtered to `Weapon_damage_mitigation` and the existing fresh-host combat test;
- full `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog`;
- `dotnet build DantesRoleplay.slnx --no-restore`;
- `dotnet test DantesRoleplay.slnx --no-build`;
- scoped `git diff --check`.

No MCP protocol walk is required because no MCP surface or dependency registration changes.

## Completion receipt and exit gate

Record results in `adoption/evidence/DND-CODE-ADOPTION-SLICE-11C-RECEIPT.md`, mark 11C accepted, and
then author 11D family acceptance. Do not expand into excluded damage concerns.
