# E8 Slice 2 implementation — bounded indexed fan-out

Status: **Accepted.**  
Owner: `platform/e8/E8-DEPENDENCY-PLAN.md`, Slice 2  
Contract: `platform/e8/E8-SLICE-2-SELECTOR-RECONCILIATION.md`

## Exact boundary

Implement only the confirmed generic `fanoutSelectorJson` subscription-version field and its
bounded relationship/component-presence fan-out. Do not add a consumer subscription, game
terminology, component JSON filtering, scheduling, traversal, or JavaScript data access.

## Implementation sequence

1. Add the versioned field, migration, relationship indexes, content hashing, catalog transfer,
   commit payload, and readback support.
2. Validate the closed four-property selector at registration, including role/source/mode/scope,
   component-definition, and child-mechanic rules.
3. Route selected candidates in ordinal order after a complete preflight of candidates,
   projections, and execution budgets; execute each through the ordinary reaction/audit path.
4. Cover registration, deterministic selection, no-op, cap, preflight, failure, and compatibility
   behavior; validate catalog and run the complete suite.

## Exit condition

All Slice 2 contract acceptance evidence is automated and passing. The resulting generic platform
surface is documented by a receipt. Consumer migration remains out of scope.
