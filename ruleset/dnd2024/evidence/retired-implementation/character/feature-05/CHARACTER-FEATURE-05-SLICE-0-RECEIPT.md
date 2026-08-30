# Character Feature 5 Slice 0 receipt — staged composition proof

Status: **Implemented and accepted.**

## Delivered boundary

- `IStagedWorldComposer` builds an immutable, read-only overlay of an ordered effect bundle.
- A root reserves one target, declares every entity ID children may touch, and starts with the
  target creation effect. Each appended fragment is revalidated against the full bundle.
- The overlay implements the normal read interface for existing validators and rejects every
  mutation call. It stores no actor, campaign, component, receipt, event, or audit row.
- C15's `ICampaignCharacterParticipationPlanner` returns the four canonical attachment effects
  against either a persistent actor or that staged target. It performs no transaction, write, or
  audit.

## Evidence

- `CharacterFeature05Slice0Tests` passed: **2/2** on 2026-08-21.
- The positive path stages an absent actor, appends C15 participation, then invokes the unchanged
  CH1 profile and CH2 ability planners against that same virtual scope. It proves no actor or
  participation exists before the final root-owned apply and that the resulting seven-effect
  bundle applies atomically.
- The negative path proves direct staged-world mutation throws and a fragment naming an undeclared
  actor is rejected with `STAGED_ENTITY_NOT_ALLOWED`; persistent state remains absent.

## Deferred

No CH5 create/validate request, receipt, action mechanic, MCP surface, character content,
definition selection, item creation, or permanent actor state is included. Those remain CH5
Slices 1–2 and their named CH3/CH4/Items/ruleset dependencies.
