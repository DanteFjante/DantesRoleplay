# D&D 2024 CC3C implementation — durable feature-identity grants

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC3C**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3C](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; the selected background locator and selected
class locator already carried by their source-bound creation profiles/progression
Outcome: make every selected background Origin Feat and every selected class level-1 feature a
durable actor-side identity grant while continuing to deny unimplemented behavior.
Exclusions: feat or class-feature behavior, resources, choices within features, spells, equipment,
advancement beyond level 1, grant deletion/reversal, UI discovery, migrations, and protocol kinds.
Allowed files/areas: one new D&D component definition/schema, the existing basic creator and its
procedure, the D&D acceptance-test harness, this dependency plan/roadmap/status line, and this
slice's evidence.
Stop point: all 48 accepted background/class pairs persist exact source-bound identity grants and
matching behavior deferrals atomically; no grant is treated as executable rules behavior.

## Confirmed decisions

The user's 2026-08-27 direction to prefer complete models with correct schemas even when mechanics
remain absent, followed by the request to continue character creation, confirms the new permanent
component ID `dnd2024.character-feature-grants` and its additive actor-state meaning.

- A grant proves only that the character has the named immutable feature identity.
- Each grant records its declaring background or class, grant kind, source reference, and the
  configuration or class level needed to interpret the declaration later.
- Behavior implementation status is not copied into this durable component. The existing
  unresolved-entitlement ledger remains the negative authority until a later mechanic replaces
  that deferral with implemented state/behavior.
- Existing basic-creation input and transaction ownership remain unchanged.

No migration, optional rule, house rule, C# semantic change, endpoint, or MCP kind is introduced.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Origin Feat | The selected background names the character's Origin Feat | `dnd2024.background-creation-profile.originFeat` | Persist one configured `origin-feat` identity grant |
| Class features | The class table names features gained at level 1 | `dnd2024.class-progression` and its reader | Persist every returned level-1 `class-feature` identity grant |
| Source provenance | Background and class grants come from different source sections | source-bound profile/progression records | Copy the declaring owner's exact source reference onto each grant |
| Behavior boundary | Having a feature does not prove that its rules have been implemented | creation record unresolved entitlements | Keep one behavior deferral for every persisted grant and add no effects beyond identity state |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/documents/advancement/item-grant.mjs`, keeps configured source UUIDs separate from the actor
items created from them and records the source UUID beside each created actor item. Its duplicate
skip and reversal handling also show why provenance must survive the initial grant. CC3C adopts the
identity/provenance separation, but not Foundry's embedded-item model, optional skipping, reversal,
or runtime code. No Foundry code, data, assets, IDs, or dependency are copied.

## Prerequisite evidence

- [CC3A](evidence/DND2024-CHARACTER-CREATION-CC3A-RECEIPT.md) proves the four source-bound
  background profiles and their exact Origin Feat declarations.
- [CC3B](evidence/DND2024-CHARACTER-CREATION-CC3B-RECEIPT.md) proves the compatible closed request,
  48 background/class pairs, and the existing behavior-deferral semantics.
- [CC2H2](evidence/DND2024-CHARACTER-CREATION-CC2H2-RECEIPT.md) proves all twelve level-1 class
  progression declarations and immutable feature identity records.
- The basic creator remains the sole D&D composition owner; the generic action runner remains the
  sole transaction/replay/rollback owner.

## Runtime artifacts

- New component `dnd2024.character-feature-grants` with a closed schema and catalog description.
- Revised basic creator JavaScript/procedure to derive and atomically apply the grant ledger.
- Revised focused catalog/creation tests proving referential integrity, exact grants, pending
  behavior, replay, rollback, and the complete 48-pair matrix.
- No new mechanic, content ID, fixture, migration, transaction owner, C# rule, or public protocol
  kind.

## Authoritative state and closed input

Roles, children, and the accepted four-/five-property input shapes are unchanged. The caller may
not supply feature IDs, grant provenance, configuration, class level, behavior status, pending
entries, effects, or transaction identity.

The selected source-bound background profile supplies the Origin Feat definition/configuration and
its source reference. The effect-free class-progression child supplies the class's level-1 feature
definition IDs; the selected class profile supplies their declaring source reference. Catalog
acceptance verifies that every referenced ID resolves to one active `feature` content definition
under the registered SRD source.

## Behavior, result, and typed effects

1. Validate the existing roles, source bindings, child results, and request exactly as before.
2. Derive one `origin-feat` grant with `definitionId`, `grantedByDefinitionId`, `grantKind`,
   `configurationKey`, and exact background `sourceRef`.
3. Derive one `class-feature` grant per level-1 entitlement with `definitionId`,
   `grantedByDefinitionId`, `grantKind`, `classLevel: 1`, and exact class `sourceRef`.
4. Reject duplicate grant identities and sort grants deterministically by kind, definition, source,
   and configuration/level.
5. Add the component through the existing actor effect bundle and list it in applied component
   evidence. Retain the existing `behavior-unimplemented` pending entry for every grant.
6. Commit through the existing transaction. Replay, rollback, participation creation, and all
   other character state remain unchanged.

Missing and empty are invalid for the component: every accepted basic character has exactly one
Origin Feat plus at least two class-feature grants. The mechanic result reports the total grant
count but does not expose or claim feature behavior.

## Failure, replay, and rollback contract

Malformed/dangling/duplicate Origin Feat or class-feature declarations, a wrong class prefix,
missing source state, source drift, an empty entitlement list, or an unexpected implemented-status
claim fails before effects. Existing malformed input, wrong-role, collision, stale-content, and
child failures remain no-change failures. Exact replay returns the prior committed result; an
injected late failure leaves no actor, grant component, participation, or relationship.

## Implementation sequence

1. Add the closed component definition/schema and active-slice contract.
2. Derive/apply deterministic identity grants in the existing JavaScript transaction proposal.
3. Update the procedure and focused catalog/basic-creation tests.
4. Run focused tests, complete D&D tests, disposable catalog validation, and the full solution.
5. Write one receipt and update CC3C status once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Origin grants | all four backgrounds persist their exact configured Origin Feat once |
| Class grants | all twelve classes persist every and only level-1 progression feature once |
| 48-pair matrix | every background/class combination has exact deterministic grants |
| Referential integrity | every granted ID resolves to one active SRD feature identity |
| Behavior denial | each grant retains its matching `behavior-unimplemented` pending entry |
| Closed schema | missing, empty, extra, duplicate, or malformed grant state is rejected |
| Transactions | exact replay and injected late rollback remain atomic |
| Compatibility | prior omitted and completed origin-choice requests remain valid |

## Verification commands

- focused feature-grant/basic-creation tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a fresh disposable database; and
- full solution tests. No protocol walk is required because protocol/dependency registration does
  not change.

## Completion receipt and exit gate

Delivered by the
[CC3C completion receipt](evidence/DND2024-CHARACTER-CREATION-CC3C-RECEIPT.md). This slice stops
without feature behavior, embedded
items, deletion/reversal, resource owners, spell resolution, equipment, or UI work.
