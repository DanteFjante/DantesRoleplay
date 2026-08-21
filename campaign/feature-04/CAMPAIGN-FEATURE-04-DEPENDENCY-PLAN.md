# Campaign Feature 4 dependency plan — link an existing quest to campaign continuity

Status: **Planned; blocked by verified C3 and Quest Slices 0–3, plus shared scope/link/query confirmation.**
Last updated: 2026-08-20

## Execution rule and target

C4 is repository-mode work governed by AGENTS.md, procedure.system.create-feature,
procedure.system.modify, procedure.world.change, and procedure.mcp.add-tool. Catalog work uses
roleplay validate catalog in a disposable database; no persistent import occurs without a separate
integration/release decision.

A trusted host can attach one existing, active, same-campaign quest to a C3 arc and one or more
chapters in that arc. The fixed campaign resume view then consumes the existing bounded quest
summary and actionable objective summary. The operation writes context links only; it cannot create,
advance, complete, fail, reopen, archive, reward, or otherwise alter the quest or objective.

## Boundary

Included: explicit arc-to-quest and chapter-to-quest context links; exact campaign/arc/chapter/
quest scope and lifecycle checks; expected-state link creation; fixed bounded digest composition;
fresh-host readback; and atomic link/replay/rollback/isolation tests.

Excluded: quest/objective component or relationship changes; quest creation and transition
mechanics; automatic chapter/arc changes; event-driven progress, rewards, notifications, player
authorization, AI prose, copied quest summaries/state, arbitrary queries, migration, and a second
transaction.

## Source, ownership, and confirmation boundary

| Authority | Evidence | Decision |
| --- | --- | --- |
| C3 | C3 root/chapter/arc invariants and fixed trusted-host digest | C4 attaches only to existing C3 records; it never changes their lifecycle or digest core. |
| Quest Q0–Q3 | QUEST_IMPLEMENTATION_PLAN.md first scope, components, manual lifecycle, evidence/read model | Quest system owns quest/objective identity, state, evidence, visibility and its bounded summary. C4 is a consumer. |
| World/core | procedure.world.change/model/naming and current event/audit path | Directed links, permanent IDs, atomic effects, structural evidence; no raw caller effects. |
| Public surface | procedure.mcp.add-tool | A campaign link operation and resume-result extension need capability/dispatch/query contract confirmation together. |

Confirm these new permanent/public meanings together before implementation:

| Artifact | Proposed meaning |
| --- | --- |
| game.core.campaign.arc.features-quest | Directed empty-data link from C3 arc to one active quest in the same campaign. A quest has exactly one such C4 arc link. |
| game.core.campaign.chapter.features-quest | Directed empty-data link from a chapter to an active quest already linked to its arc. A quest may link to one or more chapters in that one arc. |
| procedure.campaign.quest-context | Governs scope/lifecycle validation, attach semantics, replay, digest consumption, and recovery. |
| campaign commit operation attach-quest-context | Accepts campaign, arc, chapter, quest, expected quest active state; creates required missing context links atomically. |
| campaign resume query | Extends C3's fixed trusted-host result with a bounded quest context section, not a free-form quest reader. |

The quest-side owner must first confirm its one campaign-scope convention and its Q3 trusted-host
summary contract. C4 must reference those existing identifiers; it must not guess or add a parallel
quest-in-campaign relationship. If Q1–Q3 choose different names/statuses/summary bounds, revise
this plan before implementation.

## Closed attach operation

The proposed request is:

~~~text
commit kind campaign
{
  operation: "attach-quest-context",
  campaignId: active C2 campaign ID,
  arcId: C3 active arc ID,
  chapterId: C3 active or closed chapter ID,
  questId: existing active quest ID,
  expectedQuestStatus: "active"
}
~~~

Unknown/null/extra fields, quest/objective state, effects, link data, audit/event fields, arbitrary
filters, scripts, SQL, or caller-selected summary data reject. The caller supplies no relationship
data: both C4 links have exactly empty object data.

The runner resolves the C2 campaign root, requested C3 arc/chapter, quest-side campaign scope,
and the chapter in-arc link. It requires all records active where their owner defines active;
chapter may be active or closed only. It rejects a quest outside the campaign, a chapter outside
the named arc, terminal/archived quest, missing expected status, reversed/duplicate link, or an
existing arc link to another arc.

The private derived effect list is deterministic:

1. create the arc-to-quest link only if absent;
2. create the chapter-to-quest link only if absent.

At least one link must be missing. Thus the successful effect/event count is one for adding a new
chapter context to an already attached quest, or two for the first attachment. If both links
already exist, reject as replay rather than reporting a second success. A different chapter within
the same arc may be attached later through the same operation; this is how a quest spans chapters.

The runner begins one transaction, validates/dry-runs the exact list, allocates one root operation
ID, applies it through the current effect/event/guard path, records a success audit containing only
IDs/link count, and commits once. Guard, event, notification, audit, cancellation, or exception
failure rolls back all C4 links and success evidence. An existing-pattern post-rollback failure
audit may exist only as unsuccessful.

## Resume composition

C4 extends C3's fixed campaign query rather than creating another reader. For the selected campaign,
the trusted-host result adds at most three active quests in canonical order: priority then quest ID.
Each entry is supplied by Q3's already approved summary and contains quest ID, title, status,
authored summary, descriptive visibility, and at most three Q3 actionable-objective summaries in
their quest-defined display order. It also contains sorted linked chapter IDs and its one arc ID.

The campaign reader does not inspect raw objective truth, evidence, hidden criteria, or unrelated
quests; it calls the quest-owned bounded projection and joins only C4 links. It stores no quest
copy, cache, lifecycle field, objective array, completion date, or player-facing alternative.
This remains a trusted-host view. C5, not C4, owns actual audience authorization/filtering.

## Dependency tree and slice

~~~text
C4 campaign quest context
├─ C3 campaign root/chapter/arc/digest                       [must be verified]
├─ Q0–Q3 quest scope, lifecycle, evidence, bounded summary  [must be verified]
├─ shared campaign scope + C4 links/query contract           [semantic leaf]
│  └─ Slice 1: attach runner and composed fixed digest
└─ P1 played continuity proof                                [blocked parent]
~~~

| Artifact | Slice 1 change |
| --- | --- |
| Catalog | Confirmed two C4 link conventions and procedure only; no quest component, fixture quest, event type, or subscription. |
| Semantic runner | One transaction-owning context-link service over C3 and quest-owner reads. |
| Public surface | One closed campaign operation and fixed resume extension, capability/dispatch/guard together. |
| Read model | Join C4 links to Q3 bounded projections only; enforce three-quest/three-objective limits server-side. |
| Tests | Link counts, scope/lifecycle, replay, same-arc multi-chapter, rollback, no-quest-write, digest bounds, fresh readback, protocol walk. |

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| First valid attachment | One scoped active quest, C3 active arc/chapter. | Exactly two links/events and one success audit; quest/objective component bytes unchanged. |
| Later chapter context | Same quest/arc, distinct valid chapter. | Exactly one chapter link/event; no second arc link or quest mutation. |
| Replay/collision | Both links exist, reverse/duplicate link, another arc, wrong campaign, missing in-arc link, terminal/archived quest, or stale quest status. | Named rejection with no C4 success evidence. |
| Closed input | Missing/null/extra/type-invalid or state/effect/link/audit/event/SQL/script/filter/summary input. | Named recovery and no state change. |
| Lifecycle isolation | Close/advance C3 chapter; quest owner advances/completes/fails a quest. | Neither action writes the other subsystem's lifecycle state; digest reflects current quest-owner summary only. |
| Atomic failure | Link/event/guard/subscription/notification/audit/cancellation failure. | No C4 link, structural event, notification, or success audit persists; optional failure audit is unsuccessful. |
| Digest | More than three linked active quests/objectives and linked hidden quest material. | Only three Q3-approved summaries in canonical order; no raw hidden truth or unrelated quest leaks. |
| Fresh readback | New context/session reads campaign. | C3 continuity plus valid Q3 context reconstructs from links/state alone, with no cache. |
| Repository gate | Focused tests, catalog validation, surface guard, protocol walk, full suite at acceptance, diff check. | All pass without persistent import. |

## Exit gate and change rule

C4 is verified only when a fresh imported C3 campaign can attach an existing same-scope quest, add
a later chapter context in the same arc, and resume bounded quest context without changing one byte
of quest/objective lifecycle data. Invalid, replay, cross-scope, and injected-failure paths leave
no partial links. P1 may use the resulting continuity only after this evidence exists.

Revise before implementation if Q0–Q3 do not expose a stable campaign scope or bounded summary,
the quest can belong to multiple campaigns/arcs, a chapter link must alter quest progress, real
player authorization is required, or the public surface is rejected. Never solve those gaps by
copying quest state into campaign components, accepting raw effects, or making campaign code own
quest transitions.
