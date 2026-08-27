# Character creation CC2D implementation - Heroic Inspiration grant foundation

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2D within CC2
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Heroic Inspiration*
(PDF p. 183) and *Character Origins > Character Species > Human > Resourceful* (PDF p. 86)
Outcome: Re-adopt one canonical held-Heroic-Inspiration state and a guarded normal grant action so
later authenticated rule sources, including Human Resourceful, have one shared target owner.
Exclusions: Long Rest lifecycle or recovery, Resourceful triggering, overflow transfer, Heroic
Inspiration consumption/rerolling, dice-result replacement, actor creation, and public surfaces.
Allowed files/areas: this document/tree/roadmap; D&D Heroic Inspiration component, grant mechanic,
and procedure; disposable D&D harness and focused tests; completion receipt.
Stop point: accepted presence/grant foundation with Resourceful still blocked on authenticated
completed-Long-Rest evidence.

## Confirmed decisions

- Re-adopt the permanent `dnd2024.heroic-inspiration` component,
  `mechanic.dnd2024.heroic-inspiration.grant`, and
  `procedure.mechanic.dnd2024.heroic-inspiration` IDs from retained recovery evidence, adapted to
  the current catalog validator and current nonempty character-profile contract.
- The user's continuing character-creation request, D&D 2024 fidelity direction, and instruction
  to continue confirm these SRD-core IDs inside this bounded leaf.
- Presence means exactly one held instance; absence means none. No boolean, counter, source, die,
  result, rest, or history field is added.
- Duplicate normal grant fails. The source rule's option to give newly gained Inspiration to
  another player character is a later, recipient-authorized transition and is not silently chosen.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Held state | A player character can have only one Heroic Inspiration at a time | new empty presence component recovered from archive | component presence is the single availability fact |
| Use | expend it immediately after rolling any die to reroll and use the new roll | no accepted die-attempt/replacement composition owner | consumption is excluded rather than detached from its die |
| Overflow | a new instance is lost unless given to a player character who lacks one | no accepted recipient/authorization transition | normal duplicate grant fails with no state change |
| Resourceful | Human gains Heroic Inspiration whenever it finishes a Long Rest | Human profile exists; rest completion owner is absent | this slice supplies only the target grant foundation |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/data/actor/character.mjs`, `module/applications/actor/character-sheet.mjs`, and
`module/documents/actor/actor.mjs`. Useful reference behavior is a single boolean availability fact
and a distinct completed-rest phase emitted only after bulk rest updates. CC2D adopts the
single-instance separation only; it does not adopt direct UI toggling, actor-field mutation, code,
data, assets, or a runtime dependency.

## Prerequisite evidence

- [CC2C receipt](evidence/DND2024-CHARACTER-CREATION-CC2C-RECEIPT.md) proves the active Human
  identity and its Skillful/Versatile behavior boundaries while retaining Resourceful as pending.
- `dnd2024.character.profile` is an accepted optional descriptive component on an existing actor;
  in this bounded action, a valid nonempty profile is the existing player-character eligibility
  marker recovered from the prior accepted design.
- Generic component add, action transaction, operation replay, projection, and catalog activation
  owners are already accepted.

## Runtime artifacts

| Artifact | Boundary |
| --- | --- |
| `dnd2024.heroic-inspiration` | closed empty presence state for exactly one held instance |
| Heroic Inspiration grant mechanic | validates existing character eligibility and absent held state, then adds `{}` |
| Heroic Inspiration procedure | governs presence and normal grant; forbids source, use, transfer, correction, and history |

## Authoritative state and closed input

The only role is `subject`; its projection declares both `dnd2024.character.profile` and
`dnd2024.heroic-inspiration`. Input is exactly `{}`. Character eligibility and current held state
come only from the projected subject. Callers cannot provide species, rest, grant source, current
availability, target component, recipient, die, or result.

## Behavior, result, and typed effects

Require an existing subject with a valid current nonempty profile. If Heroic Inspiration is absent,
return exactly one `component.add` containing canonical `{}` plus fixed SRD provenance in result
data. Effects are committed by the generic action runner; events and notifications are empty. Seed
does not affect output. A valid present component is a duplicate failure rather than an overwrite.

## Failure, replay, and rollback contract

Missing/extra/nonobject input, missing/malformed/invalid profile, malformed Inspiration state, or a
duplicate grant throws before effects. A replay of the same operation returns the recorded success
without a second add; a distinct duplicate operation fails and preserves revision/value. Failed
validation changes no state. Generic transaction rollback owns injected apply failure.

## Implementation sequence

1. Add the bounded component metadata/schema and governing procedure.
2. Add the guarded JavaScript grant and catalog record.
3. Register the component only in the disposable D&D harness and add focused tests.
4. Run focused tests, D&D regression, catalog validation, full acceptance, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| First grant | valid profiled character gains canonical `{}` with one effect |
| Replay/duplicate | same operation replays; distinct second grant fails without revision change |
| Closed input | null, array, primitive, or extra keys fail without state |
| Character gate | absent, empty, malformed, unknown-field, or untrimmed profile fails |
| Corrupt held state | malformed or nonempty Inspiration state fails without overwrite |
| Dependency boundary | no species, rest, feat, campaign, recipient, or die role/input is accepted |
| Compatibility | CC1-CC2C, D&D regressions, activation, catalog validation, and full suite remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests.Character_creation_heroic_inspiration`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`

No protocol walk is required because CC2D adds no MCP surface or dependency registration.

## Completion receipt and exit gate

Accepted by [the CC2D completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2D-RECEIPT.md).
CC2D is collapsed to verified in the dependency tree. Work stopped before Long Rest, Resourceful,
overflow transfer, consumption/rerolling, or actor creation.
