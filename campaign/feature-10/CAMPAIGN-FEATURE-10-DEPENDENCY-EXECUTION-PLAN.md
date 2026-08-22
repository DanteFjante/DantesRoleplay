# Campaign Feature 10 prerequisite execution plan — one new world plus campaign

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **R1–R5 are complete. R6 is the next and only implementation assignment.**
Last updated: 2026-08-21

## Purpose

Prepare the missing dependencies that let C10 create one fixed, hand-authored small world and one
campaign referencing it in one transaction. This plan does not author a new world, campaign,
procedure, schema, public operation, or runtime implementation.

The existing C10 plan remains authoritative for the final capability. This document orders the
dependencies, identifies one hidden composition seam, and gives Terra a bounded reading set for
each independent pass.

## Reconciled dependency state

| Dependency | Current state | Consequence |
| --- | --- | --- |
| C2 existing-world campaign bootstrap | Implemented and verified. | Its validation/root-effect behavior can be reused, but its public bootstrapper owns a transaction and audit, so it cannot be called as a C10 child. |
| C3/C4, Q2/Q3, S1–S3, W2, W4 | Implemented or accepted. | They are sufficient to attempt the required P1 played-session proof. |
| P1 played existing-world proof | Verified; see `CAMPAIGN-FEATURE-10-P1-RECEIPT.md`. | Satisfies C10's played-existing-world evidence gate. |
| World-owned small-world composer | Missing. | Must be planned and implemented by the World owner; C10 must not create world state itself. |
| C2 effect-free composition adapter | Missing. | Must return campaign-only validation/effects against a staged new world without a nested transaction or audit. |
| Cross-root authority | Unratified. | No C10 schema, public operation, coordinator, or fixture may be implemented until ratified. |

## Non-negotiable boundary

The world child owns world root, topology, faction, motives, facts, rumour, secret, clues, their
IDs, and world-only effects. The campaign child owns the campaign root, reference policy, and
campaign-only effects. One later outer coordinator owns the one transaction, event chain,
notifications, and operation audit. Neither child may call a transport handler, open/commit a
transaction, or record a separate audit when used by C10.

The only unavoidable review stop is R3: C10 already declares the coordinator, ID, fingerprint,
and failure/audit meanings to be a semantic confirmation boundary. Terra prepares the exact
decision packet but must not choose or implement those permanent meanings without ratification.

## Common Terra requirements

Before every slice, Terra reads `AGENTS.md`, this plan, the named owning plan, the current
`STATUS.md`, `SUBSYSTEM_IMPLEMENTATION_HANDOFF.md`, and the live governing procedure contract.
It searches the catalog and source for existing owners before proposing an ID, works in a disposable
database for fixtures/tests, never imports into the persistent database, and stops after the named
slice receipt. Each implementation pass receives a separate populated handoff; do not reuse a
handoff for the next row.

## Ordered slices

| Order | Assignment | Owner | Depends on | Exit gate |
| ---: | --- | --- | --- | --- |
| R1 | P1 played existing-world proof | Integration evidence | Existing verified contracts | **Verified:** one disposable end-to-end proof and receipt; no runtime feature change. |
| R2 | World small-world composer dependency plan | World | W1/W3/W4 and C10 boundary | **Completed:** [W17 composer plan](../../world/feature-17/WORLD-FEATURE-17-SMALL-WORLD-COMPOSER-PLAN.md) and its active handoff define the World child; no runtime artifact exists. |
| R3 | C10 cross-root composition ratification | Campaign + World review | R1 and R2 | **Ratified:** [cross-root record](CAMPAIGN-FEATURE-10-R3-CROSS-ROOT-RATIFICATION.md) freezes coordinator, child seams, namespaces, fingerprint, public-operation direction, and rollback/audit contract; no runtime artifact. |
| R4 | World effect-free composer | World | R2/R3 | **Verified:** W17 returns the fixed graph as deterministic World-only staged effects; see its [receipt](../../world/feature-17/WORLD-FEATURE-17-SLICE-1-RECEIPT.md). |
| R5 | C2 effect-free campaign composition adapter | Campaign | R3/R4 | **Verified:** Campaign-only C2-equivalent effects validate against W17's staged World; see the [receipt](CAMPAIGN-FEATURE-10-R5-COMPOSITION-SEAM-RECEIPT.md). |
| R6 | C10 immutable preview | C10 composition owner | R1/R3/R4/R5 | Repeated preview is deterministic and entirely read-only. |
| R7 | C10 atomic create | C10 composition owner | R6 | One reviewed world plus campaign commits once; all injected failures roll back everything. |

## R1 — record P1 played existing-world proof

### Exact outcome

Add a focused disposable integration test and short P1 receipt. It must prove stored-state
continuity, not narration or transcript recall, using only already accepted public/owner paths.
No production behavior, catalog record, permanent ID, or migration is allowed in this slice.

### Required reads

- `CAMPAIGN_CREATION_PLAN.md`: **First played-session acceptance scenario**.
- `campaign/feature-02/CAMPAIGN-FEATURE-02-DEPENDENCY-PLAN.md`,
  `campaign/feature-03/CAMPAIGN-FEATURE-03-DEPENDENCY-PLAN.md`, and
  `campaign/feature-04/CAMPAIGN-FEATURE-04-DEPENDENCY-PLAN.md`.
- World Feature 2 movement and the World Feature 4 receipts under `world/feature-04/`.
- `quest/feature-02/QUEST-FEATURE-02-DEPENDENCY-PLAN.md` and
  `quest/feature-03/QUEST-FEATURE-03-DEPENDENCY-PLAN.md`.
- `session/feature-01`, `session/feature-02`, and `session/feature-03` dependency plans and their
  validation receipts.
- `catalog/procedures/campaign/procedure.campaign.create.md`,
  `procedure.campaign.chapter.md`, `procedure.campaign.session.md`, and
  `catalog/procedures/game/core/world/procedure.game.core.world.knowledge.md`.

### Required proof sequence

1. Import the catalog into a disposable database and create the established existing-world
   campaign through C1 validation plus C2 creation.
2. Initialise C3 continuity; create/attach the established same-campaign quest through its owner
   and C4; start one session through S1.
3. With an existing fixture actor, perform one governed connected-location move, reveal one
   permitted clue, and accept or advance one quest objective through Q2.
4. End the session through S3 without changing world, quest, or chapter state outside their
   owning operations.
5. Dispose the context, open a fresh context over the same disposable database, and read the C3,
   Q3, and session resume projections. Assert the concrete next decision, movement result,
   revealed clue state, quest state/evidence, chapter status, and factual recap are reconstructed
   from stored records only.
6. Record a P1 receipt with the test name, exact state assertions, and repository checks. Do not
   begin C10 in this pass.

### Acceptance and stop

The test must cover normal flow, fresh-context readback, one invalid/replay call that leaves state
unchanged, and no reliance on a transcript. Run focused tests and the full suite once for this
evidence feature. Stop if any existing owner cannot support the sequence; report the smallest
missing owner instead of adding a C10 workaround.

## R2 — plan the World-owned small-world composer

### Exact outcome

Create a separate World feature plan and World handoff for a fixed composer. It validates one
closed local-key blueprint and returns only typed world results/effects for the C10 outer root.
It does not write, expose a public operation, begin a transaction, audit, create a campaign, or
select generated content. Do not assign a new World feature number or permanent catalog ID until
the World owner-map search confirms both.

### Required reads

- `campaign/feature-10/CAMPAIGN-FEATURE-10-DEPENDENCY-PLAN.md`: target, ownership decisions,
  missing leaves, and C10 Slice 0–2 boundaries.
- `WORLD_AND_LORE_PLAN.md` plus the complete W1, W3, and W4 dependency plans and receipts:
  `world/feature-01`, `world/feature-03`, and `world/feature-04`.
- `catalog/procedures/world/procedure.world.change.md`,
  `catalog/procedures/game/core/world/procedure.game.core.world.location.md`, and
  `catalog/procedures/game/core/world/procedure.game.core.world.knowledge.md`.
- `campaign/feature-02/CAMPAIGN-FEATURE-02-DEPENDENCY-PLAN.md` and
  `DantesRoleplay/Campaign/CampaignBlueprint.cs` only to understand the campaign child’s current
  reference validation; they do not grant World ownership.
- `EXECUTABLE_WORKFLOW_PLAN.md` and the relevant effect/event transaction contracts.

### Required plan content

The World plan must freeze the fixed C10 graph: one world root, one region, three locations and
canonical adjacency, one faction, two motives, one fact, one rumour, one secret, and three clues.
It must state closed authored input, local-key uniqueness, canonical local-key ordering,
deterministic namespaced ID derivation, cross-record scope/visibility rules, exact typed-effect
order, collision behavior, and zero-write validation. It must also declare missing/null/empty
behavior, malformed/cross-scope rejection, and all child results that C10 preview needs.

The plan’s first implementation slice must be the lowest independent World capability. It must
not include C10 preview/create, a public command, an audit, or a nested transaction.

### Acceptance and stop

Run the plan-quality audit from `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md`. Record the
owner search and leave the plan **awaiting semantic ratification**. Stop before runtime work.

## R3 — ratify C10 composition authority

### Exact outcome

Produce one decision record, then obtain ratification before code. It must select exactly one
outer coordinator and freeze both child interfaces. A recommended shape is a separately scoped
composition service, rather than making either the World or Campaign child own the other graph.

### Required reads

- The complete C10 dependency plan and R1/R2 receipts/plans.
- `campaign/feature-02/CAMPAIGN-FEATURE-02-DEPENDENCY-PLAN.md`,
  `DantesRoleplay.DataAccess/CampaignBootstrapper.cs`, and
  `DantesRoleplay/Campaign/CampaignBlueprint.cs`.
- The ratified World composer plan from R2 and its proposed result/effect contract.
- `DantesRoleplay.DataAccess/EffectApplier.cs`, `DantesRoleplay.DataAccess/ActionRunner.cs`,
  `catalog/procedures/world/procedure.world.change.md`, and the event/audit contracts they use.
- `EXECUTABLE_WORKFLOW_PLAN.md`.

### Ratification packet

The record must give one exact answer for each item: coordinator owner/class boundary; child
method inputs/results; ambient transaction ownership; single operation/audit and event/notification
correlation; immutable fingerprint canonicalisation; world/campaign ID namespaces; fixed blueprint
limits; create replay/staleness/collision behavior; and named failure-injection stages. It must
also confirm that the existing `campaign` commit kind is extended with closed operations rather
than introducing a new kind, or explicitly approve a different public surface.

### Acceptance and stop

No code, catalog artifact, migration, or fixture is changed. The decision record must prove that
each child can validate/materialise without nested commit and that all rejected paths leave no
success audit. Stop until this packet is ratified.

## R4 — implement the World effect-free composer

### Exact outcome

Implement only the first accepted World-composer slice from R2. Given a closed fixed blueprint,
it returns deterministic proposed world identity, local-key mapping, counts, validation problems,
and ordered world-only effects. It applies nothing and has zero event/notification/audit output.

### Required reads

- The ratified World composer plan and its active handoff.
- W1/W3/W4 plans, their catalog components/procedures, and the R3 ratification record.
- `DantesRoleplay.DataAccess/StagedWorldComposer.cs`, `EffectApplier.cs`, and existing C15/CH5
  effect-free planner patterns only as transaction-composition examples.

### Acceptance and stop

Test valid graph generation, exact counts/order, repeated identical output, invalid local keys,
duplicate keys, all malformed content/scope/visibility cases, ID collisions, and zero-write
before/after state. Validate catalog only if this slice changes catalog artifacts. Write its World
receipt and stop; do not implement C10 preview.

## R5 — implement the C2 effect-free campaign composition adapter

### Exact outcome

Add a narrow internal adapter to the existing C2 owner. It accepts the reviewed campaign portion
and the staged World result/view from R4, reuses C1/C2 validation semantics, and returns only the
campaign root, in-world reference, and campaign-reference effects. It must not call
`CampaignBootstrapper.CreateAsync`, start/commit a transaction, emit/audit independently, or add a
new public operation.

### Required reads

- `campaign/feature-01/CAMPAIGN-FEATURE-01-DEPENDENCY-PLAN.md` and
  `campaign/feature-02/CAMPAIGN-FEATURE-02-DEPENDENCY-PLAN.md`.
- `DantesRoleplay/Campaign/CampaignBlueprint.cs`,
  `DantesRoleplay.DataAccess/CampaignBootstrapper.cs`, and current C1/C2 tests.
- R3 ratification record and the accepted R4 output contract.
- `catalog/procedures/campaign/procedure.campaign.create.md`.

### Acceptance and stop

Prove the adapter returns C2-equivalent campaign effects against the staged valid world, rejects
stale/invalid/missing world evidence and campaign-ID collisions with no effects, and preserves the
existing public C2 validate/create behavior. Add no C10 command or coordinator. Record a C2
composition-seam receipt and stop.

## R6 — implement C10 immutable preview

### Exact outcome

Under the ratified C10 contract, add the one closed preview route. It invokes R4 and R5, combines
their typed results only, returns proposed IDs/local-key mapping/counts/visibility review/warnings
and one fingerprint, and writes nothing.

### Required reads

- Full C10 plan, R3 ratification record, R4/R5 receipts and active contracts.
- `catalog/procedures/campaign/procedure.campaign.create.md` and the exact public-surface files
  named by the R3 handoff.
- `DantesRoleplay.DataAccess/EffectApplier.cs` for validation boundaries only; preview must never
  invoke non-dry-run apply.

### Acceptance and stop

Prove byte-identical repeated preview, closed input, child error propagation, local-key/ID
conflicts, visibility rejection, no durable reservation, and exact no-write comparisons for
entities/components/relationships/events/notifications/operations. Run the protocol walk if R3
changes the public surface. Record the preview receipt and stop.

## R7 — implement C10 atomic create

### Exact outcome

Implement the ratified outer coordinator’s create route. It accepts only the exact previewed
blueprint/fingerprint, revalidates both children and collisions, applies world then campaign
effects within one ambient transaction, routes events/reactions, records exactly one root audit,
and commits once.

### Required reads

- Full C10 plan and every R1–R6 receipt.
- R3 ratification record, the coordinator handoff, child contracts, and effect/event/audit source
  files named in that handoff.
- Existing atomic root tests in C2 and World features for failure/rollback conventions.

### Acceptance and stop

Prove the exact fixed graph and campaign-to-world reference on success. Inject failure at world
root, topology, lore, campaign root, cross-reference, event, notification, and audit stages; every
case must leave neither graph, accepted event, notification, nor success audit. Prove stale
fingerprint, replay, and final-ID collision reject unchanged. Run focused tests, catalog
validation if applicable, full suite, protocol walk if public registration changes, and
`git diff --check`; record the final C10 receipt and stop.

## First Terra handoff

Create the first active handoff only for **R1 — P1 played existing-world proof**. Its allowed files
are the new focused integration test, the P1 receipt, and the named readiness/status references;
it may not modify world, campaign, quest, session, catalog, or MCP runtime code. Any missing public
path found during R1 is a blocker report, not permission to repair that dependency.
