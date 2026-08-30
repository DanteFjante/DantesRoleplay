# DND2024 Party Slice 3 receipt — cinematic Overview and shared-property seam

Status: **accepted 2026-08-30**

Implementation document: `web/DND2024-PARTY-CINEMATIC-OVERVIEW-SLICE-3-IMPLEMENTATION.md`

Dependency tree: `web/DND2024-WEB-INFORMATION-HUB-DEPENDENCY-TREE.md`, Leaf 10 / E2–E5

Local page revision: **15**

## Delivered

- Replaced the selected-character Overview's generic three-card dashboard with an original
  cinematic party-RPG composition: dominant companion focus, large identity treatment, status and
  record hierarchy, defining details, and carried-equipment preview.
- Added direct Overview actions to the existing Sheet and Inventory sections without adding routes,
  writes, or parallel character state.
- Kept initials and abstract ornament as the portrait fallback. No appearance, portrait, or copied
  Baldur's Gate 3 art/layout asset was invented or adopted.
- Added a shared Party holdings area for owned locations and wagons/caravans. Both report **Not
  recorded**, explicitly distinguishing missing ownership authority from an authoritative empty
  collection.
- Verified live state contains no party entity, ownership relationship, wagon/caravan entity, or
  vehicle component. Campaign references, visits, containment, and Orban's caravan history remain
  excluded as ownership evidence.
- Added E5 to the Party dependency path so exact audience-safe property, vehicle information, and
  cargo remain a named future boundary rather than an implicit UI guess.

## Evidence

- Full React companion suite: **127 passing, 0 failing**.
- Production server-bundle build passed and emitted the revised script and stylesheet.
- Revision 14 was exported before publication. Published `dnd2024-play` revision **15** with five
  assets; control readback reported active/latest revision 15, and the page, script, and stylesheet
  each returned HTTP 200.
- The actual `PartyView` component was rendered with the current Orban-shaped record for isolated
  visual acceptance. Desktop inspection confirmed the intended companion, detail, equipment, and
  holdings hierarchy. The Inventory call to action opened the exact existing Inventory section and
  browser logs contained no warnings or errors.
- Narrow verification at 390 × 844 confirmed the composition stacks without page-level horizontal
  overflow (`scrollWidth` equaled `clientWidth`); the existing dossier navigation remains its own
  intentional horizontal scroller.
- During final publication, another local process occupied port 6217 without a host-authorized
  table seat. The published bundle remained readable, but live campaign interaction returned the
  existing denied state; visual acceptance therefore used the same built component on an isolated
  local harness without changing game data or the concurrent host process.

## Deliberate exclusions

- a party entity or decision that the campaign root itself owns shared property;
- new ownership relationship kinds, schemas, migrations, or live property records;
- a wagon, caravan, mount, owned location, cargo item, or inventory mutation;
- deriving ownership from a visit, campaign reference, current location, containment, or prose;
- character portrait generation without an exact entity-owned appearance/media contract;
- derived sheet calculations, nested inventory, and Orban mechanical-draft approval; and
- copied Baldur's Gate 3 artwork, layout, typography, UI assets, or trade dress.

## Next Party boundary

Confirm whether shared property belongs to the current campaign root or to a new persistent party
entity. Then author one ownership contract and audience projection that can return exact owned
locations and vehicle instances; vehicle detail may compose its existing profile/durability state
and bounded direct cargo containment. Do not create a sample wagon unless the campaign actually
acquires one.
