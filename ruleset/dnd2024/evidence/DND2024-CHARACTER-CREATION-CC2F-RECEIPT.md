# D&D 2024 character creation CC2F completion receipt

Status: **accepted**
Completed: 2026-08-27
Implementation: [CC2F authenticated rest episode start](../DND2024-CHARACTER-CREATION-CC2F-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185) and
*Rules Glossary > Short Rest* (PDF p. 187)

## Delivered boundary

- Added `dnd2024.rest-episode` as a closed active/duration-ready Short/Long Rest timing-evidence
  state with corrected source locators. It stores policy/world/start evidence but no activity,
  interruption, recovery, benefit, completion result, or caller assertion.
- Added `mechanic.dnd2024.rest.begin`, which binds exact creature, active world, and canonical
  policy roles; requires valid current HP >= 1; derives start minute from the authoritative
  `game.core.world.clock`; derives 60/480 minutes from policy; and accepts only rest kind.
- A successful start atomically adds the active episode and qualified `dnd2024.rest.world`
  membership. It emits no event/notification and changes no base-world component.
- Updated the D&D source-registry procedure to require the existing `game` owner as an explicit
  ordered base whenever a D&D mechanic consumes generic world/clock state. No parallel world/time
  component or D&D-specific C# branch was added.
- Corrected two generic gaps in the already accepted base-application seam: application-scoped ECS
  writes now admit component types owned by the immutable revision's exact direct bases, and
  automatic action mapping resolves an explicitly qualified base component against its matching
  allowed owner instead of returning during the primary-owner check. Unrelated owners remain
  rejected. The generic correction has its own
  [kernel receipt](../../../platform/application-kernel/receipts/APPLICATION-KERNEL-BASE-COMPONENT-SEAM-CORRECTION-RECEIPT.md).
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reference-reviewed for
  initiation/configuration separation from result calculation, bulk mutation, and completed-rest
  hooks. No Foundry code, data, UI, assets, direct mutation model, or runtime dependency was adopted.

## Acceptance evidence

| Check | Result |
| --- | --- |
| Focused rest-begin cases | 12 passed: Short/Long start, exact derivation, atomic state/membership, replay, duplicate, closed input, HP/world/clock/policy drift, and base-mapping gate |
| Generic ECS/execution regression | 11 passed, including exact direct-base admission and unrelated-owner rejection |
| Full D&D regression class | 175 passed |
| Catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full solution | 1,204 shared tests passed and 21 Local AI tests passed |
| Public/protocol surface | unchanged; no protocol walk required |

## Deliberate exclusions

CC2F does not advance time, infer sleeping/light activity, detect or record interruptions, resume an
interrupted Long Rest, mark duration ready, finish a rest, apply any benefit, enforce a prior
completion's 16-hour restart wait, or grant Resourceful Heroic Inspiration. The next dependency is
authenticated scoped clock/event progress plus interruption handling, then one completion/recovery
root that can emit the trustworthy Long Rest completion evidence Resourceful requires.
