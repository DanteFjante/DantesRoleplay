# DND2024 mechanic repair WD1 — canonical weapon damage application

Status: **implemented; focused acceptance passed, parent acceptance pending**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md), conditions, checks, and combat
Ruleset alignment: **dnd2024-compatible**
Outcome: adapt `mechanic.dnd2024.weapon-damage.apply` to current HP, Temporary HP, and mitigation
state without requiring retired weapon-profile or creature-attached rest components.
Exclusions: rolling weapon damage, rest interruption, death saves, activity execution, schema/content
changes, and live data.
Allowed areas: this document and repair tree; damage-apply mechanic/contract/procedure; focused tests.
Stop point: application consumes exact child results and emits only current HP/Temporary HP effects.

## Confirmed boundary

- The damage child owns damage type/amount and weapon identity; application does not reread a
  retired weapon profile.
- The mitigation child owns immunity, resistance, vulnerability, and Petrified resistance facts.
- Temporary HP is spent before Hit Points; zero Temporary HP removes its component.
- Canonical rest interruption belongs to the separate rest entity/event lifecycle and is not
  inferred from a damaged creature.

## Acceptance

- mitigation, Temporary HP, Hit Point reduction, and overkill paths;
- exact child-role binding and malformed current state failures;
- no retired profile/rest component in the direct contract;
- JavaScript compilation, focused execution, owner audit, and `git diff --check`.

Focused execution verifies resistance, Temporary HP removal, and canonical Hit Point reduction. The
body compiles and the direct contract contains no retired profile or rest owner.
