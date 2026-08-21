# Session Feature S4 dependency plan — checkpoint evidence, interruption recovery, and scoped restore

Status: **Planned; implementation awaits the S0 checkpoint policy, accepted S1–S3 session lifecycle, a snapshot owner, and C11-compatible domain classification.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.mcp.add-tool`, the [Session Operations Plan](../../SESSION_OPERATIONS_PLAN.md), [S0](../feature-00/SESSION-FEATURE-00-DEPENDENCY-PLAN.md), [S1](../feature-01/SESSION-FEATURE-01-DEPENDENCY-PLAN.md), [S2](../feature-02/SESSION-FEATURE-02-DEPENDENCY-PLAN.md), [S3](../feature-03/SESSION-FEATURE-03-DEPENDENCY-PLAN.md), [Campaign Feature C8](../../campaign/feature-08/CAMPAIGN-FEATURE-08-DEPENDENCY-PLAN.md), and [Campaign Feature C11](../../campaign/feature-11/CAMPAIGN-FEATURE-11-DEPENDENCY-PLAN.md). It writes no runtime artifact.

S4 owns session-facing checkpoint evidence and the session recovery boundary. It does not assume that an audit row, recap, database copy, or event history is itself a restorable snapshot. Any restore is a named, scoped, owner-approved transaction—not a file overwrite or generic rollback.

## Target capability

A trusted host can obtain and inspect one named checkpoint reference for a S0-approved campaign-session boundary, including its declared scope, source/version evidence, and recovery status. A host interruption leaves a valid active session resumable through S2; it never creates an implicit checkpoint or partial session transition. If—and only if—S0 selected restore and every state domain/conflict/transaction owner is accepted, an authorized host can validate and restore exactly the checkpoint’s declared scope atomically with auditable proof.

The first fixture is one S0-ratified campaign/session checkpoint after an S1/S3 lifecycle boundary. It proves a reusable reference-and-scope contract, not full database backup, arbitrary time travel, branch creation, undo of every game action, automatic crash recovery, or cross-campaign copying.

### Included

- One named checkpoint identity/reference with explicit capture boundary, declared scope/version, owner provenance, and bounded readback.
- An evidence-only checkpoint path as the default S0 policy: create/inspect reference evidence without a restore write.
- Interruption/restart behavior: atomic roots leave no partial record; an active session resumes through S2 and a closed session is read historically through S3.
- A separately gated restore branch that classifies every relevant domain as restored, referenced, unchanged, or unsupported before it writes.
- C11-compatible checkpoint identity/audit proof and domain classification handoff; C11 remains read-only fork preview owner.

### Excluded

- Unscoped database/file backup or restore, destructive overwrite, automatic rollback/undo, partial domain restore, best-effort copy, conflict guessing, checkpoint selection by an AI, or restoring a newer campaign without explicit policy.
- Starting/resuming/ending sessions or writing recaps, player roster/control, gameplay action wrapping, action/event replay, travel/time/quest/world/character/item mutations, branch creation, merge/reconciliation, or browser write controls.
- Treating event/audit history, a chat transcript, a model memory, a recap, a file timestamp, or an operation ID alone as a checkpoint.

## Ownership and state classification

| Concern | Authoritative owner and S4 rule |
| --- | --- |
| Session lifecycle/recap | S1/S3/C8. S4 validates the selected session boundary but does not reopen/end/alter recap state except through an explicitly approved restore root. |
| Checkpoint identity and reference | S4/C8, or a confirmed generic snapshot owner. It records a named pointer plus declared scope/evidence, never an unclassified deep copy embedded in a session. |
| Snapshot bytes/transaction log/storage medium | Dedicated snapshot/save owner, currently missing. S4 must not choose database-copy, SQLite file, event replay, or cloud storage semantics by itself. |
| Campaign/world/quest/character/item/action domains | Their own plans. Restore classifies and invokes each owner only after it accepts the exact restore semantics; S4 cannot raw-write components/relationships. |
| Domain classification/fork preview | C11. S4 supplies stable checkpoint identity/scope/provenance; C11 classifies reference/copy/unsupported for read-only fork preview and never creates a fork. |
| Identity/authorization | CH14/identity policy when restore is enabled. Initial trusted-host checkpoint evidence grants no player restore capability. |
| Events/audit/notifications | Existing root transaction policy. Evidence is auditable but operation IDs/events are not checkpoint payloads. |

The checkpoint model has four deliberately separate concepts:

1. **Boundary:** the exact session/campaign state moment selected by an approved root.
2. **Reference:** stable checkpoint identity plus storage/provenance/scope declaration.
3. **Snapshot contents:** data preserved by the snapshot owner, never inferred from the reference.
4. **Restore plan:** a later typed, validated domain-by-domain effect plan, never an inverse of arbitrary history.

Collapsing these concepts would make a recap look restorable or let a storage implementation silently decide game semantics.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Checkpoint record | `game.core.campaign.session-checkpoint`, attached to a distinct checkpoint entity or supplied by a generic snapshot owner. It contains only stable checkpoint identity, `scopeContractVersion`, `captureBoundary`, storage/provenance reference, and lifecycle/availability metadata; exact schema waits for snapshot-owner confirmation. |
| Session checkpoint scope | `game.core.campaign.session.has-checkpoint`, a directed relationship from session to checkpoint with empty data, only if the generic snapshot owner lacks an equivalent scope link. |
| Governing contract | `procedure.campaign.session`, extended with checkpoint/read/recovery semantics; any restore operation also requires the snapshot owner's procedure. |
| Checkpoint mechanism | `mechanic.game.core.campaign.session.checkpoint`, a C8 coordinator that obtains a typed snapshot-owner result and never writes snapshot bytes/effects directly. |
| Restore mechanism | **Intentionally unnamed until scope ownership is confirmed.** It is not authorized by an evidence-only checkpoint and cannot be a generic `restore` endpoint. |

Confirm whether a generic snapshot/checkpoint record already exists, identity allocation, capture boundary, storage/retention/availability lifecycle, precise scope contract, session link direction, audit evidence, C11 selector compatibility, and restore authority before authoring. If a generic owner exists, S4 uses it rather than adding session-specific duplicate state.

## Checkpoint and restore boundaries

### Evidence-only default

Under S0’s recommended policy, the checkpoint operation may validate/create one named reference after the snapshot owner confirms a captured boundary. It returns `checkpointId`, `sessionId`, `campaignId`, `scopeContractVersion`, availability, and literal next action. It accepts no domain list, storage location, bytes, file path, SQL, raw effects, audit/event ID, transcript, or caller-made snapshot. A checkpoint read returns only its bounded metadata and declared classification, not protected snapshot contents.

Interruption does not alter durable session lifecycle automatically. If a host stops after a successful S1 start, S2 resumes the still-active session; if it stops during an atomic root, the root commits fully or rolls back. If it stops after S3 end, the historical S3 read applies. A missing/corrupt checkpoint is named evidence failure, not permission to reconstruct state from chat or current data.

### Restore branch — explicitly gated

If S0 selects C8 restore, or a later amendment promotes the evidence-only model, the restore request may be designed only after all prerequisites are met. It must bind a canonical checkpoint identity, an authorized principal/host context, expected current state versions, and a confirmed restore policy. It may not accept a file path, target campaign, arbitrary scope, domain toggle, raw effect, `force`, or client-declared conflict resolution.

Before a restore can validate, the snapshot owner and each domain owner must produce a canonical plan classifying every affected campaign, session, world, quest, character, item, relationship, containment, resource, event/audit, and external artifact as exactly one of:

- **restored** by a named owner through typed effects;
- **referenced** and intentionally unchanged;
- **preserved current** because it is outside scope; or
- **unsupported**, which blocks restore.

The plan also states newer-state conflict policy, active-session/participant constraints, source/provenance compatibility, event/audit behavior, notification policy, cancellation/timeout, recovery after failure, and fresh readback. Any domain not classified blocks. A generic database replacement, `--force`, or deletion is never a valid restore implementation.

## Resolution and transaction rules

1. Resolve one valid session, its single campaign scope, selected lifecycle boundary, and S0-approved checkpoint timing. Reject missing/multiple/corrupt/dangling/cross-scope session or checkpoint state.
2. Ask the snapshot owner to validate/capture the exact boundary and return typed reference/evidence. S4 verifies scope-contract version and C11-compatible provenance; it never serializes domain bytes itself.
3. For checkpoint evidence, create/reference only the confirmed checkpoint record/link in one C8 root transaction. `validate` returns zero effects; capture cannot use a stale validation result.
4. For ordinary interruption, use S2/S3 read behavior. Do not create a checkpoint, mutate a status, or retry an incomplete root automatically.
5. For restore, obtain the complete domain classification and typed owner effects first. Validate authorization, lifecycle, expected current versions, source compatibility, checkpoint availability, and every cross-domain guard inside one confirmed outer root.
6. Apply only the agreed typed restore effects in canonical owner order; events, notifications, checkpoint availability change, and success audit participate atomically as confirmed. Any failure/cancellation/timeout rolls back every restore effect and leaves the checkpoint readable. Failure audit follows existing policy.

If no single root can atomically compose all restored domains, restore is unavailable even if checkpoint evidence exists. The system may still inspect the checkpoint or run C11 fork preview; it must not partially restore a campaign.

## Dependency graph and slices

~~~text
S0 checkpoint policy + S1/S3 session boundaries
├─ generic snapshot/checkpoint storage and provenance owner                 [missing primary leaf]
├─ C11 checkpoint selector/domain-classification compatibility              [consumer gate]
├─ confirmed C8 reference/identity/retention contract                       [semantic gate]
├─ S2/S3 interruption and historical-read evidence                          [continuity prerequisite]
└─ restore only: identity authorization + every domain owner + outer root   [separate restore gates]
   ├─ Slice 1: evidence-only named checkpoint and readback
   ├─ Slice 2: interruption/restart proof and C11 handoff
   └─ Slice 3: optional fully scoped atomic restore after all domain proof
      └─ C11 fork preview and later branch/collaboration work
~~~

### Slice 1 — named checkpoint evidence

**Prerequisites:** S0 selects evidence-only or authorizes its prerequisite capture owner; S1/S3 lifecycle boundary and generic snapshot/reference owner are accepted; C11 identity/provenance expectations are confirmed.

1. Add the confirmed checkpoint vocabulary/reference relationship only if no generic owner supplies them; implement zero-effect validate and typed capture/reference composition.
2. Test valid checkpoint after each allowed boundary, no/corrupt/dangling/cross-scope session, unavailable snapshot owner, ID/replay/collision, scope version mismatch, metadata bounds, audit provenance, and no raw bytes/domain copy.
3. Query checkpoint/session/campaign fresh and prove C11 can select/classify the reference without a fork/write.
4. Run focused tests and `roleplay validate catalog` after catalog work.

**Exit:** one named checkpoint has inspectable, versioned scope/provenance evidence without promising a restore or copying unclassified game state.

### Slice 2 — interruption and continuity proof

**Prerequisites:** Slice 1 accepted; S1/S2/S3 atomic transaction/read contracts are verified.

1. Simulate host loss after start, after independently committed play, during failed/cancelled checkpoint, and after end; prove exact S2/S3/checkpoint readback paths with no repair write.
2. Test failed root rollback, stale/replayed checkpoint, unavailable/corrupt reference, fresh-host recovery, C11 no-write preview handoff, and no transcript/cache/database-file fallback.
3. Run focused continuity/protocol tests and full suite at S4 evidence acceptance.

**Exit:** interruption has deterministic recovery/read behavior and checkpoint evidence remains trustworthy; no incomplete root produces a fabricated or partial snapshot.

### Slice 3 — optional atomic restore

**Prerequisites:** S0 selected restore or an approved amendment; every affected domain has a restore classification/owner; identity authorization, conflict policy, snapshot bytes, outer transaction, events/audit, and failure recovery are independently accepted.

1. Produce a dry-run canonical restore plan that explicitly classifies every domain and rejects any unsupported/unclassified/newer-conflict state.
2. Implement one narrow fixture restore through the confirmed outer coordinator with no raw database/file overwrite.
3. Test authorization denial, wrong/unavailable/corrupt checkpoint, every domain mismatch, stale/newer state, owner/effect/guard/reaction/event/audit failure, cancellation, timeout, retry, rollback, and fresh readback.
4. Run focused restore tests, catalog validation where applicable, full suite, protocol/security walk where surface changes, and a recovery rehearsal before acceptance.

**Exit:** the exact declared fixture scope restores atomically and audibly, or the feature remains evidence-only; there is no partial/forced restore mode.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Checkpoint identity | One named checkpoint links to exactly one allowed session boundary and declared scope/provenance contract. A recap, event, audit ID, chat, or database file alone never qualifies. |
| Evidence-only safety | Creating/reading checkpoint evidence creates no implicit restore capability, domain copy, action replay, player authority, or mutation outside confirmed reference state. |
| Interruption | Atomic operation failure leaves no partial checkpoint/session effects. Host restart follows S2 for active and S3 for ended sessions; no automated repair/rollback is invented. |
| Domain classification | A restore plan lists every domain as restored/referenced/preserved/unsupported. Unclassified or unsupported data blocks the entire restore before effects. |
| Restore atomicity | If enabled, every typed owner effect, lifecycle/participant compatibility step, event/notification, and audit commits or rolls back together. Newer-state conflict never uses force overwrite. |
| C11 compatibility | C11 can inspect one checkpoint and produce a deterministic no-write fork classification from its declared scope/provenance; S4 never creates a fork. |
| Scope boundary | S4 does not own session lifecycle/recap, campaign/world/quest/character/item/rules truth, roster/player control, narration, collaboration, or generic backup service. |
| Fresh evidence | A fresh host reads checkpoint metadata and recovery state from durable owners alone; no private file path, transcript, cache, or model memory is needed. |

## Evidence and change control

The implementation receipt records S0 policy, confirmed snapshot/checkpoint/restore owners, checkpoint scope contract/version, C11 handoff, canonical capture/read fixtures, interruption proofs, domain classification table, and—only if enabled—restore conflict/rollback/recovery/fresh-host evidence, catalog validation, full suite, and protocol/security results. It does not contain snapshot bytes, credentials, file paths, raw effects, secrets, chat, or root operation IDs as payload.

Amend S4 before supporting a new checkpoint boundary/domain, automatic capture, restore scope, database/file overwrite, branch creation/merge, player self-service, remote/cloud storage, active-session continuation after restore, retention/purge, browser controls, or multi-host recovery. Those belong to the snapshot/save owner, C11/new fork plan, CH14/identity, Website/API/deployment, S5/S8/S9, or a dedicated data-lifecycle plan.
