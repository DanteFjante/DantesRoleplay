# DND2024 mechanic repair BC1 — canonical burden and carrying capacity

Status: **implemented; focused acceptance passed, parent acceptance pending**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md), physical burden and carrying capacity
Ruleset alignment: **dnd2024-compatible**
Source: `source.dnd2024.srd-5.2.1`, `Rules Glossary > Carrying Capacity`
Outcome: adapt `mechanic.dnd2024.item-burden.read` and
`mechanic.dnd2024.carrying-capacity.read` to canonical definition links, positive item quantities,
metric item weights, creature ability scores, and creature Size.
Exclusions: inventory admission, container volume/weight limits, encumbrance variants, magic
containers, item movement, schema changes, live data, events, and effects.
Allowed areas: this document and repair tree; the two mechanics/contracts/procedures; focused tests.
Stop point: both mechanics execute with exact canonical kilogram measures and no retired
`dnd2024.item-definition` reference remains in their active dependency closure.

## Confirmed boundary

- Item instances retain `dnd2024.core.definition-link` and `dnd2024.item.quantity`.
- Definition entities supply optional physical state through `dnd2024.item.physical`; burden
  requires its `weight` member and fails closed when weight is absent or malformed.
- Canonical weight uses exact rational kilograms. The SRD pounds formula converts with the exact
  repository factor `1 lb = 45,359,237 / 100,000,000 kg` before comparison.
- Quantity must be positive for a physical item. A root without a definition link is a custody root;
  a contained node without a definition link is invalid rather than silently weightless.
- Both mechanics remain deterministic, read-only, bounded to containment depth four, and use no
  caller-supplied weight or burden.

## Behavior and failures

`item-burden.read` resolves each definition reference, validates one canonical kilogram weight,
multiplies by current quantity, and sums the subtree with overflow-checked rational arithmetic.
`carrying-capacity.read` derives the SRD Size multiplier and Strength formula, converts both capacity
results to kilograms, consumes exactly one matching burden child result, and compares exact
rationals.

Missing definitions, weights, quantities, Strength, Size, child results, unsupported units,
malformed data, and arithmetic overflow fail before any output. Both return empty effects, events,
and notifications.

## Acceptance

- focused direct execution with a nested current item instance and canonical definition;
- quantity multiplication and exact kilogram sum;
- Tiny/Medium/Large capacity boundaries and exact burden comparison;
- malformed/missing weight, zero quantity, unknown Size, wrong unit, and overflow failures;
- contract-owner audit for the two mechanics;
- JavaScript body compilation, prototype suite, and `git diff --check`.

Focused current-schema execution passes. The parent prototype acceptance remains pending because an
unrelated campaign-root ordering test changed concurrently; this leaf did not edit that subsystem.
