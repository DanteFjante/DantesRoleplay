# D&D code-adoption Slice 11F receipt — Temporary HP state and bounded healing

Date: 2026-08-26  
Status: **accepted**

## Delivered boundary

- Activated closed `dnd2024.temporary-hit-points` positive-buffer state with absence as zero.
- Activated explicit first-grant, keep, replace, and expiry transitions using only supported typed
  component effects.
- Activated bounded healing against current HP with exact requested/applied/lost result data.
- Healing at maximum is a successful no-change result with no identical-value revision.
- Healing and Temporary HP transitions remain independent and emit no unsupported event or
  notification.
- Added fresh-activation, closed-input, corruption, no-change, replay, source, and separation tests.

## Verification

- JavaScript syntax: both new scripts passed `node --check`.
- Focused Temporary HP/healing tests: **3 passed, 0 failed**.
- Complete `Dnd2024AbilityCheckTests`: **89 passed, 0 failed**.
- Catalog validation: **144 valid records**, the same **21 unrelated advisories**, no live data.
- Solution build: **0 warnings, 0 errors**.
- Shared tests: **1,114 passed, 0 failed** plus Local AI **21 passed, 0 failed**.

## Deliberate exclusions

Weapon damage does not consume the new buffer until 11G. Long Rest expiry, dying consequences,
healing sources, events, live-state migration, public operations, and C# rule code remain absent.
