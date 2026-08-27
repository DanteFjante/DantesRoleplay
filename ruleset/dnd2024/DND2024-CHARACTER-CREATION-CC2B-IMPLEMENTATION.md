# Character creation CC2B implementation - Human Skillful contribution

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2B within CC2
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Character Origins > Character Species > Human
> Skillful* (PDF p. 86)
Outcome: Resolve one skill choice for a bound species profile that declares the `skillful` trait
into a canonical proficiency contribution for the eventual atomic creation root.
Exclusions: direct actor writes, merging the final skill set, Human Resourceful/Versatile, other
species traits, feat selection, Long Rest, Inspiration consumption, public-surface changes, and
actor creation.
Allowed files/areas: this document/tree/roadmap; D&D application species mechanic/procedure;
focused D&D tests; completion receipt.
Stop point: accepted deterministic zero-effect Skillful contribution that reuses the existing
skill-proficiency owner and leaves the remaining Human entitlements blocked.

## Confirmed decisions

- Add `mechanic.dnd2024.species-skillful.resolve` and
  `procedure.mechanic.dnd2024.species-skillful` as the named trait owner. The user's request to
  implement character creation and direction to continue confirm these permanent IDs within this
  bounded SRD-faithful slice.
- Do not create a second skill-state component. The result identifies
  `dnd2024.skill-proficiencies` as its target owner and contributes one skill value; the later
  atomic creation root must union this with class/background grants before one component add.
- Gate behavior by the bound profile's declarative `skillful` trait key, never by a Human ID branch.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Skillful | gain proficiency in one skill of choice | `dnd2024.skill-proficiencies` owns the final complete set | accept exactly one of the existing 18 skill IDs and return a contribution, not a write |
| Trait entitlement | Human's profile declares `skillful` | `dnd2024.species-profile` | resolver fails if the bound profile does not declare the trait |
| Duplicate proficiency | the source grants proficiency, not an extra multiplier | later atomic merge root | contribution is a set-union candidate; duplicate policy is resolved once with all creation grants |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/data/advancement/trait-data.mjs` and
`module/applications/advancement/trait-flow.mjs`. CC2B adopts only the split between a configured
choice pool and a separately recorded chosen value. It does not copy Foundry code/data or perform a
direct actor update.

## Prerequisite evidence

- [CC2A receipt](evidence/DND2024-CHARACTER-CREATION-CC2A-RECEIPT.md) proves the Human profile
  declares `skillful` and exposes it as unresolved.
- `dnd2024.skill-proficiencies` is the active closed owner for the 18 SRD skill IDs and its recorder
  already canonicalizes a complete set.

## Runtime artifacts

The new mechanic requires one `species` role with `dnd2024.character.content-definition` and
`dnd2024.species-profile`. Its input is exactly `{ "skill": "perception" }`. Output identifies the
species, source trait, selected skill, target component ID, and `set-union` contribution policy.
Effects, events, and notifications are empty.

## Authoritative state and closed input

Role binding supplies species identity/profile/entitlement. The caller supplies only one skill ID.
It may not supply a species ID, target component, merge policy, source, current skills, final skill
set, proficiency bonus, ability, modifier, or effect.

## Behavior, result, and typed effects

Validate the same canonical active species identity/profile contract as CC2A, require exactly one
`skillful` declaration, validate the selected value against the existing 18-skill vocabulary, and
return one canonical contribution for `dnd2024.skill-proficiencies.skills`. Do not inspect or mutate
an actor. Seed never affects output.

## Failure, replay, and rollback contract

Missing/extra/unknown/non-string input, malformed/inactive/noncanonical content, source mismatch,
or absent/duplicate `skillful` entitlement throws with no effects. The same roles/input produce
byte-identical data under every seed; ActionRunner replay applies zero effects.

## Implementation sequence

1. Add the pure trait resolver and governing procedure.
2. Add entitlement, positive, negative, source-drift, deterministic, and replay tests.
3. Run focused tests, catalog validation, D&D regression, full acceptance, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Human Skillful | all 18 exact skill IDs can resolve one at a time |
| Contribution | output targets `dnd2024.skill-proficiencies.skills` with `set-union`; no effect |
| Entitlement | Human succeeds; a species lacking `skillful` fails |
| Invalid input | unknown, missing, extra, or derived fields reject |
| Drift/canonicality | malformed/mismatched/noncanonical source content rejects |
| Determinism/replay | seed-independent bytes and zero-effect replay |
| Compatibility | CC1, CC2A, all D&D regressions, and catalog activation remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests.Character_creation_species_skillful`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`

No protocol walk is required because CC2B adds no MCP surface or dependency registration.

## Completion receipt and exit gate

Accepted by [the CC2B completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2B-RECEIPT.md).
CC2B is collapsed to verified in the dependency tree. Work stopped before Resourceful, Versatile,
final skill merging, or actor creation.
