# Knowledge and facts — Slice 4 receipt

Completed: 2026-08-21

## Delivered

- `IKnowledgeLexicalIndex`, a provider-neutral boundary for derived knowledge search documents.
- `SqliteKnowledgeLexicalIndex`, using SQLite's bundled FTS5 virtual table rather than a new
  database extension. The table is a replaceable per-world projection, not canonical state.
- `IKnowledgeLexicalSearchCoordinator`, a trusted-GM reader that rebuilds a world corpus and
  searches bounded terms with world, kind, subject, archive, and as-of filters.
- Canonical hydration after every candidate result. This rejects stale, wrong-world, archived, and
  no-longer-effective records even if the derived index is behind.
- Focused coverage for historical/current toll retrieval, stale-index archive rejection, full
  rebuild, and incremental upsert.

## Verification

- `dotnet build DantesRoleplay.DataAccess/DantesRoleplay.DataAccess.csproj --no-restore` passed
  with zero warnings and zero errors.
- The Slice 4 tests pass as part of the 20-test focused lexical/vector/hybrid matrix completed at
  Slice 5 acceptance. The custom-output test-host problem did not occur from the normal build path.
- The test project retains one pre-existing xUnit analyzer warning in
  `KnowledgeAcquisitionCoordinatorTests`.
- No catalog records changed, so no catalog import or migration is required for this derived
  projection.

## Explicit exclusions retained

No vector query, embedding write, Ollama completion call, authorization policy, player/public
retrieval, MCP tool, external FTS extension, or canonical knowledge write path was added. Slice 5
adds semantic/hybrid retrieval on top of this lexical fallback.
