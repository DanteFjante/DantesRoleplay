---
id: procedure.mechanic.dnd2024.spell-identity
category: ruleset.dnd2024.core.data.spell-identity
name: Define immutable D&D 2024 spell identities
governs: catalog authoring of dnd2024.spell-identity on versioned spell content entities
status: active
---

## Description

Defines immutable source-cited spell identity catalog data. A spell identity is a versioned content
definition, not a character spell-list entry, spellcasting resource, casting operation, or resolved
spell effect.

## Instructions

1. Create a permanent `content.dnd2024.spell.<key>.v<version>` entity for every identity. Its key,
   version, level, and source reference are write-once; a correction creates a reviewed successor
   entity rather than changing an identity that a later profile or receipt may reference.
2. Record only the stable spell key, version, spell level, and individual source locator.
3. Seed Fire Bolt, Cure Wounds, and Dancing Lights only. They demonstrate a Cantrip, a level-1
   spell, and a concentration-duration spell identity without declaring a class spell list or
   resolving any spell.

## Constraints

- Do not attach a spell identity to an actor, class membership, campaign, encounter, action, turn,
  item, or active effect. Feature 31 later owns accepted spellcasting profiles/resources; Feature
  32 owns resolution and casting.
- A spell level is source metadata, not a slot cost, selection eligibility, permission, or derived
  statistic. A Cantrip does not grant a known spell, and a level-1 spell does not grant a slot.
- Do not encode school, casting time, range, components, target, duration, attack, save, dice,
  damage, healing, condition, concentration, class list, resource cost, executable code, or an
  arbitrary payload.
- This slice has no action mechanic. A profile can be read with zero effects; later consumers must
  use their established resource, action, and effect owners.

## Verification

- Fresh-import the catalog and prove the two identities have exact key/version/level/source data
  with no spellcasting mechanic or actor component.
- Reject malformed, mismatched, unknown, extra, or executable data through the closed schema and
  focused assertions. Repeated catalog reads must be deterministic and effect-free.
