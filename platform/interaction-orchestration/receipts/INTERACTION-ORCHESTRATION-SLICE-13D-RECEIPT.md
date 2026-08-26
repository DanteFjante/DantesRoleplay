# Interaction orchestration Slice 13D receipt — bounded task agendas and fresh-state work batches

Status: **accepted 2026-08-25**

## Delivered boundary

- Both explicitly selected outer providers support the permanent no-tools task
  `system.interaction.task-agenda` with the strict `interaction_task_agenda_v1` schema. Provider
  selection remains startup-only and never falls back across local/remote boundaries.
- The outer AI may return 1–8 ordered intent-level tasks, 1–4 batches per task, at most 16 total
  batches, and earlier-task dependencies. The server rejects missing/unknown/duplicate fields,
  invalid/future dependencies, control characters, and every confirmed count/UTF-8/JSON/depth
  boundary, then assigns all goal/task/batch identities itself.
- The existing private application conversation owns bounded process-local progress. Each batch is
  independently resolved inner-first against current state/contracts, with at most one typed outer
  fallback. Only one inert proposal can await confirmation.
- Executing one confirmed batch can persist only that existing proposal. A successful durable
  receipt completes the batch and performs at most one fresh next planning attempt; the next
  proposal always waits for a separate confirmation. No background or multi-execution loop exists.
- Failed/unresolved work pauses the whole agenda, blocks dependency descendants, and starts no
  independent task automatically. Receipt/idempotency conflicts without durable work leave the
  exact pending batch unchanged.
- `activeAgenda` safely exposes bounded task/batch progress and resolution/execution receipt IDs.
  Optional `replaceActiveAgenda` explicitly discards only unfinished process memory. The reusable
  web element shows progress and requires an explicit replacement choice.
- The accepted per-batch `learn` option remains unchanged. No automatic outer-fallback learning,
  recipe promotion, database workflow, migration, route, MCP tool, authorization capability, or
  game rule was added.

## Evidence

- Focused task-agenda/provider/planning suite: **39 passed**.
- Focused application-conversation workflow/UI suite: **18 passed**.
- Broader agenda/provider/conversation compatibility run: **42 passed**.
- `dotnet build DantesRoleplay.slnx --no-restore`: **0 warnings, 0 errors**.
- `dotnet test DantesRoleplay.slnx --no-restore`: **845 shared tests and 20 local-AI tests passed**.
- Catalog validation: **144 records valid** (14 mechanics, 50 procedures, 33 components,
  10 event types, 2 subscriptions, and 35 entities); the existing 21 advisory warnings remained,
  and no live data was touched.
- EF migration drift check: no model changes remain after the current worktree migrations; Slice
  13D itself added no migration or persistence entity.
- Protocol walk: **6 passed, 2 intentionally skipped, 0 failed**.
- `git diff --check`: passed with working-copy line-ending notices only.
- Ruleset-neutral production scan found no D&D, caravan, attack, or `game.core` vocabulary in the
  task-agenda/provider/coordinator boundary.

## Deliberate stop

Slice 13D adds no durable agenda recovery, whole-goal consent, parallel/background execution,
cross-batch transaction or value binding, automatic continuation after failure, arbitrary model
tool/code access, or automatic recipe generalization/promotion. Those concerns remain excluded or
assigned to Slices 13E–13F. Slice 13E has not begun.

The user confirmed the complete Slice 13D semantic/public contract and completed-feature acceptance
on 2026-08-25.
