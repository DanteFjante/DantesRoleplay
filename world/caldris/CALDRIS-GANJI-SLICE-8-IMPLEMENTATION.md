# Caldris Slice 8 implementation — Ganji’s lived beginning

Status: **accepted**
Owner/roadmap: Caldris character-linked hooks and application World state
Dependency tree/leaf: Caldris playable opening; character-linked narrative preparation
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: not applicable; no D&D rule is implemented
Outcome: revise Ganji’s origin and add the named people, place, and independent cat that shape his opening in Alderwick.
Exclusions: playable actor creation, campaign participation, D&D species/class mechanics, combat statistics, controllable companion mechanics, active quest lifecycle, and played outcomes.
Allowed files/areas: this document, the Ganji character integration, one additive/update-only runtime manifest, one validation receipt, and one reviewed live `system.world-state.sync`.
Stop point: the approved setting records commit and read back; actor creation remains governed by the character-creation mechanic.

## Confirmed decisions

The player delegated names and setting particulars to the GM on 2026-08-30. Ganji is 18, abandoned by his human father and elven mother in Merebutton, spent two weeks travelling after his escape, uses hands or found implements in a fight, is laid-back about losses, and begins alone with Nettle, an independent cat who understands him. Character death is an allowed consequence. Catlike traits remain subtle and unexplained, with optional rather than central background hooks.

## Authoritative owners and behavior

`system.world-state.sync` owns the one reviewed atomic World update. Existing secret `secret.caldris.ganji.button-hills-origin` is revised in place without renaming its entity. Merebutton is a public nested location; Master Orun Vale and Nettle are public/GM World reference records respectively. No record supplies an actor, combat effect, class feature, ability, creature stat block, or instruction to control Nettle.

## Verification

Parse the exact manifest, dry-run it, commit the byte-identical payload, and read back every affected entity, component, containment, and relationship. Run `roleplay validate catalog` and focused World synchronizer tests.
