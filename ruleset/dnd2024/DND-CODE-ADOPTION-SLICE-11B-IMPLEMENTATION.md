# D&D code-adoption Slice 11B implementation — mitigation state and defender profile

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), complex-behavior lane  
Dependency tree/leaf: [Slice 11 design](DND-CODE-ADOPTION-SLICE-11-DESIGN.md), damage-mitigation 11B  
Ruleset alignment: `dnd2024-owned`  
Source ID and locators: `source.dnd2024.srd-5.2.1`, `Playing the Game > Damage and Healing >
Resistance and Vulnerability > No Stacking/Order of Application` and `Immunity` (PDF p. 17), plus
`Rules Glossary > Petrified > Resist Damage` (PDF p. 186)  
Outcome: recover one canonical mitigation component, its closed administrative writer, and an
effect-free resolver that composes the existing Condition state-effects owner.  
Exclusions: damage amount/type input, mitigation arithmetic, HP changes, weapon-damage changes,
temporary HP, healing, damage events, 0-HP consequences, death saves, concentration, source-grant
tracking, migrations, public operations, and production C#.  
Allowed files/areas: this document; one component definition/schema under
`catalog/applications/dnd2024/components/combat/`; two mechanic contracts/scripts and two procedures
under the D&D application combat owner; focused `Dnd2024AbilityCheckTests` harness/tests; adoption
notice, roadmap/dependency status, and the 11B receipt.  
Stop point: activated storage/writer/profile contracts with focused and regression evidence; the
existing weapon-damage application remains unchanged.

## Confirmed decisions

[Slice 11A](DND-CODE-ADOPTION-SLICE-11A-IMPLEMENTATION.md) accepted reuse of the archived permanent
IDs and fixed the rule, source, ownership, dependency, and later transaction boundary. The schema is
new to the current activated application but retains the archived meaning, with the broad locator
replaced by the exact accepted SRD locator. Existing campaign bindings are not upgraded.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Base memberships | creatures/objects can have Resistance, Immunity, or Vulnerability to damage types | recovered `dnd2024.damage-mitigation` | three required unique canonical lists; no damage instance or arithmetic stored |
| No stacking | multiple instances for the same type count once | schema plus writer | each list is duplicate-free and canonicalized |
| Petrified | grants Resistance to all damage | `mechanic.dnd2024.d20-test.state-effects` | resolver consumes one declared child result and reports `petrified`; no duplicate Condition parser/state |
| Missing state | repository authority distinction | application ECS presence | missing mitigation is unknown with empty reported lists; present empty lists are known-empty |
| Later arithmetic | Immunity prevents damage; Resistance rounds down; Vulnerability follows Resistance | future 11C consumer | procedure records the contract but this leaf receives no amount/type and emits no HP effect |

## External implementation reference

The exact pinned Foundry review is recorded by 11A. It supports separate mitigation state and a
calculation-before-mutation seam. 11B does not copy Foundry code or adopt its actor fields, `ALL`
sentinel, bypasses, hooks, UI, caller overrides, or runtime.

## Prerequisite evidence

- [11A receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-11A-RECEIPT.md).
- Current `dnd2024.conditions` and `mechanic.dnd2024.d20-test.state-effects` distinguish absent from
  known Condition state and return deterministic effective Conditions without effects.
- Current mechanic requirements support declared child composition and role rebinding.
- Current application ECS supports versioned application schemas and typed `component.add`/`set`.
- Archived Feature 15 Slice 2/3 receipts, component, mechanics, procedures, and tests are recovery
  evidence; their current adaptations are revalidated here.

## Runtime artifacts

| Artifact | Disposition |
| --- | --- |
| `dnd2024.damage-mitigation` definition/schema | recover/adapt; exact locator, current application placement |
| `mechanic.dnd2024.damage-mitigation.write` | recover/adapt; exact source, closed result envelope, no arithmetic |
| `procedure.mechanic.dnd2024.damage-mitigation` | recover/adapt; current owner and exact boundary |
| `mechanic.dnd2024.damage.resolve` | adapt; replace duplicate raw Condition validation with one composed state-effects child |
| `procedure.mechanic.dnd2024.damage.resolve` | adapt; document the dependency-aware profile and future consumer contract |

No event type, projection mapping, result/effect kind, source overlay, extension, migration, public
kind, application registration, or production host seam is added.

## Authoritative state and closed input

The component is a closed object with required `resistances`, `immunities`, `vulnerabilities`, and
fixed `sourceRef`. Each list uses the thirteen accepted SRD damage-type IDs in canonical order.
Types may occur across different lists so independent known memberships are not erased.

The writer accepts exactly:

```json
{"mode":"record|correct","resistances":[],"immunities":[],"vulnerabilities":[]}
```

`record` requires absence; `correct` requires a complete valid existing component. The writer fixes
source provenance and canonicalizes input order. The resolver accepts exactly `{}`, binds a
`defender`, reads optional mitigation state, and composes exactly one
`mechanic.dnd2024.d20-test.state-effects` child with `subject -> defender`.

Callers may not supply source references, known flags, Conditions, Petrified, damage amount/type,
arithmetic results, HP, effects, events, or notifications.

## Behavior, result, and typed effects

The writer returns its mode, canonical lists, prior state, and fixed provenance, then proposes
exactly one `component.add` or `component.set`. It consumes no randomness and emits no event or
notification.

The resolver validates one declared child result for the same defender and reports:

- `test: damage-mitigation-profile`, defender ID, mitigation/Condition known flags;
- canonical immunity, resistance, and vulnerability lists;
- whether Petrified is effective; and
- exact mitigation and Petrified source references.

It is deterministic and emits no effects, events, or notifications. It does not calculate damage.

## Failure, replay, and rollback contract

Missing roles/child output, extra input fields, malformed present component JSON, wrong keys/types,
duplicates, noncanonical stored order, wrong provenance, invalid Condition child identity, or child
subject mismatch fails before effects. A failed writer leaves state unchanged. Successful writes use
the existing generic action transaction and operation key; identical replay cannot apply a second
effect. No injected multi-effect rollback test is needed because the writer proposes one effect and
adds no host seam; full transaction rollback remains owned by the accepted generic kernel.

## Implementation sequence

1. Add the component definition/schema and procedure contract.
2. Add the closed writer and focused record/correct/no-change tests.
3. Add the resolver with declared Condition child composition and focused unknown/known/Petrified
   tests.
4. Validate activation, schemas, JavaScript syntax, catalog, focused D&D tests, and regression.
5. Record the receipt and stop before weapon-damage integration.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Activation/schema | all five records are activated; component schema compiles |
| Record | absent state yields one add and canonical exact stored state |
| Correct | valid present state yields one set and prior state in result |
| Writer negatives | wrong mode/shape/types/duplicates, record-present, correct-absent, corrupt state all fail unchanged |
| Resolver unknown | missing mitigation and Conditions report both unknown with empty lists and no effects |
| Resolver known-empty | present empty mitigation reports known and remains distinct from absence |
| Resolver profile | canonical lists are returned unchanged |
| Petrified dependency | existing Condition writer/state-effects child makes resolver report Petrified without duplicate state |
| Child boundary | wrong/missing/mismatched child fails without effects |
| Replay | successful identical operation replays without a second revision |
| Compatibility | existing combat mechanics and existing source-profile behavior remain unchanged |
| Regression | JavaScript, focused D&D suite, catalog validation, build, and full suite pass |

## Verification commands

- `node --check` for both new JavaScript mechanics;
- focused tests filtered to `Damage_mitigation`;
- full `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog`;
- `dotnet build DantesRoleplay.slnx --no-restore`;
- `dotnet test DantesRoleplay.slnx --no-build`;
- `git diff --check -- catalog/applications/dnd2024 ruleset/dnd2024 DantesRoleplay.Tests/Dnd2024AbilityCheckTests.cs`.

No MCP protocol walk is required because no MCP surface or dependency registration changes.

## Completion receipt and exit gate

Record results in `adoption/evidence/DND-CODE-ADOPTION-SLICE-11B-RECEIPT.md`, mark 11B accepted, and
stop. Weapon-damage behavior remains blocked until a separate active 11C implementation document.
