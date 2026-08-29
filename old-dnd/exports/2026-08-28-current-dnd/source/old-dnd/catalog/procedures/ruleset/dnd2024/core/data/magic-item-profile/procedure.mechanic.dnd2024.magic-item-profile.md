---
id: procedure.mechanic.dnd2024.magic-item-profile
category: ruleset.dnd2024.core.data.magic-item-profile
name: Define immutable D&D 2024 magic-item profiles
governs: catalog authoring of dnd2024.magic-item-profile on versioned magic-item content entities
status: active
---

## Description

Defines immutable source-cited magic-item catalog profiles. A profile is a versioned content
definition, not an ordinary physical-item definition or campaign item instance. It declares which
later rule families an item needs without reproducing or performing their mechanics.

## Instructions

1. Create a permanent `content.dnd2024.magic-item.<key>.v<version>` entity for every profile.
   Its profile identity/version and source reference are write-once; a correction creates a
   reviewed successor entity rather than changing a referenced definition in place.
2. Record only source classification, rarity, attunement requirement, physical-use mode,
   activation family, consumable flag, charge-policy kind, and canonical declared effect family.
3. Seed only Potion of Healing, Boots of Elvenkind, and Amulet of Health in this slice. These
   demonstrate consumable/non-attuned wearable/attunement-required wearable declarations without
   establishing physical possession or executing one benefit.

## Constraints

- Do not attach a profile to a creature, item instance, container, campaign, encounter, or turn.
  Feature 23 owns later physical definitions, instances, custody, and equipment state.
- `requiresAttunement` is source metadata, not an actor attunement or permission. No profile may
  count toward a limit, begin/end a rest, establish contact/distance, or grant an effect.
- Do not encode remaining charges, a bearer, price, spell, dice, healing amount, AC/attack/save
  modifier, target, action cost, command word, duration, executable code, or arbitrary payload.
- This slice has no action mechanic. A profile can be read with zero effects; later item-family
  work must use the named state and effect owners.

## Verification

- Fresh-import the catalog and prove the three profiles have exact key/version/source/classification
  data with no item-instance or creature component.
- Reject malformed, mismatched, unknown, extra, or executable data through the closed schema and
  focused assertions. Repeated catalog reads must be deterministic and effect-free.
