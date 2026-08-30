# Feature 32 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Added the closed `dnd2024.spell-resolution-profile` component and its governing static-
  definition procedure.
- Co-located one profile with each immutable spell identity: Fire Bolt declares an instantaneous
  spell-attack/damage interface, Cure Wounds declares an instantaneous special/healing interface,
  and Dancing Lights declares a concentration-duration special/light interface.
- Every profile declares only its version, matching spell key/version/source, action family,
  range/target/area family, duration family, concentration requirement, resolution family, and
  canonical consequence-family key.

## Explicitly not delivered

No spell list, slot, cast action, action spending, target, range check, area, D20 roll, save,
damage, healing, condition, active effect, effect ending, duration clock, concentration state,
event, subscription, actor state, or campaign state was added.

## Verification evidence

- `CatalogFeature31Tests` and `CatalogFeature32Tests` prove co-located identity/profile agreement,
  exact static fixtures, an instant-versus-concentration-duration distinction, and rejection of
  mismatched source/identity/lifecycle data and encoded effect data.
- `roleplay validate catalog` passed with repository-wide near-duplicate warnings. Disposable
  validation did not touch live data.

## Next boundary

Feature 32 Slice 2 remains blocked on a confirmed active-effect identity, ending protocol, and
event-composition contract. It must not substitute a caster/target component or implicit duration
clock for that missing lifecycle owner.
