# D&D 2024 character creation CC2E completion receipt

Status: **accepted**
Completed: 2026-08-27
Implementation: [CC2E immutable standard rest policy](../DND2024-CHARACTER-CREATION-CC2E-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185) and
*Rules Glossary > Short Rest* (PDF p. 187)

## Delivered boundary

- Re-adopted `dnd2024.rest-policy` under the current bounded schema and activated exactly one
  immutable `content.dnd2024.rest-policy.standard.v1` entity.
- Corrected the retained archive's stale page references to the actual pinned SRD 5.2.1 PDF:
  Long Rest is page 185 and Short Rest is page 187.
- The policy declares exact minimum duration/HP, sleep/light-activity limits, restart wait,
  partial-rest threshold, resumed-interruption extension, interruption vocabularies, and bounded
  consequence-handoff labels. Changed values fail schema validation.
- Removed the archive's `expire-temporary-hit-points` label from this policy. That behavior is
  sourced by the Temporary Hit Points rule and must remain with its own later recovery owner.
- Added no executable mechanic, episode, clock read/advance, event, subscription, recovery,
  Resourceful trigger, or state mutation.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reference-reviewed for
  separation among rest configuration, result calculation, bulk updates, and completed-rest
  notification. No Foundry code, data, UI, assets, direct mutation model, or runtime dependency was
  adopted.

## Acceptance evidence

| Check | Result |
| --- | --- |
| Focused rest-policy case | 1 passed: activation, schema acceptance, exact content/source values, changed-value rejection, and disposable persistence |
| Full D&D regression class | 163 passed |
| Catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full solution | 1,190 shared tests passed and 21 Local AI tests passed |
| Public/protocol surface | unchanged; no protocol walk required |

## Deliberate exclusions

CC2E does not start, time, interrupt, resume, ready, complete, or recover from a rest. It does not
grant Resourceful Heroic Inspiration or prove the 16-hour restart gate. The next bounded dependency
is rest-episode/start state bound to this exact policy and authoritative world-clock evidence,
followed by completion/recovery reactions and then the Resourceful source grant.
