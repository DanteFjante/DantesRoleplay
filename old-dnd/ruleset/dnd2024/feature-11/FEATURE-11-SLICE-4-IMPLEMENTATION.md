# Feature 11 Slice 4 implementation — authoritative Initiative-roll evidence

> **D&D implementation reference:** Inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding this mechanic. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **active**  
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`, Feature 11  
Dependency tree/leaf: `ruleset/dnd2024/feature-33/FEATURE-33-SLICE-3-INTERRUPTION-OWNER-RECONCILIATION.md`, Feature 11 Initiative evidence  
Ruleset alignment: **dnd2024-owned**  
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Playing the Game > Combat > The Order of Combat > Initiative*  
Outcome: Each successful recorded Initiative order emits one authoritative event per creature that actually rolled Initiative.  
Exclusions: Rest subscription/state, interruption/resumption, turn lifecycle, combat outcome, spellcasting, exertion, action economy, and new MCP verbs.  
Allowed files/areas: initiative-order catalog mechanic/procedure; `dnd2024.initiative.rolled` event type/schema; focused Feature 11 test; existing catalog test harnesses that execute the Initiative-order parent and therefore must import registered event types; generated catalog manifest if required by catalog validation; receipt and owner-status reconciliation.  
Stop point: Accepted Initiative-roll evidence only; do not consume it from Feature 33.

## Confirmed decisions

The 2026-08-22 cross-owner confirmation authorizes this narrow permanent public event contract:

- event ID `dnd2024.initiative.rolled`, with the closed payload fields `subjectId`, `encounterId`,
  and fixed Initiative source reference;
- only `subjectId` is declared as an E8 dynamic entity-payload field;
- the existing encounter Initiative-order parent emits events only after every child result and
  tie decision have validated, in final recorded Initiative-order sequence; and
- each event names the subject then encounter in `entityIds`, has no instance scope, and shares
  the parent action's transaction, audit, replay, and rollback boundary.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Initiative | Every combat participant rolls a Dexterity-based D20 Test; ordering follows the result. | `mechanic.dnd2024.initiative.roll` and `mechanic.dnd2024.encounter-initiative-order` | This slice records evidence only after that existing composition succeeds; it neither rolls nor orders again. |
| Rest interruption | A rest is interrupted when the creature rolls Initiative. | Feature 33 owns rest timing. | The event is generic authored evidence for a later subscriber; this slice changes no rest. |

## Prerequisite evidence

- The Initiative-order parent already composes exactly one validated Initiative result per contained
  participant and records one atomic snapshot.
- Platform E8 Slice 1 accepts one schema-declared payload entity field as a reaction role. No
  subscription is added in this slice.
- Feature 33 Slice 2 and the Slice 3 interruption reconciliation establish the later consumer and
  explicitly identify Initiative evidence as Feature 11's missing leaf.

## Runtime artifacts

| Artifact | Change |
| --- | --- |
| `dnd2024.initiative.rolled` | New active event type with a closed payload schema and `x-dantes-entity-payload-fields: ["subjectId"]`. |
| `mechanic.dnd2024.encounter-initiative-order` | Revise only to emit the one event per final ordered participant. |
| `procedure.mechanic.dnd2024.encounter-initiative-order` | Revise event ownership and exact emission boundary. |

## Authoritative state and closed input

This slice accepts no new action input. It consumes the existing closed Initiative-order request,
validated contained roster, composed child outputs, and approved tie decisions. `subjectId` and
`encounterId` are derived from that validated composition; callers may not supply event identity,
event payload, Initiative count, source reference, event scope, or effects.

## Behavior, result, and typed effects

After all child outputs, roster checks, and tie decisions produce the final order, the parent still
returns its existing single encounter `component.add` effect. It also declares one
`dnd2024.initiative.rolled` event for each final order row, ordinally in that order. Each event
contains `{ subjectId, encounterId, sourceRef }`, entity IDs `[subjectId, encounterId]`, and no
instance scope. It changes no result-data shape, no participant state, and no ordering arithmetic.

## Failure, replay, and rollback contract

Every existing failed order path declares no events. A malformed event schema, duplicate/missing
child, invalid role/entity binding, event-routing failure, or effect failure aborts the same root
transaction: no order component, accepted Initiative event, audit success, or partial consumer
reaction may remain. Equivalent fresh executions retain their existing deterministic order and
emit byte-identical event payloads in the same sequence.

## Implementation sequence

1. Add the event type/schema first.
2. Revise the parent mechanic and procedure without changing its input, effects, order, or data.
3. Add focused import/action/ledger tests for successful ordered evidence and no-event failures.
4. Validate the catalog and complete the acceptance checks; write a receipt and stop.

## Acceptance matrix

| Case | Assertion |
| --- | --- |
| Successful two-creature order | Exactly two events occur in final Initiative order; each has the closed derived subject/encounter/source payload and matching entity IDs. |
| Tie decision | Event order exactly follows the validated chosen tie order. |
| Existing rejection paths | Empty/drifted roster, malformed/missing/duplicate child, or invalid tie decision emit no event and write no order. |
| Isolation | No participant, turn, rest, spell, movement, or action-economy state changes. |
| Replay/rollback | Same seed/input emits equal event sequence; routed failure rolls back the order and all events. |
| Catalog compatibility | Existing Initiative order, turn lifecycle, and non-subscribing event behavior retain their contracts. |

## Verification commands

- `dotnet test --no-build --no-restore --filter FullyQualifiedName~CatalogFeature11InitiativeEventTests`
- `roleplay validate catalog`
- `dotnet test DantesRoleplay.slnx --no-restore`
- `git diff --check`

## Completion receipt and exit gate

Record the exact events, focused/catalog/full-suite results, and exclusions in
`FEATURE-11-SLICE-4-RECEIPT.md`. Update Feature 33's tree only to mark the Initiative input
verified. Stop before Feature 33 consumption or any other interruption source.
