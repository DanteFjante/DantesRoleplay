# DND2024 mechanic repair IN1 — canonical Initiative roll

Status: **implemented; focused acceptance passed, parent acceptance pending**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md), conditions, checks, and combat
Ruleset alignment: **dnd2024-compatible**
Outcome: adapt `mechanic.dnd2024.initiative.roll` to canonical feature entitlements and remove its
retired creature-attached rest dependency.
Exclusions: rest lifecycle mutation, encounter creation, Alert Initiative swapping, condition
derivation, schema/content changes, and live data.
Allowed areas: this document and repair tree; Initiative mechanic/contract/procedure; focused tests.
Stop point: the mechanic reads current ability scores and entitlements, remains compatible with the
encounter-order child result, and references no retired component.

## Confirmed boundary

- Dexterity comes from `dnd2024.creature.ability-scores` and its modifier remains derived.
- Alert availability is exactly one `dnd2024.feat.alert` feature entitlement. Explicit opt-in uses
  the derived level child's Proficiency Bonus.
- Initiative interruption is no longer inferred from creature state. Canonical rests are separate
  entities with event-owned timing, so the compatibility result reports `restInterruption: null`.
- Explicit non-condition roll circumstances retain the existing advantage/disadvantage rules.

## Acceptance

- ordinary and Alert Initiative with canonical entitlement payloads;
- absent entitlement, malformed level child, and invalid circumstances failures;
- result remains accepted by the encounter-order result shape;
- JavaScript compilation, focused execution, owner audit, and `git diff --check`.

Canonical Alert opt-in passes focused seeded execution and returns the encounter-compatible null rest
plan. The body compiles and its contract has no retired owner.
