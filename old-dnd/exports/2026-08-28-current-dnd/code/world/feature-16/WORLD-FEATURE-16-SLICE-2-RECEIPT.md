# World Feature 16 — Slice 2 receipt

Status: **verified**  
Date: 2026-08-20

## Delivered

- Added `commit(kind: "itinerary-advance")`, with the exact itinerary request, fingerprint, and
  leg index as its closed input.
- It re-reads the itinerary before any change, rejects a stale plan, maps only the named leg to
  Feature 8/12/13/15's existing action roles, and executes exactly that owner.
- It re-plans only after the owner action has committed, returning the reached state rather than a
  pre-authorized later leg.

## Evidence

- Focused stale/one-leg/re-plan coverage passes; the portal path moves the traveller once and
  preserves the root clock.
- `roleplay validate catalog`: **158 records valid** with 26 advisory warnings; no live state
  touched.
- Full suite and public-surface protocol walk: **447 passed**.

## Completion

Both Feature 16 slices are implemented and verified. The feature remains limited to proposing and
advancing one stored legal leg at a time; it does not add automatic travel, access, encounters,
provisions, or non-fixed teleportation.
