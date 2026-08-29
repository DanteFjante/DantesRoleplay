# Knowledge and facts — Slice 5 receipt

Completed: 2026-08-21

## Delivered

- One canonical `IKnowledgeSearchDocumentSource` shared by FTS5 and vector indexing. Documents are
  atomic and contain one knowledge record, its concise summary, subject name/ID, and safe metadata.
- `IKnowledgeHybridSearchCoordinator` with bounded forced rebuild and hash-aware synchronization.
  Unchanged facts are not re-embedded; additions or removals trigger atomic per-world replacement.
- Stable vector generations derived from Ollama provider, exact model, manifest digest, and
  dimensions. Successfully installing a new generation marks other generations stale.
- Deterministic reciprocal-rank fusion with bounded lexical/vector candidates and stable ID as the
  final tie-breaker. Exact canonical ID lookup remains available to the trusted GM.
- Canonical hydration and timeline/status/kind/subject rechecks after fusion. Vector distance never
  decides truth, knowledge, sensitivity, or authorization.
- Complete FTS fallback when embeddings, sqlite-vec, or a compatible indexed generation are absent.
- Host configuration for the existing `qwen3-embedding:4b` profile and an explicit pinned
  sqlite-vec extension path. Both remain disabled unless configured.

## Runtime configuration

The MCP host reads ordinary .NET configuration. Environment variables use double underscores:

```text
Knowledge__Embedding__Enabled=true
Knowledge__Embedding__Model=qwen3-embedding:4b
Knowledge__Embedding__Dimensions=2560
Knowledge__Vector__ExtensionPath=C:\absolute\path\to\vec0.dll
```

Providing the vector path enables sqlite-vec. The extension still must match the approved v0.1.9
version and SHA-256 pin. `DANTESROLEPLAY_SQLITE_VEC_EXTENSION` remains supported by the focused
native tests and as a host path fallback.

## Verification

- The full solution builds with zero errors. Its only warning is the pre-existing xUnit analyzer
  warning in `KnowledgeAcquisitionCoordinatorTests`; Slice 5 adds no analyzer warning.
- Focused coverage includes semantic-only recall, lexical fallback, exact-ID retrieval, stable
  fusion, unchanged-hash skipping, per-world vector replacement, generation drift/staleness, and
  Ollama response validation.
- The 20-test focused lexical/vector/hybrid matrix passed. The eight Ollama provider tests also
  passed with live integration enabled, and the six-test MCP protocol walk passed after the DI
  additions.
- The complete repository suite passed 675/675.
- The local `qwen3-embedding:4b` model returned finite 2,560-element vectors for the fixed semantic
  set. Cosine target/distractor results were 0.521/0.374 for concealed correspondence, 0.566/0.274
  for the current market toll, and 0.690/0.325 for the nighttime observatory signal.
- The earlier test-host failure was confined to the custom `bin-slice2` output folder. Running from
  the normal build output resolved it; no product or test assertion failure remains.
- No sqlite-vec path is currently configured in the shell, so this turn did not repeat the real
  native end-to-end run. The pinned extension's load, KNN, persistence, backup, and stale-generation
  checks remain recorded in the Sol infrastructure receipt and their focused tests were extended.
- No catalog record or canonical database schema changed; catalog validation and migration are not
  applicable.

## Explicit exclusions retained

No `qwen3:8b` completion call, answer synthesis, background proposal queue, tool calling, public/MCP
knowledge tool, player authorization, or canonical knowledge mutation was added. Those remain Slice
5B, Slice 5C, and Slice 6 boundaries.
