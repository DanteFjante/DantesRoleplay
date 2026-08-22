# E7 dependency plan — atomic staged composition and virtual projections

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; Slice 1 is the next and only authorised implementation pass after E6.**
Last updated: 2026-08-21

## Execution rule

This is planning only. A future pass re-reads `procedure.system.modify`, world-effect validation,
projection, action/audit, and E6 contracts before changing the kernel. It implements one accepted
slice with focused fault-injection and replay tests, then the full suite and diff check, records a
receipt, and stops. It does not create a character, monster, public action, or catalog-owned game
workflow while proving the internal overlay.

## Target capability

A declared root coordinator can validate an ordered set of source-specific child proposals against
one isolated virtual effect overlay—including a reserved newly created entity—and commit the whole
accepted bundle once or leave the world unchanged.

### Included

- A bounded staged-composition model for explicitly declared child mechanics, reserved entity
  roles, virtual projection after prior validated proposals, complete rollback, audit/replay, and
  a generic create-then-read fixture.
- Consumer migration for CH5 character creation and Feature 35 monster bootstrap only after the
  generic kernel behavior is verified.

### Excluded

- Arbitrary workflows, loops, dynamic child discovery, background jobs, partial commits,
  compensating transactions, user drafts, a new public action kind, game-state ownership, or an
  unrestricted “run mechanics until successful” engine.

## Existing evidence and owner decision

Effects are intentionally only proposed until the root completes; regular child projections can
therefore see only committed state. This protects atomicity but makes it impossible to create an
actor and ask its existing writers/readers to validate the actor in the same root. `MechanicComposer`
and the effect simulation/projection boundary jointly own this generic concern. CH5 and F35 must
not add separate creation transactions or half-built actors.

## Dependency graph

~~~text
E7 staged composition and virtual projections                         [blocked parent]
├─ atomic validated effect application / audit / replay               [implemented]
├─ E6 closed dependent object handoff                                  [required]
├─ reserved entity-role declaration                                    [missing Slice 1 leaf]
├─ virtual validated-effect overlay and projection                     [blocked: Slice 1]
├─ staged child execution / effect ordering                            [blocked: Slice 1]
├─ generic create-then-read validation fixture                         [blocked: Slice 1]
└─ CH5/F35 consumer roots                                               [blocked: E7 + their owners]
~~~

## Ownership decisions

1. A virtual entity is a root-local reservation, not an entity committed early. Its permanent ID
   is validated once against live and soft-deleted IDs and is visible only through one declared
   reserved role in that root overlay. It cannot be named by caller input after reservation or
   reused by a sibling/root/concurrent action.
2. Every stage sees committed state plus a purely in-memory overlay of preceding **validated**
   effects. The overlay is not a database context, cannot query arbitrary records, and materialises
   only the components/containment/relationships a stage already declares. Unrelated roles still
   use the ordinary frozen projection resolver.
3. Each stage returns the same structural effects it would normally propose. Before the next stage
   sees them, E7 validates their shape/references against the overlay and applies only their
   projection-relevant form to a fresh immutable overlay snapshot. The final root sends one
   unchanged ordered effect list through the existing `EffectApplier` dry-run/apply path.
4. Slice 1 supports only `entity.create` for the reserved entity and `component.add` to that
   entity. Every other effect type, a non-reserved create, a component replacement/merge/remove,
   containment, or relationship is rejected as unsupported virtual state. Later support is a
   separately accepted slice, never an optimistic invisible overlay.
5. E7 composes structure; it never decides a character/monster’s source, HP, inventory, campaign,
   authorization, or readiness. Those remain consumer plans and writers.

## Slice order and stop gates

| Slice | Starts only when | Exit gate |
| --- | --- | --- |
| 1. Internal overlay proof | E6 verified and core effect/projection contracts confirmed | Generic root reserves one entity, stage one creates/adds closed test state, stage two reads it virtually, and final commit is atomic. |
| 2. Declared staged-root contract | Slice 1 verified | Closed catalog declaration exposes eligible stage/role forms, static reserved target context, and rejects cycles/undeclared virtual access. |
| 3. Additional effect vocabulary | Slice 2 and per-effect overlay rules confirmed | Required normal writer effects are made virtually visible one type at a time, with ordinary EffectApplier equivalence evidence. |
| 4. CH5/F35 adoption | Slice 3 and each consumer’s source/state owners verified | One consumer fully creates its actor or nothing commits. |

## Slice 1 specification

The initial slice is an internal, non-game proof only. It adds no catalog declaration form for
authors and no player-visible route. It introduces a root-local `VirtualEffectOverlay` and a
projection adapter with exactly these inputs: the committed base projection, one reserved entity
ID/name, and the preceding validated virtual effects. The adapter must be pure and serializable:
given equal inputs it returns byte-identical projections and has no database write capability.

The fixture has two hard-coded internal stages. Stage one proposes exactly (in order)
`entity.create(reserved-id, reserved-name)` and one `component.add` for a closed test component.
The overlay validates both against the same permanent-ID/component-definition rules the normal
effect path uses, then exposes a new entity projection containing only its ID/name and that
declared test component. Stage two requests that one component through the reserved role and
returns zero effects after proving its exact data. The outer root finally forwards stage one's
unchanged two effects through the existing dry-run then apply transaction.

The slice must prove all of the following before any stage two execution: invalid/reserved/taken/
soft-deleted ID; missing/wrong component definition; duplicate create/add; wrong effect order;
create of a non-reserved ID; any unsupported effect type; a role not declared virtual; component
not declared in the stage requirement; malformed effect data; overlay/base-projection mismatch;
and an attempt by ordinary roles to read virtual state. The no-write proof observes that no entity,
component, relationship, containment row, event, receipt, or success audit exists before the
outer final apply.

Fault injection must cover stage-one validation, overlay materialisation, stage-two projection,
stage-two mechanic failure, final dry-run, final apply, guard, event, receipt, and transaction
commit. Every failure leaves no reserved entity or component in the database. A success records
the ordinary one-root audit/effect receipts only; it creates no special virtual-state history.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Happy path | Stage two receives exactly the newly created reserved entity projection and one declared component; final apply creates exactly the two stage-one effects in declared order. |
| Isolation | No ordinary role can name/read the virtual entity; stage two sees no undeclared component/relationship/contents or raw preceding effects; the overlay reads/writes no database row. |
| Closed virtual vocabulary | Any effect outside `entity.create(reserved)` then `component.add(reserved)` rejects before stage two; duplicate/colliding/deleted IDs and unknown definitions use the same rejection class as ordinary effects. |
| Projection fidelity | Stage-two missing/extra component requirements and malformed test data fail exactly as normal projection/component validation would; committed base roles remain byte-identical. |
| Atomicity | Each injected stage/dry-run/apply/guard/event/audit failure leaves no entity/component/receipt/event/notification and rolls back the root. |
| Replay | Same root seed, reservation, base state, and stage output produces byte-identical stage projections, final effect order, audit seed, and result. |
| Compatibility | Existing non-staged ActionRunner/composer/effect tests remain byte-identical; no public catalog syntax, action kind, or game fixture is added. |
| Repository | Focused overlay/projection/effect/runner tests, full suite, architecture/contract update review, and diff check pass. |

### Exit gate

Stop after virtual visibility and atomic rollback are proven under tests. Do not implement CH5,
F35, a public staged declaration, or any game content in Slice 1.

## Plan-quality audit

- Virtual state is root-local, typed by declared roles, and cannot become a second database or
  partial-commit path: yes.
- Effects, projections, audit, and actor source/state semantics retain distinct owners: yes.
- Slice 1 specifies closed virtual vocabulary, injected-failure, collision, projection fidelity,
  isolation, order, replay, compatibility, and stop evidence: yes.

## Plan-change rule

Split the work if virtual projection requires arbitrary database querying, cross-root visibility,
long-lived reservations, partial commits, or an author-defined execution loop.
