# Campaign Feature 13 dependency plan — deterministic opportunity pool and campaign-time selection

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; blocked by verified C6, Quest Q2 activation, clock evidence, and semantic confirmation**  
Last updated: 2026-08-20

## Target capability

A trusted host can invoke one named campaign-time opportunity evaluation for a campaign. From that
campaign's active, eligible clock-roll opportunities, the campaign owner forms a bounded canonical
candidate set, uses the engine's recorded seeded random source once, and activates at most one
linked draft quest through the existing quest owner. The chosen opportunity, candidate set, seed
evidence, roll, clock evidence, derived activation event, quest transition, notification, and
audit all belong to one root operation.

This makes future opportunities durable and inspectable rather than hidden in an AI prompt. It does
not automatically run on a timer: a scheduler or session feature must invoke the confirmed
evaluation operation with current clock evidence. Nothing is selected from an empty candidate set.

### Included

- A C6-compatible revision of campaign opportunity state for 1–16 opportunities per active
  campaign, with modes event or clock-roll.
- Closed clock-roll eligibility fields: positive integer weight, optional eligible-from minute,
  optional expiry minute, one-time flag, and dormant/activated/expired/archived lifecycle.
- One trusted-host campaign-time evaluation operation with expected world-clock revision.
- Canonically sorted candidate set, one recorded seeded selection, at most one quest activation,
  root-transaction rollback, notification, and fresh readback.
- Migration/readback of the existing C6 event opportunity without changing its prior semantics.

### Excluded

- Automatic schedulers, repeated polling, manual offer acceptance, player authorization, AI choice,
  dynamic event-filter authoring, multiple winners, weighted tables shared across campaigns,
  cooldowns beyond one-time/expiry, quest creation/objective mutation, party votes, rewards,
  encounters, browser controls, or a new random-number source.
- Campaign ownership of the draft-to-active quest transition. Q2 remains its sole writer.

## Ownership and semantic confirmation boundary

C13 extends the existing C6 opportunity owner; it does not add a competing campaign service.
Confirm the following permanent/public meanings, C6 migration, seed provider, and exact time
source together before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| game.core.campaign.opportunity | Revised closed state for event and clock-roll opportunities, including status, activation mode, summary, visibility, one-time, and mode-specific eligibility fields. |
| campaign.opportunity.evaluate-clock-roll | Trusted-host C13 commit operation: evaluate eligible opportunities once at an expected root-clock revision. |
| campaign.opportunity.selected | Derived semantic event that records campaign, opportunity, quest, source clock revision, canonical candidate IDs, random-source version, seed evidence, roll range/value, and root operation ID. |
| procedure.campaign.opportunity | Existing C6 owner, revised for state validation, clock-roll eligibility, seeded selection, evidence, and recovery. |
| opportunity selection reaction | One C13 campaign-owned mechanism that chooses at most one candidate and emits the derived event; it never edits quest state. |
| quest activation reaction | Existing Q2 owner reaction to the derived event, which performs only the closed draft-to-active transition. |

Do not implement until the confirmation names the existing root-clock projection, seeded random API,
random-source version and seed disclosure policy, notification audience, exact operation surface,
and C6 record migration rule. The proposed migration maps every valid C6 record to its existing
status, activationMode event, summary, and visibility plus oneTime true; it adds no clock-roll
fields and preserves every established C6 relationship, event subscription, and event behavior.

## Closed state and operation contract

The revised component is closed:

~~~text
game.core.campaign.opportunity
{
  status: "dormant" | "activated" | "expired" | "archived",
  activationMode: "event" | "clock-roll",
  summary: trimmed text, 1–1,000 Unicode scalar values,
  visibility: "public" | "party" | "gm",
  oneTime: boolean,
  weight?: integer, 1–1,000,
  eligibleFromMinute?: non-negative integer,
  expiresAfterMinute?: non-negative integer
}
~~~

Event opportunities may not carry clock-roll fields and retain C6's fixed event semantics.
Clock-roll opportunities require weight, may use either minute bound, and must satisfy
eligibleFromMinute <= expiresAfterMinute when both are present. Component data contains no quest,
campaign, world, arc, chapter, clock, random seed, candidate list, cooldown, source event, or
selection result; those remain relationships and operation history.

The established C6 campaign/opportunity, opportunity/quest, and opportunity/chapter/arc links
remain exactly one each. C13 permits 1–16 such opportunities per active campaign. Every linked
quest is a same-campaign draft quest and every context chapter belongs to its linked arc.

The closed evaluation request is:

~~~text
{
  campaignId: canonical entity id,
  expectedClockRevision: non-negative integer
}
~~~

The result is exactly one of:

~~~text
{
  status: "activated" | "no-eligible-opportunity" | "stale-clock" | "blocked",
  selectedOpportunityId?: canonical entity id,
  selectedQuestId?: canonical entity id,
  clockRevision: non-negative integer,
  candidateOpportunityIds?: canonical sorted entity-id array,
  randomEvidence?: { sourceVersion, seedReference, range, roll }
}
~~~

Only activated returns selection and random evidence. No-eligible-opportunity records the verified
clock read and a normal no-selection audit but consumes no random value and changes no opportunity
or quest. Stale-clock is a no-write rejection.

## Eligibility, selection, and transaction algorithm

1. Resolve one active campaign, its one world link, the authoritative root clock, and the supplied
   expected revision. Wrong scope, missing clock, or a revision mismatch returns blocked or
   stale-clock before random selection.
2. Read at most 16 same-campaign opportunities with their exact context/quest links. Reject
   malformed, duplicate, cross-campaign, terminal-quest, invalid-mode, invalid-time, or broken
   chapter/arc state rather than silently skipping bad durable data.
3. Before selection, each active clock-roll opportunity past its expiry bound becomes expired under
   the same root operation. A remaining opportunity is eligible only when active campaign/context
   records are valid, its linked quest is draft, status is dormant, and current minute meets its
   inclusive eligible-from bound. Activated, expired, and archived records never enter the
   candidate set.
4. Sort candidates by canonical opportunity ID. The registered engine random source receives only
   that canonical candidate set and positive weights; it returns exactly one auditable roll and
   selected ID. No caller, LLM, or system-clock value chooses the result.
5. Build one outer transaction: replace selected opportunity with activated state; declare
   campaign.opportunity.selected; let Q2 validate and activate the exact linked quest; emit the
   bounded notification and one success audit. Other candidates remain unchanged.
6. A failure in eligibility, random evidence, event routing, Q2, notification, audit, exception,
   or cancellation rolls back the source operation, opportunity, quest, event, and success
   evidence. A fresh repeat after success finds the selected one ineligible.

## Dependency order and slices

~~~text
C13 deterministic opportunity pool and campaign-time selection
├─ C3/C4 campaign, chapter/arc, and quest context                  [must be verified]
├─ C6 event-opportunity owner and root reaction proof               [must be verified]
├─ Quest Q2 draft-to-active transaction                             [must be verified]
├─ W5/root-clock and recorded seeded random-source evidence         [must be verified]
├─ confirmed state revision, migration, operation, event, seed policy [semantic boundary]
│  └─ Slice 1: C6-compatible schema migration and read-only eligibility
└─ verified Slice 1
   └─ Slice 2: one seeded selection/quest activation transaction
~~~

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Opportunity-pool foundation | C6/Q2/clock/random contracts are verified and state/migration meanings are confirmed. | Fresh import/readback proves valid C6 event records migrate unchanged and valid clock-roll records have closed bounded eligibility. |
| 2 | Seeded campaign-time selection | Slice 1 is verified. | One canonical eligible pool selects and activates at most one quest atomically, with complete evidence and no hidden randomness. |

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| C6 compatibility | A migrated event opportunity retains event mode and can only use C6's fixed event path; it cannot enter a clock-roll candidate set. |
| Eligible pool | A same-campaign pool with several valid clock-roll opportunities produces canonical candidate IDs, one engine-seeded roll, and one selected opportunity/quest. |
| Empty pool | No eligible record yields no-eligible-opportunity, no random call, no derived event, no quest activation, and no notification. Only required dormant-to-expired replacements may occur. |
| Boundaries | Before eligible-from or after expiry, wrong lifecycle, non-draft quest, wrong campaign/arc/chapter, or archived/inactive state excludes/rejects exactly as confirmed. |
| Stale/replay | Wrong clock revision rejects before selection; repeating a successful one-time evaluation cannot offer or activate that opportunity twice. |
| Determinism/evidence | Same persisted state plus recorded seed source produces the same selected ID and roll evidence; operation history identifies the candidate set, source version, seed reference, range, and roll. |
| Rollback | Invalid selection effect, Q2 failure, event/notification/audit failure, cancellation, or random-evidence failure leaves every opportunity, quest, event, notification, and success audit unchanged. |
| Isolation | No scheduler, world clock mutation, chapter/arc transition, quest objective change, event subscription authoring, AI decision, or player-facing access behavior is added. |
| Repository acceptance | Focused C13 state/selection/rollback tests, roleplay validate catalog, relevant protocol walk, full suite, and git diff --check pass. |

## Completion boundary

C13 is complete when an auditable campaign-time trigger can select at most one eligible future
quest from a durable weighted pool and activate it through Q2 in one rollback-safe transaction.
Stop before automated scheduling, player choice, arbitrary event filters, cooldowns, multiple
winners, or AI-directed selection.
