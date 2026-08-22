# Campaign Feature 11 dependency plan — campaign fork preview

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; blocked by verified C8 checkpoint/snapshot/restore evidence and fork vocabulary confirmation.**
Last updated: 2026-08-20

## Target capability

A host can inspect a named audited checkpoint and receive a read-only campaign-fork preview that
classifies each state domain as referenced, copied, or unsupported before any fork is created.

## Included and excluded

Included: one checkpoint selector; deterministic inclusion classification for campaign, session,
world, quest, character, and item domains; provenance/source evidence; copy counts; unsupported
reasons; and no-write preview tests.

Excluded: creating a fork, deep-copying relationships, cloning worlds/characters/items/quests,
cross-database copying, merge/reconciliation, multiplayer branches, website controls, or automatic
checkpoint selection.

## Ownership

Campaign lifecycle owns fork preview and provenance. Every referenced domain retains authority over
its own copy/reference semantics. No campaign code may silently deep-copy another owner’s graph.

## Dependencies and slices

~~~text
C11 campaign-fork preview
├─ C8 session operations and named checkpoint evidence        [must be verified]
├─ snapshot/restore scope proof                                [must be verified]
├─ confirmed state-domain classification policy                [semantic leaf]
│  └─ Slice 1: read-only preview
└─ actual campaign fork                                        [excluded future feature]
~~~

## Required confirmation

Confirm checkpoint identity/audit proof, supported state domains, reference/copy/unsupported
classification, canonical ordering, copy counts, provenance fields, visibility handling, and
stable unsupported/recovery results.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Valid checkpoint | Stable preview lists each domain and classifies every record without writes. |
| Missing/corrupt checkpoint | Recoverable rejection; no inferred snapshot and no state change. |
| Unsupported domain | Explicit unsupported reason; it is not silently omitted or copied. |
| Determinism | Same checkpoint/state yields identical classification/order/counts. |
| Visibility | Preview respects confirmed host/GM boundary and does not reveal excluded data. |
| No-write | No campaign, world, quest, character, item, event, notification, or audit state is created. |

## Exit gate

C11 completes when a host can safely decide whether a future fork is possible from an explicit
preview. Actual forking requires a new feature plan.
