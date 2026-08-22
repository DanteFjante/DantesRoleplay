# E8 dependency plan — dynamic event role binding and bounded fan-out

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Slices 1–2 accepted; consumer adoption remains separately owned.**
Last updated: 2026-08-21

## Execution rule

This is planning only. A future pass re-reads E1 event, subscription, guard/reaction, chain-limit,
projection, and `procedure.system.modify` contracts before one coherent slice. It adds focused
event-dispatch/rollback/replay tests, preserves the behavior of fixed-role subscriptions, runs the
full suite and diff check, writes a receipt, and stops before consumer migration.

## Target capability

An event subscription can bind one declared reaction role from one declared, validated entity-ID
field in its accepted event payload, only when that value is also one of the accepted event's live
`entityIds`; a later bounded indexed subscription can deterministically select active records in
the same declared scope without a feature inventing a scheduler or an unbounded event query.

### Included

- Closed event-payload role binding, event-schema validation, deterministic matching/order,
  reaction projection/audit/replay, and one generic event fixture.
- Later bounded indexed fan-out by a registered component/scope key for active state such as
  rests/effects, with explicit limits and all-or-nothing chain behavior.

### Excluded

- Arbitrary JSON payload paths, arbitrary database predicates/SQL, dynamic code, background
  polling/scheduling, automatic time advancement, cross-world scans, multicast choice UI,
  notifications as state, or game-specific event semantics.

## Existing evidence and owner decision

E1 subscriptions require every ordinary role to be fixed in `fixedRoleEntityIds`; tracked IDs only
filter subscriptions. Reactions declare no child mechanics. That prevents a generic reaction from
receiving the real damaged/resting/hidden creature named by an event and prevents a root clock event
from finding every active state in that world. Subscription matching and projection binding—not
Features 17/18/32/33—own the correction.

The schema-valid payload is not by itself sufficient authority to select a receiver: Slice 1 treats
the event's live validated `entityIds` as the trust boundary. Slice 2 treats one declared
relationship plus one declared component-presence index as the trust boundary; it never queries
arbitrary component JSON.

## Dependency graph

~~~text
E8 dynamic event roles and fan-out                                  [blocked parent]
├─ E1 event ledger, guards, subscriptions, chain limits             [implemented]
├─ declared event schemas and payload validation                     [implemented]
├─ payload entity-role binding                                       [accepted Slice 1]
├─ deterministic one-receiver reaction fixture                       [accepted Slice 1]
├─ bounded indexed scope selector                                    [implemented Slice 2]
├─ deterministic fan-out / chain-limit aggregation                   [implemented Slice 2]
└─ F17/F18/F32/F33 consumers                                          [awaiting each consumer owner slice]
~~~

## Ownership decisions

1. Slice 1 permits only an exact top-level payload field declared by the event type to bind one
   optional or required ordinary role. No JSON paths, string parsing, or caller-supplied role ID is
   allowed. Its value must be a nonempty string occurring exactly once in `event.entityIds`.
2. The event ledger payload selects the dynamic entity only inside that `entityIds` boundary. The
   subscription validates that entity exists, is visible in the normal event scope, and satisfies
   the reaction role's declared component requirements at dispatch; stale/missing/corrupt roles
   fail the root transaction as ordinary reaction failure.
3. Slice 2 selection is an indexed, catalog-declared selector over one component-presence state
   and one declared relationship direction proving scope membership. It has a fixed maximum match
   count, canonical ordinal entity-ID order, and exact chain/audit reporting. It is not a general
   event query language.
4. Fan-out creates no scheduler: a reaction runs only because an accepted action emitted an event.
   The root clock remains the only time coordinate and only its existing owner advances it.

## Slice order and stop gates

| Slice | Starts only when | Exit gate |
| --- | --- | --- |
| 1. Exact payload role binding | E1 contracts and event schemas re-read | One reaction receives one event-named entity through a declared role; mismatches roll back the root. |
| 2. Indexed bounded fan-out | Slice 1 and [selector confirmation](E8-SLICE-2-SELECTOR-RECONCILIATION.md) | One scoped event selects a canonically ordered bounded active set and every reaction joins/rolls back atomically. |
| 3. Consumer adoption | Prior slices plus each event owner | One named F17/F18/F32/F33 consumer adopts no private subscription workaround. |

## Slice 1 specification

Revise event-type version metadata with `entityPayloadFields: ["subjectId"]`, where every listed
field is a direct property of an object-root JSON Schema and accepts only a non-null string. Revise
subscription version metadata with a mutually exclusive
`roleFromEventPayload: { "subject": "subjectId" }` mapping. Slice 1 accepts exactly one mapping:
one ordinary declared reaction role, one active exact event type, and one listed direct field. The
mapped role cannot be fixed or have another source; the reaction remains childless and all other
required roles remain fixed.

At dispatch, do normal type/status/tracked/scalar-payload/scope matching first, then read exactly
`event.payload.subjectId`. It must be a nonempty string listed exactly once in `event.entityIds`;
the router resolves it under ordinary event-scope visibility and verifies normal role component
requirements before projection or mechanic execution. It then constructs the binding as a fixed
role would, leaving `ctx.event` and `ctx.eventEntities` unchanged. Keep `fixedRoleEntityIds`,
tracked filters, scalar payload equality, routing order, seed derivation, history shape, and failure
behavior byte-identical when the new mapping is absent. The generic fixture emits one typed event
referencing exactly one test entity and a reaction returns zero effects plus structured proof of
only its bound role.

Use distinct stable failure codes (names confirmed against existing conventions) for invalid field
declaration, invalid mapping, malformed runtime field, payload ID absent from event entity IDs,
stale receiver, and projection mismatch. A registration failure writes no version; a dispatch
failure runs no receiver and rolls back the root without partial state or execution evidence.

### Exit gate

Stop after one exact payload-to-role binding is verified. Do not implement indexed fan-out, rests,
effects, clock advancement, or any game reaction in Slice 1.

## Slice 1 acceptance matrix

| Area | Required proof |
| --- | --- |
| Declaration | Reject inactive/mismatched event type; absent, duplicate, non-string, nullable, array, or non-top-level entity fields; unknown/fixed/duplicate roles; more than one mapping; children; and alternate role sources. |
| Normal route | The fixture role equals the exact payload value; normal required components project; fixed roles and the event envelope retain their current behavior. |
| Trust boundary | Reject null/empty/non-string payload, absent/repeated `entityIds` occurrence, missing/soft-deleted receiver, cross-scope receiver, and component mismatch before mechanic execution. |
| Atomicity | Inject failure at binding validation, projection, mechanic, effect dry-run/apply, guard, event write, execution/history/receipt write, and commit; assert no partial world/event/execution/notification/success row. |
| Replay and compatibility | Same root seed/input produces the same outputs and execution order; all fixed-role, tracked-ID, and scalar `payloadEquals` fixtures keep accepted/rejected outcomes and stored metadata when the new field is absent. |
| Repository | Focused tests and full suite pass; validate catalog when fixtures change; whitespace search and diff check pass. |

## Slice 2 specification — bounded indexed fan-out

Do not start until Slice 1 is accepted and the relationship/index owners are re-read. The closed
selector must name exactly: (a) a nonempty exact event scope, (b) one catalog-declared relationship
type and direction proving a candidate belongs to that scope, and (c) one catalog-declared component
definition whose *presence* means the candidate is active. It may not use payload JSON, component
JSON, a filter expression, or an empty/global scope.

Retrieve candidates only through the owned relationship and component-presence indexes; deduplicate
by entity ID, sort with ordinal entity-ID comparison, validate the entire selected set before the
first receiver runs, and give every receiver the same event envelope plus the one declared selected
role. Confirm the hard maximum against current `procedure.event.chain-limits` immediately before
implementation. Reject one-over-bound sets before execution—never silently truncate—and continue to
apply existing per-subscription and chain-wide limits to every receiver.

Required Slice 2 proof includes empty/one/many candidates; inactive/no-component, wrong relation or
direction, other scope, duplicate relation, stale entity, and malformed index entries; insertion-
independent canonical ordering and replay; exact-bound success and every relevant chain-limit
failure; fault injection during lookup, each projection/mechanic, nested events/effects, audit, and
commit; and a query-plan/equivalent test that proves no JSON or cross-scope scan. One generic
relationship/component fixture is the exit gate. No consumer migration, scheduler, catch-up timer,
or special selector is allowed.

## Plan-quality audit

- Dynamic role identity is sourced only from a declared validated event field, not a caller or
  query expression: yes.
- E1 remains the event/chain owner and the root clock remains the only time owner: yes.
- Slice 1 covers malformed/stale/cross-scope input, deterministic order, replay, rollback, and a
  no-scheduler stop gate: yes.
- Slice 2 names its selection facts, ordering, bound behavior, and no-JSON-query test before any
  consumer asks for fan-out: yes.

## Plan-change rule

Split a new feature if a consumer needs recursive graph traversal, arbitrary filters, more than one
dynamic role before the data model proves it, a distinct index shape, fan-out beyond confirmed chain
bounds, or fan-out without an accepted causal event. E8 itself does not permit reaction children:
Feature 18 still waits for E6; Feature 33 waits for Slice 2 and must declare active episode/world
membership through the accepted component and relationship records.
