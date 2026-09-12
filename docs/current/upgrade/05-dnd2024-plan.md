# DND2024 application upgrade plan assignment

Owner task: 01a09179-4f0e-7c00-9411-cddb4b9ecbba. Worktree: C:/repo/DantesRoleplay-dnd2024-plan, DETACHED. Baseline: 6023d31b or its equivalent platform code 9395cd8e.

## Deliverable

Create docs/current/DND2024-UPGRADE-PLAN.md and a short current-README routing entry. The user explicitly requested a durable plan for repairing the DND2024 application against the upgraded platform using compatible parallel work and economical models.

This lane plans the broader application/game work; it does not implement all DND2024 rules. The website refactor is already authorized and assigned in workstream 04. Identify that dependency instead of duplicating its work.

## Required contents

Ground the plan in current DND catalog mechanics, component schemas, queries, procedures, and the React frontend under src/system/web-interface/dnd2024. Map concrete gaps to platform authoring, grants, runtime services, dynamic reads/actions, manual/intent discovery, INNER jobs, scheduling/observers, and website contracts.

Define fixed shared contracts, exact file owners, dependencies, implementation order, and bounded vertical slices with observable outcomes. Include usable starter prompts for parallel workers in DETACHED worktrees, with one master integrator and no new branch proliferation. Use existing Sol implementation workers or smaller models for narrow tasks; reserve expensive coordination for actual shared decisions.

Preserve live campaign data and provenance, export before editing corresponding database-authored records, and specify migration/recovery prerequisites. Distinguish unavailable mechanics/content, intentional limitations, and claims that require real fixtures. Do not promise complete DND2024 rules coverage merely because platform services exist. Do not preload rulebook PDFs, world-building workspaces, or quarantined history.

Plan focused invariant checks and one final integrated acceptance, not a full test inventory. Keep C# generic and DND mechanics in catalog JavaScript. Reuse existing frontend/data owners and avoid a framework rewrite.

## Current checkpoint

Delivered from the clean detached worktree at commit
`cc0fd197b26a2021464aeeb8162adcc12cfaf5c3` (`Add D&D 2024 upgrade execution plan`). The commit
adds `docs/current/DND2024-UPGRADE-PLAN.md` and one routing row in `docs/current/README.md`; its
parent is integration baseline `6023d31bd1233bd20d5d061a17736d1674b5ca05`. No branch was created.

The plan fixes generic and D&D file owners, dependency order, eight bounded vertical slices,
Sol/Terra/Luna roles, detached-worktree starter prompts, focused checks, one final integrated
acceptance, and live migration/recovery. Slices 0–5 describe the current preservation and website
lane while explicitly leaving the already assigned website implementation to workstream 04.
Slices 6–8 plan future catalog/runtime adoption and mechanic/content repairs without claiming full
D&D 2024 coverage.

Key evidence and constraints are recorded in the plan: the generic composition contract already
supports bounded structure with free-form props and minimum required props; the D&D frontend needs
a reconciled component mapping, component failure isolation, and a safe bridge to existing generic
bindings. Thirty-one canonical/legacy duplicate IDs have different bytes and are protected by
compatibility retention, so none can be removed without provenance and consumer evidence. Live
application/source registration is unknown, and no concrete D&D declarative workflow, durable
schedule, observer, or inner-worker fixture was found; these paths remain unavailable until real
fixtures and dependencies exist.

Validation was documentation-scoped: the staged diff check passed, every named existing owner
path was verified, and an independent Luna review was incorporated. No full suite, live database,
or external provider was run. The original checkout was clean at
`d32489ab15407657b045d0ecb3d049525613f4f0` before checkpoint updates began; this lane staged or
committed none of its coordinator-owned checkpoint changes.

Coordinator review produced a docs-only compatibility revision at
`03d3f25ee89c030e30ee7a395c1e468a437c61e5`. It aligns the plan with shared
`system-navigation`/`system-theme.js`, allows compatible parallel lanes with disjoint file owners,
makes proposed D&D seams provisional against workstream 04, removes mandatory helper gates, and
adds this durable checkpoint requirement to every starter prompt. The detached worktree is clean
at `03d3f25ee89c030e30ee7a395c1e468a437c61e5`.

Next action: coordinator cherry-picks the two-commit range
`cc0fd197b26a2021464aeeb8162adcc12cfaf5c3^..03d3f25ee89c030e30ee7a395c1e468a437c61e5`
onto master. Workstream 04 then reconciles its current website delivery against Slices 0–5; later
catalog/runtime and game repair work begins only when the plan's real fixtures and dependencies are
available.
