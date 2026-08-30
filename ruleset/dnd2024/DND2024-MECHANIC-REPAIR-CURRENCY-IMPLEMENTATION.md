# DND2024 mechanic repair CU1 — derived canonical currency value

Status: **implemented; focused acceptance passed, parent acceptance pending**
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)
Dependency tree/leaf: [mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md), inventory, item state, and currency
Ruleset alignment: **dnd2024-compatible**
Source: `dnd2024.source.srd-5.2.1`, Equipment > Coins > Coin Values
Outcome: derive carried coin value from canonical denomination definition IDs and item quantities.
Exclusions: stored currency-value schema, wallets, exchange, spending, price resolution, transfers,
schema/content changes, and live data.
Allowed areas: this document and repair tree; currency mechanic/contract/procedure; focused tests.
Stop point: value is rule-derived without the retired monolithic item definition component.

## Confirmed boundary

- Physical coin stacks are ordinary canonical item instances whose definition links select copper,
  silver, electrum, gold, or platinum piece definitions.
- Exact copper-piece ratios are D&D rules calculation inputs owned by this JavaScript mechanic, not
  duplicated state: `1`, `10`, `50`, `100`, and `1000`.
- Noncurrency item definitions are ignored; a visible canonical coin stack requires positive
  quantity and exact safe-integer arithmetic.

## Acceptance

- mixed denomination totals and deterministic order;
- nested depth-four traversal and noncurrency exclusion;
- zero quantity, malformed link, and overflow failures;
- JavaScript compilation, focused execution, owner audit, and `git diff --check`.

Mixed canonical silver/gold stacks and noncurrency exclusion pass focused execution; the body
compiles and the active contract has no retired owner.
