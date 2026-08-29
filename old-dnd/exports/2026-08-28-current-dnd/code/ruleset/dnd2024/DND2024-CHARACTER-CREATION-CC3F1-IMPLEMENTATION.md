# D&D 2024 CC3F1 implementation — all-cash starting equipment alternative

Status: **accepted**
Evidence: [CC3F1 receipt](evidence/DND2024-CHARACTER-CREATION-CC3F1-RECEIPT.md)
Feature/slice: **D&D 2024 character creation / CC3F1**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3F1](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; each class's *Starting Equipment* row
(PDF pp. 28–77), *Character Backgrounds* (PDF p. 83), and *Equipment > Coins > Coin Values*
(PDF p. 89)
Outcome: let any of the 4 × 12 accepted background/class combinations choose both legal cash
alternatives and receive one real, contained Gold Piece stack in the existing atomic transaction.
Exclusions: background/class packages, individual gear choices, packs/containers, auto-equip,
baseline AC changes, encumbrance consequences, buying/selling, currency exchange, and UI guidance.
Allowed files/areas: the existing class creation-profile schema and twelve class entities, creation
record schema, basic creator/contract/procedure, D&D acceptance-test harness, this dependency
plan/roadmap/status line, and this slice's evidence.
Stop point: cash/cash is durable inventory and both satisfied equipment deferrals are removed;
every package alternative remains a later bounded leaf.

## Confirmed decisions and compatibility

The user's standing approval for D&D-2024-aligned changes, optional extensions, correctly shaped
models before full mechanics, and continued implementation confirms the following additive schema
and public-surface meaning:

- class profiles may declare the exact cash alternative and exact Gold Piece definition ID;
- basic creation may accept optional `equipmentChoices:{background:"cash",class:"cash"}`; and
- the mechanic gains an optional `currency` role bound to the canonical Gold Piece definition.

No new permanent ID, migration, C# rule, endpoint, MCP kind, or transaction owner is introduced.
The new class fields are optional at the component-schema/runtime compatibility boundary so an old
profile can still use the omitted basic path. Current authored class models contain both fields.
Supplying equipment choices requires both fields and the exact currency role; omission retains the
existing equipment-pending path and needs neither.

## D&D 5e 2024 alignment

| Class | Package residual GP | Cash alternative GP |
| --- | ---: | ---: |
| Barbarian | 15 | 75 |
| Bard | 19 | 90 |
| Cleric | 7 | 110 |
| Druid | 9 | 50 |
| Fighter | 4 (A) / 11 (B) | 155 |
| Monk | 11 | 50 |
| Paladin | 9 | 150 |
| Ranger | 7 | 150 |
| Rogue | 8 | 100 |
| Sorcerer | 28 | 50 |
| Warlock | 15 | 100 |
| Wizard | 5 | 55 |

Every accepted SRD background declares a 50 GP cash alternative in its existing creation profile.
CC3F1 models only the class cash scalar because package structure/content belongs to later package
leaves; it does not mislabel an incomplete package model as complete.

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/data/item/templates/starting-equipment.mjs`, separates equipment alternatives, item links,
and wealth. CC3F1 adopts only that separation by resolving the SRD cash alternative into this
repository's existing fungible item-instance/quantity/containment model. No Foundry code, data,
assets, IDs, or runtime dependency are copied.

## Prerequisite evidence

- [CC3A](evidence/DND2024-CHARACTER-CREATION-CC3A-RECEIPT.md) proves all four background profiles,
  each exact 50 GP alternative, and the complete 48-pair creation matrix.
- [CC3E3](evidence/DND2024-CHARACTER-CREATION-CC3E3-RECEIPT.md) proves the current atomic actor,
  pending ledger, compatibility handling, replay, rollback, and independent review boundary.
- `currency.dnd2024.gold-piece.v1` is an active source-bound fungible `dnd2024.item-definition`.
- Existing item instance, quantity, containment, inventory, burden, and currency-value readers own
  physical inventory semantics; no parallel wealth component is created.

## Authoritative declarations and closed input

Current class profiles add both `startingEquipmentCashGp` (positive integer) and
`startingEquipmentCurrencyDefinitionId` (the canonical Gold Piece definition). Both present or
both absent is valid; authored profiles always have both. The selected background's existing
`startingEquipment.cashAlternativeGp` remains authoritative for its half of the grant.

The optional input is exactly `{background:"cash",class:"cash"}`. No amount, total, item ID,
definition ID, stack key, slot, source, effect, or pending entry is caller-authored. The optional
currency role must be the exact declared definition, must contain a valid fungible GP item
definition, and is ignored only when equipment choices are omitted.

## Behavior, record, and typed effects

1. Validate exact background/class declarations, choice input, and canonical Gold Piece definition.
2. Derive total GP as background cash plus class cash and derive one bounded deterministic item ID
   from the reserved actor ID.
3. Extend the immutable record with `selections.startingEquipmentChoices` and one
   `createdItemIds` entry. Remove only the background and class equipment deferrals.
4. In the existing transaction, create the actor and normal components, then create one Gold Piece
   entity, add `dnd2024.item-instance`, add `dnd2024.item-quantity`, and contain it under the actor
   in `inventory.currency`.
5. Exact replay returns the prior result; any late failure rolls back actor, currency, participation,
   relationships, and containment together.

## Failure, replay, and rollback contract

Partial/non-cash/extra choices, missing/wrong/corrupt currency role, absent/partial/invalid class cash
declarations, bad background cash, source drift, overlong derived ID, duplicate actor/item, or any
prior creation failure adds nothing. The omitted path remains compatible and produces no currency.

## Implementation sequence

1. Add optional paired cash fields to the class profile schema and exact declarations to all twelve
   class entities.
2. Extend the creation record and basic creator requirements/input/state/effects/pending evidence.
3. Update the mechanic/procedure and add declaration, 48-pair, inventory/value, failure, replay,
   rollback, and omission-compatibility tests.
4. Run focused tests, complete D&D tests, disposable catalog validation, full solution, protocol
   walk, and independent review.
5. Write one receipt and update CC3F1 status once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Declarations | all twelve exact cash amounts and canonical definition IDs |
| 4 × 12 cash | total equals 50 + class amount; one visible physical stack; both deferrals absent |
| Inventory | existing inventory/currency readers discover the stack and exact copper value |
| Omission | existing 48-pair matrix has no item and retains both equipment deferrals |
| Failure | bad choices/role/profile/source/ID/collision leave no partial state |
| Transactions | exact replay is stable; injected late failure rolls back item and containment |
| Surface | protocol walk discovers the optional role/input behavior without a new operation kind |

## Verification commands

- focused basic-creation/item/currency tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a fresh disposable database;
- full solution tests; and
- the real JSON-RPC protocol walk because the mechanic's optional role is a public requirement
  change.

## Completion receipt and exit gate

Acceptance requires a CC3F1 receipt containing delivered scope and command results. This slice
stops without package/item cohorts, nested definition automapping, equipment selection, auto-equip,
AC changes, currency exchange, buying/selling, multiclassing, or UI work.
