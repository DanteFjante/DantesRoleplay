# Character creation CC2E implementation - immutable standard rest policy

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2E within CC2
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185)
and *Rules Glossary > Short Rest* (PDF p. 187)
Outcome: Activate one immutable, source-cited standard-rest policy definition so later rest episodes
derive timing, eligibility, interruption, and consequence handoffs from catalog content.
Exclusions: actor rest state, clock access/advance, episode creation, interruption evidence,
completion, recovery effects, Resourceful, Heroic Inspiration grants, and public surfaces.
Allowed files/areas: this document/tree/roadmap; D&D rest-policy component/content/procedure;
disposable D&D harness and focused tests; completion receipt.
Stop point: accepted immutable policy with no executable rest transition.

## Confirmed decisions

- Re-adopt permanent `dnd2024.rest-policy`,
  `content.dnd2024.rest-policy.standard.v1`, and
  `procedure.mechanic.dnd2024.rest-policy` IDs from retained recovery evidence, correcting the SRD
  page locators to this repository's pinned PDF (Long Rest p. 185; Short Rest p. 187).
- The user's continuing character-creation request and D&D 2024 fidelity direction confirm these
  SRD-core IDs inside this bounded prerequisite leaf.
- Policy benefit names are non-executable handoff labels. Each later recovery effect remains owned
  by its authoritative component/mechanic and must prove its own source rule.
- Temporary Hit Points expiry is not copied into this policy's Long Rest list because it is sourced
  by the Temporary Hit Points rule, not the Long Rest glossary entry.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Short Rest | 1 hour, at least 1 HP, light activity, three stopping interruptions | new immutable policy | declare exact eligibility/timing/interruption data and two handoff labels |
| Long Rest | at least 8 hours, at least 6 sleeping, no more than 2 light activity, at least 1 HP | new immutable policy | declare exact minimums and handoff labels without applying benefits |
| Long Rest cadence | wait at least 16 hours after finishing before starting another | new immutable policy | declare 960-minute restart wait for later receipt validation |
| Interrupted Long Rest | four interruption types, Short Rest credit after at least 1 hour, +1 hour per resumed interruption | new immutable policy | declare exact interruption and timing values; no caller assertion becomes evidence |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/applications/actor/rest/long-rest-dialog.mjs` and
`module/documents/actor/actor.mjs`. Useful reference behavior is the separation of rest
configuration/result calculation from bulk actor updates and the post-update completion phase.
CC2E adopts only the declaration-versus-execution separation; it does not adopt code, data, UI,
direct actor mutation, optional settings, assets, or a runtime dependency.

## Prerequisite evidence

- [CC2D receipt](evidence/DND2024-CHARACTER-CREATION-CC2D-RECEIPT.md) supplies the shared Heroic
  Inspiration target while proving Resourceful still needs authenticated completed-rest evidence.
- The retained rest-policy files are recovery evidence only. Their useful static shape is adapted
  under the current bounded validator and corrected source locators.
- Exact current SRD text was read from the repository's pinned 364-page SRD 5.2.1 PDF.

## Runtime artifacts

| Artifact | Boundary |
| --- | --- |
| `dnd2024.rest-policy` | closed immutable standard Short/Long Rest declaration |
| `content.dnd2024.rest-policy.standard.v1` | one active v1 catalog entity carrying the exact policy |
| rest-policy procedure | authoring contract; forbids actor state and executable recovery |

## Authoritative state and closed input

There is no action input or role in CC2E. Catalog content owns every value. The policy entity ID,
policy key/version, source reference, timings, interruption vocabularies, activity limits, and
benefit handoff vocabularies are exact and immutable.

## Behavior, result, and typed effects

CC2E has no executable mechanic and emits no effects, events, notifications, or result. Activation
validates the policy through its component schema. Later mechanics must bind the entity as a role;
they may not accept an equivalent caller-supplied policy object.

## Failure, replay, and rollback contract

Missing/extra fields, changed values/order, wrong source/page, unknown interruptions/benefits,
alternate policy IDs, or malformed data fail schema validation or focused exactness tests. There is
no transaction, replay, randomness, rollback, or live-state mutation in this slice.

## Implementation sequence

1. Add the bounded rest-policy schema/metadata and authoring procedure.
2. Add the one immutable standard v1 content entity.
3. Register the component in the disposable D&D harness and add exact static tests.
4. Run focused tests, D&D regression, catalog validation, full acceptance, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Activation | component/procedure/entity are selected by `dnd2024-core` |
| Exact source | Long Rest p. 185 and Short Rest p. 187 are fixed |
| Exact policy | every timing, eligibility, interruption, and handoff list matches the source decision |
| Bounded state | extra or changed keys/values fail schema validation |
| No execution | no rest mechanic, effect, event, subscription, or actor mutation is added |
| Compatibility | CC1-CC2D, D&D regressions, catalog validation, and full suite remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests.Character_creation_rest_policy`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`

No protocol walk is required because CC2E adds no MCP surface or dependency registration.

## Completion receipt and exit gate

Accepted by [the CC2E completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2E-RECEIPT.md).
CC2E is collapsed to verified in the dependency tree. Work stopped before a rest episode, clock
reaction, recovery, Resourceful trigger, or Heroic Inspiration source grant.
