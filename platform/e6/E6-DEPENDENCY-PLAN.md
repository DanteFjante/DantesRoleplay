# E6 dependency plan — typed dependent mechanic composition

Status: **Slices 1–2 accepted; consumer adoption is next.**
Last updated: 2026-08-21

## Execution rule

Slice 1 is accepted and recorded in [its receipt](E6-SLICE-1-RECEIPT.md). A future Slice 2 pass
must re-read `procedure.system.create-feature`,
`procedure.system.modify`, mechanic metadata/projection/composition contracts, public capability
descriptions, and the current affected tests. It implements exactly one accepted slice, proves old
behavior is unchanged when the declaration is absent, runs focused and full suites plus
`git diff --check`, records a receipt, and stops. No consumer feature is migrated in the E6 pass.

## Target capability

A parent mechanic can declare an acyclic dependency between child mechanics so a later child
receives a statically declared, validated copy of one earlier single child's object-valued `data`
result, while all child effects still remain proposals in the one parent transaction.

### Included

- Closed child-dependency declaration metadata, topological execution, deterministic seed ordinal,
  typed `data` handoff, and read-only child-result provenance.
- One closed input-binding form for a complete object from a named earlier child result,
  plus static literals and inherited parent input already supported today.
- C# validation, projection/composer tests, contract/capability discovery, and one generic
  effect-free fixture proving a two-child data flow.

### Excluded

- Arbitrary JSONPath, templates, string interpolation, JavaScript/C# expressions, result
  mutation, caller-selected child keys/paths, effect application between children, virtual
  entities, database queries, dynamic role lookup, event subscriptions, or game rules.

## Existing evidence and owner decision

`MechanicComposer` currently resolves every declared child before parent source and allows only
inherited parent input, a static literal, or a top-level object from parent input. Child results are
frozen proposals. This prevents the derived path cost, Hide/surprise/social context, or cast
admission result from safely becoming another child's input. The composer—not any game feature—is
the single owner of a general solution.

## Dependency graph

~~~text
E6 typed dependent child composition                                [blocked parent]
├─ existing mechanic metadata, projection, deterministic child seed [implemented]
├─ closed dependency/binding declaration schema                      [accepted Slice 1]
├─ declaration validation: source child, result shape, acyclic DAG  [accepted Slice 1]
├─ topological child execution and deterministic seed ordering       [accepted Slice 1]
├─ frozen typed object handoff                                        [accepted Slice 1]
├─ root proposal aggregation and generic action proof                 [accepted Slice 2]
└─ consumer-specific adoption                                         [next: consumer plans]
~~~

## Ownership decisions

1. Binding is declaration-time structure, never code. A child names exactly one earlier child
   result key through `inputFromChildData`; it cannot select `narration`, effects, events,
   notifications, logs, role IDs, or an arbitrary path. The receiving child still validates its
   normal closed input itself.
2. Only a producer with exactly one invocation and an object-valued `output.data` can be bound in
   E6. A `forEachContentsOf` producer, scalar/array/null/malformed `data`, partial merge/pointer,
   string interpolation, and cross-parent reference all fail. A consumer needing any transformed
   value publishes/consumes a new closed context object instead.
3. The composer computes a stable topological order: dependencies first, then ordinal lexical
   result-key order among ready children. Existing parents with no dependency declarations retain
   their current lexical child order and derived seeds. A parent that adds a dependency records
   the final execution order in the frozen child-result envelope/audit.
4. Every child still executes against a frozen projection of committed state. Its effects join the
   outer proposal list only after all validated children complete; E6 does not make a prior child
   effect visible to a later child, and it does not use output effects/events as data.

## Slice order and stop gates

| Slice | Starts only when | Exit gate |
| --- | --- | --- |
| 1. Closed declaration and effect-free fixture | `procedure.system.modify`, metadata, composer, and public discovery contracts confirmed | **Accepted** — focused tests, catalog validation, and the 642-test suite pass. See [receipt](E6-SLICE-1-RECEIPT.md). |
| 2. Effect aggregation compatibility | Slice 1 verified | **Accepted** — ordered child proposals join the single root action; see [receipt](E6-SLICE-2-RECEIPT.md). |
| 3. Consumer adoption | Slice 2 and each consumer’s amended plan | One named consumer adopts a closed binding without copied calculation or caller-derived input. |

## Slice 1 specification

### Runtime artifacts

- Revision of the child-mechanic declaration model/schema and `MechanicComposer`, including a
  closed nested `inputFromChildData: { resultKey }` declaration.
- Revision of the governing mechanic-composition contract and discoverable capability description.
- Focused tests and two generic test mechanics whose results/effects are empty.

### Closed declaration and behavior

A dependent child declares exactly one new mutually exclusive input source:

~~~text
inputFromChildData: { resultKey: "<another child key>" }
~~~

`inputFromChildData` cannot appear with `inheritInput: true`, non-default `input`,
`inputFromParentProperty`, `inputForEachItem`, or a consumer `forEachContentsOf` declaration. Its
producer must be a sibling child with no `forEachContentsOf`; it must resolve before the consumer
in the declared directed acyclic graph. Result keys remain unique nonblank identifiers under the
existing requirements parser. The binding cannot name itself, an absent/later unresolved child, a
child in another mechanic, a result list, or any output field other than `data`.

Before any child executes, the composer parses every new declaration, rejects conflicting sources,
builds the sibling graph, detects all cycles, validates depth/child-count limits, and calculates
the stable topological order. At runtime it executes the producer once, parses only its
`MechanicOutput.Data`, rejects absent/invalid/non-object data, serializes a deep immutable copy,
and uses that complete object as the consumer's input. It never aliases the producer's object or
permits the consumer to change the parent-visible child result. A failure before/within any child
prevents the parent source from running and preserves the existing root transaction behavior.

Slice 1's two generic fixtures have zero effects, events, notifications, and random calls. The
producer returns `data: { "context": "fixture" }`; the consumer accepts exactly that object and
returns its own zero-effect confirmation data. This proves transport rather than a D&D-specific
calculation. Existing declarations retain byte-identical parsing, lexical invocation order, child
projection shape, result envelope, and seed derivation.

### Acceptance matrix

| Case | Assertion |
| --- | --- |
| Happy path | Producer returns exactly one object-valued `data`; consumer receives a byte-identical deep copy as its full input; parent receives both frozen results and zero effects. |
| Declaration validation | Missing/null/non-object/extra `inputFromChildData`; unknown/self producer; conflicting inherited/static/parent-property/foreach input source; producer/consumer foreach; duplicate key; unresolved dependency; and every graph cycle reject before random call or child execution. |
| Runtime data failure | Producer `data` absent, malformed, scalar, array, null, or over the existing serialized-output limit rejects before consumer/parent source; no proposal commits. |
| Isolation | Producer cannot mutate consumer input; consumer cannot mutate producer result; consumer sees no producer effects/events/logs/roles; virtual/uncommitted effects remain invisible. |
| Ordering and replay | Lexically ready independent children retain old ordinals/seeds; dependency order is topological with lexical tie break; identical graph/input/seed replays byte-identically. |
| Effects and rollback | A forced consumer failure or invalid proposed child effect aborts the complete root, preserves committed state byte-for-byte, and emits no partial success audit/event/notification. |
| Compatibility | Existing parent-child fixtures using inherited, static, parent-property, and foreach input remain byte-identical in selection, projection, result order, effects, and seeds. |
| Repository | Focused parser/composer/action tests, full suite, public-discovery guard where applicable, and diff check pass. |

### Exit gate

Stop after the generic fixture proves closed, acyclic, deterministic object handoff with no game
semantics or visibility of uncommitted effects. Do not migrate F20/F32/F34/F38 in this slice.

## Slice 2 specification — root proposal aggregation

### Boundary

Slice 2 makes already-executed child proposals part of the single top-level action proposal. It
does not apply a child proposal early, alter child projections, add a child-to-child effect input,
or migrate a game mechanic. The parent remains the only action that can commit.

### Ordered proposal contract

For every invocation, aggregate in depth-first execution order:

1. a child's recursively composed descendants;
2. that child's own `effects`, declared `events`, and declared `notifications` in source order;
3. subsequent sibling invocations in the resolved topological/lexical order; and
4. the top-level parent output last.

The runner dry-runs, guards, applies, audits, and returns precisely this one concatenated output.
Any child, parent, effect, event, notification, guard, cancellation, or apply failure rolls back
the entire transaction; no child proposal becomes visible to another child or is committed on its
own. `ctx.children` continues to expose each direct child's unmodified own output, not a live
proposal channel or a flattened descendant stream.

### Runtime artifacts

- A composition-level proposal envelope carrying only ordered effects, events, and notifications.
- Recursive composition returns that envelope with its frozen projection; child execution carries
  it upward without changing the child output captured in `ctx.children`.
- The top-level action runner merges the envelope with the parent output immediately before its
  existing dry-run/apply path, preserving the root narration, data, decision, logs, seed, and
  projection audit.

### Acceptance matrix

| Case | Assertion |
| --- | --- |
| Direct ordering | Two independent children, then parent, produce one exact effect/event/notification order matching lexical execution order. |
| Dependency ordering | A producer, its dependent consumer, and an independent ready child use the resolved topological order; only data travels between the dependent pair. |
| Recursive ordering | Grandchild proposals precede its child, which precedes the top-level parent. |
| Atomicity | An invalid child proposal, invalid child event/notification, or parent failure produces no world write, no structural event, and no partial success audit. |
| Compatibility | A parent with no child effects is byte-for-byte equivalent to the existing action output and receives the same seed/projection. |
| Isolation | A later child still reads only committed state and its declared input; it cannot observe an earlier child effect/event/notification. |

### Exit gate

Stop after generic action-runner tests prove aggregation, ordering, replay, and rollback. Run the
focused tests, catalog validation if a contract changes, the full suite, and `git diff --check`.
Do not add a consumer fixture or any game-specific rule in this slice.

## Plan-quality audit

- One generic data-flow capability, with no game vocabulary or code-evaluation escape hatch: yes.
- Existing child input, effect proposal, deterministic seed, and projection owners remain explicit:
  yes.
- Slice 1 has closed declarations, exact input source, ordering, failure, replay, rollback, and
  compatibility assertions plus a stop gate: yes.

## Plan-change rule

Split into a new platform feature if a consumer needs scalar transformation, arbitrary path
evaluation, conditional branching, dynamic child creation, or reading prior proposed effects.
