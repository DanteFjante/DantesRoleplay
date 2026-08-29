# D&D optional legacy equipment Slice 2A receipt — hempen rope compatibility definition

Status: **accepted**
Completed: 2026-08-26
Accepted implementation:
[Slice 2A](../../DND-OPTIONAL-LEGACY-EQUIPMENT-SLICE-2A-IMPLEMENTATION.md)

## Delivered

- Added `item.dnd2024.hempen-rope-50-foot.v1` only under the disabled-by-default
  `dnd2024-extension.legacy-equipment` source. Its archived static facts remain definition version
  1, adventuring gear, separate stacking, 5 pounds, and 50 feet.
- Replaced the archive's unsupported SRD claim with the exact extension source ID and archive
  locator. The source archive remains hash-locked at
  `5103289F8A87B8CDC057ADD232C0995FF00B2AF49A73EEBFC8A770D3D4B27779`; the reviewed target is
  `2E966BFEDB14A0E0C08D4FB7322942B53BD6C05C814F072E9C24711A3E1374CD`.
- Generalized only `dnd2024.item-definition.sourceRef.sourceId` from an SRD constant to a bounded
  authored source ID. The governing procedure and regression test require all 31 core item
  definitions to retain `source.dnd2024.srd-5.2.1`.
- Proved core-only activation excludes the rope and explicit core-plus-extension activation retains
  it. A disposable carried instance contributes exactly 5 pounds through the existing effect-free
  burden reader.
- Added no JavaScript rule, C# rule branch, typed effect, price, rope behavior, live registration,
  campaign mutation, database migration, or automatic source selection. Quiver remains absent.

## Evidence

- Focused extension packaging and rope-consumption checks: 5 passed, 0 failed.
- D&D 2024 core plus optional packaging checks: 85 passed, 0 failed.
- Full shared suite: 1,106 passed, 0 failed; local-AI suite: 21 passed, 0 failed.
- Release solution build: 0 warnings, 0 errors.
- Catalog validation: 144 core records valid with the same 21 advisory overlaps; no live data was
  touched. The separate extension record is directly validated against the activated shared schema.
- Repository `git diff --check` passed with existing line-ending notices only; the complete Slice 2A
  file set has no accidental trailing whitespace.

## Deliberate exclusions and next gate

This slice stops before Quiver, item prices, rope actions, tying/breaking/climbing behavior, another
optional item, UI selection, campaign installation, or migration of an existing non-empty campaign.
Any next content family remains a separate coherent slice with exact provenance and source-profile
acceptance.
