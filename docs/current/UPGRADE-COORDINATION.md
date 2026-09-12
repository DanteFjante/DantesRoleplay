# Upgrade coordination and recovery

This is the durable entry point for the current work. The user explicitly requested repository plans and progress records because task messages can disappear. Read this page, then only the assigned workstream document. Update the checkpoint when ownership, a decision, verification, a commit, or the next action changes.

## User requirements

- Land the completed generic platform upgrade on master, preserving the latest master changes.
- Delete every LOCAL branch except master after preserving unfinished and historical work. Leave GitHub branches, remote refs, and stashes alone.
- Make the MCP manual extensive and usable from an AI's first session, with intent-based selection of relevant instructions.
- Rework shared website navigation, design, reusable components, and themes. Show applications with usable published pages; do not show supporting/non-website applications as disabled navigation.
- Improve DND2024 pages with reusable components, API reads/actions/forms, and local handling of incomplete JSON.
- Produce an actionable DND2024 upgrade plan with compatible parallel assignments and economical model use.
- Finish the remaining implementation, load the changes into master and the live database, and start a verified release the user can try (explicitly authorized 2026-09-12).

Display data may contain additional fields. Each component checks the fields it needs and contains its own missing-data failure. Transport provenance, authorization, scope, freshness, and mutation validation remain strict.

## Current checkpoint

Last coordinator update: 2026-09-12, 13:24 UTC. The scoped implementation, cleanup, deployment, theme corrections, and new Green & Wood default theme are complete.

| Boundary | Verified state |
| --- | --- |
| Original platform plan | All 30 scoped slices implemented and integrated at 9395cd8e |
| Final implementation acceptance | Complete at 0a445b54: 3,456/3,456 full tests; protocol 8 passed and 2 intentional retired cases skipped; 618 catalog records valid with seven known warnings |
| master landing | COMPLETE: base platform at 5795db00, extensive manual and shared/DND website improvements at ef73bab1, preserving previous master changes and the DND upgrade plan |
| Source verification | The final master merge matched accepted 0a445b54; bounded DND header/audience/theme correction landed as b5c0e802, followed by Rules fixes 3a72f17b/5b83da07 and Campaign theme fix 182ebf18; focused checks passed |
| Existing guide alignment | 497b212c incorporated in the candidate as e32878d8 |
| Branch cleanup | COMPLETE: deleted 40 local non-master branches, detached 22 clean worktrees at their exact commits; master is the only local branch; remote refs, both stashes, and all worktree directories preserved |
| Manual | Merged on master through ef73bab1, including e0c8e95f, 8ee31837, and tested metadata fixture alignment; cold-start and full integrated checks passed |
| Shared website | Merged on master through ef73bab1; final combined browser checks 99 passed; desktop/mobile and light/dark previews inspected |
| DND website | Merged on master through ef73bab1; full mounted tests 79/79, typecheck/build passed; root preview recheck shows zero accessibility violations in the controlled inventory fixture in light and dark |
| DND upgrade plan | Landed on master as 73eaf060 and 53a43029; parallel ownership, shared theme/navigation, existing website work, and durable checkpoints reconciled |
| Live deployment | COMPLETE: database migrations, reviewed catalog changes, application activation 66, state-space binding 40, home 9, and corrected DND page 80 are live on port 6217; launcher, database integrity, live MCP/manual retrieval, and final live browser checks passed. |
| Shared theme default | Green & Wood with original gold accent is implemented on master at 1764d64c and live; explicit System/Light/Dark choices remain supported; canonical web-composition manual synchronized at v2 |

Implementation, verification, landing on master, and deployment are separate states. Never call a worktree delivery a master update or a live deployment.

Final verified pre-deletion backup: `C:/repo/DantesRoleplay/.tmp/branch-cleanup/20260912-113124/local-refs-pre-cleanup.bundle`, SHA256 `4589002B80F55D9E1F7FE8C7BA6D6BF0A50C16AC8749842FF033B837E61ED045`. The adjacent manifest records exact branch tips, worktree commits, remote refs, and stashes; `result.json` records completed cleanup. Every worktree's current HEAD was checked against the refreshed bundle before deletion. Active detached working directories are preserved.

The newer extensive manual and shared/DND website changes passed combined acceptance at `0a445b54`: 3,456 full tests passed, eight protocol cases passed with two intentional retired skips, and build/browser/catalog gates passed. They are merged onto actual master at `ef73bab1`. Rehearsal against copied live data exposed a duplicate theme control and obsolete Player filtering in the shared website; the bounded correction landed as `b5c0e802`, with 91 context tests, six mounted header/resilience tests, a targeted faction continuation test, typecheck, and production build passed. [Local deployment and database activation](upgrade/06-local-deployment.md) is complete. A supported managed restart cleared a damaged served module image; all 11 shared JavaScript files match accepted source, pinned disk, and fresh HTTP responses. Final browser checks verified application navigation, one theme control, shared-table context, real Lore records, all 35 factions through continuation, and light/dark rendering with no new browser warnings or errors. Live MCP retrieves the new manuals and ranks them for matching intents. Open `http://127.0.0.1:6217/` or `/ui/dnd2024-play` to try the release.

The broader DND2024 rules and catalog follow-up remains the separately requested [upgrade plan](DND2024-UPGRADE-PLAN.md). Existing incomplete game records can still show local missing-field notices; this deployment preserves that data rather than inventing replacements.

The reported Rules theme gap was corrected in the existing stylesheet and published as DND page 79. Navigation, filters, selected cards, detail blocks, related controls, and empty/error states now use shared theme values. Ten focused Rules checks and the production build passed; live light/dark inspection, all 14 published rules, search-empty behavior, focus styling, and browser error checks passed. The launcher profile was updated and its owned process restarted to retain matching page verification on future starts.

The reported Campaign Overview theme gap was corrected at `182ebf18` and published as page 80. The hero gradient, crest, decorative ring, metadata, premise editor surface, and DM banner now use shared theme values. Five focused Campaign checks and the production build passed. Live light/dark inspection confirmed the actual campaign content and readable themed surfaces with no browser warnings or errors; the saved launcher profile matches page 80.

The requested Green & Wood theme restores the earlier forest surfaces, warm ivory, wood borders, and gold `#c99b52` as the shared default. Master `1764d64c` passed 100 shared browser checks, the host asset check, and catalog validation. An isolated host/source release contains the reviewed theme and manual change; only the existing web-composition manual advanced to v2. Live homepage and DND checks confirmed appearance, persistence, and clean browser logs. Green & Wood remains selected in the user's browser; DND page 80 and gameplay state are retained.

## Workstreams and ownership

| Workstream | Durable plan/checkpoint | Worker |
| --- | --- | --- |
| Platform verification, master landing, local cleanup | [01](upgrade/01-platform-master-cleanup.md) | root agent website_integration for verification; coordinator for landing/cleanup |
| Extensive MCP manual | [02](upgrade/02-mcp-manual.md) | root agent final_guide_alignment |
| Shared website navigation, theme, components | [03](upgrade/03-shared-website.md) | task 01a0917b-34d0-7cc1-bbbf-9689abd0b2e9 |
| DND2024 component and page improvements | [04](upgrade/04-dnd2024-website.md) | root agent workflow_publication_finish |
| DND2024 upgrade implementation plan | [05](upgrade/05-dnd2024-plan.md) | task 01a09179-4f0e-7c00-9411-cddb4b9ecbba |
| Local deployment, database synchronization, and usable site | [06](upgrade/06-local-deployment.md) | root agent website_integration; coordinator performs final integration/browser verification |

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
