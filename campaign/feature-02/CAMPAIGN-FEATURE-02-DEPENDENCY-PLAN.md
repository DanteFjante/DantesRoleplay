# Campaign Feature 2 dependency plan — atomically bootstrap an existing-world campaign

Status: **Implemented and verified in a disposable imported world; no persistent catalog import performed.**
Last updated: 2026-08-20

## Execution rule

C2 is repository-mode work governed by AGENTS.md, procedure.system.create-feature,
procedure.system.modify, procedure.world.change, and procedure.mcp.add-tool. Catalog edits use
`roleplay validate catalog` against a disposable database; persistent import is only for an approved
integration/release boundary.

The semantic runner—not the caller or MCP handler—owns C1 revalidation, the generated effect list,
one outer transaction, structural-event routing, and the success audit. It may call the existing
effect applier inside that ambient transaction. It must never nest a commit or split bootstrap
across independently committed calls.

## Target capability

After C1 has returned a valid review, a host can submit the exact closed CampaignBlueprint and its
matching review fingerprint to create one active campaign root that references one existing active
world. The request commits all campaign records, structural evidence, and success audit together,
or commits none of them. It never creates, copies, or changes world content.

## Boundary

### Included

- One closed `campaign/create` operation on C1's approved campaign commit kind.
- Same-transaction revalidation of the full blueprint and supplied fingerprint.
- One campaign entity/root component, one in-world relationship, and C1's canonical reference
  relationships only.
- Internal dry-run of the exact derived effects; structural events, guards, subscriptions and
  notifications within the same transaction; concise result; and fresh readback.
- Collision, stale-review, cancellation, injected-failure, rollback, and concurrent-create tests.

### Excluded

- World creation/copying/mutation; locations, containment, factions, motives, knowledge, clues,
  rumours, clock, travel, history, characters, items, quests, chapters, arcs, sessions, AI,
  opportunity, authorization, notification-policy, or correction ownership.
- Caller-supplied effects, child IDs, component/relationship data, audit/event/notification fields,
  transaction/retry controls, arbitrary filters, SQL, JavaScript, or a durable C1 reservation.
- Idempotent success replay, partial repair, migrations/new tables, a fourth tool, a second commit,
  or a campaign-specific structural-event type.

## Source and contract basis

| Authority | Evidence | Decision supplied |
| --- | --- | --- |
| Development workflow | AGENTS.md; procedure.system.create-feature; procedure.system.modify | One reviewable slice, explicit semantic confirmation, focused tests, no persistence bypass. |
| C1 | C1 closed request/result, validation algorithm, and exit gate | Same blueprint plus matching fingerprint, canonical references, expected write counts, and zero retained review state. |
| World model | procedure.world.change, procedure.world.model, procedure.world.naming | Permanent ID, closed component data, directed relationships, generated effects, and all-or-nothing application. |
| Runtime transaction pattern | `ActionRunner`, `EffectApplier`, `EventLedger` | An outer runner owns begin/rollback/commit; effects, structural events, guards, notifications, and success audit share one root operation. |
| Public surface | procedure.mcp.add-tool | One approved kind, thin dispatch, capability/dispatcher parity, and literal recovery calls. |
| Campaign roadmap | CAMPAIGN_CREATION_PLAN.md Slice 2; STORY_FIRST_ROADMAP.md C2 | Existing-world reference only, no partial state, and C3 consumes C2 readback. |

C1's repository search found no owner for campaign root/reference/procedure/public kind. C2 reuses
only C1's proposed vocabulary; it cannot invent a second bootstrap workflow, component, or
world-owned campaign link.

## Ownership and confirmation boundary

C2 owns the one-time creation transaction and its result. C1 owns blueprint grammar and campaign
root/reference vocabulary. World features own all selected world records; C3 owns durable
chapter/arc state; later slices own quest, sessions, audience authorization, AI, and opportunities.

Confirmed implementation boundary:

| Artifact | Proposed meaning |
| --- | --- |
| `commit(kind: "campaign")` with `operation: "create"` | A new closed operation on the C1-approved kind. It remains a public-surface change even if `validate` already exists. |
| `procedure.campaign.create` | Extends C1 validation with create input/result, active initial lifecycle, stale/replay behavior, transaction/audit rule, and recovery. |
| `game.core.campaign.root` | C1's confirmed component. C2 writes it once with the closed initial data below. |
| `game.core.campaign.in-world` | Exactly one directed relationship from campaign to one active world root, with empty data. |
| `game.core.campaign.references` | One directed relationship per C1-canonical reference; data is exactly `role` plus `audience`. |
| One semantic runner | Proposed name `CampaignBootstrapRunner`; final name may follow existing conventions, but exactly one service owns the transaction. |
| Replay policy | A committed permanent campaign ID never returns a second success. A later caller reads it back instead. |

Do not begin implementation unless the public create operation, root `active` status, closed result,
failure-audit behavior, and outer transaction owner are confirmed together. If the architecture
cannot do this without nested commits, revise the plan—do not divide the bootstrap into calls.

## Closed request, initial root, and result

~~~text
commit(kind: "campaign")
{
  operation: "create",
  blueprint: CampaignBlueprint,
  reviewFingerprint: 64-character lowercase hex
}
~~~

`CampaignBlueprint` is precisely C1's closed object. C1 writes no reservation, so the caller must
resend it. The caller may not send a validation result, effects, counts, world snapshot, event,
audit, component/relationship data, or child IDs.

Success writes this exact root component data to `campaignId`:

~~~text
{
  status: "active",
  title: blueprint.title,
  premise: blueprint.premise,
  partyGoals: blueprint.partyGoals,
  toneAndBoundaries: blueprint.toneAndBoundaries,
  rulesetScope: "dnd2024",
  creationMethod: "manual",
  reviewFingerprint: supplied matching fingerprint
}
~~~

World/reference IDs stay in relationships. `initialChapter`, `initialArc`, and
`futureQuestShapedProblem` are C1-validated input only: C2 stores no chapter, arc, quest, GM prose,
or hidden state before its owning slice exists.

~~~text
CampaignCreateResult {
  status: "created" | "rejected",
  campaignId: requested campaign ID,
  worldId: resolved world ID | null,
  reviewFingerprint: supplied fingerprint | null,
  referenceCount: 4–12 | null,
  structuralEventCount: 6–15 | null,
  operationId: committed success or recorded failure operation ID,
  problems: ordered { code, path, reason, recovery }[],
  next: one literal supported call
}
~~~

On success, structural events are exactly `3 + referenceCount`: entity create, component add,
in-world relationship, and one reference relationship per reference. The kernel emits existing
structural event types; C2 declares no campaign-specific event. Rejection returns null
world/fingerprint/reference/event values and no success operation ID. A failure audit may be
recorded only after rollback under the existing runner pattern, with `success: false` and no claim
that a campaign was created.

## Deterministic create algorithm

1. Reject a non-`campaign/create` envelope, non-object/unknown/missing/wrong-type input,
   malformed fingerprint, or any caller-derived/write field before effects are constructed.
2. Begin the one outer transaction before reading mutable campaign/world state.
3. Run C1's validator against the submitted blueprint inside that transaction. It must return a
   valid result with C1's canonical reference order and counts.
4. Compare its recalculated fingerprint byte-for-byte to the supplied value. Any changed blueprint,
   lifecycle, scope, visibility, reference revision, or campaign-ID collision rejects as
   `STALE_REVIEW` or the more specific C1 failure, with zero effects.
5. Verify C1's component/relationship definitions remain active and have their confirmed schema.
6. Create this immutable internal effect list, in this order and with nothing else:

   1. `entity.create(campaignId, title)`;
   2. `component.add(campaignId, game.core.campaign.root, closed root data)`;
   3. `relationship.create(campaignId, existingWorldId, game.core.campaign.in-world, {})`;
   4. canonical `relationship.create(campaignId, reference.entityId,
      game.core.campaign.references, { role, audience })` for every C1 reference.

7. Dry-run that exact private list. An invalid effect or guard denial rolls back; the committed
   sequence must be byte-for-byte the dry-run sequence.
8. Allocate one root operation ID and apply the list through the existing effect applier inside the
   ambient transaction. Structural event/guard/subscription/notification failure rolls it all back.
9. Record one campaign-create success operation with the same root operation ID, campaign/world
   IDs, fingerprint, and affected-entity summary—not the raw blueprint or GM-only content. Commit
   once.
10. Return the closed result and literal C3-compatible read call. A fresh read reconstructs one
    root, one world link, and canonical references. Exception/cancellation rolls back, clears
    tracked state, then may record only an unsuccessful failure audit outside the aborted unit.

## Recursive dependency analysis

~~~text
Campaign Feature 2: atomic existing-world root bootstrap
├─ C1 validated CampaignBlueprint + matching fingerprint                 [must be verified]
├─ C1 root/reference definitions and validate public surface             [must be verified]
├─ W1 root/location containment; W3 NPC/faction; W4 knowledge/visibility [verified]
├─ existing effect/event/operation transaction path                       [verified foundation]
├─ confirmed create operation + one outer transaction                     [semantic leaf]
│  └─ Slice 1: runner, catalog contract, public operation, tests
└─ C3 chapters/arcs/resume                                                 [blocked parent]

World composition, chapters/arcs, quests, sessions, and AI proposals [excluded]
~~~

## Slice order and stop gate

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | One semantic bootstrap transaction | C1 is verified; IDs/schemas/procedure and the public create operation are confirmed; transaction/failure-audit behavior is proved against the existing effect path. | A fresh imported world accepts exactly one reviewed campaign, commits all structural evidence together, and all invalid/replay/collision paths lack successful campaign creation. |

## Slice 1 — runtime artifacts and invariants

| Artifact | Change |
| --- | --- |
| Campaign procedure/catalog | Extend `procedure.campaign.create` and finalize only C1's root/relationship definitions. No fixture campaign, new event type, or database migration. |
| Semantic runner | Add one transaction-owning bootstrap service: C1 revalidation, immutable effect construction/dry-run/apply, success audit, rollback. |
| Public surface | Add closed `create` description and dispatch together; MCP handler is a thin delegate. |
| Registration | Register the runner and existing stores/applier/operation dependencies exactly once. |
| Tests | Focused create, rollback, replay, concurrency, and readback tests; capability/dispatcher guard and protocol walk for the public operation. |

- Only the runner begins/commits/rolls back. `EffectApplier` and `EventLedger` join its transaction.
- Effects are derived implementation data; callers never see or edit the list.
- Root operation, rows, structural events, subscriptions/notifications, and success audit are one
  unit. If any fails, none persist.
- A failure audit is outside that unit, explicitly unsuccessful, and never success evidence.
- Post-commit read failure never retries creation; it reports the supported read call.

## Acceptance matrix

| Test class | Setup | Exact expected result |
| --- | --- | --- |
| Minimal valid | C1-reviewed start, 2 NPCs, one faction, no knowledge. | One active root, one world link, four references, seven structural events, one success operation. |
| Maximum valid | C1 maximum: start, 3 NPCs, faction, 8 knowledge. | 12 references and 15 structural events; no other campaign record. |
| Closed root/links | Fresh read after create. | Exact root shape; one empty-data world link; canonical reference links; no containment, world/reference IDs in root, chapter/arc/quest, or GM prose. |
| Stale review | Change reference lifecycle/scope/visibility/revision, blueprint, or fingerprint after validate. | `STALE_REVIEW`/C1 problem, zero effects/success event/notification/audit. |
| Closed input | Missing/null/extra/wrong-type or effects/result/count/event/audit/child-ID/SQL/script/filter input. | Named path/recovery, no create state. |
| Replay/collision | Repeat after success; two concurrent same-ID requests. | At most one committed root and success operation; loser has no links/events/success evidence. |
| Fault injection | Component/relationship/effect, guard, structural event, subscription, notification, audit, cancellation, or exception fails at every phase. | Rollback leaves no campaign rows, structural event, notification, or success audit; optional failure audit is unsuccessful. |
| World isolation | Snapshot selected/unselected world records, links, clock, history, faction, and knowledge. | Byte-for-byte unchanged; only campaign entity/root/outgoing links and derived evidence write. |
| Readback/public surface | New context and fresh MCP session. | One campaign reconstructs correctly; capability/dispatcher agree; handler has no bootstrap logic; failure fixes are literal calls. |
| Repository gate | Focused tests, catalog validation, public-surface guard, protocol walk, full suite at acceptance, diff check. | All pass; no persistent import unless separately approved. |

## Slice 1 exit gate

C2 is verified only when a fresh imported existing world yields one fully atomic campaign root from
a C1-reviewed blueprint, all structural evidence shares one root operation, and failure, replay,
collision, and concurrency tests prove neither partial nor duplicate successful creation. Only then
may C3 consume the persisted readback.

## Plan-change rule

Revise before implementation if C1 cannot revalidate within the same transaction, a campaign needs
a non-active initial status, success must be replay-idempotent, chapter/arc data must exist before
C3, notifications/events cannot join the transaction, fingerprint revisions are unstable, or the
campaign surface is rejected. Do not persist unreviewed blueprints, copy world state, accept raw
effects, swallow event failure, or split the operation to work around any of these.
