# D&D 2024 character creation CC2C completion receipt

Status: **accepted**
Completed: 2026-08-27
Implementation: [CC2C Human Versatile with Skilled](../DND2024-CHARACTER-CREATION-CC2C-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Character Origins > Character Species > Human > Versatile*
(PDF p. 86) and *Feats > Origin Feats > Skilled* (PDF p. 87)

## Delivered boundary

- Re-adopted `dnd2024.feat-profile` under the current bounded schema and activated immutable v1
  identities for all four SRD Origin feats: Alert, Magic Initiate, Savage Attacker, and Skilled.
  Profiles declare only source identity, Origin category, and exact repeatability.
- Added `mechanic.dnd2024.species-versatile-skilled.resolve`, a pure JavaScript owner for the
  source-recommended Human Versatile → Skilled path, plus its governing procedure.
- The resolver binds both species and feat definitions, proves the `versatile` entitlement and
  Skilled behavior identity, and accepts exactly three unique skill/tool choices.
- Choice order is canonicalized into independent `set-union` contributions for the existing
  `dnd2024.skill-proficiencies.skills` and `dnd2024.tool-proficiencies.tools` owners. All-skill,
  all-tool, and mixed combinations are supported.
- The resolver emits no effects, events, or notifications and creates no parallel selected-feat or
  proficiency state. Other Origin-feat profiles do not imply implemented benefits.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reference-reviewed for
  bound item grants, configured trait pools, staged chosen values, and delayed actor application.
  No Foundry code/data/assets or runtime dependency was adopted.

## Acceptance evidence

| Check | Result |
| --- | --- |
| Focused Versatile/Skilled cases | 13 passed: four-profile inventory, mixed/all-skill/all-tool contributions, order/seed independence, replay, entitlement/behavior gates, invalid/duplicate/derived choices, and source drift |
| Full D&D regression class | 150 passed |
| Catalog validation | 144 records valid; 21 existing near-duplicate advisories; no live data touched |
| Full solution | 1,176 shared tests passed and 21 Local AI tests passed |
| Public/protocol surface | unchanged; no protocol walk required |

## Deliberate exclusions

CC2C does not persist a selected feat, merge final skill/tool state, resolve duplicates against
Skillful/background/class choices, or implement Alert, Magic Initiate, or Savage Attacker benefits.
It does not implement Human Resourceful, Long Rest, Inspiration consumption, or actor creation.
The Human species path now has behavior owners for Skillful and the recommended Versatile/Skilled
choice; Resourceful and final atomic grant composition remain its blockers.
