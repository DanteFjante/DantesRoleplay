---
id: procedure.campaign.quest-context
category: campaign
name: Attach quest context to campaign continuity
governs: commit(kind: "system.interaction-execute") operation attach-quest-context
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Attach an active quest to an existing campaign arc and chapter as continuity context without
changing quest or campaign lifecycle state.

## Matches

## Instructions
1. Read the active quest and the campaign with `query(kind: "entities", ids: ["quest.*",
   "campaign.*"])` against the application state space that owns them.
2. Submit exactly `operation`, `campaignId`, `arcId`, `chapterId`, `questId`, and
   `expectedQuestStatus: "active"`.
3. The first attachment creates the empty-data arc-to-quest and chapter-to-quest context links.
   A later chapter in the same arc creates only its missing chapter link.
4. After success, read the campaign context back. It carries at most three active linked quests in
   quest-id order and at most three objectives per quest in quest-owned display order.

## Constraints
- The campaign, active arc, active-or-closed chapter, and Q3-valid active quest must already exist
  in the same quest-owned campaign/arc/chapter scope.
- This operation writes context links only. It never creates or changes quest/objective state,
  chapter/arc lifecycle, evidence, rewards, events other than structural link events, or prose.
- Both links use exactly `{}` data. Replay, cross-campaign, cross-arc, stale-status, reverse,
  duplicate, malformed, or conflicting attachment rejects atomically.
- There is no `campaign` commit kind, no `campaign-resume` query kind, and no `quest-summary`
  query kind. An application supplies this operation as a mechanic: resolve it with
  `query(kind: "system.interaction-plan")` and run it with
  `commit(kind: "system.interaction-execute")`.
- The campaign context read is a trusted-host view. Visibility/audience labels are descriptive
  and do not provide player authorization.
