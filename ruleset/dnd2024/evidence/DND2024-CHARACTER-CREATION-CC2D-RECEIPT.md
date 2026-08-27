# D&D 2024 character creation CC2D completion receipt

Status: **accepted**
Completed: 2026-08-27
Implementation: [CC2D Heroic Inspiration grant foundation](../DND2024-CHARACTER-CREATION-CC2D-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Heroic Inspiration* (PDF p. 183)
and *Character Origins > Character Species > Human > Resourceful* (PDF p. 86)

## Delivered boundary

- Re-adopted `dnd2024.heroic-inspiration` under the current bounded schema. Component presence is
  exactly one held Heroic Inspiration instance; absence is none. No count, source, recipient, rest,
  die, result, expiry, or history is stored.
- Added `mechanic.dnd2024.heroic-inspiration.grant`, a state-backed JavaScript normal grant with
  exactly one profiled-character role and exactly `{}` input. Eligibility and held state cannot be
  asserted by callers.
- A first grant adds canonical `{}` atomically. Same-operation replay records no second effect; a
  distinct duplicate, invalid profile, invalid input, or corrupt held state fails without changing
  value or revision.
- Fixed SRD provenance is returned in result data. Events and notifications are empty; species,
  feat, rest, campaign, recipient, and dice context are neither accepted nor inferred.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reference-reviewed for
  single-instance Inspiration state and its post-update completed-rest phase. No Foundry code,
  data, assets, direct mutation model, or runtime dependency was adopted.

## Acceptance evidence

| Check | Result |
| --- | --- |
| Focused Heroic Inspiration cases | 12 passed: first grant, exact effect/result, replay, distinct duplicate, strict input, profile gates, corrupt state, and no-change failures |
| Full D&D regression class | 162 passed |
| Catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full solution | 1,189 shared tests passed and 21 Local AI tests passed |
| Public/protocol surface | unchanged; no protocol walk required |

## Deliberate exclusions

CC2D does not authenticate or resolve a Long Rest, invoke the Human Resourceful trait, select an
overflow recipient, transfer a newly gained instance, or consume Inspiration against a concrete die
attempt. It does not complete Human species grants or create an actor. The next dependency is a
bounded Long Rest lifecycle/completion owner that can produce trustworthy event evidence; only then
may Resourceful call this shared grant target.
