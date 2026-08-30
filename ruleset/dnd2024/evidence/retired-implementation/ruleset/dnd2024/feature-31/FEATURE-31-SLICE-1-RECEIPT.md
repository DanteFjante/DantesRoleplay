# Feature 31 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Added the closed `dnd2024.spell-identity` component and its governing static-definition
  procedure.
- Added immutable, source-cited identities for Fire Bolt (Cantrip), Cure Wounds (level 1), and
  Dancing Lights (Cantrip). Dancing Lights is the minimal static identity expansion required for
  Feature 32’s concentration-duration profile proof; it does not create concentration state.
- Each identity declares only its key, version, spell level, and individual source locator.

## Explicitly not delivered

No class spell list or casting profile, actor known/prepared state, spell slot, casting ability,
derived save DC or attack modifier, cast action, target, range, duration, Concentration state,
spell attack/save, damage, healing, condition, active effect, event, subscription, or campaign
state was added.

## Verification evidence

- `CatalogFeature31Tests` proves exact static readback for all three
  identities and rejection of a mismatched level, invalid version, unapproved source, and encoded
  healing data.
- `roleplay validate catalog` passed with repository-wide near-duplicate warnings. Disposable
  validation did not touch live data.

## Next boundary

Feature 31 Slice 2 remains blocked on a ratified caster source/class seam for an immutable casting
profile and spell-list declaration. Feature 32 can now begin its separate static
spell-resolution-profile slice, but it must not create a cast action or any spell effect.
