# D&D optional legacy equipment Slice 2A implementation — hempen rope compatibility definition

Status: **accepted**  
Owner/roadmap: [D&D 2024 roadmap](ROADMAP.md)  
Dependency tree/leaf: [Application source profiles](../../platform/application-kernel/APPLICATION-SOURCE-PROFILES-DEPENDENCY-PLAN.md), Slice 2A  
Ruleset alignment: `dnd2024-compatible`; this is explicitly non-core legacy compatibility content  
Source ID and locator: local archive
`old-dnd/catalog/world/entities/item.dnd2024.hempen-rope-50-foot.v1.json`, SHA-256
`5103289F8A87B8CDC057ADD232C0995FF00B2AF49A73EEBFC8A770D3D4B27779`; not an SRD rule source  
Outcome: opt in to one immutable legacy hempen-rope definition without changing SRD core content.  
Exclusions: price, rope actions, tying/breaking/climbing rules, Quiver, item instances, automatic
campaign installation, public operations, generic C#, database migration, and live activation.  
Allowed files/areas: the existing item-definition schema and procedure; the legacy-equipment
extension content directory and manifest; D&D activated-content tests; this plan, dependency status,
roadmap, and receipt.  
Stop point: core-only still excludes the rope and all core item definitions remain SRD-cited;
core-plus-extension retains and consumes the 5-pound, 50-foot compatibility definition.

## Confirmed decisions

The user confirmed on 2026-08-26 that D&D 2024 core stays as close as possible to the official 2024
rules and that other or altered content may be activated as an extension before campaign creation.
This confirms reuse of permanent ID `item.dnd2024.hempen-rope-50-foot.v1`, generalization of the
shared item-definition provenance field, and the explicitly non-SRD extension record. Existing
non-empty campaign schema migration remains excluded.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Core adventuring gear | core records must cite `source.dnd2024.srd-5.2.1` and exact item locators | `dnd2024-core`; item-definition schema/procedure | a new regression checks every core item-definition source ID after the schema is generalized |
| Legacy rope identity | archive asserts hempen, 50 feet, and 5 pounds; SRD 5.2.1 evidence does not support the subtype/length claim | hash-locked archive record | preserve those three static facts only under `dnd2024-extension.legacy-equipment` |
| Provenance | optional content must not falsely claim SRD authority | source-profile package manifest | replace the archived SRD sourceRef with the exact optional source ID and archive locator |
| Runtime use | immutable definitions are referenced by exact item instances; burden derives from rational mass | existing item-definition, item-instance, and burden owners | prove a 5-pound carried instance through existing mechanics without new rules or effects |

## External implementation reference

The pinned Foundry dnd5e reference contains no adopted exact 2024 hempen-rope source for this leaf,
and its package/runtime formats are not relevant to the repository's immutable item-definition
owner. No Foundry code, content, assets, metadata, or rule meaning are copied. The local archived
record is the sole compatibility source and is hash-locked above.

## Prerequisite evidence

- [Extension packaging receipt](../../platform/application-kernel/receipts/APPLICATION-SOURCE-PROFILES-SLICE-1-RECEIPT.md)
  proves the optional source is separate, disabled by default, and requires `dnd2024-core`.
- [Remaining static-content gates](adoption/evidence/DND-CODE-ADOPTION-SLICE-10-REMAINING-STATIC-CONTENT-GATES.md)
  proves why the rope was excluded from SRD core and why Quiver remains unsafe.
- Existing item-definition, item-instance, burden, transfer, schema validation, activation, and
  exact state-space binding owners remain unchanged.

## Runtime artifacts

- Revised schema meaning: `dnd2024.item-definition.sourceRef.sourceId` changes from the SRD constant
  to a bounded authored source ID. All other item-definition fields and bounds remain unchanged.
- Revised governing procedure: core definitions must retain the SRD source ID; optional definitions
  must cite their exact selected source.
- Recovered extension entity ID: `item.dnd2024.hempen-rope-50-foot.v1`.
- Extension entity path:
  `catalog/extensions/dnd2024/legacy-equipment/content/entities/adventuring-gear/item.dnd2024.hempen-rope-50-foot.v1.json`.
- Extension provenance: source ID `dnd2024-extension.legacy-equipment`, locator
  `Legacy archive > item.dnd2024.hempen-rope-50-foot.v1`.
- No migration or live registration is written. A changed schema becomes a new immutable component
  type version only at a later explicit registration/activation boundary.

## Authoritative state and closed input

The archived hash and exact target record are authoring inputs. Runtime callers never supply item
mass, length, stack policy, provenance, or definition identity. The selected activation manifest
chooses whether the extension entity exists; campaign item instances reference its exact immutable
definition ID.

The target preserves only: definition version 1, adventuring-gear kind, separate stack policy,
5-pound rational mass, 50-foot rational length, and extension provenance. It adds no behavior,
capacity, price, equipment mode, activity, or free-form rule text.

## Behavior, result, and typed effects

Core-only preview and activation exclude the extension entity. Core-plus-extension includes it and
the shared schema accepts its honest extension source ID. Creating a disposable physical instance
through existing test setup makes the existing burden reader report exactly 5 pounds. Reading
burden is effect-free; this slice adds no mechanic or transaction owner.

## Failure, replay, and rollback contract

Archive hash drift, changed ID/name/static fact, a claimed SRD source, wrong extension source,
unsupported fields, schema failure, core-source leakage, duplicate entity identity, activation
omission, or unexpected effect fails focused acceptance. Failed validation changes no activation,
component registration, ECS state, campaign, or live database.

## Implementation sequence

1. Generalize only item-definition source identity and strengthen the governing provenance rule.
2. Add the transformed hash-locked entity under the optional extension source.
3. Add core-provenance, schema, source-profile, exact-transform, and burden-consumption tests.
4. Run D&D, catalog, solution, and repository acceptance and record the receipt.

## Acceptance matrix

| Case | Expected |
| --- | --- |
| Archive lock | exact archived SHA-256 matches before target comparison |
| Exact transform | only sourceRef and clear compatibility display labeling differ from archived static data |
| Core isolation | core-only active source paths exclude the rope entity |
| Core provenance | every core `dnd2024.item-definition` still cites `source.dnd2024.srd-5.2.1` |
| Extension provenance | rope cites `dnd2024-extension.legacy-equipment`, never SRD |
| Schema | existing core definitions and the extension rope validate under the revised schema |
| Opt-in | core-plus-extension activation retains the exact rope path |
| Consumption | one carried rope contributes exactly 5 pounds to existing burden output |
| Effects | definition and burden reads introduce no new mechanic/effect behavior |
| Quarantine | Quiver remains absent from core and extension |

## Verification commands

- focused optional legacy-equipment and activated D&D tests;
- D&D 2024 conformance suite;
- Release solution build and full shared suite;
- `roleplay validate catalog`; and
- `git diff --check`.

## Completion receipt and exit gate

Acceptance is recorded in
[`adoption/evidence/DND-OPTIONAL-LEGACY-EQUIPMENT-SLICE-2A-RECEIPT.md`](adoption/evidence/DND-OPTIONAL-LEGACY-EQUIPMENT-SLICE-2A-RECEIPT.md).
The slice stops before Quiver, prices, rope behavior, another optional item, or campaign
installation.
