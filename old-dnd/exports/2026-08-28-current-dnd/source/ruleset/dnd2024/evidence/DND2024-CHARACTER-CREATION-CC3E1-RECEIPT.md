# D&D 2024 character creation CC3E1 receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC3E1 implementation](../DND2024-CHARACTER-CREATION-CC3E1-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, Rules Glossary Armor Training (PDF p. 176) and each selected
class's registered source locator

## Delivered boundary

- Restored the retained `dnd2024.armor-training` component into the authored D&D application
  catalog with a closed Light/Medium/Heavy/Shield membership schema and fixed SRD attribution.
- Restored/adapted an effect-free diagnostic reader and a closed administrative record/correct
  writer. Empty is known none, absence is unknown, order is canonical, and invalid prior state is
  never repaired silently.
- Basic character creation now derives armor training only from the validated selected class
  profile, persists it for all classes including known-none, and removes only the satisfied
  `armor-training:*` state-owner deferrals.
- All twelve class declarations and all 48 background/class combinations preserve exact training;
  reordered source declarations fail before effects.
- Armor membership participates in the existing actor/participation transaction, replay, and late
  rollback. No equipped-item check, AC formula, Shield bonus, untrained drawback, C# rule,
  migration, endpoint, MCP kind, or transaction owner changed.

## Evidence

- Focused armor-owner/basic-creation group: 63 passed, 0 failed, covering absent/known-none,
  record/correct/replay, canonicalization, invalid input/prior state, every class, the full 48-pair
  matrix, reordered-profile rejection, exact creation replay, and late rollback.
- Complete `Dnd2024AbilityCheckTests`: 271 passed, 0 failed.
- Fresh disposable base-catalog validation: 144 valid records and 21 existing non-blocking
  near-duplicate advisories; no live data touched. The activated D&D harness compiled and
  exercised the restored application component, reader, writer, and creation integration.
- Full solution after concurrent web edits settled: 1,310 shared tests and 21 Local AI tests
  passed, 0 failed.
- `git diff --check` passed. No protocol walk was required because MCP/protocol registration did
  not change.

## Deliberate exclusions

This receipt completes armor-training membership only. Equipped-armor eligibility, untrained D20/
spellcasting consequences, Armor Class and Shield calculations, don/doff timing, Speed, equipment,
multiclass aggregation, temporary grants, and UI discovery remain separate slices.
