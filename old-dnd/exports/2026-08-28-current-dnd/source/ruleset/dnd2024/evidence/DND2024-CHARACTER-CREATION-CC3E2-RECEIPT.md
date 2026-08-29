# D&D 2024 character creation CC3E2 receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC3E2 implementation](../DND2024-CHARACTER-CREATION-CC3E2-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, Equipment Tools (PDF pp. 93–94), Bard level 1
(PDF pp. 31–32), and Monk level 1 (PDF pp. 49–50)

## Delivered boundary

- Basic character creation now accepts optional closed `classToolChoices` input derived from the
  selected class profile: exactly three distinct Musical Instruments for Bard or one Artisan's
  Tool or Musical Instrument for Monk. Every other class rejects the property.
- Omission remains backward compatible and keeps the exact class-owned choice pending. A supplied
  valid choice removes only that pending entry, stores canonical immutable selection evidence, and
  unions it with fixed/selected background and fixed class tool membership.
- Bard, both Monk option families, background/class choice composition, duplicate membership,
  invalid counts/families/vocabulary/cross-class data, exact replay, and late rollback are covered.
  The existing 48 background/class matrix proves omission compatibility.
- The shared tool vocabulary was corrected from 36 to all 37 SRD tools by adding the previously
  omitted `lyre` to the component schema, administrative writer, Versatile/Skilled resolver, and
  creator choice family. Recorder, Skilled, and Bard tests exercise it.
- No new permanent ID, migration, C# rule, endpoint, MCP kind, item, tool behavior, or transaction
  owner was introduced.

## Evidence

- Focused basic-creation/tool/Versatile group: 82 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 282 passed, 0 failed.
- Fresh disposable catalog validation: 144 valid records and 21 existing non-blocking
  near-duplicate advisories; no live data touched.
- Full solution: 1,321 shared tests and 21 Local AI tests passed, 0 failed.
- `git diff --check` passed. No protocol walk was required because MCP/protocol registration did
  not change.

## Deliberate exclusions

This receipt proves selected tool-proficiency membership only. Tool checks/actions/expertise,
physical tool items, starting equipment, restricted Martial weapon membership/enforcement,
spellcasting, feature behavior, multiclassing, retraining, and UI discovery remain separate slices.
