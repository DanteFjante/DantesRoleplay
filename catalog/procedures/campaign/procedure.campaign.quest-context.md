---
id: procedure.campaign.quest-context
category: campaign
name: Attach quest context to campaign continuity
governs: commit(kind: "campaign") operation attach-quest-context
status: active
---

## Instructions

1. Read the active quest with `query(kind: "quest-summary", id: "quest.*")` and the campaign with
   `query(kind: "campaign-resume", id: "campaign.*")`.
2. Submit exactly `operation`, `campaignId`, `arcId`, `chapterId`, `questId`, and
   `expectedQuestStatus: "active"`.
3. The first attachment creates the empty-data arc-to-quest and chapter-to-quest context links.
   A later chapter in the same arc creates only its missing chapter link.
4. After success, read campaign resume. It returns at most three active linked quest summaries in
   quest-id order and at most three objectives per quest in quest-owned display order.

## Constraints

- The campaign, active arc, active-or-closed chapter, and Q3-valid active quest must already exist
  in the same quest-owned campaign/arc/chapter scope.
- This operation writes context links only. It never creates or changes quest/objective state,
  chapter/arc lifecycle, evidence, rewards, events other than structural link events, or prose.
- Both links use exactly `{}` data. Replay, cross-campaign, cross-arc, stale-status, reverse,
  duplicate, malformed, or conflicting attachment rejects atomically.
- Campaign resume is a trusted-host view. Visibility/audience labels are descriptive and do not
  provide player authorization.
