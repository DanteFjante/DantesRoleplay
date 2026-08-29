# D&D code-adoption Slice 8E implementation — inventory state and transitions

Status: **accepted**  
Parent: [Slice 8 complete native-recovery design](DND-CODE-ADOPTION-SLICE-8-DESIGN.md), leaf 8E  
Prerequisites: accepted application typed effects, containment, reference projection, and child
snapshot authority  
Ruleset alignment: `dnd2024-owned`  
Source: `source.dnd2024.srd-5.2.1`, Equipment  
Outcome: Recover the five classified inventory state contracts and all fourteen classified item
state/transition mechanics.  
Exclusions: Static SRD item fixture import, prices/purchases, magic effects, ammunition automation,
derived Armor Class, carrying totals, player permission, migrations, public operations, and archive
deletion.  
Allowed areas: classified inventory components/mechanics/procedures, generic containment/effect
regression tests only if a defect is exposed, D&D activated-path tests, and Parent 8 evidence.  
Stop point: all fourteen mechanics have activated success/failure/atomicity evidence.

## State and dependency boundary

`dnd2024.item-definition` is immutable catalog state; `dnd2024.item-instance` stores only its exact
definition entity ID; containment is sole custody; `dnd2024.item-quantity` is a positive fungible
count whose derived stack key equals that definition ID; `dnd2024.equipment-state` stores only held,
worn, or unequipped; `dnd2024.item-activity` is a closed immutable consume-and-grant descriptor.

Mechanics name all role dependencies. Definition references are projected from instance IDs,
container contents are bounded, and no mechanic queries a database. Administrative create/place and
move helpers are distinct from admitted transfer. Split, merge, consume, activity use, equipment,
and transfer propose typed effects only; the generic runner owns exact snapshot authorization,
transaction, rollback, duplicate-ID failure, containment cycle safety, and replay.

## Acceptance

Acceptance covers record/read/create/place/move; positive stack record/split/merge/partial/final
consume with conservation and direct-content refusal; eligible equip/read/unequip and custody
refusal; whole-item transfer and container admission; descriptor-driven atomic activity use;
closed inputs, corrupt/mismatched definitions, duplicate entities, revisions, containment revisions,
operation replay, rollback, application preview/activation, JavaScript syntax, and regressions.
