# D&D 2024 basic character creation MVP receipt

Status: **accepted**
Date: 2026-08-27
Owner: [CC-MVP implementation](../DND2024-CHARACTER-CREATION-MVP-IMPLEMENTATION.md)
Source: `source.dnd2024.srd-5.2.1`, *Character Creation* (PDF pp. 19–22), *Classes >
Fighter* (PDF pp. 47–48), and *Character Origins* (PDF pp. 83–86)

Historical boundary: this receipt proves the original Fighter template. The later
[all-class receipt](DND2024-CHARACTER-CREATION-ALL-CLASS-RECEIPT.md) widens the same creation root to
all twelve SRD level-1 class models without changing the evidence recorded below.

## Delivered boundary

- Added the immutable `dnd2024.character-creation-record` applied-versus-pending ledger and the
  governed `mechanic.dnd2024.character.basic.create` catalog JavaScript composition root.
- A closed request now creates one Soldier/Fighter level-1 actor for any accepted SRD species,
  derives abilities, Size, Speed, level, XP, Hit Points, baseline Armor Class, skills, saves, and
  weapon proficiencies, and records every unresolved entitlement without granting behavior.
- The same generic application transaction creates the actor, fourteen components, the active
  participation entity/component, and both D&D-owned participation relationships. No D&D formula,
  ID, branching rule, migration, or new public operation kind was added to C#.
- Exact operation replay is idempotent. Invalid input, inactive/source-drifted state, an existing
  actor, or a late injected transaction failure leaves no partial actor or participation.

## Evidence

- Focused basic-creation matrix: 9 passed, 0 failed.
- Complete `Dnd2024AbilityCheckTests`: 211 passed, 0 failed.
- Fresh disposable catalog validation: 144 valid records and 21 existing non-blocking advisories;
  no live data touched.
- Full solution: 1,249 shared tests and 21 Local AI tests passed, 0 failed.
- Fresh-context assertions read the actor and both participation links after commit; the existing
  character-sheet and Initiative mechanics consume the created state successfully.
- Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected for its
  staged advancement, Hit Point, and Size-selection patterns. No code, data, or assets were copied.

## Deliberate exclusions

This receipt does not claim source-complete character creation. Species traits, Savage Attacker,
Fighter feature behavior, armor training, starting equipment, languages, gaming-set choice,
spellcasting, rest completion, Resourceful, advancement, multiclassing, and UI discovery remain
separately gated. Every deferred entry grants no behavior until its owning feature is accepted.
