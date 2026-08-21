# Knowledge and facts — Sol infrastructure receipt

Status: **Sol-owned local embedding and persistent vector foundation implemented; focused verification passed**  
Date: 2026-08-21

## Accepted boundary

This slice implements only infrastructure that is independent of the unresolved lore schema. It
adds no fact/knowledge component or relationship ID, no world-state write, no migration, no MCP
kind, no player-facing query, no authorization claim, and no automatic local-model tool execution.

## Implemented

- Provider-neutral core contracts for embedding identity/readiness/batches and a derived knowledge
  vector index. Embedding identity is provider, exact model, manifest digest, and dimensions.
- Disabled-by-default `OllamaEmbeddingProvider` in DataAccess using the loopback-only Ollama API.
  It checks `/api/tags` and `/api/show`, requires embedding capability and exactly 2,560 dimensions,
  then uses `/api/embed` with truncation disabled.
- Closed batch/input/time limits, finite-vector/count/dimension validation, and stable failures for
  disabled, invalid, missing, unavailable, malformed, cancelled, or timed-out providers. These
  failures permit the later search owner to fall back to FTS.
- A DataAccess-only `SqliteVecExtensionProbe` that loads one explicit native extension path into
  the same `Microsoft.Data.Sqlite` provider used by the kernel, disables further extension loading,
  and requires the exact loaded DLL hash and reported version before use.
- A provider-neutral, persistent `SqliteVecKnowledgeVectorIndex`. It lazily creates disposable
  generation/document/`vec0` projection tables, partitions candidates by generation and world,
  replaces changed vectors by stable knowledge ID, and rejects stale generations or model-identity
  drift. Canonical world tables remain untouched and no EF migration is required.
- Architecture reconciliation: procedure/mechanic vector triggers remain unchanged, while the
  intended large world-knowledge corpus has a separately approved optional local derived-vector
  path. Canonical state remains SQLite and FTS remains the fallback.

## Real local evidence

| Check | Result |
| --- | --- |
| Installed embedder | `qwen3-embedding:4b`; Ollama reports embedding capability and 2,560 dimensions. |
| Live embedding call | Two inputs returned two finite 2,560-element vectors through the implemented provider. |
| Native vector artifact | `sqlite-vec` `v0.1.9` Windows x86-64 loadable extension. |
| Release archive SHA-256 | `51581189D52066B4DFC6631F6D7A3EAB7DEDC2260656AB09CA97AB3FB8165983` |
| Loaded `vec0.dll` SHA-256 | `FCF98662A7AD9DCE394B96A88F91032047823831B951C76636787C312A6476E6` (the runtime pin). |
| Provider compatibility | Loaded into `Microsoft.Data.Sqlite`/bundled `e_sqlite3`; `vec_version()` succeeded. |
| Real vector operation | Created a temporary `vec0` table, inserted two vectors, and returned the correct nearest row. |
| Persistent-index proof | World partitioning, content replacement, reopen, SQLite backup/reopen, stale-generation rejection, and model-identity rejection passed. |
| Focused tests | 13 passed, including a live Ollama readiness/embed call and the actual native Windows extension. |

The release DLL was downloaded only to a unique local temporary directory for the spike. It is not
committed or silently installed. Production packaging remains a separate reviewed dependency step.

## Repository verification

- `DantesRoleplay` and `DantesRoleplay.DataAccess` build with zero warnings/errors.
- The test project builds with zero warnings/errors when existing project-reference builds are
  suppressed, and all 13 focused tests pass against the current compiled dependencies.
- The full solution build completed with zero errors. It reported nine copy-retry warnings because
  an unrelated pre-existing `testhost` process held the normal test output DLL; the isolated focused
  build had zero warnings. Full-suite feature acceptance remains for the completed feature, not this
  infrastructure slice.
- No catalog file changed, so catalog validation is not applicable. No MCP registration or public
  surface changed, so a protocol walk is not required for this slice.

## Handoff boundary

The Sol-owned infrastructure spike and persistent-index proof are complete for the supported Windows
x86-64 development runtime. The native DLL remains an explicit deployment input rather than a
committed binary; adding other runtime targets or a release bundling mechanism is a future Sol task.

Terra should not invent the world-state vocabulary. The next gate is the proposed semantic packet in
`KNOWLEDGE_AND_FACTS-SOL-SEMANTIC-CONFIRMATION.md`. Once that one boundary is approved, Terra can
implement Slice 1 without revisiting the Ollama or sqlite-vec architecture. Sol is needed again only
for a migration, new native runtime packaging, authorization/leakage review, or a cross-plan conflict.
