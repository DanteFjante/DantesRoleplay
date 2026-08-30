# D&D 2024 weapon activity harness convergence

Feature/slice: **DND2024 weapon activity repair / acceptance-harness convergence**  
Status: **accepted**  
Owner/roadmap: [D&D 2024 mechanic contract-owner repair](DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md)  
Dependency tree/leaf: canonical weapon activities, parent-acceptance follow-up  
Ruleset alignment: **dnd2024-compatible**  
Source ID and locator: `dnd2024.source.srd-5.2.1`; no new rule interpretation in this slice  
Outcome: remove the retired `dnd2024.weapon-profile` component from the broad test harness, repair
the stale mechanic-reference regex left by the accepted namespace cutover, and exercise retained
combat tests through the accepted weapon/activity owners.  
Exclusions: no new catalog ID, component, mechanic behavior, content migration, or public surface;
no restoration of the retired weapon-profile component.  
Allowed files/areas: this document, `DantesRoleplay.Tests/Dnd2024AbilityCheckTests.cs`, the closed
`mechanicId` regex in current D&D component schemas, focused test commands, and a completion receipt
under `ruleset/dnd2024/adoption/evidence/`.  
Stop point: the broad D&D harness constructs from current component owners, superseded legacy
assertions are removed or converted, focused acceptance passes, and the next independent full-suite
failures are reported without expanding this slice.

## Confirmed decisions

The user authorized creating or correcting the required artifact without a separate approval on
2026-08-30. Existing implementation evidence is more specific: the weapon-profile component was
deliberately retired, and the accepted normalized owners must remain authoritative. No permanent ID
or runtime behavior changes in this slice. The authorization also covers the mechanical schema
correction from the retired `mechanic.*` identity form to the already-confirmed canonical
`dnd2024.mechanic.*` form.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Weapon identity | Weapons have category and properties | `dnd2024.item.weapon` | Test fixtures use the normalized weapon facet. |
| Attack mode | A weapon can expose one or more attack uses | `dnd2024.activity.membership` plus activity facets | Tests bind an explicit member activity. |
| Attack and damage | Ability eligibility, damage, and range belong to the selected use | `dnd2024.activity.attack`, `.damage`, and `.range` | No monolithic profile is reconstructed. |

## External implementation reference

No new D&D behavior is implemented. The accepted canonical-weapon-activities slice already records
the relevant rules and external implementation review; this corrective slice only aligns tests with
that delivered contract.

## Prerequisite evidence

- [Canonical weapon activities implementation](DND2024-MECHANIC-REPAIR-WEAPON-ACTIVITIES-IMPLEMENTATION.md)
  records all 38 weapon definitions, 51 attack activities, and the explicit ban on recreating
  `dnd2024.weapon-profile`.
- `Dnd2024WeaponActivityRepairTests` is the focused acceptance owner for normalized weapon data and
  mechanics.
- The broad test run currently stops while `DndHarness.CreateAsync` attempts to load the retired
  component file.

## Runtime artifacts

No new artifacts. The harness registers the existing normalized component definitions and loads
representative weapon/activity fixtures using existing IDs. Existing calculation-reference schemas
accept the canonical mechanic namespace and reject the retired inverted form.

## Authoritative state and closed input

The canonical component schemas and authored weapon/activity entities remain authoritative. Tests
may provide only the existing mechanic inputs and explicit bound roles, including `activity` where
the current contract requires it.

## Behavior, result, and typed effects

Retained broad combat tests run against one normalized dagger weapon and its member melee activity.
Writer coverage expects the current multi-facet effect set. Dedicated catalog-wide weapon coverage
remains in `Dnd2024WeaponActivityRepairTests`; superseded profile/link assertions are not duplicated.

## Failure, replay, and rollback contract

Missing, malformed, nonmember, and wrong-ability activity state continues to fail closed under the
existing mechanics. This slice changes no transaction or replay behavior. Harness setup must fail if
a current required component definition is absent, but must not require a retired definition.

## Implementation sequence

1. Converge the shared component-schema mechanic-reference regex on `dnd2024.mechanic.*`.
2. Register normalized weapon/activity component definitions in the broad harness.
3. Replace the legacy combat fixture and role bindings with explicit weapon/activity state.
4. Remove superseded catalog-shape assertions already owned by focused current-schema tests.
5. Run focused harness, weapon-activity, catalog, and broad acceptance checks.

## Acceptance matrix

- Positive: harness creation and representative attack/damage paths use a member activity.
- Negative: existing malformed and nonmember paths remain unchanged and fail closed.
- Compatibility: all unrelated broad tests continue to use the same disposable state and actions.
- Surface/fresh import: no public or persisted runtime surface changes.

## Verification commands

- `dotnet test DantesRoleplay.Tests --filter FullyQualifiedName~Dnd2024WeaponActivityRepairTests`
- `dotnet test DantesRoleplay.Tests --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet test DantesRoleplay.Tests`
- `dotnet run --project DantesRoleplay.Cli -- validate catalog`
- `git diff --check`

## Completion receipt and exit gate

Record the delivered boundary and exact command results in
`ruleset/dnd2024/adoption/evidence/DND2024-WEAPON-ACTIVITY-HARNESS-CONVERGENCE-RECEIPT.md`, mark this
document accepted, and stop before changing any independent failing owner.
