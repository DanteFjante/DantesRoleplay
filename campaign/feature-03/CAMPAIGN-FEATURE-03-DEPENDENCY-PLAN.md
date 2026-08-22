# Campaign Feature 3 dependency plan — chapters, one arc, and a quest-free resume view

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Implemented and verified in a disposable C2 campaign; no persistent catalog import performed.**
Last updated: 2026-08-20

## Execution rule and target

C3 is repository-mode work governed by AGENTS.md, procedure.system.create-feature,
procedure.system.modify, procedure.world.change, and procedure.mcp.add-tool. Catalog edits run
roleplay validate catalog in a disposable database; persistent import needs separate approval.

One semantic continuity runner—not the MCP handler or caller—owns validation, expected state,
derived effects, one outer transaction, events, audit, and outcome. It lets a trusted host initialize
an active C2 campaign with one active chapter and one active arc; deliberately advance or close that
chapter or conclude that arc; then reconstruct a fixed, bounded resume digest after a fresh session.
No quest is required and no transition may mutate a world, quest, character, item, clock, or faction.

## Boundary and truthful handoff

Included: campaign-owned chapter/arc entities, closed components and links; re-ratified initial
seed; one-active-chapter/arc invariant; expected-state transitions; trusted-host resume; and
rollback/replay/cross-scope/readback tests.

Excluded: quest links, automatic transitions, player authorization/filtering, AI prose, clocks,
sessions, notifications, UI, branching/multiple arcs, copied content, arbitrary history filters,
caller-selected milestones, raw effects/audits/events, migrations, and a second transaction.

| Source | Decision |
| --- | --- |
| C2 root/result | C3 starts only from one active persisted campaign, its world link, canonical references, and the existing transaction/event/audit path. |
| C1 initial chapter/arc fields | C1 retained no review reservation and C2 stored no chapter, arc, or GM prose. C3 must not pretend it can recover those details automatically. |
| Campaign/story roadmaps | Quest-free continuity, bounded fresh-host reconstruction, and isolated lifecycle. |
| Core world model and MCP contract | Permanent IDs, closed data, directed links, atomic effects, and confirmed public surface. |

Before initialization, the host must re-ratify this CampaignContinuitySeed against the retained C0
brief and C1 review. It is a human confirmation, not a fingerprint replay or an inferred audit:

~~~text
{
  campaignId: active C2 campaign ID,
  chapter: { localKey: "chapter." + lowercase dot/hyphen suffix,
             title: trimmed 1–160, partyQuestion: trimmed 1–500,
             gmContext: absent | trimmed 1–1,000 },
  arc:     { localKey: "arc." + lowercase dot/hyphen suffix,
             title: trimmed 1–160, partyStake: trimmed 1–500,
             gmContext: absent | trimmed 1–1,000 }
}
~~~

### Ratified test continuity seed

The already-authorized test campaign uses `campaign.test.sealed-observatory`, with opening chapter
`chapter.opening` titled **The Ledger Signal** and arc `arc.observatory` titled **The Observatory's
Claim**. Their party question/stake and GM contexts are the retained C0 brief text. This ratifies
only C3's test continuity data; it does not create persistent campaign state.

Child IDs derive only as campaignId plus localKey. Callers do not supply child IDs or embed child
lists in the root.

## Ownership and proposed vocabulary

| Artifact | Proposed meaning |
| --- | --- |
| game.core.campaign.chapter | Closed state: active or closed; title, party question, optional GM context, and required closing summary only when closed. No campaign/world/arc/quest IDs or milestone list. |
| game.core.campaign.arc | Closed state: active, resolved, or abandoned; title, party stake, optional GM context, terminal closing summary. No chapter/world/quest IDs. |
| game.core.campaign.has-chapter / has-arc | Directed empty-data links from campaign root to child chapter/arc. |
| game.core.campaign.chapter.in-arc | Directed empty-data chapter-to-arc context link, not containment. |
| procedure.campaign.chapter | Governs seed, expected-state transitions, audit/event behavior, and recovery. |
| Campaign commit operations | initialize-continuity, advance-chapter, close-chapter, conclude-arc. |
| Campaign query kind | Fixed trusted-host resume view accepting campaign ID only, never arbitrary graph access. |

All rows above are new permanent/public vocabulary and require one semantic confirmation together.
C3 permits exactly one active chapter and one active arc; every chapter links to that arc. Branching
or multiple arcs belong to [Campaign Feature 12](../feature-12/CAMPAIGN-FEATURE-12-PARALLEL-ARC-CONTINUITY-PLAN.md), which must revise this cardinality explicitly rather than treating this first
continuity proof as the permanent campaign limit.

## Closed operations

Chapter data is exactly status active or closed, title 1–160, partyQuestion 1–500, optional
gmContext 1–1,000, and closingSummary. Closing summary is forbidden while active and required,
trimmed 1–1,000, while closed. Arc data substitutes partyStake and permits active, resolved, or
abandoned; terminal arc states likewise require closingSummary. Unknown/null/untrimmed fields,
invalid keys, or cross-campaign IDs reject.

| Operation | Payload | Effects on success |
| --- | --- | --- |
| initialize-continuity | Seed above | 7: create chapter/arc, add both components, campaign-to-chapter, campaign-to-arc, chapter-to-arc links. |
| advance-chapter | Campaign/current chapter ID, expected active state, closing summary, next chapter key/title/question/optional GM context | 5: close old chapter; create/add/link new active chapter; link it to active arc. |
| close-chapter | Campaign/chapter ID, expected active state, closing summary | One complete component replacement. |
| conclude-arc | Campaign/arc ID, expected active state, resolved or abandoned outcome, closing summary | One complete component replacement. |

Initialize requires no existing C3 children. Advance requires exactly one active chapter and arc and
atomically closes the named chapter while creating its successor. Close leaves no active chapter.
Concluding an arc never changes a chapter; a terminal arc cannot be revived. Full component
replacement is mandatory; merge is forbidden.

## Algorithm and read model

1. Reject unknown/malformed input, raw derived fields, or missing/unratified seed before effects.
2. Start one transaction; resolve the active C2 root, its one world link, C2 references, target
   C3 records and expected status.
3. Reject wrong campaign, missing/reversed/duplicate links, malformed state, broken active
   invariant, stale/terminal target, or derived-ID collision.
4. Build only the table-listed ordered effects, dry-run that unchanged private list, allocate one
   root operation ID, apply it under the ambient transaction, route guards/events/subscriptions,
   write one success audit with IDs/statuses but not GM prose, and commit once.
5. Any effect, guard, event, notification, audit, exception, or cancellation rolls back all C3
   rows and success evidence. A failure audit may be written after rollback only as unsuccessful.

The proposed fixed call is query campaign by campaign ID. It returns root display data/world ID;
current chapter/arc including GM context; canonical C2 reference ID, role, authored summary and
descriptive visibility; and at most five closed chapter milestones. Milestones are derived, not
stored, from committed world component-replacement events for closed chapters, newest-first by
timestamp, sequence, then event ID. This is a trusted-host view; C5 owns player-safe filtering.

## Dependency tree, slice, and tests

~~~text
C3 chapter/arc continuity and resume
├─ C2 root, world link, canonical references                  [must be verified]
├─ C0/C1 material plus re-ratified continuity seed            [must be verified]
├─ effects/events/audit transaction foundation                [verified]
├─ confirmed C3 vocabulary/public read surface                [semantic leaf]
│  └─ Slice 1: seed, transitions, digest, catalog, tests
└─ C4 quest integration                                       [blocked parent]
~~~

Slice 1 adds only confirmed catalog contracts, one transaction-owning runner, four closed commit
operations, one fixed campaign query, capability/dispatcher guard coverage, focused state/effect/
rollback/replay/scope/event-order/readback tests, catalog validation, and a protocol walk.

| Case | Exact expected result |
| --- | --- |
| Initialize | Valid C2 root plus seed yields 2 children, 2 components, 3 links, 7 structural events, one success audit, exactly one active chapter/arc. |
| Advance | Old chapter closes; one successor links to the same active arc; exactly 5 effects/events. |
| Close/conclude | One component replacement; neither operation changes the other lifecycle. |
| Stale/replay/collision | Old status, terminal/cross-campaign target, duplicate key, or repeat rejects with no C3 success evidence. |
| Injected failure | Component/link/event/guard/subscription/notification/audit/cancellation failure leaves no C3 rows, event, notification, or success audit. |
| Isolation | World, C2 root/links, quest, faction, clue, clock, character, and item state remain byte-identical. |
| Digest | Fresh read after over five closes returns only fixed C2/C3 data and five correctly ordered milestones. |
| Trust boundary | Digest labels trusted-host behavior and never claims descriptive visibility enforces secrecy. |

## Exit gate and change rule

C3 is verified only when a fresh imported C2 campaign initializes, advances/closes one chapter,
concludes its arc, and reconstructs the bounded trusted-host digest without a quest. All invalid,
stale, replay, cross-campaign, and injected-failure paths leave no partial C3 state. C4 may then
link a quest but cannot own chapter/arc lifecycle.

Revise first if the seed cannot be re-ratified, C2 readback is incoherent, player authorization is
required, event order cannot derive milestones, or a transition needs quest/world/clock change.
Multiple arcs or branching are owned by C12 and require its explicit cardinality/migration gate.
Never work around this with root child lists, copied state, parallel milestone arrays, raw effects,
or descriptive visibility treated as access control.
