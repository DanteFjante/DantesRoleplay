# System modularization Slice 23 implementation — Standalone local AI and source scanning

Status: **accepted with repository-level exception**  
Owner/roadmap: [Platform roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Local-AI branch](SYSTEM-MODULARIZATION-DEPENDENCY-PLAN.md#dependency-tree)  
Ruleset alignment: **ruleset-independent**  
Source ID and locator: **not applicable**  
Outcome: Extract generic embedding and schema-bound completion into a project with no game-system
project dependency, and add deterministic bounded scanning for file, directory, and wildcard path
specifications.  
Exclusions: Automatic ingestion/indexing, watchers, OCR/archive parsing, network paths policy,
game/campaign interpretation, model-written game state, MCP surface changes, SQLite-vector
extraction, and the user's in-progress Information files.  
Allowed files/areas: Generic model contracts/providers/options and their tests, new local-AI
project/scanner/tests/manifest, Knowledge vector contract split, consumer references/configuration,
architecture guards/evidence.  
Stop point: Local-AI tests, consumer tests, architecture guards, catalog validation, and build pass.

## Confirmed decisions

- `DantesRoleplay.LocalAI` is a standalone assembly with no project reference to the kernel,
  persistence, game adapters, protocol host, or authored catalog.
- Existing namespaces remain temporarily compatible while ownership moves to the new assembly.
- The provider owns no task names. Consumers supply their bounded allowed task classes.
- A literal file scans that file; a literal directory scans recursively; a glob scans matching files
  and recursively scans matching directories. Results are deduplicated and ordered by canonical
  path.
- Reparse points are not traversed. File count, individual size, and aggregate size are bounded.
  Binary and text files are both represented; UTF-8 text is decoded only when valid.

## D&D 5e 2024 alignment

The component contains no ruleset IDs, game entities, mechanics, procedure IDs, or game outcomes.

## External implementation reference

No Foundry review is relevant to generic local file discovery or provider isolation.

## Prerequisite evidence

- [Local routing quarantine receipt](SYSTEM-MODULARIZATION-SLICE-22-RECEIPT.md).
- Existing Ollama provider tests cover disabled/readiness/schema/budget/fallback behavior.
- Architecture manifest/guard establishes the intended dependency direction.

## Runtime artifacts

- `DantesRoleplay.LocalAI` production assembly.
- Generic source scan request/options/document/problem/result contracts and scanner.
- Existing embedding/completion contracts and Ollama adapters, now component-owned.

## Authoritative state and closed input

Input is an explicit nonempty list of bounded path specifications plus scanner limits. Source files
remain authoritative; scan results are derived snapshots and do not mutate source or game state.

## Behavior, result, and typed effects

The scanner canonicalizes paths, expands directory/glob inputs, skips reparse traversal, reads
bounded content, computes SHA-256, detects valid UTF-8 text, and returns stable documents plus typed
problems. It performs no writes and invokes no model.

## Failure, replay, and rollback contract

Invalid specifications, missing roots, access failures, oversize files, and count/aggregate limits
produce typed problems. Identical accessible inputs replay to the same ordered paths, bytes, hashes,
and text. Rollback is removal of the new project reference and relocation of unchanged provider
types; no durable state exists.

## Implementation sequence

Split game-facing vector contracts; create isolated projects; move generic provider code/tests;
remove game task defaults; implement scanner/tests; add dependency/vocabulary guards; verify; receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| File/directory/glob | Scanner tests cover literal file, recursive directory, `*`, `?`, and `**`. |
| Stable/deduplicated | Overlapping inputs return one canonical-path-sorted document each. |
| Boundary | Local-AI project has zero project references and forbidden game vocabulary. |
| Safety | Missing, reparse, oversize, count, and aggregate boundaries are typed/no-write. |
| Provider compatibility | Existing embedding/completion suites pass from isolated test project. |
| Consumer compatibility | Knowledge, routing, story, Information, and DI tests compile/pass. |

## Verification commands

- `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/...`
- `dotnet test DantesRoleplay.Tests/... --filter "...consumer and GuardTests..."`
- `roleplay validate catalog`
- `dotnet build DantesRoleplay.slnx --no-restore`

## Completion receipt and exit gate

Record Slice 23 receipt with delivered scanner contract, project-boundary evidence, and deliberate
indexing exclusions before further game-rule eviction.
