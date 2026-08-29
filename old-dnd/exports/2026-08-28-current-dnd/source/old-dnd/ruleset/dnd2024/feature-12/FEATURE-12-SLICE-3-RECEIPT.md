# Feature 12 Slice 3 receipt — validated resource spending

Completed: 2026-08-20

## Outcome

Added `mechanic.dnd2024.turn-budget.spend`, the sole normal consumer of an admitted turn budget.
It verifies the complete budget, active lifecycle state, Initiative snapshot, containment roster,
and subject membership before setting exactly one complete budget component. Action, Bonus Action,
free interaction, and movement require the derived active participant; Reaction remains available
to any admitted roster participant and refreshes at the start of that participant's next turn.

The mechanic accepts one closed resource input. Movement additionally requires a positive five-foot
multiple and rejects overspending rather than truncating it. It does not make attacks or any other
rule cost a resource.

## Verification

- Focused Feature 11/12 coverage: 10 passed.
- `roleplay validate catalog`: 226 records valid, 4 advisory near-duplicate warnings, and no live
  data touched.
- The repository catalog immutability test passes in isolation. Two parallel full-suite attempts
  each had 494 passing tests and one failure in that same test because another catalog test rewrote
  `catalog/manifest.json` during its before/after snapshot; this is a test-isolation issue, not a
  Feature 12 behavior failure.
- `git diff --check` reported only the workspace-wide existing CRLF advisory messages and no
  whitespace errors.

## Boundary held

Feature 12 tracks explicit spending only. It does not infer resource costs for attacks, spells, or
any other resolver. Feature 13 is the sole planned owner of incapacitation and speed-zero spend
prohibitions; Feature 19 will consume the established Reaction allowance for triggers.
