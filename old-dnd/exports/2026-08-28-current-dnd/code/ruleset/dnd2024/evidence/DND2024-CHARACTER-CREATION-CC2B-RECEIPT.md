# D&D 2024 character creation CC2B completion receipt

Status: **accepted**
Completed: 2026-08-27
Implementation: [CC2B Human Skillful contribution](../DND2024-CHARACTER-CREATION-CC2B-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Character Origins > Character Species > Human > Skillful*
(PDF p. 86)

## Delivered boundary

- Added `mechanic.dnd2024.species-skillful.resolve`, a pure JavaScript owner for the Skillful
  choice, and its governing procedure.
- The mechanic binds an immutable species definition/profile and proves the declarative
  `skillful` entitlement. It has no Human content-ID branch, so another reviewed profile declaring
  the same trait can reuse the owner.
- Exactly one of the existing 18 canonical skill IDs resolves into a contribution targeting
  `dnd2024.skill-proficiencies.skills` with `set-union` semantics.
- The resolver emits no effects, events, or notifications. The later atomic creation root remains
  responsible for combining species, background, and class contributions into one complete
  skill-proficiency component.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reference-reviewed for
  configured trait choice pools and separately staged chosen values. No Foundry code/data/assets
  or runtime dependency was adopted.

## Acceptance evidence

| Check | Result |
| --- | --- |
| Focused Skillful cases | 25 passed: all 18 skill IDs, exact target/contribution shape, entitlement rejection, invalid/derived input, source drift, seed independence, and zero-effect replay |
| Full D&D regression class | 137 passed |
| Catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full solution | 1,163 shared tests passed and 21 Local AI tests passed |
| Public/protocol surface | unchanged; no protocol walk required |

## Deliberate exclusions

CC2B does not write or merge an actor's final skills, grant Expertise, implement Human Resourceful
or Versatile, activate a feat benefit, hook Long Rest, consume Heroic Inspiration, or create an
actor. Human remains blocked from atomic creation until those owners and the combined grant root
are implemented.
