# Component convergence HI1 implementation - Heroic Inspiration marker

Status: **accepted**
Feature/slice: **DND2024 component convergence / HI1**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [component convergence dependency tree](../../prototype/dnd2024/planning/DND2024-COMPONENT-CONVERGENCE-DEPENDENCY-TREE.md), leaf 3
Ruleset alignment: `dnd2024-compatible`
Source ID and locator: existing rule provenance remains `source.dnd2024.srd-5.2.1`,
`Rules Glossary > Heroic Inspiration` (PDF p. 183); this slice changes no rule meaning
Outcome: replace the canonical `dnd2024.heroic-inspiration` component key with the prototype ECS
owner `dnd2024.character.heroic-inspiration` while preserving the existing closed marker payload and
grant behavior.
Exclusions: Heroic Inspiration consumption, die rerolls/result replacement, overflow transfer,
Human Resourceful, rest integration, profile convergence, database writes, aliases, and every other
component migration.
Allowed files/areas: this document; the Heroic Inspiration catalog component descriptor/schema,
grant mechanic/procedure; direct Heroic Inspiration test references; the convergence tree; and the
completion receipt.
Stop point: one canonical target key passes focused behavior, fresh catalog validation, D&D
regression, and full-suite acceptance with no old-key runtime reference.

## Confirmed decisions

- The user's direction to continue after the component crosswalk and the explicit proposal to begin
  this first bounded migration confirms `dnd2024.character.heroic-inspiration` as the permanent
  canonical key for this slice.
- The existing and prototype payloads are equivalent closed empty objects. Presence remains exactly
  one held instance and absence remains none; no schema meaning changes.
- The old key is retired atomically in catalog files. There is no read alias, dual write, or
  compatibility component.
- A read-only audit of `data/dantesroleplay.db` found no definition, component instance, mechanic
  source, or procedure contract using either key. No live-state migration or database write is
  therefore required.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Held state | a player character can hold only one Heroic Inspiration | accepted CC2D empty presence component | rename the component only; preserve presence/absence semantics |
| Normal grant | a valid absent marker receives one canonical marker | `mechanic.dnd2024.heroic-inspiration.grant` | retain algorithm and bind its projection/effect to the target key |
| Use and overflow | require later die-result and recipient context | no accepted owner in this slice | remain excluded |

This compatible migration does not reinterpret the SRD, import 2014 behavior, accept derived caller
authority, or add D&D logic to C#.

## External implementation reference

No new Foundry dnd5e review is relevant to a repository-internal component-key convergence. The
accepted CC2D owner already records its review of Foundry commit
`275bed0be4ccfa15e6b3347acccb8da8784726d9` for single-instance state. This slice adopts no Foundry
code, data, schema, behavior, or dependency.

## Prerequisite evidence

- [CC2D receipt](evidence/DND2024-CHARACTER-CREATION-CC2D-RECEIPT.md) proves the current marker and
  guarded grant behavior.
- The prototype
  `schemas/components/character/heroic-inspiration.schema.json` declares the confirmed target key
  with the same closed empty-object state.
- The component crosswalk covers all 40 canonical owners and identifies this marker as the only
  payload-equivalent ID migration.
- The read-only live database audit on 2026-08-28 returned zero old-key and zero target-key rows in
  component definitions, component instances, mechanic sources, and procedures.

## Runtime artifacts

| Artifact | Change |
| --- | --- |
| `dnd2024.character.heroic-inspiration` | canonical descriptor/schema replaces the retired old-key files |
| `mechanic.dnd2024.heroic-inspiration.grant` | existing mechanic ID retained; projection and effect use the target component |
| `procedure.mechanic.dnd2024.heroic-inspiration` | existing procedure ID retained; governance names the target component |
| D&D test harness | registers and asserts the target component only |

No public operation, C# runtime contract, migration, fixture alias, or database artifact is added.

## Authoritative state and closed input

The `subject` projection supplies `dnd2024.character.profile` and the optional target marker.
Input remains exactly `{}`. The caller cannot supply current availability, component ID, source,
recipient, rest, species, die, roll, or result.

## Behavior, result, and typed effects

The existing validation and result remain unchanged. A valid profiled subject without the target
marker receives one `component.add` for `dnd2024.character.heroic-inspiration` with canonical `{}`.
A valid present target marker is a duplicate failure; malformed target state is an invalid-state
failure. The generic action transaction remains the effect owner. Events and notifications remain
empty, and the deterministic result retains its existing SRD provenance.

## Failure, replay, and rollback contract

- Missing/extra/nonobject input, invalid profile, malformed marker, or duplicate grant throws before
  effects and changes no state.
- Same-operation replay returns the recorded result without a second add.
- A distinct duplicate operation fails without changing value or revision.
- The old component key is absent from active catalog/runtime references and cannot be newly
  written through this mechanic.
- Generic transaction rollback continues to own injected apply failure. This slice introduces no
  separate migration transaction because the audited live database has no affected state.

## Implementation sequence

1. Replace the component descriptor/schema filenames and ID with the target key.
2. Rebind the existing grant mechanic and procedure without changing rule behavior.
3. Update only direct test registrations/assertions for the component key.
4. Prove old-key absence, focused behavior, fresh catalog import, D&D regression, and full-suite
   compatibility.
5. Record a completion receipt and collapse leaf 3 to verified.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Target registration | fresh catalog import registers only `dnd2024.character.heroic-inspiration` |
| First grant | valid profiled character gains canonical `{}` through one add effect |
| Closed input/profile gates | existing negative cases remain no-change failures |
| Present/corrupt state | target-key duplicate and malformed-state cases retain behavior |
| Replay/rollback | replay produces no second add; existing transaction behavior remains green |
| Old-key retirement | no active catalog, mechanic, procedure, or current test reference remains |
| Compatibility | complete D&D test class and full solution remain green |

## Verification commands

- `rg -n --glob '!old-dnd/**' --glob '!ruleset/dnd2024/evidence/**' --glob '!ruleset/dnd2024/DND2024-CHARACTER-CREATION-CC2D-IMPLEMENTATION.md' "dnd2024\\.heroic-inspiration" catalog DantesRoleplay.Tests prototype ruleset/dnd2024`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests.Character_creation_heroic_inspiration`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`
- `git diff --check`

No protocol walk is required because no MCP surface or dependency registration changes.

## Completion receipt and exit gate

Accepted by the
[Heroic Inspiration convergence receipt](evidence/DND2024-COMPONENT-CONVERGENCE-HEROIC-INSPIRATION-RECEIPT.md).
The component convergence leaf is verified. Later component cohorts require separate active slices.
