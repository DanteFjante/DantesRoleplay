# D&D code adoption Slice 0A implementation — pinned donor baseline

Status: **accepted 2026-08-25**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption plan, Slice 0 / 0A](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: **not applicable** — this slice interprets no D&D rule or content.
Outcome: Pin the approved external engineering sources, reproduce the standalone donor's install,
build, and complete test baseline in a disposable checkout, fingerprint both repositories and their relevant
license/package files, and retain reviewable evidence without importing either repository.
Exclusions: D&D rule selection or adaptation; Foundry build/runtime integration; catalog records;
application source registration or activation; permanent runtime IDs; component/projection/effect
schemas; public operations; migrations; live database access; modification of `old-dnd/`; and any
production Node/npm dependency.
Allowed files/areas: this document; `ruleset/dnd2024/adoption/donor-lock.json`;
`ruleset/dnd2024/adoption/tools/Invoke-DonorBaseline.ps1`; one baseline evidence file and receipt
under `ruleset/dnd2024/adoption/evidence/`; the dependency plan and owning roadmap status.
Stop point: Stop after a fresh disposable run verifies exact commits/trees, records relevant file
hashes and versions, passes the standalone donor install/build commands, completes and records its
full test result without hiding donor failures, fingerprints the Foundry
reference without installing or executing it, cleans its default temporary checkout, and records no
change outside the allowed documentation/tooling boundary.

## Confirmed decisions

- The user's 2026-08-25 instruction to implement Slice 0 confirms the selective-transplant policy,
  exact donor roles, proposed pins, and no-automatic-activation boundary.
- Primary donor pin: `greghcarr/dnd-srd-engine` commit
  `ead852b19b9e45f54f43e193caf4f10aad91a91b`.
- Foundry engineering-reference pin: `foundryvtt/dnd5e` 6.0.x commit
  `275bed0be4ccfa15e6b3347acccb8da8784726d9`.
- The checkout/cache is disposable and outside the repository-authored catalog. The baseline tool
  deletes its uniquely named temporary root by default after validating that the resolved path is a
  direct child of the operating-system temporary directory with the expected prefix.
- Donor lock/evidence names are development-tooling records, not application/catalog/runtime IDs.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Rule behavior | Not inspected or adopted. | Future D&D-owned feature slice plus SRD 5.2.1 | Test results establish donor reproducibility only, never correctness. |
| Runtime state/effects | Not read or changed. | Application kernel and SQLite | Donor repositories remain outside runtime authority. |
| Source licensing | Repository license/notice bytes at exact commits. | Donor repositories and later provenance policy | Hash and record them; interpret allowed reuse in Slice 0B. |

## External implementation reference

This slice fingerprints, but does not adopt behavior from, the approved sources:

- `dnd-srd-engine` is the standalone donor whose own build/test contract is reproduced.
- Foundry dnd5e is a source-inspection reference. It is intentionally not installed, built, loaded,
  or treated as a portable runtime library in this slice.

## Prerequisite evidence

- The [application-kernel completion receipt](../../platform/application-kernel/receipts/APPLICATION-KERNEL-COMPLETION-RECEIPT.md)
  proves application registration, source overlays, schemas, projections, sandbox execution,
  effects, transactions, replay, and audit already exist and need no donor replacement.
- The active dependency plan assigns external code only to selective adaptation after a reproducible
  pinned baseline.

## Runtime artifacts

None. Development-only artifacts:

- `donor-lock.json`: exact URLs, commits, roles, branch evidence, and commands.
- `Invoke-DonorBaseline.ps1`: bounded disposable checkout and baseline verifier.
- one immutable run evidence document containing timestamps, tool versions, exact commits/trees,
  relevant file hashes, commands, exit codes, and summarized test counts.

## Authoritative state and closed input

The committed donor lock owns requested repository URL, exact 40-character commit, source role,
submodule policy, and allowed commands. Git owns checked-out commit/tree identity. Repository files
at that commit own package/license bytes. The tool accepts only an optional existing Git executable,
Node executable, npm command, temporary parent, and keep-checkout switch; none changes the pins.

The tool must reject a non-temporary root, unexpected checkout HEAD/tree, command failure, absent
required license/package file, malformed package version, or unsafe cleanup target.

## Behavior, result, and typed effects

1. Create one unique direct child under the resolved operating-system temporary directory.
2. Clone each repository without a floating dependency and detach at the exact lock commit.
3. Initialize the standalone donor's pinned submodules.
4. Record Git/Node/npm versions, commit/tree IDs, package version, and SHA-256 hashes for license,
   notice, package lock, and relevant attribution files when present.
5. Run `npm ci`, the donor build script, and the donor non-watch test command; retain exit codes and
   parsed pass/fail/skip counts plus bounded output tails.
6. Do not install or execute Foundry; fingerprint its checked-out source/reference files only.
7. Emit deterministic JSON except for run timestamp, elapsed time, and temporary checkout path.
8. Delete the exact temporary root by default after validating its containment and prefix.

Typed effects and transactions: none. The tool writes only its disposable checkout and requested
evidence output.

## Failure, replay, and rollback contract

Any clone, checkout, submodule, hash, install, or build failure makes the verifier fail. A completed
test command is donor evidence even when the donor reports test failures: the verifier records its
exit code, counts, and bounded output, and labels the baseline `reproduced-with-test-failures`
instead of claiming green status. An aborted/unparseable test run still makes verification fail. A
mismatched commit/tree fails before package execution. Cleanup occurs in a `finally` path unless
explicitly retained for diagnosis. Re-running with the same lock must reproduce commit/tree/file
hashes and materially equivalent test counts; timestamp, duration, package download logs, and
temporary paths are not parity fields.

## Implementation sequence

1. Add the exact donor lock and bounded disposable verifier.
2. Run it against fresh temporary checkouts using the bundled Git/Node runtime where practical.
3. Review raw output, create the durable baseline evidence with exact hashes/results, and rerun in
   default cleanup mode to prove no retained checkout is required.
4. Validate JSON/PowerShell syntax, local Markdown links, clean boundary, and diff whitespace.
5. Write the Slice 0A receipt, mark it accepted, and activate Slice 0B only after this stop point.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Exact pins | Checked-out HEAD and tree match the lock; no branch/floating ref is runtime input. |
| Reproducibility | Standalone donor clean install and build pass; non-watch tests complete with retained pass/fail/skip counts and exact failing-test evidence. |
| Reference isolation | Foundry is cloned/fingerprinted only; no install/build/runtime import. |
| Licensing evidence | Relevant license/notice/attribution bytes have SHA-256 hashes at each exact commit. |
| Cleanup | Default run removes only its verified unique temporary root. |
| Failure | Wrong commit, missing required file, install/build failure, or aborted/unparseable tests return non-zero and cannot emit a verified baseline; completed donor test failures remain explicit evidence. |
| Repository boundary | No catalog, runtime, database, archived D&D, application source, or public surface changes. |

## Verification commands

- PowerShell parser validation for `Invoke-DonorBaseline.ps1`.
- Fresh disposable invocation using the exact lock and bundled Git/Node/npm executables.
- JSON parse/shape checks for the donor lock and raw/durable evidence.
- Independent Git/tree/file-hash comparison against the durable evidence.
- Local Markdown link validation and `git diff --check` for Slice 0 files.

Catalog validation, full solution tests, and the protocol walk are not applicable because no
catalog, C#, runtime dependency registration, migration, or protocol surface changes.

## Completion receipt and exit gate

Write `adoption/evidence/DND-CODE-ADOPTION-SLICE-0A-RECEIPT.md`, mark this document accepted, and
update the dependency plan/roadmap to show 0A complete and 0B next. Stop before interpreting license
permissions, defining per-import provenance/coverage contracts, selecting a D&D rule, importing a
donor file, or changing runtime/application/catalog state.
