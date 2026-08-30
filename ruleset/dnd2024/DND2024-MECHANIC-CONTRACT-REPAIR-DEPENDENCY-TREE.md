# DND2024 mechanic contract-owner repair dependency tree

Status: **superseded for remaining work by the complete-campaign dependency graph; delivered evidence retained**
Ruleset alignment: **dnd2024-compatible**
Owner: [D&D 2024 roadmap](ROADMAP.md)
Parent plan: [complete-campaign dependency graph](DND2024-COMPLETE-CAMPAIGN-DEPENDENCY-GRAPH.md)
Activation consumer: [contract and recipe cutover](DND2024-CONTRACT-AND-RECIPE-CUTOVER-DEPENDENCY-TREE.md)

## Outcome

Repair every active D&D mechanic whose contract or JavaScript still requests a retired pre-cutover
component. Existing mechanic IDs remain stable where behavior still has a canonical owner. Add the
smallest current-style component, archetype, relationship, or authored-content owner when a useful
accepted D&D capability has no canonical owner; search the current catalog first so a renamed owner
is reused instead of duplicated. A mechanic is superseded only when the current model intentionally
derives the value or the archived behavior is no longer useful.

The user confirmed current-style permanent IDs and authorized missing D&D owners and content on
2026-08-30. Generic C# rule logic, live data, source registration, and archived evidence remain out
of scope.

This tree preserves the delivered repair evidence and the narrower owner inventory. It no longer
selects the next implementation leaf; remaining ordering is owned by the complete-campaign graph.

## Dependency tree

```text
Close all active mechanic contract owners and useful accepted gaps [subsumed; delivered evidence retained]
├── exact physical burden and carrying capacity [implemented; parent acceptance pending]
├── inventory, item state, and currency [item primitives/currency implemented; transfer/activity pending]
├── weapon definitions and executable activities [implemented; parent acceptance pending]
├── conditions, checks, and combat [Initiative/damage application implemented; condition owner pending]
├── character creation, progression, and rest [progression/species reads implemented; remainder pending]
├── old-implementation and authored-content gap audit [pending]
└── complete owner audit and activation acceptance [pending]
```

## Repair rules

- Search current component, archetype, and content owners before changing a contract.
- Keep one mechanic ID and one `dnd2024.ruleset.*` category for each capability.
- Adapt inputs/results only when required to express canonical component state; document the exact
  boundary in the active family slice and test the current schema payload.
- Do not recreate retired stored totals, monolithic item definitions, final Armor Class, encounter
  cursors, or other intentionally derived authorities.
- If a useful family lacks a current state or transaction owner, add the smallest composable owner
  under the confirmed boundary and record its permanent IDs in that family's implementation slice.
- A missing behavior outside the accepted old implementation is added only when the same audit
  proves its current authored definition is otherwise non-executable or structurally incomplete.

## Exit gate

Every active contract references only registered component IDs, every mechanic body compiles,
focused current-schema behavior passes, the prototype suite passes, and the exact application
preview can be activated without exposing retired component requirements.
