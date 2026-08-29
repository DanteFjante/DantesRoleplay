# Campaign Feature 10 dependency plan — compose a new world

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; C2, P1, the R3 boundary, W17 composer, and C2 composition seam are verified.
Blocked by the C10 preview and atomic create slices.**
Last updated: 2026-08-20

Execution order for the missing leaves and Terra reading sets:
[C10 prerequisite execution plan](CAMPAIGN-FEATURE-10-DEPENDENCY-EXECUTION-PLAN.md).

## Target capability

A host can review one fixed, hand-authored small-world campaign blueprint and, after approval, create
the new world and its campaign together as one atomic unit. The campaign references the newly
created world; neither side is left behind if validation, an effect, event routing, or audit writing
fails.

This is the second campaign-creation path. It is not a shortcut around the existing-world campaign
path, a procedural world generator, or permission for campaign creation to directly own world state.

### Included

- One fixed small-world blueprint shape: a world root, one region, three locations, canonical
  adjacency, one faction, two recurring motives, one fact, one rumour, one secret, and three clues.
- A review-only preview that reports resolved local keys, proposed permanent IDs, world/campaign
  creation counts, reference/visibility checks, and all blocking problems without writing state.
- One approved outer transaction that creates the validated world graph and campaign root together.
- One complete root audit/outcome and full rollback proof, including failures at each creation stage.

### Excluded

- AI-generated or free-form world content, iterative editing, partial creation, import from another
  world, cloning, campaign forks, campaign-to-quest links, characters, items, maps, travel,
  procedural/random generation, player authorization, website creation, cross-database work, or
  nested workflow execution.
- Rewriting the existing world, sharing its world-owned entity IDs, copying campaign state into a
  world component, or allowing either owner to independently commit inside the outer transaction.

## Source and contract basis

No D&D rule determines how an authored campaign and setting are created. This is an application
composition boundary.

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Campaign roadmap | Campaign creation plan, C10 row and cross-plan creation boundaries | C10 creates one new world plus campaign only after existing-world evidence, through one outer owner. |
| Campaign foundation | Campaign Slices 1–3 | C2 is the future validated campaign-root bootstrap owner; C10 may not bypass its final contract. |
| World ownership | World Feature 1 plan and procedure.game.core.world.location | World roots, locations, containment, adjacency, and world identifiers remain world-owned. |
| World lore ownership | World Features 3–4 plans and their procedures | Factions, motives, facts, rumours, secrets, and clues remain world-owned and use explicit scope/reference links. |
| Atomic structural writes | procedure.world.change and action/event transaction evidence | An all-or-nothing root must leave no entities, components, links, events, notifications, or success audit after failure. |
| Workflow boundary | Executable workflow plan | A workflow may later supply an ambient transaction runner, but it is draft and cannot be assumed as C10 infrastructure. |
| Played-evidence rule | Campaign creation plan, Recommended order | C10 waits until an existing-world campaign/session path has been proven before adding cross-owner risk. |

Repository searches found no campaign new-world composer, world blueprint composer, cross-root
transaction service, campaign component/procedure, or C10 fixture owner. Those are missing
dependencies, not permission to add a second owner.

## Dependency and ownership decisions

1. The world owner validates and materialises world-only blueprint content. It owns world-root,
   topology, faction, motive, and knowledge identifiers and their typed structural effects.
2. The campaign owner validates and materialises campaign-only content, including the reference to
   the world root supplied by the world composer. It must use the verified C2 contract, not invent
   a parallel campaign root.
3. Exactly one ratified outer coordinator owns the transaction, root audit, event chain, and final
   result. The world and campaign composers are called capabilities that return typed validation
   results/effects; neither opens or commits a nested transaction.
4. Local blueprint keys are transient review inputs. Permanent entity IDs are derived/allocated only
   by the approved composer after the preview validates them; callers cannot submit arbitrary raw
   effect lists.
5. Preview is read-only. An approved create uses the reviewed immutable fingerprint and rejects a
   changed, stale, or mismatched blueprint rather than silently recomputing a different result.
6. A world generated for C10 belongs exclusively to the new campaign at this first boundary.
   Sharing, copying, forking, or importing a world is deliberately later work.

## Missing leaves and recursive dependency analysis

~~~text
Campaign Feature 10: create one reviewed new world plus campaign
├─ W1 topology and W3/W4 lore contracts                               [implemented and verified]
├─ C2 validated existing-world campaign bootstrap                      [implemented and verified]
├─ P1 played existing-world campaign/session evidence                  [verified; P1 receipt]
├─ world-only small-world blueprint composer                           [missing world-owned leaf]
│  └─ separate world-feature plan: validation and typed effects, no independent commit
├─ cross-root transaction authority and review fingerprint             [missing semantic leaf]
│  └─ C10 Slice 0: ratify one outer coordinator and failure/audit boundary
└─ C10 preview/create composition                                      [blocked parent]
   ├─ Slice 1: immutable preview over both validated child results
   └─ Slice 2: one atomic create through the approved coordinator

Forks, imports, generated content, quests, characters, and website wizard [excluded]
~~~

The world-composer leaf must be planned by the world owner before any C10 implementation
assignment. It is not folded into a campaign feature merely because C10 consumes it.

## Cross-root confirmation boundary

Before implementation, ratify all of the following together:

- the single outer coordinator: campaign-owned, world-owned, or a separately approved composition
  owner;
- how each child composer receives the ambient transaction and returns typed effects/results;
- one root audit and event/notification correlation policy;
- immutable preview fingerprint format and stale-preview rejection;
- permanent-ID allocation/namespacing policy for both new graphs;
- the fixed small-world blueprint content limits and exact party/GM visibility review; and
- the failure-injection stages that prove no partial world or campaign state remains.

No permanent C10 IDs, public commit kind, C# game-specific helper, workflow definition, schema, or
fixture is proposed until this boundary is confirmed. If the eventual C2 or world-owned composer
contract answers any item differently, this plan must be revised before assigning a slice.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Cross-root creation ratification | C2 and the world-owned composer design are available for comparison; existing-world campaign evidence is verified. | One outer coordinator and complete review/failure contract are approved; no runtime artifact is written. |
| 1 | Read-only new-world campaign preview | C2, P1, the world-owned composer, and Slice 0 are verified. | The fixed blueprint returns stable resolved local keys, proposed IDs, counts, warnings, fingerprint, and all errors with zero writes. |
| 2 | Atomic new-world campaign create | Slice 1 is verified. | The exact fingerprinted preview creates the whole new world/campaign graph in one transaction; every injected failure rolls back both graphs and success evidence. |

## Slice 0 — ratify composition authority

This is the lowest C10 step, but it is blocked until C2 and the world-owned composer have their own
reviewed designs. It is an architecture decision, not an implementation pass.

The ratification record must state:

- which owner begins/commits/rolls back the outer transaction;
- the called world and campaign validation/materialisation interfaces;
- whether event/notification handling occurs before the outer commit and how rollback erases it;
- where the single root operation/failure audit is recorded;
- deterministic ID allocation and collision checks; and
- exact recovery behavior for invalid, stale, duplicate, or failed preview/create requests.

**Exit gate:** the decision satisfies the campaign plan's cross-plan boundary: called capabilities
validate their own inputs and return typed results/effects, while neither independently commits.

## Slice 1 — deterministic preview

The preview accepts a closed, fixed small-world campaign blueprint. It may include only declared
local keys and authored descriptive content; it cannot include SQL, JavaScript, raw effects,
permanent IDs, arbitrary event filters, or generated prose.

It first calls both validated child composers, then combines only their typed results into:

- proposed world/campaign identity summaries;
- local-key-to-proposed-ID mapping in canonical order;
- exact entity/component/relationship creation counts by owner;
- resolved cross-root reference from campaign to proposed world root;
- party/GM visibility review and warnings;
- a canonical immutable fingerprint; and
- complete named validation failures.

The preview has zero effects, no events, no notifications, no audit success record, and no durable
reservation. Concurrent equivalent previews are allowed; create performs fresh collision/staleness
checks.

### Slice 1 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Valid fixed blueprint | Stable proposed IDs, counts, world/campaign reference, visibility review, and same fingerprint across repeated calls. |
| Bad world content | Invalid topology, faction, motive, or knowledge graph is reported by the world composer; no campaign preview is treated as valid. |
| Bad campaign content | Invalid campaign fields or reference policy is reported by the campaign composer; no world state is written. |
| Cross-owner conflict | Duplicate local keys, proposed ID collision, wrong world reference, or visibility leak reports named errors and no fingerprint. |
| Closed input | Missing, null, duplicate, unknown, raw-effect, permanent-ID, generated-content, or wrong-type fields fail before any write. |
| No-write proof | Entities, components, links, events, notifications, operations, and catalog data are byte-identical before/after preview. |

## Slice 2 — atomic composition create

Create accepts only the exact approved preview fingerprint and validated closed blueprint. The outer
coordinator revalidates the fingerprint, allocates/checks permanent IDs, invokes world and campaign
materialisation under one ambient transaction, routes structural events/reactions inside that
transaction, and commits once.

Success returns the new campaign root, new world root, their explicit reference, creation counts,
one root operation/audit correlation, and a bounded initial campaign summary. It does not create a
quest, character, item, travel state, or website session.

### Slice 2 acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Happy path | One root transaction creates the reviewed fixed world graph and one campaign referencing its new world root. |
| Fingerprint/staleness | Missing, changed, expired, or mismatched preview fingerprint rejects with zero writes. |
| Failure injection | Failure at world root, topology, lore, campaign root, cross-reference, event, notification, or audit stage leaves neither graph nor success evidence. |
| Collision/replay | A duplicate final ID or repeated create request rejects; it cannot attach a second campaign or partially recreate the world. |
| Scope/visibility | Campaign references only its new active world; party summary does not expose GM-only world knowledge. |
| Readback | New roots and owned records reconstruct through ordinary world/campaign inspection, with no copied world state in campaign fields. |
| Repository acceptance | Focused tests, catalog validation where catalog content changes, full suite, required protocol walk if public surface changes, and git diff --check pass. |

## Completion boundary

C10 completes only after a played existing-world campaign has proven the base model and the reviewed
fixed blueprint can create one new world plus campaign atomically. Stop before cloning/forking,
template or AI generation, maps, quests, items, characters, and wizard UI.
