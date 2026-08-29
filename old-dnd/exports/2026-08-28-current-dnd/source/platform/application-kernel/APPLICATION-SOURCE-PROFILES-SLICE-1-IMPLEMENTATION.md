# Application source profiles Slice 1 implementation — optional D&D extension packaging

Status: **accepted 2026-08-26**  
Owner/roadmap: [Application source profiles dependency plan](APPLICATION-SOURCE-PROFILES-DEPENDENCY-PLAN.md), consumed by the [D&D 2024 roadmap](../../ruleset/dnd2024/ROADMAP.md)  
Dependency tree/leaf: Slice 1, extension catalog packaging  
Ruleset alignment: `dnd2024-compatible`; this slice packages content but defines no D&D rule  
Source ID and locator: not applicable; no rules-bearing content is introduced  
Outcome: package one disabled-by-default optional D&D source outside the SRD-faithful core glob.  
Exclusions: legacy item records, rule mechanics, component schemas, source-registration migrations,
automatic dependency expansion, campaign migration, UI selection, and activation of live data.  
Allowed files/areas: `catalog/extensions/dnd2024/`; the existing D&D source-registry procedure;
focused extension packaging tests; this dependency plan, D&D roadmap, and receipt.  
Stop point: the core-only profile excludes every extension file, while an explicit core-plus-
extension profile includes the closed package manifest and has a different deterministic preview.

## Confirmed decisions

The user confirmed on 2026-08-26 that D&D 2024 core remains as close to the official 2024 rules as
possible and that additions or alterations may be separately activatable extensions selected before
campaign creation. This confirms the permanent source/package IDs and the closed package-manifest
schema in this slice. Non-empty campaign migration remains excluded.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Core rules | SRD 5.2.1 remains the only core rule authority | `dnd2024-core` and `procedure.mechanic.dnd2024.source-registry` | the core glob remains unchanged and cannot include `catalog/extensions/` |
| Optional compatibility | not an SRD rule and never silently core | exact application source profiles | the extension declares `classification: compatibility` and `enabledByDefault: false` |
| Dependency | D&D extensions require the core source | package manifest plus D&D setup policy | `requiredSourceIds` is exactly `["dnd2024-core"]`; callers still submit the exact closed profile |
| Future content | each later rule/content cohort owns its own source and semantic review | D&D code-adoption plan | this scaffold contains no item, mechanic, component, entity, or procedure record |

## External implementation reference

There is no relevant Foundry rule implementation because this slice introduces no D&D behavior.
The pinned Foundry `system.json` is package-distribution evidence only and is not adopted: native
immutable source registration, preview, activation, and campaign binding remain the owners. No
Foundry code, metadata shape, assets, or runtime dependency are copied.

## Prerequisite evidence

- [Slice 0 receipt](receipts/APPLICATION-SOURCE-PROFILES-SLICE-0-RECEIPT.md) proves exact selectable
  source profiles, immutable activation evidence, and non-empty campaign migration gating.
- The existing D&D source-registry procedure owns `dnd2024-core` and SRD citation meaning.
- The remaining-static-content audit quarantines legacy hempen rope and Quiver, so this packaging
  slice deliberately stops before adopting either record.

## Runtime artifacts

- New source/package ID: `dnd2024-extension.legacy-equipment`.
- New extension ID: `dnd2024.legacy-equipment`.
- New closed schema: `catalog/extensions/dnd2024/extension-package.schema.json`.
- New inert package manifest:
  `catalog/extensions/dnd2024/legacy-equipment/extension-package.json`.
- Registration convention: allowed root `workspace`, relative glob
  `catalog/extensions/dnd2024/legacy-equipment/**/*`, trusted, precedence 100, logical identity
  `dnd2024-extension.legacy-equipment`.
- No database row or live registration is created by repository editing; tests use disposable
  registries.

## Authoritative state and closed input

The package manifest is authored catalog metadata. It is closed to schema version, extension and
application identities, source ID, display metadata, classification, exact required source IDs,
and default-enabled state. It contains no executable code, rule prose, paths outside its package,
campaign ID, activation fingerprint, or derived game value.

Runtime selection continues to use the exact `sourceIds` profile from Slice 0. The manifest records
the setup dependency but does not let a caller mutate registrations or make the extension core.

## Behavior, result, and typed effects

Registering the extension adds one immutable source registration. A core-only preview contains only
`dnd2024-core` documents. Selecting the core and extension adds the package manifest as an inert
source document and changes the preview/activation fingerprint. Reordering the two selected IDs is
deterministic. No JavaScript executes and no typed effect, ECS state, or transaction is produced.

## Failure, replay, and rollback contract

The manifest fails schema validation if it omits the exact core dependency, enables itself by
default, adds unknown fields, or uses an unknown classification. Focused package checks additionally
pin the permanent extension, application, and source identities. Missing or unknown source selection
continues to fail through Slice 0. Failed validation or preview changes no registry, activation,
campaign, or live database state.

## Implementation sequence

1. Author the closed package schema and inert legacy-equipment package manifest outside core.
2. Extend the existing source-registry procedure with the package boundary and exact registration.
3. Add focused schema, registration, exclusion, opt-in, and determinism tests.
4. Validate the catalog, D&D suite, solution, and full repository suite; record a receipt.

## Acceptance matrix

| Case | Expected |
| --- | --- |
| Manifest | schema compiles and the authored manifest validates exactly |
| Core isolation | core glob and core-only preview include no `catalog/extensions/` document |
| Registration | the optional source registers with its exact permanent source ID and separate glob |
| Opt-in | core-plus-extension contains the package manifest and differs from core-only |
| Determinism | reordered explicit source IDs produce the same extended preview fingerprint |
| Default | manifest is compatibility-classified, requires core, and is disabled by default |
| No rules | package contains no component, content entity, mechanic, procedure, query, or JavaScript |
| No live mutation | tests and validation use disposable state only |

## Verification commands

- focused `Dnd2024ExtensionPackagingTests`;
- D&D 2024 conformance suite;
- Release solution build and full shared suite;
- `roleplay validate catalog`; and
- `git diff --check`.

## Completion receipt and exit gate

Evidence is recorded in
[the Slice 1 receipt](receipts/APPLICATION-SOURCE-PROFILES-SLICE-1-RECEIPT.md). The slice stops
before adding the quarantined legacy equipment records or another extension content family.
