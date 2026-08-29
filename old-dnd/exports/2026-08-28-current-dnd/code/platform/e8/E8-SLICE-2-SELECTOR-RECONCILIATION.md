# E8 Slice 2 selector reconciliation — bounded indexed fan-out

Status: **Confirmed; implementation is authorised.**  
Owner: `platform/e8/E8-DEPENDENCY-PLAN.md`, Slice 2  
Ruleset alignment: **ruleset-neutral**  
Source: Not applicable; this is generic event-routing infrastructure.

## Purpose and boundary

Slice 1 is accepted: one event payload entity ID can bind one reaction role. Slice 2 adds a
separate, bounded way for one reaction subscription to select a set of already-active entities in
the accepted event's declared scope. It contains no rest, clock, effect, creature, character, or
D&D rule behavior.

It does not introduce a scheduler, a JSON/component-data filter, an arbitrary query, recursive
traversal, a second dynamic role, or a consumer subscription.

## Confirmed existing owners

| Concern | Existing owner/evidence | Consequence |
| --- | --- | --- |
| Event scope and root transaction | `EventDetail.Scope`, `EventRouter`, `EffectApplier` | The existing accepted event remains the only trigger and transaction owner. |
| Subscription scope/mode/versioning | `Subscription`/`SubscriptionVersion`, `SubscriptionStore` | The selector belongs on a subscription version and only a reaction can use it. |
| Scope membership fact | `relationship` table: directed `(from, to, kind)` | One explicit relationship direction proves membership; relationship `Data` is never read. |
| Active-state fact | `component` table indexed by `DefinitionId` | Component presence—not component JSON—means selected/active. |
| Projection/audit/seeds/limits | `ProjectionResolver`, `EventExecution`, `ChainBudget` | Each selected receiver follows the existing reaction path; no JavaScript gets store access. |

## Proposed public contract

### Subscription version field

Add a versioned closed JSON field, defaulting to `{}`:

```json
"fanoutSelector": {
  "role": "receiver",
  "relationshipKind": "scope.member",
  "direction": "scope-to-candidate",
  "componentId": "active.marker"
}
```

The empty object preserves existing behavior. A nonempty selector has exactly the four shown
properties and no others.

| Property | Rule |
| --- | --- |
| `role` | One declared ordinary reaction role. It cannot be fixed or payload-bound. All other required roles remain fixed. |
| `relationshipKind` | A nonempty permanent dotted relationship kind, max 100 characters. It is compared exactly; relationship `Data` is never inspected. |
| `direction` | Exactly `scope-to-candidate` or `candidate-to-scope`. |
| `componentId` | One existing component-definition ID. Its presence on the candidate is the sole active-state test. |

`fanoutSelector` and `roleFromEventPayload` are mutually exclusive. A selector is invalid for a
guard, an empty subscription scope, a child-bearing reaction, an unknown/fixed role, an unknown
component definition, or a scope that does not equal the accepted event scope.

The field participates in migration, subscription detail/readback, commit payload, catalog
import/export, validation, and content hashes. It creates no new commit kind or router verb.

### Selector algorithm

1. Perform the existing active/type/exact-scope/tracked/scalar-filter subscription match.
2. For a selector subscription, require the registration scope to be nonempty and exactly equal
   to `event.Scope`.
3. Use one relationship index for the declared direction and the component-presence index for
   `componentId`. The scope endpoint is the subscription scope string; the opposite endpoint is a
   candidate entity ID.
4. Deduplicate candidate IDs, sort them with ordinal string comparison, and reject more than
   **8** candidates. Eight is the current public maximum for a subscription's executions in one
   chain (`procedure.event.chain-limits`); results are never truncated.
5. Before the first receiver executes, validate the entire selected set: each candidate still
   exists and is not soft-deleted, has the declared component, belongs through the declared
   directed relationship, and fits both the subscription execution limit and remaining chain
   budget.
6. For each candidate in canonical order, add only `role -> candidateId` to the normal fixed
   bindings, resolve the existing projection, derive the existing per-reaction seed from its
   ordinal, and run the normal reaction/audit/effect path.

Every selected reaction receives the same accepted event envelope. Its sole changing input is the
one selected role. Empty selection is a successful no-op. Lookup/validation/execution failure
aborts the whole root transaction with no partial world, event, execution, notification, or success
evidence.

## Required generic indexes

The current component `DefinitionId` index is the active-state index. Add two relationship indexes
without changing relationship meaning:

- `(FromEntityId, Kind, ToEntityId)` for `scope-to-candidate`;
- `(ToEntityId, Kind, FromEntityId)` for `candidate-to-scope`.

The slice must include a query-plan/equivalent test proving the selector uses only these columns
and `component.DefinitionId`; it may not load or filter relationship/component JSON.

## Required acceptance evidence

- Registration rejects malformed, unknown, fixed, mixed-source, guard, child, empty-scope,
  missing-component, invalid-direction, and extra-property declarations.
- Empty, one, several, exact-eight, and nine candidates; wrong direction/scope/component; duplicate
  relationships; deleted candidate; and corrupted persisted selectors are covered.
- Candidate order is insertion-independent and replay-stable.
- All selected candidates validate before the first JavaScript invocation; subscription/chain limits
  fail before executing any receiver.
- Failure at lookup, projection, mechanic, emitted effect/event, audit, and commit rolls back the
  root atomically.
- Empty selector behavior remains byte-for-byte compatible with Slice 1/fixed-role routing.

## Confirmation record

The requested continuation confirms all of the following public-surface decisions:

1. `SubscriptionVersion.fanoutSelectorJson` uses the exact closed four-property shape above and
   defaults to `{}`.
2. Existing nonempty `Subscription.Scope` is the selector's exact scope authority; it is not
   duplicated inside the selector and global/empty scope is forbidden.
3. The two direction names and candidate semantics above are the only relationship selector
   shapes, component presence is the only active-state predicate, and the hard selected-set cap is
   eight with reject-not-truncate behavior.

## Planning receipt

- Runtime artifacts created: none.
- Catalog/runtime behavior changed: none.
- Next authorised work: one active E8 Slice 2 implementation document and only its generic
  indexed selector/fan-out slice.
