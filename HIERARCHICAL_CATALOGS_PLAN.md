# Hierarchical catalog navigation — Terra implementation plan

Status: **Ready for implementation after the Slice 0 public-contract confirmation.**

This plan is written for a Terra High implementation pass. It replaces the older proposal in
this file. The repository has already implemented much of the category-path foundation, so the
remaining work is a bounded catalog-navigation feature, not a catalog redesign.

No persistent catalog import is authorized by this plan. Catalog validation must use the
disposable `roleplay validate catalog` path.

## 1. Outcome

Make the procedure and mechanic catalogs navigable as trees through the existing three-verb MCP
surface. A client must be able to:

1. list the category roots for procedures or mechanics;
2. open one category branch and see its direct children;
3. see both the number of records directly on a node and the number anywhere below it;
4. list or search records in a selected branch with the existing `category` argument; and
5. combine that branch with the existing procedure/mechanic text search and mechanic scope.

This is navigation only. It must not change rule selection, mechanic execution, world-state
semantics, catalog authority, version identity, content hashes, or action behavior.

## 2. Current verified baseline

Do not reimplement these parts. Read them before editing and preserve their decisions.

| Area | Existing owner | Current behavior |
|---|---|---|
| Category grammar and tree projection | `DantesRoleplay/Categories/CategoryPath.cs` | Validates one lowercase dot-delimited path, matches a node plus descendants at a dot boundary, derives ancestors, children, direct counts, and subtree counts. |
| Procedure branch filtering | `DantesRoleplay.DataAccess/ProcedureStore.cs` | `FindAsync(category: ...)` returns the named node and descendants, excluding prefix siblings such as `play` versus `player`. |
| Mechanic branch filtering | `DantesRoleplay.DataAccess/MechanicStore.cs` | Same branch behavior while preserving text ranking and scope preference. |
| Category counts | `IProcedureStore.GetCategoriesAsync` and `IMechanicStore.GetCategoriesAsync` | Return flat exact-path/count pairs from which `CategoryPath.Browse` can derive a tree. |
| Write validation | procedure and mechanic store checks | `category-path` rejects whitespace, uppercase, empty segments, bad hyphens, and paths over the 100-character column limit. |
| Catalog filesystem paths | `DantesRoleplay.DataAccess/Catalog/CatalogLayout.cs` | Maps dot-delimited categories to nested directories and validates category paths. |
| Catalog content | `catalog/procedures/` and `catalog/mechanics/` | Already contains real hierarchical paths, including `ruleset.dnd2024.core.*`. Flat historical values such as `system` remain valid one-level paths. |
| Unit evidence | `CategoryPathTests`, `ProcedureStoreTests`, `MechanicStoreTests` | The focused baseline passed 72/72 tests on 2026-08-21 before this plan was revised. |

The missing feature is the public category browser. `CategoryPath.Browse` currently has no MCP
caller, `VerbSurface` does not advertise a `categories` query kind, and `QueryTool` does not
dispatch one.

## 3. Proposed design to ratify and implement

### 3.1 One primary category path

Each procedure and mechanic keeps exactly one `category` string. A single segment such as
`system` is an ordinary root path, not a legacy exception. Do not add a category table, parent
foreign key, category entity, tags, or multiple-category array.

### 3.2 One branch meaning for `category`

For `query(kind: "procedures")` and `query(kind: "mechanics")`, `category` means the selected
node **and every descendant**. This behavior already exists and is tested.

Do not add either of the stale plan's proposed alternatives:

- no `categories: []` multi-filter; and
- no `recursive` flag.

Those alternatives would create three overlapping ways to express the same branch selection.
Text search remains an AND condition with the branch filter. Mechanic scope behavior remains
unchanged.

### 3.3 Public browser query

Add one query kind behind the existing `query` tool:

```text
query(kind: "categories", catalog: "procedures")
query(kind: "categories", catalog: "procedures", category: "ruleset.dnd2024.core")
query(kind: "categories", catalog: "mechanics", category: "ruleset.dnd2024.core.gameplay")
query(kind: "categories", catalog: "mechanics", includeInactive: true)
```

Arguments:

| Argument | Required | Meaning |
|---|---:|---|
| `catalog` | yes | Closed value: `procedures` or `mechanics`. |
| `category` | no | The branch to open. Omit for roots. It uses the existing category-path grammar, not arbitrary string-prefix matching. |
| `includeInactive` | no | Default `false`. When true, archived records contribute to counts. It has the same visibility meaning as the corresponding record listing. |

Use the existing `category` argument rather than introducing a synonymous `prefix` argument.
`catalog` is the only new public parameter on `QueryTool`.

### 3.4 Response contract

Return the standard `ToolEnvelope`. Its data must serialize to this shape:

```json
{
  "catalog": "mechanics",
  "includeInactive": false,
  "branch": {
    "path": "ruleset.dnd2024.core",
    "direct": 0,
    "subtree": 24,
    "children": [
      {
        "path": "ruleset.dnd2024.core.combat",
        "segment": "combat",
        "direct": 0,
        "subtree": 6
      }
    ]
  }
}
```

Count meanings are exact:

- `direct`: records whose category is exactly this path;
- `subtree`: records on this path or any descendant;
- child nodes: exactly one segment below the opened branch;
- root browsing: `branch.path` is the empty string and its children are the root segments;
- intermediate nodes do not need stored placeholder records;
- a valid but unused branch returns an empty successful branch, not an error.

Children remain ordinally sorted by full path. Counts count catalog records, not versions or
virtual nodes.

### 3.5 Failure contract

Use standard recoverable failures:

- missing or unknown `catalog` -> `INVALID_CATALOG`, naming the two valid values, with fix
  `query(kind: "categories", catalog: "procedures")`;
- malformed `category` -> `INVALID_CATEGORY`, returning the corrective reason from
  `CategoryPath.TryValidate`, with the same root-browser fix;
- valid but empty branch -> successful empty result plus a root-browser next step.

Do not silently normalize category case, dots, whitespace, or hyphens. `catalog` may be trimmed
and compared case-insensitively in the same way `kind` is normalized; category paths are stable
identifiers and must pass the canonical grammar as supplied.

## 4. Scope and dependency map

```text
CategoryPath (existing core grammar/tree)
        ^
        |
procedure/mechanic exact-path counts
        ^
        |
CategoryTools (new thin MCP handler)
        ^
        |
QueryTool dispatch + VerbSurface declaration
        ^
        |
capabilities / orient / procedure contracts / protocol walk
```

Expected production files in scope:

- `DantesRoleplay/Procedures/IProcedureStore.cs`
- `DantesRoleplay/Mechanics/IMechanicStore.cs`
- `DantesRoleplay.DataAccess/ProcedureStore.cs`
- `DantesRoleplay.DataAccess/MechanicStore.cs`
- `DantesRoleplay.MCPServer/Tools/CategoryTools.cs` (new)
- `DantesRoleplay.MCPServer/Tools/QueryTool.cs`
- `DantesRoleplay.MCPServer/Tools/VerbSurface.cs`
- `DantesRoleplay.MCPServer/Tools/ProcedureTools.cs`
- `DantesRoleplay.MCPServer/Tools/MechanicTools.cs`
- `DantesRoleplay.MCPServer/Tools/OrientTool.cs`

Expected catalog contracts in scope:

- `catalog/procedures/system/procedure.system.hierarchical-catalogs.md` (new, after confirmation)
- `catalog/procedures/system/procedure.system.inspect.md`
- `catalog/procedures/system/procedure.system.use.md`
- `catalog/procedures/mechanics/procedure.mechanic.find.md`
- `catalog/procedures/mcp/procedure.mcp.add-tool.md`
- `catalog/procedures/ruleset/dnd2024/core/governance/procedure.mechanic.dnd2024.ruleset.md`
- `catalog/manifest.json` as generated/updated by the repository catalog workflow

Expected test files in scope:

- `DantesRoleplay.Tests/CategoryPathTests.cs`
- `DantesRoleplay.Tests/ProcedureStoreTests.cs`
- `DantesRoleplay.Tests/MechanicStoreTests.cs`
- `DantesRoleplay.Tests/VerbToolTests.cs`
- `DantesRoleplay.Tests/GuardTests.cs`
- `DantesRoleplay.Tests/ProtocolWalkTests.cs`
- a focused `CategoryToolTests.cs` may be added if keeping the cases out of `VerbToolTests` makes
  the behavior materially clearer

Explicitly out of scope:

- database migrations or schema changes;
- changes to `ProcedureContract`, `Mechanic`, or version rows;
- changes to `ContentHash`;
- action selection, `ActionRunner`, mechanic composition, or JavaScript execution;
- world entities, components, events, subscriptions, or notifications;
- import/export format changes beyond the new/updated procedure files and manifest;
- mass rewriting old flat categories;
- a fourth MCP tool;
- a generic category repository or speculative abstraction for other catalog kinds;
- tags, aliases, localized labels, permissions, pagination, or user-defined category metadata;
- persistent `roleplay import catalog`.

## 5. Slice 0 — confirm the semantic boundary

### Purpose

Confirm the public surface and permanent procedure id before an implementation agent creates
either. This is required by `AGENTS.md` because both are semantic boundaries.

### Decisions requiring confirmation

Confirm all of the following as one boundary:

1. new query kind: `categories`;
2. new query parameter: `catalog` with values `procedures` and `mechanics`;
3. reuse `category` as the opened branch;
4. `category` always means node plus descendants; no `recursive` or `categories[]` arguments;
5. `includeInactive` controls whether archived records contribute to browser counts;
6. response shape in section 3.4; and
7. permanent procedure id: `procedure.system.hierarchical-catalogs`, category `system`.

### Terra actions

1. Read `AGENTS.md`, this complete plan, `CategoryPath.cs`, both store interfaces, both store
   implementations, `QueryTool.cs`, `VerbSurface.cs`, `OrientTool.cs`, and the five existing
   contracts listed above.
2. Inspect `git status --short`. The worktree may contain unrelated user work; preserve it and do
   not reset, clean, move, or reformat it.
3. Run the focused baseline:

   ```text
   dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~CategoryPathTests|FullyQualifiedName~ProcedureStoreTests|FullyQualifiedName~MechanicStoreTests"
   ```

4. Ask for confirmation only if the seven decisions above have not already been explicitly
   approved. Do not author the new procedure id or public kind before that confirmation.

### Exit condition

The boundary is confirmed and the baseline is recorded. No repository file changes are required
for this slice.

## 6. Slice 1 — active/inactive count parity

### Purpose

Make category counts obey the same visibility rule as procedure/mechanic lists before exposing
those counts publicly.

### Implementation

1. Extend both store interfaces so `GetCategoriesAsync` accepts `includeInactive`, defaulting to
   `false`, plus the existing cancellation token.
2. In `ProcedureStore.GetCategoriesAsync`, include archived contracts only when
   `includeInactive` is true. Preserve the current default behavior.
3. In `MechanicStore.GetCategoriesAsync`, exclude archived mechanics by default and include them
   only when requested. This fixes the current mismatch with `FindAsync`.
4. Update the two `CheckAsync` category-sprawl checks to call `GetCategoriesAsync` with
   `includeInactive: true`. An archived path is still part of the known authored taxonomy and
   should inform duplicate/sprawl guidance.
5. Update `OrientTool` to call both category-count methods with `includeInactive: true`, preserving
   its existing promise that its totals answer whether anything exists, including archived rules.
6. Do not alter the category-count record types or database entities.

Prefer a signature whose call sites remain unambiguous, for example:

```csharp
Task<IReadOnlyList<ProcedureCategoryCount>> GetCategoriesAsync(
    bool includeInactive = false,
    CancellationToken cancellationToken = default);
```

Update every call site explicitly with named arguments where a boolean/cancellation mix could be
misread.

### Tests

Add focused store tests proving:

- active and non-archived categories appear by default;
- archived-only categories do not appear by default;
- `includeInactive: true` restores archived-only categories and counts;
- procedure and mechanic stores have identical visibility semantics; and
- existing category-known guidance still sees an archived known branch.

Run:

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~ProcedureStoreTests|FullyQualifiedName~MechanicStoreTests|FullyQualifiedName~CategoryPathTests"
```

### Stop condition

Stop if changing count visibility changes any `orient()` total after its calls were switched to
`includeInactive: true`; that means an existing status invariant was missed. Diagnose before
continuing.

### Exit condition

Both stores expose stable, parity-tested counts for the public browser while existing orientation
totals and category-sprawl guidance retain their meaning.

## 7. Slice 2 — public category browser vertical slice

### Purpose

Land a complete, usable `query(kind: "categories")` path for both catalogs, including the
governing contract and discoverable capability declaration.

### Contract-first action

After Slice 0 confirmation, first author
`catalog/procedures/system/procedure.system.hierarchical-catalogs.md` with:

- `id: procedure.system.hierarchical-catalogs`;
- `category: system`;
- `status: active`;
- `governs`: the new categories query plus category-branch filtering on procedures/mechanics;
- instructions covering root browse, child browse, then record browse/search;
- direct versus subtree count meanings;
- the branch-boundary guarantee (`play` does not match `player`);
- the active/inactive rule;
- the separation between mechanic category and mechanic scope; and
- constraints forbidding placeholder nodes, multiple categories, taxonomy inference, and use of
  categories as ruleset isolation.

Do not validate or publish an intermediate state in which the active contract advertises a query
kind the code does not serve. The contract and route are one coherent slice.

### Kernel and handler implementation

1. Do not change `CategoryPath.Browse` unless a new failing test proves a defect. Its current
   response model is the public branch model this slice needs.
2. Add `DantesRoleplay.MCPServer/Tools/CategoryTools.cs` as a non-registered thin handler, following
   `ProcedureTools` and `MechanicTools` conventions.
3. Its method receives both stores, the operation log, `catalog`, `category`, `includeInactive`,
   and cancellation.
4. Validate `catalog` against the closed two-value set.
5. If `category` is supplied, validate it with `CategoryPath.TryValidate` before querying stores.
6. Select the relevant store counts, map them to core `CategoryCount` records, and call
   `CategoryPath.Browse(category, counts)`.
7. Return exactly the data shape and failures in section 3.
8. Record the public tool name `query`, never an internal `find_categories` name.
9. Give successful results callable next steps:
   - a child-browser call when a child exists;
   - a record-list call for the opened or first child branch; and
   - the root-browser call when the selected valid branch is empty.

### Public dispatch and capability declaration

1. Add optional `catalog` to `QueryTool.QueryAsync`. Keep dependency parameters untouched and use
   named arguments at direct test call sites. Do not add `categories[]`, `prefix`, or `recursive`.
2. Add `categories` to the query tool description and closed kind description.
3. Add one `categories` arm to the existing normalized-kind dispatch switch.
4. Add the matching `QueryKindSpec` to `VerbSurface.QueryKinds` with parameters exactly
   `catalog`, `category`, and `includeInactive`, governed by
   `procedure.system.hierarchical-catalogs`.
5. Do not special-case capabilities outside `VerbSurface`; the existing catalog generation and
   guard test must remain authoritative.
6. Update `ProcedureTools` and `MechanicTools` argument descriptions so `category` clearly says
   “this branch and descendants” and points to the new browser query.

### Focused behavior tests

At minimum prove through the public `QueryTool`, not only `CategoryPath`, that:

1. procedure root browsing returns only direct root nodes with correct subtree counts;
2. mechanic root browsing does the same;
3. opening an intermediate node reports `direct: 0` and a non-zero subtree when records only
   exist below it;
4. opening a leaf reports its direct/subtree count and no children;
5. a branch excludes a similarly prefixed sibling;
6. default counts exclude archived records and `includeInactive: true` includes them;
7. a valid absent branch returns a successful empty branch;
8. an unknown/missing `catalog` returns `INVALID_CATALOG` and a callable fix;
9. a malformed category returns `INVALID_CATEGORY`, the grammar reason, and a callable fix;
10. every operation-log row records tool `query`;
11. capabilities advertise exactly the three accepted arguments; and
12. the dispatcher/advertisement guard remains equal in both directions.

Also add the missing mechanic-store branch test equivalent to
`ProcedureStoreTests.Find_by_category_returns_the_branch_but_not_a_prefix_sibling`, including a
combined mechanic `query` plus `category` case so ranking/search remains an intersection.

Run:

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~CategoryPathTests|FullyQualifiedName~ProcedureStoreTests|FullyQualifiedName~MechanicStoreTests|FullyQualifiedName~CategoryToolTests|FullyQualifiedName~VerbToolTests|FullyQualifiedName~GuardTests"
```

Then run the catalog gate because this slice adds a contract:

```text
.\roleplay.cmd validate catalog
```

### Stop condition

The slice is independently usable only when a client can discover `categories` through
capabilities, browse both catalogs, follow a returned child call, and then list records in that
branch. Stop before guidance polish if any part of that round trip is absent.

### Exit condition

The new browser is a complete read-only public capability governed by an active catalog contract,
with no migration and no persistent import.

## 8. Slice 3 — discovery guidance and stale-contract repair

### Purpose

Make the working browser easy to discover without breaking existing `orient()` consumers, and
remove instructions that still claim recursive lookup is absent.

### Implementation

1. In `OrientTool`, derive procedure and mechanic root summaries by passing the exact-path counts
   to `CategoryPath.Browse(null, ...)`.
2. Add, without removing existing fields:
   - `CategoryRoots`, carrying the root `CategoryNode` values; and
   - `HowToBrowse`, containing the literal corresponding categories query.
3. Preserve existing `ByCategory`, `KnownCategories`, totals, status totals, and scope totals for
   compatibility. Removing or renaming those is a separate public-surface decision.
4. Add a categories-browser next step when at least one procedure or mechanic category exists.
5. Update `procedure.system.inspect` to browse categories before dumping a large catalog and to
   state that a branch can be combined with text search.
6. Add `categories` to the query-kind list and navigation explanation in
   `procedure.system.use`.
7. Update `procedure.mechanic.find` with the branch behavior and categories-browser call.
8. Clarify `procedure.mcp.add-tool`: query kinds remain a flat capability list, while the records
   returned by procedure/mechanic kinds may be navigated through a category tree. Remove wording
   that can be read as forbidding hierarchical record categories.
9. Update `procedure.mechanic.dnd2024.ruleset` to remove “use exact-category lookup until recursive
   catalog search exists.” Replace it with the actual root `ruleset.dnd2024.core` taxonomy and the
   categories-browser workflow. Do not rename existing D&D categories.
10. Do not rewrite unrelated historical contracts or catalog records merely to make the tree look
    uniform.

### Tests

Add or update tests proving:

- `orient()` still returns all existing fields and totals;
- `CategoryRoots` has rolled-up subtree counts;
- the new next step is syntactically callable;
- `query(kind: "capabilities")` lists `categories` and its exact arguments; and
- the repository contracts no longer contain the stale exact-only sentence.

Run:

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~CategoryPathTests|FullyQualifiedName~VerbToolTests|FullyQualifiedName~GuardTests|FullyQualifiedName~ProtocolWalkTests"
.\roleplay.cmd validate catalog
```

### Exit condition

A cold client can discover the browser from both capabilities and orientation, can understand its
counts, and receives no stale instruction claiming branch search is unavailable.

## 9. Slice 4 — feature acceptance

### Purpose

Verify the complete surface once, record concise evidence, and stop without touching the live
database.

### Protocol acceptance walk

Extend `ProtocolWalkTests` with a compact category-navigation sequence:

1. call `orient()` and follow its categories next step;
2. call `query(kind: "capabilities")` and confirm the `categories` kind/arguments;
3. browse procedure roots;
4. open one returned child path;
5. list procedures with that same `category` and prove every result lies on the node or below it;
6. browse mechanic roots and one child;
7. combine mechanic `category` with text `query` and verify the intersection;
8. try a prefix sibling fixture and prove it is excluded;
9. request a malformed path and follow the returned recovery call; and
10. confirm the audit contains only the public `query` tool name.

Keep the protocol test deterministic: seed tiny purpose-built records when repository content
would make a count brittle.

### Required gates

Run in this order:

```text
dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter "FullyQualifiedName~CategoryPathTests|FullyQualifiedName~ProcedureStoreTests|FullyQualifiedName~MechanicStoreTests|FullyQualifiedName~CategoryToolTests|FullyQualifiedName~VerbToolTests|FullyQualifiedName~GuardTests|FullyQualifiedName~ProtocolWalkTests"
.\roleplay.cmd validate catalog
dotnet test DantesRoleplay.slnx --no-restore
git diff --check
```

The protocol walk is mandatory because the MCP surface and capability registration change. The
full suite runs once at acceptance, not after every edit.

If the shared dirty worktree causes an unrelated failure, do not modify, discard, or “fix” that
unrelated work. Report the exact failing test and demonstrate that the focused hierarchy,
catalog-validation, and protocol gates pass. Do not call the feature fully accepted until the
repository's required full-suite gate can pass or the owner explicitly classifies the unrelated
failure.

### Receipt

Add a short `HIERARCHICAL_CATALOGS_RECEIPT.md` only after all acceptance gates pass. Record:

- the confirmed public arguments and permanent procedure id;
- production and contract files changed;
- focused, catalog, protocol, full-suite, and diff-check results;
- proof that no migration was added;
- proof that flat categories still browse as roots;
- proof that exact text/scope behavior remained intact;
- proof that no persistent catalog import occurred; and
- any intentionally deferred work from section 11.

Do not put operation ids, database paths, transient logs, or copied test output into the receipt.

### Exit condition

All gates pass, the receipt is concise and accurate, and no required work remains.

## 10. Acceptance matrix

| Invariant | Evidence |
|---|---|
| Existing flat categories remain valid | `CategoryPathTests`; root-browser public test |
| Hierarchical paths retain their grammar and 100-character limit | existing and focused validation tests |
| Procedure branch includes descendants | `ProcedureStoreTests` plus protocol walk |
| Mechanic branch includes descendants | new `MechanicStoreTests` plus protocol walk |
| Prefix sibling is excluded | unit, store, and protocol cases |
| Text plus category is an intersection | procedure/mechanic store or public tests |
| Mechanic scope behavior is unchanged | existing mechanic ranking tests plus one branch+scope regression if needed |
| Root and child browsing counts are correct | `CategoryPathTests` plus public handler tests |
| Intermediate nodes need no placeholder record | public branch test with `direct: 0`, `subtree > 0` |
| Archived visibility matches listings | store and public `includeInactive` tests |
| Invalid catalog/path failures are recoverable | public handler tests and protocol walk |
| Capability and dispatcher cannot drift | `GuardTests` |
| New query is discoverable to a cold client | `VerbToolTests`, `ProtocolWalkTests`, orient test |
| Existing orient response remains compatible | orient regression test |
| Catalog remains file-first and valid | `roleplay validate catalog` |
| No live data changed | no persistent import; receipt statement |
| No schema migration is needed | production diff inspection |
| Repository remains accepted | full suite and `git diff --check` |

## 11. Deferred work

Do not pull these into this feature:

- multiple category membership or tags;
- category aliases or redirects;
- localized display names/descriptions;
- category-specific permissions;
- paginated child nodes;
- moving categories into their own table;
- category browsing for event types, subscriptions, components, entities, or notifications;
- mass normalization of existing authored categories;
- category-based mechanic selection or action routing;
- a website tree widget; the MCP response is sufficient input for a later UI feature.

If real usage later demonstrates a need for one of these, write a separate dependency plan from
the accepted browser contract rather than widening this one.

## 12. Terra High completion checklist

Before reporting completion, Terra must be able to answer **yes** to every item:

- [ ] I preserved unrelated dirty-worktree changes.
- [ ] I received confirmation for the new public kind, argument, response, and procedure id.
- [ ] I reused `CategoryPath`; I did not create another path parser or matcher.
- [ ] I added no migration, category table, tag system, recursive flag, or multi-category array.
- [ ] Procedure and mechanic category counts share active/inactive semantics.
- [ ] Existing `category` record queries still mean node plus descendants.
- [ ] `query(kind: "categories")` browses roots and one branch for both catalogs.
- [ ] Direct and subtree counts are distinct and tested.
- [ ] Invalid input produces a callable recovery path.
- [ ] `VerbSurface` and `QueryTool` advertise and dispatch the exact same kind.
- [ ] `orient()` adds navigation without removing old response fields.
- [ ] Governing contracts describe behavior that actually exists.
- [ ] Focused tests, disposable catalog validation, protocol walk, full suite, and diff check pass.
- [ ] I did not run a persistent catalog import.
- [ ] I wrote the receipt only after acceptance, then stopped.
