# D&D 2024 local-table authorization repair Slice 1 receipt — restored binding

Completed: 2026-08-29

## Delivered boundary

- Restored `catalog/applications/dnd2024/metadata/authorized-knowledge.json` from the archived D&D binding contract.
- Kept the exact `dnd2024.game.core.*` component and relationship identifiers stored by the live D&D state space.
- Activated the valid D&D application preview at activation revision 6.

## Evidence

- The final activation operation `65ff9bfcbbac46cd99ef6e471380d199` completed after an identical successful dry run.
- `GET /api/audience-context` returned `200` with `status: "bound"` for the existing D&D application, Brackenford campaign, and configured actor.
- `http://localhost:5173/` returned `200`, included **The Waystone at Brackenford**, and did not include the unavailable-table fallback.

## Deliberate exclusions

No campaign, actor, participation, knowledge-state, rule, schema, authorization-policy, or prototype UI data was changed.
