# E8 Slice 1 metadata reconciliation — exact payload role binding

Status: **Confirmed and implemented; see [Slice 1 receipt](E8-SLICE-1-RECEIPT.md).**  
Reviewed: 2026-08-21

## Purpose

E8 is the existing generic owner of Feature 33's missing active-rest dispatch seam. This
reconciliation closes the one implementation gap in `E8-DEPENDENCY-PLAN.md`: the plan requires
versioned event-type and subscription metadata, but the current event and subscription models do
not have a place to store either declaration.

The change is ruleset-neutral. It contains no D&D ID, rest state, clock logic, Human behavior,
feature rule, campaign behavior, or JavaScript rule outcome.

## Confirmed current boundary

| E8 need | Current evidence | Result |
| --- | --- | --- |
| Event schema | `EventTypeVersion.PayloadSchema` persists the versioned JSON Schema; no separate event metadata exists. | The eligible payload-field declaration must live in the schema itself, not an untracked side channel. |
| Subscription registration | `SubscriptionVersion` persists fixed roles, tracked IDs, scalar payload equality, order, and limits only. | A role-to-event-field mapping needs its own versioned field; it cannot be hidden in fixed roles or scalar filters. |
| Runtime projection | `EventRouter` resolves only fixed roles and projects `ctx.event`/`ctx.eventEntities`. | It needs one generic runtime binding source, while preserving the existing shapes when unused. |
| Atomicity/replay | Event routing and `EffectApplier` already run reactions in the root transaction and preserve ordered execution evidence. | Slice 1 extends selection/binding only; it must reuse the existing transaction, audit, and seed paths. |

## Selected metadata contract

The following is the smallest compatible contract for E8 Slice 1.

### Event-type declaration

An event type may declare this optional root-level JSON-Schema extension:

```json
"x-dantes-entity-payload-fields": ["subjectId"]
```

Rules:

1. The extension is absent or a canonically sorted, duplicate-free array of one to twelve field
   names.
2. Each named field is a direct property of an object-root payload schema and its schema accepts
   only non-null strings. JSON paths, nested fields, pattern-derived fields, arrays, and objects
   are invalid declarations.
3. The extension is persisted and versioned as part of the existing payload-schema bytes. It adds
   no database column, separate event record, or event payload field.
4. A subscription using a mapped field requires that field at dispatch. A missing, empty, wrong
   type, or stale value rejects the root transaction; the extension itself does not make a field
   mandatory for unrelated subscriptions.

### Subscription declaration

Add one closed versioned JSON field to `SubscriptionVersion` and its request/detail/catalog forms:

```json
"roleFromEventPayload": { "subject": "subjectId" }
```

Rules:

1. Its stored default is `{}`. Existing registrations export/import/read exactly as before when it
   is empty.
2. Slice 1 allows exactly zero or one mapping. The mapping names one declared ordinary reaction
   role and one field listed by the selected event type's schema extension.
3. A mapped role cannot also appear in `fixedRoleEntityIds`; all other required roles remain fixed.
   Guard subscriptions, child mechanics, role arrays, multiple mappings, alternate sources, and
   caller-provided bindings remain invalid.
4. The mapping is append-only/versioned with the registration. It participates in content hashing,
   catalog import/export, validation, discovery, and audit readback.
5. The required schema migration adds only the non-null JSON text column with default `{}`. It
   introduces no game data or rule branching.

### Runtime binding

After existing subscription matching succeeds, the generic router reads the one mapped top-level
payload field. The value must be a trimmed nonempty string occurring exactly once in the accepted
event's `entityIds`. It is then supplied as the mapped role's entity ID to the existing projection
resolver, which performs the normal scope/entity/component validation.

The router leaves `ctx.event`, `ctx.eventEntities`, fixed-role handling, filter matching, execution
order, seed derivation, chain limits, effect application, audit rows, and all no-mapping behavior
unchanged. It adds no dynamic database access for JavaScript, no scheduler, and no game vocabulary.

## Rejected alternatives

| Alternative | Rejection reason |
| --- | --- |
| Put the field declaration in a prose comment or plan | It is not versioned runtime metadata and cannot be safely validated at dispatch. |
| Encode a payload field in `fixedRoleEntityIds` | That field only permits real entity IDs; a sentinel would corrupt its meaning and bypass validation. |
| Reuse `payloadEquals` | It is a scalar filter, not a dynamic binding source, and would make matching ambiguous. |
| Give JavaScript a payload/SQL query capability | It would create an unbounded, unaudited world read and violate the projection boundary. |
| Implement Feature 33's own rest subscription first | It would duplicate E8's cross-consumer platform owner. |

## Required E8 Slice 1 implementation document

After this contract is confirmed, the active implementation document must limit changes to:

- generic event-type schema-extension validation and readback;
- the new versioned subscription mapping field, migration, store/DTO/catalog serialization, and
  content-hash participation;
- generic event-router binding before the existing projection resolution;
- focused event/subscription/router/import-export tests plus one generic fixture; and
- a concise E8 Slice 1 receipt.

It must exclude E8 Slice 2 indexed fan-out, Feature 33 rest episodes, any D&D catalog behavior,
new public MCP verbs, and changes to existing fixed-role semantics.

## Confirmation gate

Confirm these two public semantics before implementation:

1. The root JSON-Schema extension `x-dantes-entity-payload-fields` is the sole declaration of
   event fields that may dynamically bind a subscription role.
2. `SubscriptionVersion.roleFromEventPayloadJson` is a versioned, closed zero-or-one mapping with
   default `{}`, requiring the migration and catalog/read-model expansion described above.

## Planning receipt

- Runtime artifacts created: none.
- Catalog/runtime behavior changed: none.
- Next authorised implementation after confirmation: Platform E8 Slice 1 only.
