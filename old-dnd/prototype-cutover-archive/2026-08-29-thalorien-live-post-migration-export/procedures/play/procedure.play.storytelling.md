---
id: procedure.play.storytelling
category: play
name: Tell a grounded interactive fantasy story
governs: trusted-host narration and state-to-fiction interpretation during play
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Turn verified campaign and world state into vivid, player-directed fantasy narration. The world
store is canon; this procedure governs how a trusted host reads and presents it, not how state is
created, changed, authorized, or retained.

## Instructions
1. At a fresh start, read this procedure, then retrieve the active campaign through
   `query(kind: "campaign-resume", id: "campaign....")`. Read only the specific world entities
   needed for the current scene. Resume from stored state, never from an assumed transcript.
2. Treat `game.core.campaign.chapter` as the current dramatic question and
   `game.core.campaign.arc` as the continuing stake. Their entity names are titles; their closed
   component state and the campaign resume are authoritative. Only
   `procedure.campaign.chapter` changes chapter or arc lifecycle state.
3. Ground recurring NPC behavior in active `game.core.world.motive` records. Ground discoveries
   in `game.core.world.fact`, `game.core.world.rumour`, `game.core.world.secret`, and
   `game.core.world.clue` records and their governing links. Prefer an existing planted clue to an
   invented solution, and never make a required conclusion depend on one clue, one roll, or one
   NPC surviving.
4. Narrate only what the characters can perceive or have permissibly discovered. An unrevealed
   clue and every secret remain hidden in the fiction. Visibility labels are descriptive metadata,
   not authorization; this trusted-host procedure does not create player-safe filtering.
5. Preserve player agency. Describe consequences, reactions, involuntary perception, and the
   changed situation, then stop before choosing a character's dialogue, voluntary movement, plan,
   belief, emotion, or next action. Free action is the default; offer examples only to clarify a
   complex or stalled decision.
6. Resolve genuine uncertainty through an existing applicable mechanic before narrating its
   outcome. Treat the accepted structured result as a hard boundary: translate its visible
   consequence into fiction without adding or contradicting effects, resources, positions,
   conditions, discoveries, or costs.
7. A normal response has three movements: show the immediate consequence, develop one meaningful
   reaction/detail/complication, and end at a concrete decision point. Use present tense, concrete
   sensory detail, active verbs, varied sentence rhythm, and dialogue driven by a stored want.
   Match length to dramatic weight rather than padding a quiet beat into a report.
8. Keep durable changes with their owners. Use the governing campaign, world, quest, session, and
   rules procedures when a supported state transition is required; narration itself never commits
   a chapter summary, clue reveal, quest/objective transition, location move, item, relationship,
   condition, reward, recap, or combat result.

## Constraints
- This procedure creates no query/commit surface, persistent record, mechanics, events,
  subscriptions, automation, authorization policy, player identity, or generated-prose archive.
- Do not expose GM-only facts through omniscient narration, invent a hidden truth at payoff time,
  silently negate a declared action, or let attractive prose decide an uncertain outcome.
- Do not state that narration has advanced a quest, ended a chapter, resolved combat, revealed a
  clue, moved a character, or granted a reward. Confirm every supported change through its owning
  procedure and subsequent readback.
- A later session/recap feature owns factual closure and any attributed narrative artifact. This
  procedure may orient the next live decision from stored facts but does not generate or store a
  canonical recap.

