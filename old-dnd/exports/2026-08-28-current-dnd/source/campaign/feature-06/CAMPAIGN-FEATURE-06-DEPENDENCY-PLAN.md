# Campaign Feature 6 dependency plan — one event-activated future quest opportunity

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; blocked by verified C4, Quest Slice 2 lifecycle, and confirmed event-chain ownership.**
Last updated: 2026-08-20

## Execution rule and target

C6 is repository-mode work governed by AGENTS.md, procedure.system.create-feature,
procedure.system.modify, procedure.event.react, procedure.subscription.modify, and the existing
event/subscription transaction contracts. It uses one existing structural source event, one
campaign reaction, and one quest-owner activation reaction. It does not introduce clocks, chance,
or AI choice.

One dormant opportunity in one campaign can activate one already-existing draft quest exactly once
when one approved committed source event matches. The opportunity state change, derived activation
event, quest draft-to-active transition, structural evidence, notification, and success audit all
join the source event's root transaction. If any part fails, the source change itself rolls back;
the opportunity remains dormant and the quest remains draft.

## Boundary and ownership

Included: one closed opportunity component; campaign/opportunity/quest/chapter/arc links; one
fixed structural-event subscription and bounded filter; one-time candidate check; derived event
handoff; quest-owned activation reaction; notification/history evidence; deterministic chain and
rollback tests.

Excluded: random rolls/weights, clocks/cooldowns/expiry, manual activation, multiple candidate
selection, generic subscription authoring UI, quest creation/objective mutation, chapter/arc
transition, player authorization, AI interpretation, and any direct campaign write to quest state.

| Owner | C6 owns | C6 cannot own |
| --- | --- | --- |
| C6 | Opportunity state, context links, source-event eligibility, derived handoff event. | Quest lifecycle or objective state. |
| Quest Q2 | Draft-to-active quest transition and its component replacement. | Opportunity status or source-event filtering. |
| Event runtime | Routing order, filters, seeds, root transaction, event ledger, guard/reaction failure. | Campaign/quest eligibility meaning. |
| C3/C4 | Existing campaign/chapter/arc and normal quest context. | Their lifecycle/state. |

## Proposed vocabulary and confirmation boundary

Confirm all permanent/public meanings together before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| game.core.campaign.opportunity | Closed state: dormant, activated, or archived; trimmed summary; activationMode exactly event; descriptive visibility. It stores no quest/chapter/arc IDs, filter, clock, weight, roll, cooldown, or outcome. |
| game.core.campaign.has-opportunity | Directed empty-data campaign-to-opportunity link. |
| game.core.campaign.opportunity.activates-quest | Directed empty-data opportunity-to-draft-quest link; exactly one. |
| game.core.campaign.opportunity.in-chapter / in-arc | Directed empty-data context links; the chapter must link to the selected arc. |
| campaign.opportunity.activated | One derived semantic event type carrying only opportunity, campaign, quest, source event ID, and root operation ID references; not a caller-written event. |
| procedure.campaign.opportunity | Defines source event, eligibility, one-time rule, derived-event handoff, notification and recovery. |
| campaign opportunity reaction | One fixed reaction to one confirmed structural event type/filter/tracked entity set. |
| quest activation reaction | Quest-owner subscription to campaign.opportunity.activated that invokes only the Q2 activation transition. |

The first source event must be selected from an existing registered structural event schema after
reading it. Its type, payload-equals filter, tracked entity IDs, scope, fixed role bindings,
subscription order, and fixture are immutable C6 contract data. C6 must not add a broad condition
language or use prose/LLM judgement to decide a match.

## Closed initial opportunity

The first fixture is created through one confirmed semantic setup path, not by a caller-supplied
effect list. It requires active C2 campaign, one C3 chapter/arc pair, and one same-campaign draft
quest. C4 may later attach the quest after activation; C4's active-quest rule must not be weakened
to make a dormant opportunity fit.

Opportunity data is exactly status dormant, a 1–1,000 trimmed summary, activationMode event, and
visibility public, party, or GM. It has exactly one link of each proposed kind. Duplicate/reversed
links, cross-campaign/world quest, terminal/archived quest, chapter outside arc, inactive campaign,
or invalid state reject. No opportunity is created after C6 initial fixture.

## Deterministic chain

1. A committed source structural event passes the fixed subscription type, scope, scalar
   payload-equals, and tracked-entity filters. Nonmatching events execute nothing and spend no
   opportunity state.
2. The C6 reaction resolves its fixed opportunity, campaign, chapter/arc, and draft quest. It
   validates dormant state, exact context/scope, and one-time eligibility. Ordinary nonmatch
   returns zero effects and declares no event.
3. A match replaces only opportunity data from dormant to activated and declares one
   campaign.opportunity.activated event. The event is derived and carries the accepted source
   event/root-operation evidence.
4. The registered quest-owner reaction receives that derived event. It validates the same fixed
   draft quest and invokes its Q2-approved draft-to-active behavior. It may change quest lifecycle
   only; it cannot alter C6 opportunity data.
5. Existing event routing applies reactions in confirmed order under the same root transaction.
   The bounded notification is emitted only after both state transitions are valid and must name
   the opportunity/quest, not hidden source details.
6. A second matching event sees activated state and produces zero effects/events/notifications.
   Any malformed state, guard denial, unavailable subscription/mechanic, invalid effect, chain
   limit, notification/audit failure, exception, or cancellation aborts the full root change.

No direct C6 command activates an opportunity. The event ledger and root operation are the only
durable evidence; no duplicate campaign history component is written.

## Dependency tree and Slice 1

~~~text
C6 one event-triggered opportunity
├─ C3 campaign chapter/arc and C4 quest context             [must be verified]
├─ Q2 draft-to-active quest transition                      [must be verified]
├─ event ledger/reactions/notifications/root rollback       [verified foundation]
├─ confirmed opportunity/event/subscription vocabulary       [semantic leaf]
│  └─ Slice 1: fixture, two reactions, proof chain
└─ clock/random/AI opportunities                             [excluded future]
~~~

Slice 1 adds the confirmed component/link/procedure/event-type/subscription/mechanic fixture
together; uses the event runtime rather than a parallel dispatcher; and adds focused eligibility,
filter, deterministic ordering/seed, one-time, chain rollback, notification/audit, and fresh event
ledger/readback tests. Run catalog validation, surface guards/protocol walk where contracts change,
full suite at acceptance, and diff check.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Matching event | One dormant opportunity becomes activated, exactly one derived event routes Q2 activation, one draft quest becomes active, one bounded notification and shared-root evidence exist. |
| Nonmatch | Type/scope/filter/tracked-entity mismatch executes no reaction and leaves source behavior/opportunity/quest unchanged. |
| Replay | Repeat matching source event sees activated opportunity; no effects, derived event, notification, or second quest activation. |
| Invalid setup | Wrong campaign/world, terminal/inactive quest, broken chapter/arc link, duplicate/reversed link, malformed filter/binding/subscription/mechanic rejects safely. |
| Atomic failure | C6 reaction, derived event, Q2 transition, guard, event ledger, notification, audit, chain-limit, cancellation failure rolls back source change, opportunity, quest, and success evidence. |
| Lifecycle isolation | Close/advance chapter, conclude arc, or quest objective transition. | None activates opportunity unless the exact fixed source event matches; C6 never writes objective state. |
| Evidence | Fresh event/history query by root operation. | Source, derived event, both state replacements, reaction executions, notification, and success audit share one causal root. |
| Boundary | Random seed, clock, cooldown, manual call, multiple candidate, AI input. | Absent from component, procedure, subscriptions, result, and tests. |

## Exit gate and change rule

C6 is verified only when a fresh imported fixture proves one qualifying committed source event
activates exactly one intended draft quest once, with all evidence under one root transaction, and
every nonmatching, repeat, invalid, or injected-failure case leaves the opportunity dormant and
quest draft. Deterministic clock-roll opportunity pools are owned by
[Campaign Feature 13](../feature-13/CAMPAIGN-FEATURE-13-DETERMINISTIC-OPPORTUNITY-POOL-PLAN.md);
AI-directed opportunity work remains a separate feature.

Revise before implementation if Q2 cannot expose a callable draft-to-active transition in a
reaction chain, the existing event schema cannot express a bounded fixed trigger, quest activation
needs player choice, a notification cannot join the root transaction, or one opportunity needs
multiple quests/arcs. Never work around this by having campaign code set quest state, emitting a
caller-written event, adding a filter language, or splitting activation across commits.
