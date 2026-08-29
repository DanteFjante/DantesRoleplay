# D&D 2024 character creation CC3F1 receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC3F1 implementation](../DND2024-CHARACTER-CREATION-CC3F1-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, class Starting Equipment rows (PDF pp. 28–77),
Character Backgrounds (PDF p. 83), and Coin Values (PDF p. 89)

## Delivered boundary

- Every active SRD class profile declares its exact starting-equipment cash alternative and the
  canonical Gold Piece definition. The component schema requires the two declarations together
  while retaining the legacy omitted shape for old stored profiles.
- Basic creation accepts the optional closed choice
  `equipmentChoices:{background:"cash",class:"cash"}`. It derives 50 GP from the selected
  background plus the selected class amount; callers cannot supply totals, item identity, stack
  identity, placement, effects, or provenance.
- The cash path requires and source-validates the exact optional Gold Piece role. In the existing
  actor transaction it creates one deterministic `item.starting-gold.*` entity, records canonical
  item-instance and quantity state, and contains it directly under the actor in
  `inventory.currency`.
- The immutable creation record stores the cash selections and created item ID and removes only the
  satisfied background/class equipment deferrals. The omitted path creates no item, needs no
  currency role, and preserves both deferrals, including for a legacy class profile.
- Existing inventory and currency-value readers discover the new stack without a parallel wealth
  owner. Exact replay creates nothing twice; input, role, declaration, ID, collision, and injected
  late-effect failures leave no partial actor, item, containment, participation, or relationship.
- No new permanent ID, migration, C# rule, endpoint, MCP kind, package model, equipment consequence,
  or transaction owner was introduced.

## Evidence

- Focused CC3F1 declaration/cash tests: 27 passed, 0 failed.
- Complete basic-character-creation group: 98 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 321 passed, 0 failed.
- Fresh disposable catalog validation: 144 valid records and 21 existing non-blocking
  near-duplicate advisories; no live data touched.
- Full solution: 1,360 shared tests and 21 Local AI tests passed, 0 failed.
- Real JSON-RPC protocol walk: 6 passed, 0 failed, 2 deliberately skipped audit/navigation cases.
- Independent read-only review found the production schema/runtime/effect/compatibility boundary
  coherent. A suspected test punctuation issue was disproved by raw-character inspection and a
  zero-warning, zero-error build.
- `git diff --check` passed with only the repository's line-ending notices.

## Deliberate exclusions

This receipt proves only the legal all-cash alternative. Background/class equipment packages,
individual item selections, missing item-definition cohorts, packs and nested containers,
auto-equip, equipped AC, encumbrance consequences, buying/selling, currency exchange, UI guidance,
multiclass equipment, and later feature behavior remain separate slices.
