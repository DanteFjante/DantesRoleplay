# Character creation CC2C implementation - Human Versatile with Skilled

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2C within CC2
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Character Origins > Character Species > Human
> Versatile* (PDF p. 86) and *Feats > Origin Feats > Skilled* (PDF p. 87)
Outcome: Activate the four immutable SRD Origin-feat identities and resolve the Human Versatile
trait's recommended Skilled path into one feat selection plus exactly three canonical skill/tool
proficiency contributions.
Exclusions: actor writes, a generic selected-feat state component, Alert/Magic Initiate/Savage
Attacker behavior, duplicate resolution across other creation sources, Resourceful, Long Rest,
public-surface changes, and actor creation.
Allowed files/areas: this document/tree/roadmap; D&D application feat component/content/procedure;
Versatile/Skilled mechanic and procedure; focused D&D tests; completion receipt.
Stop point: accepted zero-effect Versatile/Skilled plan with other Origin-feat behavior and Human
Resourceful still explicit blockers.

## Confirmed decisions

- Re-adopt `dnd2024.feat-profile` and `procedure.mechanic.dnd2024.feat-profile`, expanding the
  archived two-profile identity catalog to all four Origin feats present in SRD 5.2.1: Alert,
  Magic Initiate, Savage Attacker, and Skilled.
- Add their permanent `content.dnd2024.feature.<key>.v1` identities. The user's request to implement
  character creation, preference for D&D 2024 fidelity, and direction to continue confirm these
  SRD-core IDs and the new `mechanic.dnd2024.species-versatile-skilled.resolve` /
  `procedure.mechanic.dnd2024.species-versatile-skilled` IDs inside this bounded slice.
- Do not create parallel skill/tool state or claim that static feat identity implements a benefit.
- Bind the species and feat as roles. Input carries only three skill/tool choices, never content
  IDs, target components, source records, or final merged proficiency state.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Versatile | Human gains one Origin feat of choice; Skilled is recommended | Human species profile plus immutable feat profile | require the species to declare `versatile` and the bound feat to be active category `origin` |
| Skilled | gain proficiency in any combination of three skills or tools | `dnd2024.skill-proficiencies` and `dnd2024.tool-proficiencies` | accept exactly three unique choices and return canonical set-union contributions |
| Repeatability | Skilled can be taken more than once | immutable Skilled feat profile | record `repeatable: true`; this creation slice still selects it once |
| Other Origin feats | Alert, Magic Initiate, and Savage Attacker are SRD Origin feats | immutable profiles only | activate identity without claiming benefit readiness |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/documents/advancement/item-grant.mjs`,
`module/applications/advancement/item-grant-flow.mjs`,
`module/data/advancement/trait-data.mjs`, and
`module/applications/advancement/trait-flow.mjs`. CC2C adopts only the engineering separation among
bound feat content, configured choice pools, selected values, and staged application. It does not
adopt optional skipping, direct actor-item mutation, code, data, assets, or runtime dependencies.

## Prerequisite evidence

- [CC2B receipt](evidence/DND2024-CHARACTER-CREATION-CC2B-RECEIPT.md) proves a species trait can
  contribute to the existing skill owner without writing state.
- Human's active immutable profile declares `versatile`.
- Existing skill and tool component owners define the complete 18-skill and 37-tool vocabularies
  and canonical complete-set recorders.
- The retained Feature 28 Slice 4 receipt proves provenance for the archived feat-profile shape and
  Alert/Savage Attacker identities, without authorizing their behavior.

## Runtime artifacts

| Artifact | Boundary |
| --- | --- |
| `dnd2024.feat-profile` | immutable feature identity, Origin category, and source repeatability only |
| four Origin-feat entities | active source-cited definitions; no executable benefit payload |
| Versatile/Skilled resolver | pure role-bound validation and canonical proficiency contributions |
| procedures | static feat authoring plus the named Versatile/Skilled behavior boundary |

## Authoritative state and closed input

Roles are exactly `species` and `feat`, each requiring content identity plus its family profile.
The input is exactly:

```json
{
  "choices": [
    { "kind": "skill", "id": "perception" },
    { "kind": "skill", "id": "stealth" },
    { "kind": "tool", "id": "thieves-tools" }
  ]
}
```

Caller order is not authoritative. The resolver canonicalizes skill and tool contributions using
their existing owner vocabularies.

## Behavior, result, and typed effects

Validate canonical active species and Skilled feat definitions with matching immutable profiles,
require exactly one `versatile` entitlement, require Skilled's Origin/repeatable source facts, and
accept exactly three unique valid choice pairs. Return the selected feat definition plus separate
canonical `set-union` contributions for skill and tool component fields. Empty contribution lists
are valid when all three choices use the other family. Effects, events, and notifications are empty.

## Failure, replay, and rollback contract

Missing/extra/malformed input, other feat behavior, non-Origin/inactive/mismatched content,
noncanonical IDs, absent/duplicate Versatile, fewer/more than three choices, duplicate pairs,
unknown kinds, or unknown vocabulary members throws before output. Evaluation changes no state.
Choice order and seed do not affect output bytes; replay applies zero effects.

## Implementation sequence

1. Add the bounded feat-profile schema/metadata and static authoring procedure.
2. Add the four SRD Origin-feat entities.
3. Add the pure Versatile/Skilled resolver and procedure.
4. Register the feat profile only in the disposable D&D harness and add focused tests.
5. Run focused tests, catalog validation, D&D regression, full acceptance, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Static inventory | exactly four active v1 Origin profiles match source/category/repeatability |
| Mixed Skilled | two skills plus one tool resolve into canonical separate contributions |
| Boundary mixes | all-skill and all-tool selections succeed |
| Canonicality | choice order and seed yield byte-identical output |
| Entitlement/behavior | Human + Skilled succeeds; non-Versatile species or another feat fails |
| Invalid choice | missing/extra/duplicate/unknown kind/member rejects without effects |
| Source drift | inactive, mismatched, malformed, or noncanonical content rejects |
| Compatibility | CC1–CC2B, D&D regressions, core/extension activation, and full suite remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests.Character_creation_species_versatile`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`

No protocol walk is required because CC2C adds no MCP surface or dependency registration.

## Completion receipt and exit gate

Accepted by [the CC2C completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2C-RECEIPT.md).
CC2C is collapsed to verified in the dependency tree. Work stopped before Resourceful, other feat
behavior, cross-source duplicate resolution, or actor creation.
