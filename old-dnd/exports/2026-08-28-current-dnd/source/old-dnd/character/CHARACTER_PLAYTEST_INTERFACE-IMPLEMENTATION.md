# Character playtest interface implementation — provisional recordable actors

Status: **blocked — implementation complete; full-suite acceptance blocked by separate failures**  
Owner/roadmap: Temporary playtest seam; it does not amend the authoritative CH0–CH6 roadmap.  
Dependency tree/leaf: `character/CHARACTER_PLAYTEST_INTERFACE_DEPENDENCY_TREE.md`, confirmed
catalog-record vocabulary leaf.  
Ruleset alignment: **dnd2024-compatible**.  
Source ID and locator: Not applicable; this slice records playtest declarations only and implements
no D&D 2024 rule.  
Outcome: Existing MCP `effects` and `campaign` commands can create, attach, and revise explicitly
provisional character records for the initial game.  
Exclusions: CH3–CH6, class membership, source grant receipts, spellcasting, class/species/background
behavior, item instances/equipment, derived state, new MCP kinds/tools, and any migration to an
official character.  
Allowed files/areas: the one new catalog component/schema; one catalog procedure; the playtest
bootstrap runbook; one focused test file. No C# production change.  
Stop point: The temporary component is queryable/validatable and the runbook is executable through
existing kinds; stop before a guided wizard, class/spell mechanics, or CH5 integration.

## Confirmed decisions

- Permanent IDs are `dnd2024.playtest-character-record` and
  `procedure.character.playtest-bootstrap`.
- The component is provisional and non-executable; existing direct effects create/revise it.
- Its lifecycle is `draft` → C15 attach → `active`, with `retired` as the terminal state.
- It has a bounded declared-entry vocabulary, not an arbitrary JSON or rules-expression blob.
- No new MCP kind/tool is added; `procedure.mcp.add-tool` directs this capability to the existing
  `effects` and `campaign` kinds.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- |
| Class, spell, trait, feat, equipment labels | No rule is implemented by this slice. | CH3/CH4/CH10; Features 23, 31–32, 39 | Entries are non-authoritative labels/details only. They create no entitlement or state. |
| Supported base facts | Existing component rules remain unchanged. | Abilities, level, proficiencies, HP, Size, Speed owners | The runbook reuses their schemas; the playtest record does not duplicate them. |
| Campaign scope | C15 owns participation. | `procedure.campaign.character-participation` | The record has no campaign/actor field and cannot attach an actor. |

## External implementation reference

Not applicable. This is no D&D behavior implementation, so Foundry dnd5e is neither a source nor
a useful data-flow authority.

## Prerequisite evidence

- `procedure.world.change` already provides atomic typed effects and schema validation.
- C15's active participation attach accepts only a pre-existing actor and is separately
  transactional.
- `procedure.world.model` establishes component schemas as the correct durable representation;
  `procedure.mcp.add-tool` rejects a new kind when existing ones fit.
- The approved dependency tree records the resulting deliberate two-transaction boundary.

## Runtime artifacts

| Artifact | Action | Boundary |
| --- | --- | --- |
| `dnd2024.playtest-character-record` | Add component definition and JSON Schema | Actor-side provisional record only. |
| `procedure.character.playtest-bootstrap` | Add procedure | Documents the existing-kind setup and revision protocol. |
| `PLAYTEST_CHARACTER_BOOTSTRAP.md` | Revise | Adds draft record creation and post-C15 activation/revision instructions. |
| `CharacterPlaytestInterfaceTests.cs` | Add | Tests schema, atomic draft creation, schema rejection, revision, and C15 separation. |

## Authoritative state and closed input

The record schema requires exactly:

```json
{
  "format": "dnd2024-playtest-character-record-v1",
  "state": "draft | active | retired",
  "entries": [
    { "kind": "class", "key": "wizard", "label": "Wizard", "details": "optional" }
  ]
}
```

An entry permits only `kind`, `key`, `label`, and optional `details`. `kind` is one of `class`,
`background`, `subclass`, `spell`, `equipment`, `feature`, `species-trait`, `feat`,
`rule-ruling`, or `note`. A record cannot carry IDs/references, rules prose, inputs to a rule,
effects, component data, derived values, actor/campaign identity, or mutable resource state.

The caller supplies this component data only inside the existing `effects` payload. C15 alone
receives the campaign/actor IDs required for attachment. No backend calculation or D&D rule is
resolved by this slice.

## Behavior, result, and typed effects

1. A bootstrap `effects` payload begins with `entity.create`, then add-only supported base
   components and one add-only draft record. The existing effect applier validates all data and
   applies the entire list or none.
2. C15 attaches the pre-existing actor in its own established campaign transaction.
3. After C15 success, one `component.set` replaces the complete valid record with `state: "active"`.
4. A revision is another complete valid `component.set`; its operation history remains the durable
   record of earlier values. The procedure requires the caller to preserve old entries as retired
   entries when their narrative history matters.
5. `retired` remains readable but communicates no capability to any action/mechanic.

## Failure, replay, and rollback contract

- Invalid effect structure causes the complete initial effects list to fail, with no actor or
  record. The catalog schema separately defines/validates the component's closed form; the current
  trusted-host direct-effects path does not perform per-component schema validation at runtime.
- A duplicate component add fails; initial bootstrap cannot replay over an existing actor.
- Invalid `component.set` leaves the previous record unchanged.
- C15 failure does not alter the draft actor/record; it adds no participation. It is deliberately
  recoverable, not a hidden partial official character.
- Record entries alone cause no components, item instances, grants, events, or action results.

## Implementation sequence

1. Add the confirmed component/schema and procedure catalog files.
2. Update the playtest bootstrap JSON and operational instructions to create/activate/revise the
   record using only existing kinds.
3. Add focused catalog/import/effect/C15 tests.
4. Run focused tests, catalog validation, then the full suite and protocol walk because catalog
   discovery changes, while the MCP kind registration itself does not.

## Acceptance matrix

| Case | Expected result |
| --- | --- |
| Valid draft record | An atomically created actor has exactly its supplied valid record in `draft`. |
| Schema boundary | Catalog-schema evaluation rejects unknown fields, unknown entry kinds, rule/effect-like fields, or invalid state. The trusted-host direct-effects path is documented not to enforce this at runtime. |
| Atomicity | A malformed record in the creation list leaves no actor or record. |
| Revision | Valid complete replacement works; invalid replacement preserves the previous record. |
| C15 boundary | C15 attaches only the existing actor and changes no record; activating the record is a distinct explicit effect. |
| No rule leakage | Class/spell/trait entries produce no separate component, event, item, or mechanic state. |
| Discovery | Fresh catalog import exposes both the component and procedure. |

## Verification commands

```text
dotnet test --no-restore --filter FullyQualifiedName~CharacterPlaytestInterfaceTests
dotnet run --project DantesRoleplay.Tools --no-build -- validate catalog
dotnet test DantesRoleplay.slnx --no-restore
dotnet test --no-restore --filter FullyQualifiedName~ProtocolWalkTests
```

## Completion receipt and exit gate

Write `character/CHARACTER_PLAYTEST_INTERFACE-RECEIPT.md` after accepted evidence. The receipt
records component/procedure IDs, focused/full/protocol results, and exclusions. Stop immediately
after that; official character creation remains CH3–CH6.

Focused coverage and catalog validation pass. The required protocol walk also passes. Full-suite
acceptance is currently blocked by three Feature 11 initiative-event harness failures and five
Feature 20 movement failures; this slice does not change either owner, so no completion receipt is
written yet.
