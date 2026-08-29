---
id: procedure.mechanic.dnd2024.character-profile
category: ruleset.dnd2024.character.profile
name: Compose a C15-gated actor biography fragment
governs: CH1's C15-gated actor biography fragment only
status: active
---

## Description

CH1 records only optional campaign-visible descriptive text on an existing actor whose active
campaign scope is proven by C15. The actor entity name remains the display name. This procedure
does not create an actor, attach it to a campaign, expose player control, or add any D&D state.

## Instructions

1. A character-creation root asks the internal profile recorder to resolve the actor through
   C15's active-scope verifier before constructing its profile effect.
2. Supply one closed profile object containing only optional `pronouns`, `appearance`, and
   `biography`; omitted fields remain absent and `null`, blank, untrimmed, overlong, or extra
   fields are rejected.
3. The root applies the returned single `component.add` alongside its other approved effects. The
   profile recorder never opens a nested transaction or public command.

## Constraints

- The component contains no display name, campaign ID, relationship, source reference, ability,
  class, level, grant, item, biography visibility label, account, or authorization claim.
- Missing, withdrawn, malformed, duplicate, or inactive C15 participation yields no profile
  effect. A profile cannot establish campaign membership.
- The catalog mechanic is a draft composition declaration; CH5 alone activates an executable
  creation root once its staged-composition protocol is accepted. No public `action` route may
  bypass C15 scope verification.
- The permanent profile component is `dnd2024.character.profile`; the reserved composition
  declaration is `mechanic.dnd2024.character-profile.record`.
