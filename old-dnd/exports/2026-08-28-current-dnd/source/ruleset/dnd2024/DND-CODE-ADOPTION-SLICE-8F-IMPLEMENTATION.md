# D&D code-adoption Slice 8F implementation — inventory and carrying readers

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8F  
Prerequisites: accepted 8D Size and 8E inventory state/transitions  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, Equipment and Playing the Game > Carrying Capacity  
Outcome: Recover the four classified effect-free inventory/currency/burden/carrying readers.  
Exclusions: Stored totals, wallets, commerce, encumbrance Conditions, movement mutation, magic
exceptions, unbounded traversal, migrations, public operations, and archive deletion.  
Allowed areas: four classified mechanics/procedures, D&D activated-path tests, Parent 8 evidence.  
Stop point: all four derived views pass bounded, exact, effect-free acceptance.

## Dependency and calculation boundary

Inventory, currency, and burden read only a declared four-level containment projection and exact
definition references. Burden uses exact rational arithmetic. Carrying composes burden as a declared
child and derives SRD capacity from explicit Strength and Size; no caller supplies a cached total.
Missing/corrupt visible state and arithmetic overflow fail closed. All outputs explicitly report
their bounded nature and propose no effects, events, or notifications.

## Acceptance

Acceptance covers deterministic nested inventory and unclassified contents; mixed currency value;
separate/fungible exact burden; all Size scaling; composed burden comparison; corrupt quantity/
definition refusal; closed input; no effects; JavaScript syntax; preview/activation; and regressions.
