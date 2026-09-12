# Platform verification, master landing, and local cleanup

Owner: coordinator; current verification worker: root agent website_integration.

## Scope and order

Finish platform acceptance, incorporate retained documentation, land the upgrade on master, and delete local non-master branches only. Do not mix obsolete alternative branch implementations into the delivered platform. Preserve their exact histories in verified backups. Do not delete remote branches, stashes, live databases, or task directories still needed for tools.

1. Finish the running full suite before editing its source/catalog.
2. Fix the actual failures without weakening assertions or authority checks; run focused proofs.
3. Complete the required build, green full suite, opt-in protocol walk, and catalog validation. Record exact code revision and results.
4. Add guide alignment 497b212c and preserve newer master/documentation changes in the prepared merge.
5. Verify the resulting merge, then advance master in the original checkout. Confirm the platform code is actually present on master.
6. Refresh and verify backups for all local refs and outstanding detached work. Detach clean non-master worktrees at their exact commits, then delete every refs/heads entry except master. Preserve working directories that active tasks/toolchains use.
7. Verify master is the only local branch and remote refs/stashes remain unchanged.

The user explicitly authorized local branch deletion, including obsolete/unmerged branch names. A verified backup preserves those histories; patch-unique branches do not require a new whole-history implementation audit. Do not delete active work or an unbacked ref.

## Locations and preserved evidence

- Original checkout: C:/repo/DantesRoleplay, master was be7d0609 before the coordination documents.
- Platform integration: C:/repo/DantesRoleplay-foundation, code checkpoint 9395cd8e5e7fded637ad931ef7e864bbf1b15a0e.
- Prepared merge: C:/repo/DantesRoleplay-master-integration, DETACHED 6023d31bd1233bd20d5d061a17736d1674b5ca05. Parents are platform 9395cd8e and master be7d0609; merge base 97d30712. Zero conflicts; master's one additional change is a portrait asset.
- Verified backup: C:/repo/DantesRoleplay/.tmp/branch-cleanup/20260912-105838/local-refs-pre-cleanup.bundle.
- Backup SHA256: F6222FF9B3E992B71F0CED0ACDB5722F557E0E78CFA79EA400BBF2A0675B9C9A.
- Inventory at backup: 42 local branches, 24 preexisting clean worktrees, two stashes. Counts must be checked again before deletion. Detached 3755c76a and the merge candidate also have preservation refs/bundles.
- Pending documentation alignment: 497b212c, from C:/repo/DantesRoleplay-platform-guide-alignment.

## Verification recipe

Use the verified SDK at C:/repo/DantesRoleplay-foundation/.tmp/toolchains/dotnet-10.0.401/dotnet.exe. Set DOTNET_ROOT and DOTNET_ROOT_X64 to that SDK directory when invoking apphosts. Existing build artifacts are under .tmp/artifacts/provider-host-fix.

Run the full solution build and DantesRoleplay.Tests project. The protocol walk is excluded by default: explicitly set MSBuild IncludeProtocolWalkTests=true and filter ProtocolWalkTests after the full run finishes. Run the freshly built roleplay tool's validate catalog command; it uses disposable storage. Do not accidentally invoke an old tool binary through roleplay.cmd.

Expected report locations:
- .tmp/test-results/platform-final/platform-final.trx
- .tmp/test-results/platform-final-protocol/platform-final-protocol.trx

Record actual paths if reruns use different names. Do not overwrite useful failed-run evidence. No live provider test or live database deployment has been completed.

## Current checkpoint

- Full solution build at 9395cd8e: passed, zero warnings/errors.
- First full-suite totals: awaiting verification worker's durable update.
- Reported failures: GuardTests.No_catalog_contract_names_a_verb_kind_the_protocol_does_not_serve; SqliteStandingGrantTargetResolverTests.Existing_stateful_body_runs_real_dry_run_publishes_and_rechecks_current_authority.
- Manual diagnosis: catalog/procedures/procedure/system/inner-worker/submit.md uses the placeholder query(kind: "<qualified-id>"); the guard's existing placeholder convention is "...". Correct the authored example, not the guard or public protocol.
- Runtime diagnosis from worker: a derived state host reused a 16-operation parent validation budget although its selected state grant allowed eight. Narrow the child budget to the selected grant and expiry while retaining the shared parent ledger; prove stateful and workflow paths.
- Both fixes committed at fe60517c6df24b567bad60a9ae455f4f1372694b. Focused guard/manual/stateful/workflow checks: 4/4 passed. Corrected full build: one existing CS8604 test warning, zero errors. Fresh catalog validation: 614 records valid with seven known legacy warnings.
- Full suite rerun is active on the corrected, frozen source. Next: record complete first-run and rerun results, finish protocol verification, and release source ownership for landing.
- master landing / branch deletion: NOT performed.

## Required handoff fields

Date; exact tested HEAD; build warning/error totals; full pass/fail/skip counts and reports; protocol counts and skip reasons; catalog totals/warnings; any controlled-provider limitations; dirty state; running process/session if a test is still active; next action; explicit integration ownership release.
