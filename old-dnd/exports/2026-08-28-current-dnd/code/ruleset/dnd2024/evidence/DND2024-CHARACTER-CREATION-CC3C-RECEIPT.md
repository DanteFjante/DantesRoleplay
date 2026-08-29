# D&D 2024 character creation CC3C receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC3C implementation](../DND2024-CHARACTER-CREATION-CC3C-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, the selected background and class source locators

## Delivered boundary

- Added the canonical actor-side `dnd2024.character-feature-grants` component with a closed schema
  for `origin-feat` and `class-feature` grant shapes.
- The basic creator now derives one configured Origin Feat from the selected background and every
  level-1 feature from the effect-free class-progression child. Callers cannot inject grant state.
- Every grant records its immutable feature definition, declaring background/class, exact source
  reference, and configuration or class level. The ledger deliberately has no behavior-status or
  executable-rule field.
- All 48 background/class combinations persist exact deterministic grants whose definition IDs
  resolve to active SRD feature identities in the same application source.
- Every stored grant retains its matching `behavior-unimplemented` pending entry. No feature action,
  resource, spell, choice, modifier, or effect was invented.
- Grant state participates in the existing actor/participation transaction, replay, and late-failure
  rollback. No C# rule, migration, endpoint, MCP kind, or transaction owner changed.

## Evidence

- Focused basic-creation and closed feature-grant schema group: 62 passed, 0 failed, including the
  complete 48-pair matrix, exact replay, late rollback, referential integrity, and malformed/empty/
  duplicate/extra/source-drifted state rejection.
- Complete `Dnd2024AbilityCheckTests`: 264 passed, 0 failed.
- Fresh disposable base-catalog validation: 144 valid records and 21 existing non-blocking
  near-duplicate advisories; no live data touched. The activated D&D harness compiled and
  schema-validated the new application component and exercised its persisted values.
- Full solution: 1,303 shared tests and 21 Local AI tests passed, 0 failed.
- `git diff --check` passed. No protocol walk was required because protocol/dependency registration
  did not change.

## Deliberate exclusions

This receipt grants feature identity and provenance only. Origin-feat/class-feature behavior,
feature-contained choices, spell/resource owners, armor training, remaining class tool choices,
starting equipment/cash, advancement beyond level 1, grant deletion/reversal, and UI discovery
remain separate slices.
