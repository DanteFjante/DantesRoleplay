# World Feature 6 — Agenda-triggered clue reveal: dependency plan

**Status:** Feature 6 verified  
**Scope:** one fixture-bound reaction: an accepted advance of the Feature 3 faction agenda reveals the designated Feature 4 clue.  
**Out of scope:** autonomous world simulation, quest milestones, timers, new event types, new schemas, migrations, notification behavior, and generic multi-clue rules.

## Intended outcome

The existing Feature 3 fixture faction begins with an agenda in `ready` state. When the verified
Feature 3 agenda action commits its sole `ready → advanced` transition, the existing
`world.component.replaced` event activates one subscription. Its reaction changes the fixed
Feature 4 fixture clue `clue.feature-04.oren-letter` from `unrevealed/gm` to `revealed/party`.

The reaction is deliberately narrow:

- It owns neither the faction agenda nor generic clue-reveal policy.
- It adds no semantic event: the existing structural component-replacement event carries the
  required before/after evidence.
- It is not a scheduler or background simulation. A committed source action is the only trigger.
- It does not expose or alter facts, rumours, secrets, relationships, time, quests, campaigns, or
  notifications.
- A nonmatching event, an already-advanced agenda, or an already-revealed clue produces no
  additional change.

The following permanent public IDs and fixed fixture target are confirmed:

- `procedure.game.core.world.reactive`
- `mechanic.game.core.world.clue.reveal-on-faction-agenda`
- `subscription.game.core.world.clue.reveal-on-faction-agenda`
- `clue.feature-04.oren-letter`

## Existing owners and constraints

| Concern | Existing owner / contract | Feature 6 use |
| --- | --- | --- |
| Faction agenda transition | `mechanic.game.core.world.faction.agenda` and `game.core.world.faction` | Source action and authoritative `agenda.state` before/after values. |
| Clue reveal state | Feature 4 knowledge/clue contract | Fixed target; closed clue states and visibility transition are validated before writing. |
| Structural change event | `world.component.replaced` | Existing trigger event; payload includes `entityId`, `definitionId`, `before`, and `after`. |
| Event reactions | `procedure.event.react` | Reaction reads only accepted event context and proposes effects inside the parent root transaction. |
| Subscription routing | `procedure.subscription.create`, `SubscriptionStore`, and event-chain limits | Fixed role binding, tracked faction filter, scalar payload filters, one execution per chain. |
| Catalog import | `CatalogImporter` and catalog validation | Must materialise catalog fixtures before subscription fixed-role validation. |
| World modelling | `procedure.world.model`, `procedure.world.change`, `procedure.world.naming` | Component writes remain explicit, validated, and emitted through normal structural event behavior. |

No new world component, relationship, event type, migration, or MCP surface is required. The
catalog importer may need a minimal generic ordering/materialisation correction; that is a
prerequisite repair, not a new Feature 6 runtime contract.

## Discovered prerequisite — fresh catalog fixture binding

The Feature 6 subscription is correctly required to bind its target through
`fixedRoleEntityIdsJson`. On a fresh catalog import, adding that subscription currently fails with
`Missing entities: "clue.feature-04.oren-letter"`; `roleplay validate catalog` fails at the same
point. The failure occurs while registering the subscription, before the reaction mechanic or
agenda action can execute.

This is a generic catalog import/validation defect exposed by Feature 6, not permission to weaken
the subscription contract. Slice 0 must diagnose and repair the importer path using a minimal
production change. It must not:

- omit `fixedRoleEntityIdsJson`, hard-code the clue in reaction JavaScript, or convert the fixed
  role to an unbound/dynamic lookup;
- relax `SubscriptionStore` validation so a missing fixed entity is accepted;
- introduce a database migration, persistent import, new event type, or world-specific importer
  branch;
- rely on a test-only pre-seed or an undocumented manual ordering step.

The repair belongs to catalog-import planning/materialisation and must preserve the existing live
subscription contract: at subscription registration, every fixed role entity must already exist in
the import transaction and remain visible to the same validation query. The implementation may
adjust the importer’s dependency plan, staging, or save boundary only after a focused regression
test establishes which of those conditions fails.

## Slice order and implementation handoffs

### Slice 0 — catalog fixture-bound subscription import

**Purpose:** make a repository-authoritative fresh catalog import able to register a valid
subscription whose `fixedRoleEntityIdsJson` references a catalog fixture entity.

**Authoritative inputs:** the existing full catalog; an active subscription with one declared fixed
role; a catalog-owned fixed entity that satisfies the role requirement.

**Runtime ownership:** the minimal generic behavior is owned by `CatalogImporter` and the existing
subscription/entity stores. Feature 6 owns only the regression fixture and its catalog artifacts;
it does not acquire a separate importer API or a bypass.

**Implementation sequence:**

1. Add a focused fresh-import regression in the appropriate catalog/import test owner. It must use
   the production importer path, a new disposable database, and no manual entity writes.
2. Assert the prerequisite facts before changing production code: the fixed clue catalog entity is
   present, the event type/mechanic/subscription definitions are valid, and the import fails only
   during subscription fixed-role registration.
3. Diagnose whether the importer has failed to order, materialise, save, or expose the entity to
   the same transaction/query used by `SubscriptionStore`. Do not choose a repair based on a
   speculative ordering change alone.
4. Make the smallest generic importer/store integration repair supported by that test. Preserve
   transactional import behavior and existing catalog ordering semantics for every unrelated
   catalog kind.
5. Keep the Feature 6 subscription fixed-role binding exact: role `clue` resolves to
   `clue.feature-04.oren-letter`; no fallback target is allowed.

**Slice 0 acceptance matrix:**

| Case | Required evidence |
| --- | --- |
| Fresh full catalog import | Completes with the Feature 6 subscription present and active; its fixed role resolves to `clue.feature-04.oren-letter`. |
| Catalog validation | `roleplay validate catalog` succeeds against a disposable database with no persistent import. |
| Missing fixed entity | A deliberately invalid fixture reference fails clearly and atomically; no partial subscription remains. |
| Existing catalog compatibility | Existing catalog import tests continue to pass; the change does not reorder or mutate unrelated entities beyond the importer’s established contract. |
| Transaction visibility | The registration path can see the already-materialised fixed entity without test-only pre-seeding or a separate committed import. |

**Exit gate:** focused importer regression and existing catalog-import coverage pass;
`roleplay validate catalog` passes; a query-back proves the active subscription retains the exact
fixed role binding. Record a Slice 0 receipt with the failure diagnosis, minimal changed owner,
fresh-import evidence, negative rollback case, and validation command. Stop before reaction
behavior work.

### Slice 1 — bounded agenda-to-clue reaction catalog artifacts

**Prerequisites:** Slice 0 receipt; verified Feature 3 agenda receipt; verified Feature 4 clue
receipt; all confirmed IDs above.

1. Define `procedure.game.core.world.reactive` with the purpose, invariants, deterministic
   behavior, and failure contract for this single reaction. Reactions read only accepted event
   context inside the parent root transaction and cannot forge a `world.*` event.
2. Add `mechanic.game.core.world.clue.reveal-on-faction-agenda` in category
   `game.core.world.reactive`, mode `reaction`, active. It requires
   `world.component.replaced` and one fixed `clue` role of type `game.core.world.clue`.
3. Add `subscription.game.core.world.clue.reveal-on-faction-agenda`: active, routed to that
   mechanic, subscribed to `world.component.replaced`, `order: 0`, and
   `maxExecutionsPerChain: 1`.
4. Bind `clue.feature-04.oren-letter` only through `fixedRoleEntityIdsJson`. Track only
   `faction.feature-03.fixture`, with scalar payload filters
   `entityId: faction.feature-03.fixture` and `definitionId: game.core.world.faction`.
5. Re-run the Slice 0 importer proof with the actual Feature 6 catalog artifacts. Catalog validity
   is the exit gate for this slice; do not test gameplay behavior until it passes.

### Slice 2 — reaction behavior and accepted-action proof

**Prerequisites:** Slice 1 catalog proof.

The reaction reads `ctx.event.payload.before` and `after` and accepts only an exact
`agenda.state: ready → advanced` replacement for `faction.feature-03.fixture` with
`game.core.world.faction`. It validates the fixed clue’s closed state. If the clue is
`unrevealed/gm`, it returns exactly one complete `component.set` changing it to
`revealed/party`, preserving all other clue fields. An ordinary nonmatch or an already revealed
clue returns no effects. Malformed source data or an invalid fixed clue fails the parent root
transaction rather than widening or guessing semantics.

Add focused tests that execute the verified agenda action and query the committed root result:

| Case | Expected result |
| --- | --- |
| Happy path | Faction becomes advanced; exactly `clue.feature-04.oren-letter` becomes `revealed/party`; one reaction execution and the normal derived clue component event are committed. |
| Source rerun | The agenda action fails or makes no second agenda transition; no second clue change occurs. |
| Wrong component/event entity | Subscription does not route; clue remains `unrevealed/gm`. |
| Wrong agenda transition | Routed reaction returns no effects; clue remains unchanged. |
| Clue already revealed | Reaction returns no effects; no duplicate clue replacement is emitted. |
| Invalid fixed clue | Reaction failure rolls back the entire root transaction, including the source agenda advance. |
| Chain safety | Happy path has one reaction execution and terminates; the derived clue event does not re-enter the subscription. |

## Completion boundary

All three slices are implemented and verified in the
[implementation receipt](WORLD-FEATURE-06-IMPLEMENTATION-RECEIPT.md). Feature acceptance remains
an explicit review boundary. Generalized reaction authoring, several factions or clues, dynamic
subscriptions, quest reactions, and scheduled world progression remain later work.
