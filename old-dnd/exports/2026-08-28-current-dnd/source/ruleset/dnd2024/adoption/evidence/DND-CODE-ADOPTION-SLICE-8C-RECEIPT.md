# D&D code-adoption Slice 8C receipt — Conditions and shared state effects

Date: 2026-08-25  
Status: **accepted**  
Boundary: Parent 8 / 8C standalone Condition state and derivation

## Delivered

- Recovered the closed `dnd2024.conditions` component and administrative record/apply/clear/
  exhaust/recover writer with canonical ordering, source-scoped instances, and fixed provenance.
- Recovered the effect-free `mechanic.dnd2024.d20-test.state-effects` owner for implied Conditions,
  D20 branches, automatic save failures, Exhaustion modifiers, and resource prohibitions.
- Adapted level-six Exhaustion to store and report the lethal fact without emitting the unclassified,
  unsupported archived direct-application event. No death-state owner was invented.

## Verification

- Condition-focused activated-path cases — passed, 4/4.
- Full activated D&D suite — passed, 37/37.
- Core catalog validation — passed, 144 records with 21 existing advisory warnings; fresh D&D
  application preview/activation occurred in focused tests and no live data was touched.
- Full repository suite — passed, 1,038/1,038.
- `git diff --check` — passed with only existing line-ending notices.

## Evidence and exclusions

Tests cover source requirements and source-specific clearing, canonical order, Petrified replacing
Poisoned, Exhaustion 0–6 transitions, deterministic absent/known derivation, implied Incapacitated
and Prone, D20 branches, automatic failure, Exhaustion arithmetic, ordered prohibitions, corrupt
state refusal, and exact no-change failure. Consumer integration, turn-budget spending/refresh,
movement, damage, death mutation, subscriptions/guards, duration, fixtures, migrations, public
operations, live state, and archive changes remain excluded.
