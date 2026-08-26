# D&D code-adoption Slice 8E receipt — inventory state and transitions

Date: 2026-08-25  
Status: **accepted**  
Boundary: Parent 8 / 8E five state contracts and fourteen classified mechanics

## Delivered

- Recovered immutable item definitions and activities, physical instance references, positive
  fungible quantities, and explicit equipment state while retaining containment as sole custody.
- Recovered record/read/create/place/move; stack record/create/split/merge/consume; equip/read/
  unequip; admitted transfer; and descriptor-driven activity use through the activated action path.
- Adapted output component dependencies into each create/split/activity declaration so automatic
  mapping resolves exact component versions before a typed effect can be translated.

## Verification

- Inventory focused activated-path scenarios — passed, 5/5 composite lifecycle cases covering all
  fourteen mechanics.
- Full activated D&D suite — passed, 53/53.
- All D&D JavaScript syntax checks — passed, 43/43.
- Core catalog validation — passed, 144 records with 21 existing advisory warnings; fresh D&D
  preview/activation passed in every focused case and no live data was touched.
- Full repository suite — passed, 1,055/1,055 plus 20/20 local-AI tests.

## Evidence and exclusions

Tests prove immutable references, placement/movement, effect-free reads, stack admission,
conservation, direct-content refusal, merge deletion, partial/final consumption, equipment
eligibility/custody, transfer refusal while equipped, bounded destination admission, activity-fixed
granting, duplicate-ID rollback, and no-change failures. No static SRD item fixtures, commerce,
magic, ammunition automation, derived AC/carrying, player permissions, migration, public operation,
live state, archive mutation, or donor runtime code was added.
