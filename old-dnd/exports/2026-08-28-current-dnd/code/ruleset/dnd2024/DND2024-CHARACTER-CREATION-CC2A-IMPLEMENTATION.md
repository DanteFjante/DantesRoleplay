# Character creation CC2A implementation - species definitions and selection planning

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2A within CC2
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Character Origins > Character Species >
Parts of a Species / Species Descriptions* (PDF pp. 83–86)
Outcome: Activate the nine immutable SRD species profiles and resolve one bound species plus any
required Size choice into canonical selected-species, Size, and base-Speed data without writing
state or representing unimplemented special traits as granted.
Exclusions: actor creation, effects, trait behavior, ancestry/lineage choices, creature-type state,
skills, feats, spells, rest hooks, public-surface changes, migrations, and optional content.
Allowed files/areas: this document/tree/roadmap; D&D application species components, content,
mechanic, and procedures; focused D&D tests; completion receipt.
Stop point: accepted deterministic zero-effect species-selection plan with every undeveloped trait
reported as unresolved; CC2 remains incomplete until one species' full trait grant family exists.

## Confirmed decisions

- Re-adopt the archived permanent IDs `dnd2024.species-profile`, `dnd2024.selected-species`,
  `mechanic.dnd2024.species-selection.resolve`, and
  `procedure.mechanic.dnd2024.species-selection` instead of minting parallel owners.
- Re-adopt the nine versioned `content.dnd2024.species.<key>.v1` identities as immutable core
  declarations after checking their SRD locators and current content-definition owner.
- Replace the archived C#-backed resolver design with a catalog JavaScript planner. It consumes
  exactly the dependencies declared by its species role and produces no effects.
- The user's 2026-08-27 request to implement character creation and subsequent direction to
  continue confirms these previously archived IDs inside this bounded SRD-faithful slice. It does
  not confirm a public action, migration, optional rule, or completion of the full CC2 leaf.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Species identity | a character chooses one species and gains its listed game traits | immutable content definition plus re-adopted selected-species reference | the output references the bound definition; it never accepts a species ID in input |
| Creature Type | every SRD Character Origins species is Humanoid | immutable species profile; no actor creature-type owner exists | report `humanoid` as source data only; do not persist a new state component |
| Size | species determines Size; Human and Tiefling choose Small or Medium | `dnd2024.creature-size` remains the only actor Size owner | fixed Size is derived with `{}` input; a multi-Size profile requires exactly one allowed choice |
| Speed | species determines Speed | `dnd2024.speed` remains the only actor base-Speed owner | return its closed five-mode payload using the Speed owner's canonical source reference |
| Special traits | the character gets every listed trait | future named trait owners | return all declared keys as unresolved and set the plan blocked; never claim a trait was granted |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/data/item/race.mjs`, `module/documents/advancement/size.mjs`,
`module/applications/advancement/size-flow.mjs`, and the previously reviewed advancement manager.
CC2A adopts only the engineering separation between species content, a singleton actor reference,
configured Size options, and staged application. It does not adopt Foundry's implicit Medium
fallback, direct actor updates, code, data, assets, or runtime dependency.

## Prerequisite evidence

- [CC1 receipt](evidence/DND2024-CHARACTER-CREATION-CC1-RECEIPT.md) proves the preceding creation
  leaf is deterministic and effect-free.
- `dnd2024.character.content-definition` actively owns versioned species identity.
- `dnd2024.creature-size` and `dnd2024.speed` are active independent state owners with closed
  schemas; neither is copied into a second stored character-creation component.
- The retained Feature 26 receipts prove provenance for the archived static profiles and selected
  reference shape, but their C# resolver and old world model remain excluded.

## Runtime artifacts

| Artifact | Boundary |
| --- | --- |
| `dnd2024.species-profile` | immutable source declaration on a matching active species definition only |
| nine SRD species entities | source-cited type, allowed Size values, base Speed, trait keys, and choice-family keys |
| `dnd2024.selected-species` | minimal future actor-side reference containing only `speciesDefinitionId` |
| resolver mechanic | pure role-bound selection validation and canonical planning; zero effects/events/notifications |
| two procedures | static profile authoring and selection-planner ownership |

## Authoritative state and closed input

The only role is `species`, requiring `dnd2024.character.content-definition` and
`dnd2024.species-profile`. Role binding chooses the definition; callers may not submit an ID,
profile, Speed, creature type, trait, source reference, effect, or derived payload.

- A profile with one allowed Size accepts exactly `{}` and derives that Size.
- A profile with two allowed Sizes accepts exactly `{ "size": "small" }` or
  `{ "size": "medium" }` as applicable.

## Behavior, result, and typed effects

Validate the closed active content identity and matching immutable profile, including source,
content key/version, canonical Size and declaration order, base-Speed bounds, and unique trait and
choice-family keys. Resolve the Size, build canonical selected-species/Size/Speed data, preserve the
profile's ordered entitlements, and return every trait key as unresolved with
`grantReadiness: "blocked-unimplemented-traits"`. Effects, events, and notifications are empty.
Seed never affects the result.

## Failure, replay, and rollback contract

Missing/extra/wrong-type input, a choice on a fixed-Size species, absent choice on a multi-Size
species, disallowed Size, malformed or inactive identity, mismatched key/version/source, malformed
Speed, duplicate/noncanonical declarations, or a source-drifted profile throws before output. No
state changes on success or failure. Identical roles/input produce byte-identical data under every
seed, and ActionRunner replay applies zero effects.

## Implementation sequence

1. Re-adopt the two schemas/metadata records and the static-profile procedure.
2. Activate the nine reviewed species definitions.
3. Implement the pure JavaScript resolver and selection procedure.
4. Extend only the disposable D&D harness component registration and add focused tests.
5. Run focused tests, catalog validation, full acceptance, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Human Small/Medium | either allowed choice produces matching Size, 30-foot walk Speed, minimal selection reference, and three unresolved traits |
| Fixed Size | Dragonborn derives Medium from `{}` and rejects caller-supplied Size |
| Different base Speed | Goliath derives Medium and 35-foot walk Speed from content |
| Inventory | exactly nine active v1 profiles match SRD keys, sources, Sizes, Speeds, traits, and choice families |
| Canonicality | input property order and seed do not change output bytes |
| Invalid selection | missing/extra/unknown Size or fixed-Size choice rejects without effects |
| Source drift | malformed, inactive, mismatched, duplicated, or noncanonical content rejects |
| Trait safety | no trait is listed as granted; readiness is blocked and all declared traits are unresolved |
| Activation/compatibility | records are in core; existing D&D tests and extension selection remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`

No protocol walk is required because CC2A adds no MCP surface or dependency registration.

## Completion receipt and exit gate

Accepted by [the CC2A completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2A-RECEIPT.md).
CC2A is collapsed to verified in the dependency tree. Work stopped before trait grants or actor
creation; CC2 remains planned until named trait owners cover one complete species path.
