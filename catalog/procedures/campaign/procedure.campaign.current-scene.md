---
id: procedure.campaign.current-scene
category: campaign
name: Select the campaign current scene
governs: commit(kind: "component") declaring game.core.campaign.current-scene; commit(kind: "effects") adding, replacing, or removing one reviewed campaign current-scene record
status: active
---

## Description

Defines one exact present-tense scene selector for a campaign. It points at the current location and
may retain a current conversation, a current encounter, or both without copying any referenced
world, interaction, or ruleset state.

## Instructions

1. Attach `game.core.campaign.current-scene` only to an existing active campaign root.
2. Record exactly one location reference and optional conversation and encounter references. Every
   reference is an object containing only `entityId`.
3. The location must be an active location in the campaign's existing world. A conversation must
   be an accepted `game.core.world.interaction` whose kind is `conversation`. An encounter must be
   accepted by the active application's encounter owner.
4. Resolve the visible scene deterministically: an encounter reference selects Combat; otherwise a
   conversation reference selects Conversation; otherwise the location selects Exploration.
5. Keeping both optional references means an encounter temporarily takes priority over an existing
   conversation. Removing the encounter reference resumes that conversation without inferring it.
6. Replace the component as one closed record after reading and validating every reference. Remove
   it when the campaign has no authoritative current scene.

## Constraints

- The selector contains no names, summaries, participants, Initiative, turn state, observations,
  routes, actions, visibility decisions, or copied location state.
- The selector does not start or end a conversation or encounter, move an actor, decide audience
  access, execute travel, calculate a D&D rule, or generate narration.
- A reader must validate exact referenced state and audience access. It may not select the first
  location, interaction, or encounter, infer a focus from prose, or downgrade a malformed focus to
  a different scene kind.
- Actor-facing current-scene reads must agree with that actor's authoritative `presence`
  containment. A campaign-authorized Game Master may read the selected location without an actor.
- Adding, replacing, or removing the selector uses the generic effects transaction and its normal
  audit evidence; this procedure adds no public protocol operation or website write surface.
