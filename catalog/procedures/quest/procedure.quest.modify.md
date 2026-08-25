---
id: procedure.quest.modify
category: quest
name: Progress a campaign-scoped quest
governs: commit(kind: "quest") with closed quest lifecycle payloads
status: active
---

## Description

Apply one validated quest or objective lifecycle transition atomically and return the updated
quest state.

## Instructions

1. Read the quest entity and use its actual root status as `expectedQuestStatus`.
2. Offer only a `draft` quest. Accept only an `offered` quest. To set or unblock an objective, use
   an `active` quest, its actual objective status, and a trimmed factual `reason`.
3. `set-objective` changes only an owned active objective to `completed`, `blocked`, or `failed`.
   Completion activates every newly eligible dormant dependant in display-order then ID order.
   `unblock-objective` changes only an owned blocked objective to active after its prerequisites
   are completed.
4. Reconcile only an active quest: a failed required objective wins; otherwise all required
   objectives completed makes the quest completed; otherwise it makes no change. `fail` explicitly
   fails an active quest. `reopen-quest` restores a completed or failed quest to active, and
   `archive` archives an offered quest.
5. `reopen-objective` changes only an owned completed or failed objective to active when its
   prerequisites are completed and no direct or transitive dependant is completed.
6. Read the returned quest entity after success. `procedure.quest.inspect` owns the bounded
   trusted-host current-and-transition summary when that is the appropriate follow-up.

## Constraints

- Q2 derives every component replacement and structural effect inside one transaction. Callers
  never supply effects, child IDs, relationships, events, audit data, or target state.
- A stale expected status, malformed quest graph/context, invalid reason, blocked effect, or failed
  apply changes no quest/objective state and leaves no structural event or success audit.
- This procedure never changes campaign, world, item, character, time, reward, evidence, or
  authorization state. Reconciliation is always explicit; no objective call changes the parent
  quest implicitly.
