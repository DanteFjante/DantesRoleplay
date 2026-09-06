---
id: procedure.campaign.scene-affordances
category: campaign
name: Declare current-scene narrative affordances
governs: commit(kind: "system.component-type.register") declaring game.core.campaign.scene-affordances; commit(kind: "system.world-state.sync") adding or replacing one reviewed campaign scene-affordance record
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
Declares a bounded set of narrative opportunities that the campaign author has chosen to present for
one exact current scene. It is presentation state, not a rule or execution surface.

## Matches

## Instructions
1. Attach `game.core.campaign.scene-affordances` only to an existing active campaign root that has a
   valid `game.core.campaign.current-scene` record.
2. Copy the current-scene record's exact location and optional conversation/encounter references into
   `scene`. Do not derive, omit, add, or reorder an optional reference.
3. Record zero to 24 items. Each item contains exactly a campaign-local kebab-case `key`, a concise
   `label`, a descriptive `summary`, and visibility `party` or `gm`.
4. Keep keys unique across the record. Authored order is presentation order; it is not priority,
   eligibility, timing, or initiative.
5. `party` means the item may be included in an authorized Player projection. `gm` means it is
   available only in an authorized Game Master projection. Visibility never grants a mechanic or
   bypasses other authorization.
6. Replace the component only after re-reading the current-scene record and validating exact equality.
   When current-scene changes, replace or remove this component in the same reviewed authoring
   boundary so stale affordances cannot describe the new scene.
7. Remove the component when no authored scene opportunities are intended or no current scene exists.

## Constraints
- The component contains no mechanic or procedure ID, action type, role binding, target, input,
  eligibility result, resource cost, DC, roll, effect, outcome, route, dialogue response, generated
  text, or copied world/encounter state.
- An affordance is informative. It does not assert that an attempted action succeeds, that a D&D
  Action or Bonus Action is available, or that the browser can execute it.
- Readers must validate the full selector against the current-scene record. A location-only match is
  insufficient when conversation or encounter references differ.
- Missing, malformed, oversized, duplicate-key, stale, or unauthorized records fail closed without
  changing the current scene or revealing hidden item text/counts.
- Adding, replacing, or removing the record uses the generic effects transaction and normal audit
  evidence. This procedure creates no public protocol operation or website write surface.
