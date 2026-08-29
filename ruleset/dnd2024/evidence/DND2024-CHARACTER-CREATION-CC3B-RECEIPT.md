# D&D 2024 character creation CC3B receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC3B implementation](../DND2024-CHARACTER-CREATION-CC3B-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, Character Origin languages (PDF p. 20), Soldier (PDF p. 83),
and Gaming Set variants (PDF p. 94)

## Delivered boundary

- Added one optional closed `originChoices` object to the existing basic-creation request while
  preserving the accepted omitted-choice request exactly.
- Exact choices accept two distinct non-Common Standard languages. Soldier additionally requires
  one of Dice Set, Dragonchess Set, Playing Cards, or Three-Dragon Ante; fixed-tool backgrounds
  reject an injected tool choice.
- Complete choices apply Common plus both languages and merge the selected Soldier Gaming Set with
  any fixed class tools through the canonical proficiency components.
- The immutable creation record stores canonical `languageChoices` and, when applicable,
  `backgroundToolChoice`. Its schema rejects a standalone tool choice without language choices.
- Satisfied language/tool pending entries disappear; all equipment, feat, class, species, spell,
  and other unimplemented entitlements remain sorted and unchanged.
- Exact replay and injected late rollback use the existing transaction owner and remain atomic.

## Evidence

- Focused complete/invalid/replay/rollback origin choices: 11 passed, 0 failed, including all four
  Gaming Set variants and nine malformed or cross-background cases.
- Complete basic-character-creation group: 56 passed, 0 failed; the CC3A 48-pair omitted-choice
  compatibility matrix remains green.
- Complete `Dnd2024AbilityCheckTests`: 258 passed, 0 failed.
- Fresh disposable base-catalog validation: 144 valid records and 21 existing non-blocking
  near-duplicate advisories; no live data touched. The activated D&D harness schema-validated the
  revised application record and mechanic.
- Full solution: 1,297 shared tests and 21 Local AI tests passed, 0 failed.
- `git diff --check` passed. No protocol walk was required because protocol registration did not
  change.

## Deliberate exclusions

This receipt completes only the origin language and selectable background-tool choices. Omission
still deliberately creates an incomplete basic actor with explicit pending entries. Equipment
package/cash choice, item instantiation, Origin-feat/class-feature grants and behavior, rare or
feature-granted languages, class tool choices, source-complete species behavior, and UI discovery
remain separate slices.
