# DND2024 adaptive Current View — Slice 4 receipt

Date: 2026-08-30
Status: **source implementation complete; feature acceptance pending**
Ruleset alignment: **dnd2024-compatible presentation composition**

## Delivered boundary

Conversation and Combat now retain the exact audience-projected location description and
observations instead of replacing all place context with mode-specific data. Conversation composes
that context with visible participants. Combat composes it with Initiative and the active turn.
The existing DM-only location note is rendered consistently in all three scene modes and remains
absent when the audience projection omits it.

The component uses only the existing `WorldLocation` and `CurrentSituationReadModel` inputs. It does
not select mechanics, derive available actions, infer affordances from prose, or create a write path.

## Evidence

- Focused Current View presentation checks: **2/2 passed**.
- Full DND2024 web suite: **133/133 passed**.
- Production server build: **passed**, 1,622 modules transformed.
- Browser preview reached the production page and its expected private-table unavailable state. A
  standalone preview has no game-server audience binding, so authenticated Conversation/Combat
  visual inspection was not available; the temporary preview tab and server were closed.
- Catalog validation was not required because no catalog artifact changed.

## Deliberate exclusions

Declared available actions, mechanic eligibility, conversation choices, combat action selection,
travel execution, activation/deployment, and final feature acceptance remain outside this slice.
The generic application action control is an execution surface for an already selected mechanic;
it is not treated as an action-discovery owner.
