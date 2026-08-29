# Trail Game TG2 dependency tree — canonical run domain

Status: **accepted 2026-08-25**; [final receipt](TG2-SLICE-3-RECEIPT.md)
Owner/roadmap: [Customizable trail-survival application roadmap](TRAIL-GAME-ROADMAP.md)
Parent tree: [Trail Game dependency plan](TRAIL-GAME-DEPENDENCY-PLAN.md), TG2
Ruleset alignment: **ruleset-neutral**
Source: **not applicable; original Trail Survival state contracts**
Confirmation: [TG2 run-domain contract](TG2-RUN-DOMAIN-CONFIRMATION.md)

## Outcome and non-goals

Define the smallest versioned, application-owned component schemas needed to represent a selected
scenario, run lifecycle, elapsed time, route progress, party, members, conveyance, resources,
travel policy, unresolved choice, and terminal outcome in an isolated Trail Survival state space.

TG2 defines state shape and authority only. It does not create a playable scenario, state fixture,
simulation mechanic, calculation, transition, seed algorithm, event draw, action, projection, UI,
public route, migration, or live installation.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| Application/source revision | application kernel | verified | [TG1 receipt](TG1-SLICE-3-RECEIPT.md). |
| Component type identity/version | `ecs` | verified | `ComponentTypeIdentifier` and `SqliteComponentTypeRegistry`. |
| Bounded JSON Schema | `schema-validation` | verified | Draft 2020-12 bounded v2 compiler/value validator. |
| Authenticated registration/replay | `component-type-administration` | verified | Existing dry-run/commit/replay service and protocol kind. |
| State-space/entity/component persistence | application-scoped `ecs` | verified | Exact type reference, schema validation, revision, and isolation tests. |
| Authored component metadata/sidecar format | `catalog` | verified | Existing `ComponentDefinitionFile` metadata plus sibling `.schema.json`. |
| Run-domain meaning | `trail-survival` application | ready | Confirmed in the TG2 contract; no existing Trail owner or ID conflicts found. |

## Dependency tree

```text
TG2 canonical run domain                                                  [accepted]
├─ TG2.1 Run spine                                                        [accepted]
│  ├─ governing run-state procedure                                      [ready]
│  ├─ scenario-pin schema                                                 [ready]
│  ├─ run lifecycle schema                                                [ready]
│  ├─ elapsed clock schema                                                [ready]
│  └─ route-progress schema                                               [ready]
├─ TG2.2 Party and inventory                                              [accepted; depends TG2.1]
│  ├─ party schema                                                        [ready]
│  ├─ member schema                                                       [ready]
│  ├─ conveyance schema                                                   [ready]
│  └─ resources schema                                                    [ready]
└─ TG2.3 Decision/terminal state and acceptance                           [accepted; depends TG2.2]
   ├─ policy schema                                                       [ready]
   ├─ pending-choice schema                                               [ready]
   ├─ outcome schema                                                      [ready]
   └─ complete registration/ECS/isolation acceptance                     [verified]
```

## Conflicts and decisions

- Components store canonical facts only. Derived day labels, capacity use, route totals, health
  summaries, available commands, and progress percentages are not stored.
- Catalog identifiers are opaque strings pinned by state; authored scenario/route/resource/event
  records arrive in TG4 and are not embedded in these schemas.
- Entity IDs referenced by party membership and conveyance assignment are canonical links inside
  the run aggregate for v1. Generic containment/relationship edges are not duplicated in TG2.
- Absence of `pending-choice` means no unresolved choice; absence of `outcome` means non-terminal.
  Those components do not carry an additional active/empty sentinel.
- ECS component revision is host-owned. No component schema duplicates that revision.
- TG3 mechanics will enforce cross-component invariants and transitions. TG2 schemas enforce only
  closed shape, primitive bounds, enums, and local collection bounds.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 1 | [TG2 Slice 1](TG2-SLICE-1-IMPLEMENTATION.md) | TG1 + confirmation | **Accepted**; [receipt](TG2-SLICE-1-RECEIPT.md). |
| 2 | [TG2 Slice 2 — party and inventory](TG2-SLICE-2-IMPLEMENTATION.md) | Slice 1 receipt | **Accepted**; [receipt](TG2-SLICE-2-RECEIPT.md). |
| 3 | [TG2 Slice 3 — decision state and acceptance](TG2-SLICE-3-IMPLEMENTATION.md) | Slice 2 receipt | **Accepted**; [receipt](TG2-SLICE-3-RECEIPT.md). |

## Completion state

TG2 has no remaining leaf. The three receipts preserve all eleven schemas, governing procedure,
registration/replay, ECS round-trip, invalid/no-change, and application-isolation evidence. TG3
planning is the next roadmap boundary but is not active under this plan.

## Confirmation gates

- The permanent IDs and meanings are confirmed by
  [TG2-RUN-DOMAIN-CONFIRMATION.md](TG2-RUN-DOMAIN-CONFIRMATION.md) under the user's instruction to
  finish TG2.
- No runtime calculation or transition is confirmed here; those remain TG3 gates.
- Completed TG2 acceptance may use tests asserting the complete bounded invariant.

## Planning receipt

- Delivered runtime artifacts: one governing procedure and eleven component metadata/schema pairs;
  see the three receipts.
- Migration/public surface/live state: none.
- Active implementation owner: none; TG2 is complete.
- Exact stop observed: no TG3 mechanic, procedure/action ID, seed contract, or fixture began.
