# D&D code-adoption Slice 11D implementation — damage-mitigation family acceptance

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), damage-mitigation 11D  
Ruleset alignment: `dnd2024-owned` acceptance evidence  
Source ID and locators: `source.dnd2024.srd-5.2.1`, damage Resistance/Vulnerability/Immunity (PDF
p. 17) and Petrified Resist Damage (PDF p. 186)  
Outcome: accept the complete first Slice 11 family against fresh activation, declared dependencies,
state/effect boundaries, replay, compatibility, catalog, and repository regression.  
Exclusions: any new runtime behavior, migration, source-profile change, public surface, production
C#, and every damage concern excluded by 11B/11C.  
Allowed files/areas: this document, Parent 11/roadmap/dependency statuses, read-only verification,
and the 11D receipt.  
Stop point: durable family receipt and no active damage-mitigation leaf.

## Confirmed decisions

The user authorized SRD-faithful core behavior with non-SRD additions isolated as pre-campaign
extensions. 11A fixed the permanent IDs/rule boundary, 11B accepted state/profile ownership, and 11C
accepted weapon-damage composition. 11D creates no artifact ID or runtime change.

## D&D 5e 2024 alignment

| Concern | Accepted evidence |
| --- | --- |
| Rule meaning | exact official SRD 5.2.1 PDF hash/headings/pages in the 11A receipt |
| State | one canonical mitigation component; Conditions remain independently authoritative |
| Dependencies | resolver composes Condition state-effects; weapon application composes resolver |
| Arithmetic | Immunity, one Resistance floor-halving, then Vulnerability; Petrified halves once |
| Effects | writer has one add/set; resolver is effect-free; positive weapon damage has one HP set; zero damage has none |
| Transaction | existing application action runner owns atomic commit, replay, rollback, and audit |
| Compatibility | absent mitigation remains valid/unmitigated; no campaign binding or live database changes |

## External implementation reference

The pinned Foundry paths and exact useful behavior are recorded in 11A and 11C. Foundry remains
reference-only; no Foundry code, data, assets, or runtime dependency was adopted.

## Prerequisite evidence

- [11A decision receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11A-RECEIPT.md).
- [11B state/profile receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11B-RECEIPT.md).
- [11C behavior receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11C-RECEIPT.md).
- [Pre-Slice 11 acceptance](adoption/evidence/DND-CODE-ADOPTION-PRE-SLICE-11-ACCEPTANCE.md).

## Acceptance matrix

| Case | Required result |
| --- | --- |
| Fresh activation | component, two mechanics, and two procedures are activated and schema-valid |
| Dependency closure | mitigation -> Condition state-effects -> weapon apply graph materializes without cycles or undeclared reads |
| State semantics | absent, known-empty, canonical known, corrupt, record, and correct cases pass |
| Behavior | all type-membership/order/Petrified/no-op cases pass |
| Replay/no-change | identical writer/action operations do not double-write; invalid/corrupt state does not change HP/profile |
| Source profiles | core-only still excludes optional legacy equipment; explicit extension profile remains valid |
| Licensing | exact SRD attribution/locators present; Foundry reference-only boundary preserved |
| Regression | all D&D JavaScript, D&D tests, catalog, build, shared tests, and local-AI tests pass |

## Verification commands

- all `catalog/applications/dnd2024/mechanics/**/*.js` through `node --check`;
- focused tests filtered to `Damage_mitigation|Weapon_damage_mitigation|Fresh_host_combat_primitives`;
- full `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog`;
- `dotnet build DantesRoleplay.slnx --no-restore`;
- `dotnet test DantesRoleplay.slnx --no-build`;
- Slice 11-scoped `git diff --check` and status/read-back audit.

No protocol walk is required because no MCP surface or dependency registration changed.

## Completion receipt and exit gate

Record the consolidated evidence in
`adoption/evidence/DND-CODE-ADOPTION-SLICE-11D-RECEIPT.md`, mark the damage-mitigation family accepted,
and stop. A different complex family requires a new bounded rule/dependency leaf.
