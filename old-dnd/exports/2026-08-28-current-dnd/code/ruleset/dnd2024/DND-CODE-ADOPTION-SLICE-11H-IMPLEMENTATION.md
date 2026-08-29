# D&D code-adoption Slice 11H implementation — Temporary HP/healing family acceptance

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), Temporary HP/healing 11H  
Ruleset alignment: `dnd2024-owned` acceptance evidence  
Source ID and locators: `source.dnd2024.srd-5.2.1`, Healing (PDF p. 17) and Temporary Hit Points
(PDF p. 18)  
Outcome: accept the complete second Slice 11 family against fresh activation, schemas, declared
dependencies, effects, replay, rollback ownership, compatibility, catalog, and full regression.  
Exclusions: new runtime behavior, Long Rest expiry, dying/death state, healing sources, events,
migrations, public operations, production C#, and other complex families.  
Allowed files/areas: this document, Parent 11/roadmap/dependency status, attribution notice, read-only
verification, and 11H receipt.  
Stop point: durable family acceptance with no active Temporary HP/healing leaf.

## Prerequisite evidence

- [11E decision receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11E-RECEIPT.md).
- [11F state/healing receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11F-RECEIPT.md).
- [11G absorption receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11G-RECEIPT.md).
- Existing generic application action/effect-batch receipts remain authoritative for atomic rollback.

## Acceptance matrix

| Case | Required result |
| --- | --- |
| Fresh activation | component, three mechanics including revised weapon apply, and governing procedures activate |
| State | positive-only optional buffer; grant/keep/replace/expire and corruption boundaries pass |
| Healing | bounded HP increase, excess reporting, full-HP no-change, buffer separation pass |
| Damage | mitigation then buffer then HP; exact/partial/retained/overkill and absent compatibility pass |
| Transaction | two-effect root, replay, stale/corrupt no-change, and generic rollback ownership hold |
| Source profile | core-only and optional legacy extension activation remain independent |
| Attribution | exact SRD source/locators and Foundry reference-only boundary are recorded |
| Regression | all D&D JavaScript, D&D tests, catalog, build, shared tests, and Local AI tests pass |

## Verification commands

- all `catalog/applications/dnd2024/mechanics/**/*.js` through `node --check`;
- focused weapon/Temporary HP/healing tests and complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog`;
- `dotnet build DantesRoleplay.slnx --no-restore`;
- `dotnet test DantesRoleplay.slnx --no-build`;
- Slice 11-scoped whitespace/diff and status/read-back audits.

No protocol walk is required because no MCP surface or dependency registration changed.
