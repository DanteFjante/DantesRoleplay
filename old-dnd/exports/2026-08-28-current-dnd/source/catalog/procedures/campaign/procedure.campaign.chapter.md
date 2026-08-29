---
id: procedure.campaign.chapter
category: campaign
name: Maintain campaign chapters and arcs
governs: commit(kind: "campaign") continuity operations and query(kind: "campaign-resume")
status: active
---

## Description

Campaign continuity has exactly one active chapter and one active arc in this first delivery.
The campaign runner derives all structural effects, validates expected state inside one transaction,
and records its audit with the structural events. The resume view is trusted-host material, not
player authorization.

## Instructions

1. Initialize one active C2 campaign once with a re-ratified chapter and arc seed.
2. Advance or close only the named active chapter with a factual closing summary.
3. Conclude only the named active arc as `resolved` or `abandoned`; this never changes a chapter.
4. Read `query(kind: "campaign-resume", id: "...")` after a successful operation.

## Constraints

- Callers never supply effects, child IDs, relationship data, events, audits, or milestones.
- C3 does not create quests, world content, characters, clocks, sessions, or branching arcs.
- A closed chapter does not resolve an arc; a terminal arc is never revived by this procedure.
