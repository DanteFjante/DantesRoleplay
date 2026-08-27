# Character creation CC2G receipt - rest activity and interruption progress

Status: **accepted**
Date: 2026-08-27
Owner: [CC2G implementation](../DND2024-CHARACTER-CREATION-CC2G-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185) and
*Rules Glossary > Short Rest* (PDF p. 187)

## Delivered boundary

- Evolved `dnd2024.rest-episode` to retain the authoritative observed clock minute/revision and
  closed aggregate light-activity, sleep, and Long Rest interruption evidence.
- Updated rest start to initialize those values from mapped base-world clock state.
- Added stateless `mechanic.dnd2024.rest.progress`: caller supplies only activity intent; the exact
  interval, counters, thresholds, and duration-ready status are derived in catalog JavaScript.
- Added `mechanic.dnd2024.rest.interrupt`: every source interruption kind is policy-validated;
  interrupted Short Rest atomically removes episode/membership with no benefit, while Long Rest
  remains active and adds one required hour per interruption.
- Preserved the phase boundary: `ready` is not finished, partial Short Rest credit is reported only,
  and no recovery, recharge, Heroic Inspiration, Resourceful, event, or notification is produced.
- Corrected one unrelated uncommitted web-route acceptance issue by explicitly marking the existing
  `WebHtmlReader` handler parameter as a service binding. This changes no product behavior and made
  its seven route tests discoverable/executable.

## Evidence

- Focused rest matrix: 23 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 194 passed, 0 failed.
- Fresh disposable catalog validation: 144 valid records, 21 existing non-blocking advisories; no
  live data touched.
- Sequential full solution: 1,231 shared tests and 21 Local AI tests passed, 0 failed.
- The parallel solution invocation exhausted/crashed one test host after reporting no assertion
  failures; the required sequential run (`--maxcpucount:1`) completed cleanly and is the acceptance
  evidence.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reviewed for phase
  separation and bulk completion behavior; no source code, data, assets, or runtime dependency was
  adopted.

## Deliberate exclusions

Automatic damage/Initiative/non-Cantrip-spell/physical-exertion adapters, rest finish, Hit Point Die
handling, HP/maximum/ability/Exhaustion recovery, source-specific recharge, Temporary Hit Point
expiry, restart cadence, Resourceful, Heroic Inspiration grant, public endpoints, and final actor
composition remain later gated work.
