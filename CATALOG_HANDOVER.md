# Catalog export/import — handover

For whoever picks this up next. The design record is `CATALOG_PORTABILITY_PLAN.md` (slices, exit
gates, evidence); this document is the part that is not in the code or that plan — current state,
the decisions that are load-bearing, and the traps in this environment that cost real time.

Last worked: 2026-08-19.

---

## 1. Do this first

**Four bug fixes are written and have never been compiled.** Until this passes, the feature has a
known defect: relationships silently fail to import into a fresh database.

```powershell
dotnet build > out-build.txt 2>&1
dotnet test  > out-test.txt  2>&1
```

Expect **291 passed, 0 failed**. The previous run was 287/291; the four failures are fixed but
unverified. Then commit — `CatalogCoverageTests.cs` is untracked and the four fixes are uncommitted.

---

## 2. What this feature is

Two populations author this system and neither can use the other's tools:

- a **developer with the solution source** edits files, with an editor, a linter and git diffs;
- an **agent connected only over MCP** has no filesystem and writes into the database.

Both are legitimate. Export/import is the bridge, not a migration to one side. It exists because the
MCP-only channel forces JavaScript through JSON string escaping, and that measurably degraded the
rules: `mechanic.dnd2024.check.ability` went from 87 commented lines at v1 to 24 lines averaging 233
characters with no comments at v4. Nobody decided that; the authoring channel did.

The surface is a CLI, never MCP. The system has three MCP tools and there will not be a fourth —
import and export are things a human does with a shell open, not moves an agent makes mid-session.

---

## 3. State

| Slice | What | State |
| --- | --- | --- |
| 0 | One content fingerprint, populated everywhere | Verified live |
| 1 | Export the ruleset | Verified live |
| 2 | Import with three-way drift detection | Verified live |
| 3 | World state | Ran live; its tests had failures, now fixed and **unbuilt** |
| 4 | History export | Ran live; same |
| — | Coverage guard test | Written, **unbuilt**, **uncommitted** |

Live evidence, from the real database:

- Fingerprints went 2/20 → 20/20 and 34/55 → 55/55 with **zero versions appended**.
- Export writes **10 mechanics / 27 contracts / 7 component definitions / 6 entities = 50 records**,
  68 files including 6 schema sidecars and the manifest.
- `verify` reports the catalog and the database agree.
- Every exported record reparses to a fingerprint identical to the row it came from.

The world is mostly tombstones: **25 entity rows, 6 live**; 31 component rows, 8 on live entities.
1,325 operations. Zero relationships. Do not trust the raw row counts as coverage figures.

---

## 4. The pieces

| File | Responsibility |
| --- | --- |
| `DantesRoleplay/Content/ContentHash.cs` | **The** fingerprint. Normalise each field (CRLF/CR → LF, trim), join with U+001F, SHA-256, uppercase hex. |
| `DataAccess/ContentHashBackfill.cs` | Recomputes stale/missing fingerprints. Runs at startup **before** the seeders. |
| `DataAccess/Bootstrap/MechanicFile.cs`, `ProcedureFile.cs` | Parse **and** render the markdown. Both halves live together on purpose. |
| `DataAccess/Bootstrap/MarkdownDocument.cs` | The writer's primitives, and the guards that refuse content which would not parse back. |
| `DataAccess/Catalog/CatalogLayout.cs` | The only place that decides where a record lives. |
| `DataAccess/Catalog/CatalogManifest.cs` | The common ancestor three-way drift needs. |
| `DataAccess/Catalog/CatalogExporter.cs` | Read-only walk of the database → files. |
| `DataAccess/Catalog/CatalogReader.cs` | Files → records, reusing the parsers. |
| `DataAccess/Catalog/CatalogImporter.cs` | The drift table and the apply pass. |
| `DataAccess/Catalog/EntityFile.cs`, `RelationshipsFile.cs`, `ComponentDefinitionFile.cs` | The non-markdown formats. |
| `DantesRoleplay.Tools/` | Console host, assembly name `roleplay`. Add a tool = one file + one line in `Program.cs`. |

Commands: `export`, `import`, `verify`, `hashes`, `backfill-hashes`. All output goes to **stdout**;
only `export` and a real `import` touch disk.

### The drift table

Per record, compare three fingerprints: the file's, the database row's, and the manifest's record of
the last state at which they agreed.

| File vs manifest | DB vs manifest | Meaning | Action |
| --- | --- | --- | --- |
| same | same | nothing changed | skip |
| changed | same | developer edited the file | write a new version |
| same | changed | agent authored live | **leave the database alone**, warn |
| changed | changed | both | **conflict, refuse, write nothing** |
| identical content on both sides | | agreement, not conflict | skip |
| no manifest entry | | unattributable | conflict |

---

## 5. Invariants — do not break these without reading why

1. **One fingerprint definition.** Two would disagree the moment either was touched, silently, in
   both directions. This is why `ContentHash` is in the core project and why `SourceHash` was removed
   from both `Write*Request` records: a caller that can supply its own fingerprint can mark drifted
   content as clean.
2. **A format is two halves.** `ToMarkdown()` lives in the same file as `Parse`. Separating them is
   how a reader and a writer drift until a file that was just written no longer parses.
3. **Byte-preserved vs canonicalised is a deliberate split.** Rule source, JSON Schemas and
   requirements keep their exact bytes — a person wrote them and a person reads the diff.
   Component data is canonicalised, because it is `JSON.stringify` output from a mechanic. Both sides
   must canonicalise identically; `WorldStore` already stores `ParseObject(json).ToJsonString()`, so
   the database's own form is the canonical one.
4. **LF on every platform**, so two exports of one database are byte-identical wherever they ran.
5. **The database wins when only it moved.** An agent cannot re-create lost work from a checkout; a
   developer can.
6. **A conflict aborts the whole import**, including uncontested edits in the same run. A partly
   synchronised catalog is harder to reason about than an unapplied one.
7. **A skipped record keeps its OLD manifest entry.** Writing the database's new fingerprint would
   make the next import read the untouched file as a catalog edit and overwrite the very work this
   import just protected. Stale means it keeps reporting until somebody exports — which is what
   actually resolves it.
8. **Import never deletes**, with one consequence-of-granularity exception: the relationship set is
   one record stating the whole set, so an edge removed from the file is cut. Nothing else.
9. **Apply order: component definitions → entities → mechanics → contracts → relationships.**
   Attaching a component whose definition is missing fails on purpose.
10. **History is export-only.** No code path writes an operation from a file. An operation id and a
    seed are the claim that a rule ran at a version and produced a roll; a log writable from a file is
    not evidence. This is asserted by a test, not by refusing to read a catalog.
11. **The importer reaches past `IWorldStore` in exactly one place**: setting an entity's name. That
    interface is the effect vocabulary a *mechanic* gets, and a rule renaming a creature mid-play
    should not be possible. A developer editing a file is a different actor. It is commented as the
    single exception; do not add a second without the same argument.
12. **Entity ids get a filename guard.** An entity id is the one identifier the kernel never
    validates — whatever was passed to `CreateEntityAsync`, trimmed — so it is the only input that
    could contain `..` and turn a row into a write outside the export root.

---

## 6. Known gaps, deliberately open

Both are recorded in `CatalogCoverageTests`, marked `GAP:`, and the test fails if anyone closes one
without removing the entry.

- **`procedure_relation` is not exported.** A real table (`FromContractId`, `ToContractId`, `Kind`)
  with zero rows, because nothing in the solution reads or writes it — no store method, no MCP verb,
  no seeder. Dead schema, exactly as `SourceHash` was before Slice 0. If contract relations ever get
  an API they must join the catalog in the same change.
- **`ChangeNote` and `CreatedBy` are lost on a round trip.** Authored text, not derived: 10 of 10
  mechanics and 26 of 27 contracts have a non-empty change note on their *current* version, and
  import replaces every one with "Imported from the catalog." Closing this means carrying both as
  front matter, **outside** the fingerprint, since they describe an edit rather than the edited thing.

Coverage is otherwise total: **84 columns, 50 carried, 34 deliberately not, zero unclassified.**
The guard fails when a new table or column appears until someone classifies it, and classifying it
means writing the sentence explaining the choice.

---

## 7. Working in this environment — read this before you lose an hour

- **There is no .NET SDK.** Microsoft's package and download endpoints are blocked in the cloud
  container, and the desktop bridge VM has no `dotnet`. You cannot build, test or run anything.
  The user builds in Visual Studio and reports back.
- **Do not run `git` through the bridge.** `device_bash` cannot delete files, so every `git status`
  leaves a `.git/index.lock` it cannot remove, and the user's next git command fails with
  *"Unable to create index.lock: File exists."* This happened twice. Read git state only if you must,
  and warn the user to delete the lock afterwards.
- **The user's shell is PowerShell 5.1** — `&&` is a parse error. Use separate lines or
  `; if ($LASTEXITCODE -eq 0) { ... }`.
- **Redirected output is UTF-16.** `dotnet ... > out.txt 2>&1` from PowerShell produces UTF-16LE.
  Decode it (`raw.decode('utf-16')`) or it reads as spaced-out garbage.
- **`cat` hides control characters.** `ProcedureFile`'s field separator is a literal U+001F and was
  invisible; four attempts to reproduce its hash failed before `cat -v` showed `^_`. Use `cat -v` on
  anything whose bytes matter, and write control characters as escapes (`''`) in source.
- Editing is done with a Python patcher at `/tmp/patch.py` that preserves each file's existing line
  endings and BOM — the repository is mixed CRLF/LF and clobbering that produces enormous diffs.

### Two C# traps that bit

- `var x = cond ? list : [];` does not compile. A collection expression is target-typed only, and a
  conditional with an untyped branch has no natural type. Give it an explicit type.
- EF Core cannot translate `ThenBy(x => x.Id, StringComparer.Ordinal)`. Materialise first, then sort
  — which is also what makes the ordering deterministic across providers.

---

## 8. How to verify without a compiler

Reimplement the logic in Python and run it against a **copy** of the real database
(`DantesRoleplay.MCPServer/data/dantesroleplay.db`) and the real `catalog/`. This is not a
formality — it found:

- `MechanicFile.ContentHash` had no field separator, so `("ab","c")` and `("a","bc")` fingerprinted
  identically. `ProcedureFile` had the guard and a test; the mechanic side had neither.
- Both parsers rebuilt sections with `StringBuilder.AppendLine` → `Environment.NewLine`, so the same
  file fingerprinted differently on Windows and Linux.
- That the backfill would append **zero** spurious versions — the property the whole
  backfill-before-seeders ordering exists to protect.

**And what it could not find.** The relationship set is one record, so it never looked "absent" on
the database side; importing into a fresh database read as *"the database dropped every edge"*
instead of *"these are new"*, and skipped it. Relationships silently did not import. No simulation
against the live database could catch that, because the live database is the same database that was
exported. Only `A_world_round_trips_through_an_empty_database` found it.

Simulate to check what you can. Write the empty-database round-trip test anyway.

---

## 9. Follow-on work, in the order I would do it

1. **Re-author the ten live mechanics as readable files.** This is the payoff and it is now possible
   — `catalog/` exists and round-trips. The minified v4 sources become editable JavaScript again.
2. **A shared JavaScript prelude** for mechanics. There is no import mechanism, so the six-ability
   table is duplicated in 5 mechanics, `Math.floor((score - 10) / 2)` in 3, and 96 `throw new Error`
   sites average 23% of every mechanic's source. A prelude would cut each D&D mechanic 40–60%.
3. **A node-based mechanic test harness**, so rules can be tested without booting the server. The
   operation log shows 665 mechanic executions with 165 failures — trial and error that belongs in a
   test file, not in MCP round trips.
4. Close the two gaps in §6 if they start to matter.

Each is a separate `procedure.system.create-feature` pass.

---

## 10. One thing that is not this feature's problem

Two `ActionRunnerTests` composition tests were failing when this work started —
`Declared_children_run_before_the_parent_...` and
`Declared_children_can_compose_recursively_...`. They are the Feature 5 mechanic-composition blocker,
tracked in that plan's own "Blocker acceptance requirements" section. They passed in the most recent
run, so something fixed them; verify before relying on it.

`ROADMAP.md` claims "Repository regression baseline: 213/213 tests". That line is **stale** — the
suite is 291 tests. Do not treat it as green.
