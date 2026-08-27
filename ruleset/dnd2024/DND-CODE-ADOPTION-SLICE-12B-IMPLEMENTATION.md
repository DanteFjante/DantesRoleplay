# D&D code-adoption Slice 12B implementation — full validation and protocol evidence

Status: **accepted**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), adoption acceptance lane  
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Slice 12B  
Ruleset alignment: `ruleset-neutral` acceptance tooling  
Source ID and locator: not applicable; no rule behavior changes  
Outcome: provide and execute one fail-fast development runner for D&D JavaScript syntax, adoption
contracts/tooling, release build, disposable catalog validation, full shared/Local AI tests, and an
explicit public protocol walk.  
Exclusions: donor baselines, upstream network access, runtime changes, live databases, deployment,
and treating advisory warnings as errors without an accepted policy change.  
Allowed files/areas: this document; `ruleset/dnd2024/adoption/tools/Invoke-Slice12Acceptance.ps1`;
Slice 12B evidence/receipt.  
Stop point: one same-worktree acceptance run succeeds and its summary is recorded.

## Confirmed decisions and prerequisites

- Slice 12A supplies the fresh-host/replay/rollback acceptance prerequisite.
- Existing focused adoption tools remain owners of their contracts; the runner orchestrates them
  and does not reproduce their assertions.
- The catalog command uses its disposable database and may not point at live campaign data.
- The public surface is unchanged, but `ProtocolWalkTests` is run explicitly because Slice 12 asks
  for durable protocol evidence.

## Behavior and failure contract

The runner resolves every path from its own repository location, invokes each check with explicit
arguments, streams useful output, and stops at the first nonzero exit. It accepts executable-name
overrides for portable/offline test environments. An optional report path may receive a concise
JSON summary only after every step succeeds; parent creation and UTF-8 writing are explicit.

It must not install application dependencies, modify the donor lock, write catalog/runtime state,
hide skipped checks, or convert a failed command into a passing report.

## Acceptance matrix

| Concern | Required check |
| --- | --- |
| Catalog scripts | every active D&D JavaScript file passes syntax checking |
| Adoption tooling | contracts, conformance, transformations, mapping, effects, and rollback proofs pass |
| Compile | release solution build has no errors |
| Catalog | disposable validation succeeds; advisory count is recorded |
| Regression | full shared and Local AI suites pass |
| Protocol | `ProtocolWalkTests` passes separately and the unchanged three-verb surface is recorded |
| Failure | any child exit code stops the run and no success report is written |

## Verification and receipt

The successful same-worktree run is recorded in
`adoption/evidence/DND-CODE-ADOPTION-SLICE-12B-RECEIPT.md` and its machine-readable summary. The
leaf stops before Slice 12C changes.
