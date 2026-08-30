# DND2024 mechanic repair IP1 — canonical item primitives

Status: **implemented; focused acceptance passed, parent acceptance pending**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md), inventory, item state, and currency
Ruleset alignment: **dnd2024-compatible**
Outcome: adapt inventory inspection, item admission, quantity conservation, and equipment state to
the canonical item-instance archetype and split definition components.
Exclusions: currency exchange values, activity execution, predicate evaluation, equipped effects,
container admission, transfer, schema changes, and live data.
Allowed areas: this document and repair tree; item primitive mechanics/contracts/procedures; focused
current-schema tests.
Stop point: the selected mechanics use only `dnd2024.core.definition-link`,
`dnd2024.item.quantity`, `dnd2024.item.equippable`, and `dnd2024.item.equipment` plus registered
definition facets; no retired monolithic item or equipment-state component remains.

## Confirmed boundary

- Every runtime item instance has one definition link and a positive quantity. Quantity `1`
  represents an individually tracked item; there is no separate/fungible flag in canonical data.
- Definition identity is sufficient for split/merge compatibility. An explicitly supplied
  definition role must match the instance link and carry an active canonical version.
- Equipment is present only while equipped and records `equippedBy` plus canonical slot references.
  Unequipped is represented by absence, not a stored enum.
- Inventory inspection reports available definition facet component IDs instead of reconstructing
  the removed monolithic `kind` field.
- Administrative create/record operations materialize the complete required runtime archetype.

## Behavior and failures

Create and record operations add both definition link and positive quantity. Split, merge, and
consume preserve definition identity and count; final consumption deletes the item. Equipment
operations validate allowed slot references and direct holder identity, then add/remove canonical
equipment state. Inventory inspection is read-only and bounded to depth four.

Malformed links, inactive definitions, zero quantities, incompatible definitions, unsafe count
arithmetic, direct contents on deletable/splittable stacks, invalid holder or slots, and unsupported
equipping predicates/effects fail before output.

## Acceptance

- create/record produce a complete item-instance payload;
- split/merge/consume conserve current quantity and definition identity;
- equipment add/read/remove uses canonical state and category naming;
- inventory returns canonical quantity, equipment, and definition facets;
- JavaScript compilation, focused execution, owner audit, and `git diff --check`.

Eleven mechanic bodies compile. Seven focused repair tests pass, including complete item admission,
quantity conservation/deletion, canonical equipment, inventory facets, progression, and burden.
