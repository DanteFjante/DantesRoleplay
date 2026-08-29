# D&D 2024 character creation CC3D1A receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC3D1A implementation](../DND2024-CHARACTER-CREATION-CC3D1A-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, Alert (PDF page 87), Character Advancement,
and Initiative

## Delivered boundary

- Individual Initiative accepts only the optional Boolean `useAlertInitiativeProficiency` alongside
  its existing explicit roll circumstances. Omission or false preserves the seeded Dexterity result;
  true is never inferred.
- Optional actor feature-grant state is validated as a complete closed envelope. Exactly one
  non-repeatable `content.dnd2024.feature.alert.v1` Origin-feat/default grant makes the benefit
  available. Any schema-valid declaring grantor/source is accepted so extension content does not
  need Criminal-specific Initiative code; malformed, duplicate, wrong-kind, or misconfigured Alert
  state fails closed.
- An Alert holder requires exact current character-level state. JavaScript derives Proficiency
  Bonus as `2 + floor((level - 1) / 4)`, reports the eligible bonus and canonical Alert source, and
  adds one `feat:alert` modifier only on explicit use. No caller supplies the level, bonus, feature,
  grantor, source, modifier, roll, or result.
- Initiative remains effect-free. Existing Advantage/Disadvantage, deterministic rolls, rest plans,
  encounter ordering, tie decisions, and encounter-owned rest effects are unchanged and compose the
  adjusted child result.
- Criminal creation still grants the source-bound Alert identity. Its pending ledger now names only
  `behavior:initiative-swap`; the implemented Initiative Proficiency is no longer mislabeled wholly
  unimplemented. All other feat/class behavior denial remains unchanged.
- No new permanent ID, component schema, migration, C# rule, endpoint, MCP kind, stored Initiative
  count, parallel Proficiency Bonus field, or transaction owner was introduced.

## Evidence

- Focused CC3D1A matrix: 25 passed, 0 failed.
- Broader Initiative group: 29 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 346 passed, 0 failed. This gate initially found one stale
  48-pair assertion for Alert's old generic pending key; the assertion was corrected and the full
  D&D gate then passed.
- Fresh disposable catalog validation: 144 valid records and 21 existing non-blocking
  near-duplicate advisories; no live data touched.
- Fresh isolated full solution, including concurrent knowledge work: 1,396 shared tests and 21
  Local AI tests passed, 0 failed; build completed with 0 warnings and 0 errors.
- Real JSON-RPC protocol walk from the fresh isolated build: 6 passed, 0 failed, 2 deliberately
  skipped audit/navigation cases.
- Independent adversarial review found and drove fixes for strict object input, extension-compatible
  grant provenance, eligible-versus-applied bonus evidence, and true nonrepeatability coverage. The
  final review reported no remaining issue.
- `git diff --check` passed with only the repository's line-ending notices.

## Deliberate exclusions

This receipt does not implement Alert Initiative Swap, willing-ally consent, same-combat proof,
Incapacitated gating, post-roll timing windows, persistent individual Initiative, generic feature
expression, other Origin-feat behavior, or spell/feature execution owners.
