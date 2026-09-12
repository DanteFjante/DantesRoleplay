# Upgrade coordination and recovery

This is the durable entry point for the current work. The user explicitly requested repository plans and progress records because task messages can disappear. Read this page, then only the assigned workstream document. Update the checkpoint when ownership, a decision, verification, a commit, or the next action changes.

## User requirements

- Land the completed generic platform upgrade on master, preserving the latest master changes.
- Delete every LOCAL branch except master after preserving unfinished and historical work. Leave GitHub branches, remote refs, and stashes alone.
- Make the MCP manual extensive and usable from an AI's first session, with intent-based selection of relevant instructions.
- Rework shared website navigation, design, reusable components, and themes. Show applications with usable published pages; do not show supporting/non-website applications as disabled navigation.
- Improve DND2024 pages with reusable components, API reads/actions/forms, and local handling of incomplete JSON.
- Produce an actionable DND2024 upgrade plan with compatible parallel assignments and economical model use.

Display data may contain additional fields. Each component checks the fields it needs and contains its own missing-data failure. Transport provenance, authorization, scope, freshness, and mutation validation remain strict.

## Current checkpoint

Last coordinator update: 2026-09-12, 09:28 UTC. This is a checkpoint, not a claim that later work has finished.

| Boundary | Verified state |
| --- | --- |
| Original platform plan | All 30 scoped slices implemented and integrated at 9395cd8e |
| Platform acceptance | Corrected full suite at fe60517c passed 3,452/3,452 with zero failures/skips in 16m26s; final opt-in protocol and catalog checks are active |
| master documentation checkpoint | 2ae9168c preserves all workstream checkpoints; platform code has NOT yet been landed there |
| Prepared master/platform merge | Detached 8e2b9da20fd827dc4bd5e767165a7edefc5387e3, zero conflicts; preserves master, includes fe60517c and guide alignment; all non-documentation files match the platform test target |
| Existing guide alignment | 497b212c incorporated in the candidate as e32878d8 |
| Branch cleanup | Refreshed verified backup covers 41 local branches, 29 worktrees, and two stashes; deletion prepared but not run |
| Manual | e0c8e95f and 8ee31837 delivered; 45 focused checks plus four final theme-retrieval checks passed, 618 catalog records valid; combined manual/website integration assigned to the same worker |
| Shared website | ff3c486d delivered; 63 browser and three focused host tests passed; desktop/mobile and light/dark previews inspected |
| DND website | 220018aa delivered after shared ff3c486d; full mounted tests 79/79, typecheck/build passed; root preview recheck shows zero accessibility violations in the controlled inventory fixture in light and dark |
| DND upgrade plan | cc0fd197 and compatibility revision 03d3f25e delivered; parallel ownership, shared theme/navigation, existing website work, and durable checkpoints reconciled |

Implementation, verification, landing on master, and deployment are separate states. Never call a worktree delivery a master update or a live deployment.

Latest pre-cleanup backup: `C:/repo/DantesRoleplay/.tmp/branch-cleanup/20260912-111912/local-refs-pre-cleanup.bundle`, SHA256 `FD74420DE89A742BFDE0BFD7AFF0E6D459FE65A07AC2F8D087F3462593D1E2C7`. The adjacent manifest records exact branch tips, worktree commits, remote refs, and stashes. Active detached working directories are preserved. Cleanup helpers are in the original checkout's ignored `.tmp` directory; run the final helper only after acceptance releases the foundation checkout and the accepted platform is an ancestor of master.

## Workstreams and ownership

| Workstream | Durable plan/checkpoint | Worker |
| --- | --- | --- |
| Platform verification, master landing, local cleanup | [01](upgrade/01-platform-master-cleanup.md) | root agent website_integration for verification; coordinator for landing/cleanup |
| Extensive MCP manual | [02](upgrade/02-mcp-manual.md) | root agent final_guide_alignment |
| Shared website navigation, theme, components | [03](upgrade/03-shared-website.md) | task 01a0917b-34d0-7cc1-bbbf-9689abd0b2e9 |
| DND2024 component and page improvements | [04](upgrade/04-dnd2024-website.md) | root agent workflow_publication_finish |
| DND2024 upgrade implementation plan | [05](upgrade/05-dnd2024-plan.md) | task 01a09179-4f0e-7c00-9411-cddb4b9ecbba |

The coordinator task is 01a09056-6d13-76d0-8706-40e435fb8b03. Agent names and task IDs are recovery hints, not a substitute for files and Git evidence. If an agent is unavailable, a replacement reads its workstream checkpoint and exact implementation owners.

## Operating agreement

1. Workers use isolated DETACHED worktrees. Do not create more branches.
2. One writer owns an integration checkout at a time. Do not edit source or catalog while its full suite is reading them.
3. Workers own their code lane and their checkpoint file. The coordinator owns shared decisions, integration, master updates, cleanup, and this index.
4. Update checkpoints in the original checkout at C:/repo/DantesRoleplay/docs/current/upgrade. Do not run Git commits in that checkout concurrently; the coordinator serializes documentation commits. Workers commit code only in their assigned detached checkout.
5. Keep checkpoints concise: current commit and dirty state, last completed verification, known failures, next exact action, and dependencies. Replace stale claims instead of accumulating a transcript.
6. Default to the existing Sol implementation workers. Use smaller models for narrow mechanical work when useful. Avoid multiple review layers, repeated broad audits, and repeated full suites without changes or unresolved failures.
7. Plans and authored files never override live SQLite state. Export before editing corresponding live-authored records; import/publish only at an explicit synchronization boundary. Preserve recovery evidence.

## Resuming after lost messages

Read this index and the affected checkpoint. Inspect its worktree status and referenced commits. Check existing test reports and running task-owned processes before restarting a long test. Verify the current master and integration heads; do not assume the hashes above remain current. Preserve uncommitted files, stashes, and backup bundles. Confirm which worker currently owns the integration checkout before editing it.

Update these documents before a pause, handoff, or final response that changes the reported completion state. The original platform design and six plans remain under [PLATFORM-IMPLEMENTATION.md](PLATFORM-IMPLEMENTATION.md); the current program above adds master landing, cleanup, manual depth, and website/application work.
