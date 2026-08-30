# DND2024 mechanic repair SS1 — canonical species selection

Status: **implemented; focused acceptance passed, parent acceptance pending**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md), character creation, progression, and rest
Ruleset alignment: **dnd2024-compatible**
Outcome: adapt `mechanic.dnd2024.species-selection.resolve` to canonical species definition facets.
Exclusions: applying grants, Human Skillful/Versatile behavior, character creation, schema/content
changes, and live data.
Allowed areas: this document and repair tree; species-selection mechanic/contract/procedure; focused
current-schema tests.
Stop point: selection reads only registered species definition components and returns canonical refs.

## Confirmed boundary

- Species definitions own active `dnd2024.core.version`, `dnd2024.advancement.species`,
  `dnd2024.creature.classification`, `dnd2024.creature.body-basis`, and
  `dnd2024.creature.movement-basis` state.
- A declared default Size requires no caller choice. Otherwise the caller selects exactly one
  allowed Size entity ID.
- Movement distances and grant references are preserved exactly; selection does not convert units
  or apply grants.

## Behavior and failures

The read validates current closed component shapes, active revision, unique refs, the default/allowed
Size relationship, and canonical movement measures. Invalid state or choice fails before output.
The result reports species, creature type, selected Size, movement basis, and grant refs without
effects.

## Acceptance

- fixed and selectable Size paths;
- exact movement and grant preservation;
- inactive, invalid default, and disallowed selection failures;
- JavaScript compilation, focused execution, owner audit, and `git diff --check`.

The current-schema selectable-Size path passes focused execution and preserves canonical movement
and grant references. The mechanic body compiles and its contract has no retired owner.
