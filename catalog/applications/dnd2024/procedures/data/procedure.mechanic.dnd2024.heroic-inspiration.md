---
id: procedure.mechanic.dnd2024.heroic-inspiration
category: ruleset.dnd2024.core.state.heroic-inspiration
name: Record one available D&D 2024 Heroic Inspiration instance
governs: dnd2024.heroic-inspiration; mechanic.dnd2024.heroic-inspiration.grant
status: active
---

## Description

Owns the presence state for the one Heroic Inspiration instance that a player character currently
holds. The component is present only while that instance is available. This procedure records a
normal grant; it does not decide a source trigger, use, transfer, or correction.

## Instructions

1. Declare `dnd2024.heroic-inspiration` as a closed empty object. Presence represents exactly one
   available instance and absence is the only representation of none. Do not add availability,
   count, source, recipient, rest, die, result, expiry, or history fields.
2. `mechanic.dnd2024.heroic-inspiration.grant` accepts one `subject` role and exactly `{}` input.
   The subject must carry a valid nonempty `dnd2024.character.profile`; caller input never proves
   character eligibility or held state.
3. Missing Heroic Inspiration permits one `component.add` with canonical `{}` data. Valid present
   state is a duplicate-grant failure. Malformed state or an absent/invalid profile fails before
   effects.
4. Return fixed *Rules Glossary > Heroic Inspiration* provenance, no randomness, no events, and no
   notifications. Inspect no rest, species, feat, party, campaign, encounter, item, class, die, or
   prior action result.

## Constraints

- This grant does not establish a profile, campaign attachment, control, source-specific
  eligibility, Human Resourceful trigger, Long Rest outcome, or character-creation completion.
- Do not clear, consume, reroll, transfer, replace, duplicate, overwrite, or repair the state here.
  Those transitions require their own exact rule context and owners.
