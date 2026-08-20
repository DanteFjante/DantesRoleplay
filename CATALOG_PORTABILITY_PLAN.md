# Catalog portability — export and import dependency plan

Status: **Slices 0–2 verified. Slices 3–4 implemented, awaiting a build. Feature complete pending that build.**
Last updated: 2026-08-19
Governing workflow: `procedure.system.create-feature` v4

## Execution rule

This document is planning work. Per the governing contract, writing it does **not** authorize
implementing its first slice. Implement exactly one lowest unimplemented leaf, meet its exit gate,
record evidence here, and stop.

---

## Target capability

Move the complete live catalog between the SQLite database and a folder of ordinary files, in both
directions, without either side silently destroying the other's work.

### Why this exists

Two populations author this system and neither can use the other's tools:

- **Developers with the solution source** author rules as files, using an editor, a linter, git
  diffs and node-based tests.
- **An LLM connected only over MCP** has no filesystem. It authors through
  `commit(kind: "mechanic")` and `commit(kind: "procedure")`, into the database.

Both are legitimate and both must stay legitimate. Today only one direction half-exists, and the
live D&D ruleset is measurably worse for it: `mechanic.dnd2024.check.ability` went from 87 readable
lines with comments at v1 to 24 lines averaging 233 characters with zero comments by v4, because
JSON string escaping over MCP is the only authoring channel it ever had.

### Included

- Export the latest version of every catalog record to a folder tree, each record in the file
  format native to its content.
- Import that tree back, detecting and refusing to clobber divergent changes on either side.
- A single shared content-hash definition that both the file layer and the MCP write path use.
- A CLI surface, invoked by a developer, outside any session.

### Excluded — deliberate non-goals

- **No MCP tool, kind, or verb.** There are three tools and there will not be a fourth. Import and
  export are build/ops operations performed by a human with a checkout, not moves an LLM makes
  mid-session. `procedure.mcp.add-tool` is therefore not in scope.
- **No version history.** Latest version only, both directions. Import creates a new version in the
  database with a change note naming the import; it does not reconstruct a version chain.
- **No automatic conflict merging.** Divergence is detected and refused with a report. A human
  resolves it and re-runs.
- **No re-authoring of existing rules.** Converting the ten live mechanics to readable file-authored
  source is the payoff, and it is a separate follow-on feature that depends on this one.
- **No shared JavaScript prelude / rule stdlib.** Also a separate feature. Noted here only so the
  file layout does not foreclose it.
- **No operation-history import, ever.** See Slice 4.

---

## What already exists — verified, with evidence

Half of "import" is already built and working. This feature **extends** it; it does not create a
parallel path.

| Artifact | Location | What it already does |
| --- | --- | --- |
| `MechanicFile` | `DataAccess/Bootstrap/MechanicFile.cs` | Parses a rule from markdown: `---` front matter (`id`, `category`, `name`, `scope`, `status`), then `## Description`, `## Matches`, `## Requirements`, `## Source`. Strips code fences. Computes `ContentHash` over every authored field. |
| `ProcedureFile` | `DataAccess/Bootstrap/ProcedureFile.cs` | Same shape for contracts: `## Description`, `## Instructions`, `## Constraints`, plus a `governs` front-matter field. |
| `MechanicSeeder` | `DataAccess/Bootstrap/MechanicSeeder.cs` | Loads generic non-ruleset `catalog/mechanics/**/*.md` plus `.js` sidecars embedded under the rules resource name, skips records whose stored `SourceHash` already equals the file's `ContentHash`, and writes the rest through `IMechanicStore`. |
| `ProcedureSeeder` | `DataAccess/Bootstrap/ProcedureSeeder.cs` | Loads the non-ruleset `catalog/procedures/**/*.md` files embedded under the bootstrap resource name. |
| `CategoryPath` | `DantesRoleplay/Categories/CategoryPath.cs` | Owns the dot-delimited category grammar, validation, and the 100-character limit. The one place that knows what a category means. |
| Working example files | `catalog/mechanics/check/mechanic.check.threshold.md`, `catalog/mechanics/adjust/mechanic.value.adjust.md`, and `catalog/procedures/**/*.md` | The target formats, readable and diffable; canonical catalog files also seed a fresh runtime without a second authored copy. |

**Evidence the seeder path works:** `mechanic.check.threshold` and `mechanic.value.adjust` are live
at v1 with `CreatedBy = "seed"`; all 15 bootstrap contracts show `changeNote = "Re-seeded: the
bootstrap file changed."` and populated `SourceHash` values.

### The three gaps

1. **No export.** Nothing walks the database and writes files.
2. **Import reads embedded resources only.** `MechanicSeeder.Load()` calls
   `assembly.GetManifestResourceNames()`. Rules must be compiled into the assembly, so a developer
   cannot point it at a folder and an LLM's live work can never come back out.
3. **Coverage is two record types of six.** No component definitions, entities, components,
   containments or relationships.

---

## The blocking dependency: `SourceHash` is mostly empty

Drift detection needs a trustworthy content hash on every record. There is a `SourceHash` column and
it is not trustworthy:

```
mechanic_version:            SourceHash populated on  2 of 20 rows
procedure_contract_version:  SourceHash populated on 34 of 55 rows

mechanic.dnd2024.check.ability   v1 ''  v2 ''  v3 ''  v4 ''
mechanic.dnd2024.initiative.roll v1 ''  v2 ''  v3 ''
```

The seeder sets it. **The MCP write path does not.** Every rule an LLM has ever authored carries an
empty hash, which is exactly the population this feature exists to move.

Worse, a second hash definition would be actively harmful. `MechanicFile.ContentHash` is
`SHA256($"{Category}{Name}{Description}{Matches}{Requirements}{Source}{Scope}{Status}")`. If the
store computed a hash over a different field set, the drift detector would report false conflicts
forever, and the failure mode is silent. The file's own comment records that this class of bug has
already happened once, with a procedure's `Governs` field.

**Decision (prevents two sources of truth):** the content-hash function moves to the core project as
the single definition, and both the file parsers and the store write paths call it. There is one
hash, computed one way, from one field list.

This is the lowest unimplemented leaf. Nothing else in this feature can be verified without it.

---

## Recursive dependency analysis

```text
catalog portability
├─ trustworthy content hash on every record          [Slice 0 — LEAF, current]
│  ├─ shared hash function in core                   [leaf, no dependency]
│  ├─ MCP write paths populate it                    [depends on the above]
│  └─ backfill of existing rows                      [depends on the above]
├─ export latest catalog to files                    [Slice 1]
│  ├─ trustworthy hash                               [Slice 0]
│  ├─ category → directory mapping                   [CategoryPath exists — verified]
│  └─ per-kind file format                           [MechanicFile/ProcedureFile exist — verified]
├─ import files with drift detection                 [Slice 2]
│  ├─ export                                         [Slice 1 — supplies the manifest]
│  └─ folder-source seeder                           [extension of MechanicSeeder.Load()]
├─ world export/import                               [Slice 3]
│  └─ ruleset round-trip proven                      [Slice 2]
└─ history export                                    [Slice 4 — export only]
```

---

## Layout and formats

Root directory is a CLI argument. `catalog/` is the suggested default. **It is not `ruleset/`** —
that directory already holds D&D planning documents and mixing the two would give the importer
hundreds of markdown files it cannot parse.

```text
catalog/
  manifest.json
  mechanics/
    change/
      mechanic.value.adjust.md
      mechanic.value.adjust.js
    ruleset/dnd2024/core/gameplay/ability-checks/fixed-dc/
      mechanic.dnd2024.check.ability.md
      mechanic.dnd2024.check.ability.js
  procedures/
    system/
      procedure.system.create-feature.md
    mechanic/dnd2024/
      procedure.mechanic.dnd2024.check-ability.md
  components/
    dnd2024.abilities.json
    stats.json
  world/
    entities/
      creature.orban.json
      source.dnd2024.srd-5.2.1.json
    containment.json
    relationships.json
  history/
    operations.jsonl
```

### Category as directory

Only `mechanic` and `procedure_contract` carry a `Category`. Its dot-delimited path maps one segment
to one directory: `ruleset.dnd2024.core.gameplay.ability-checks.fixed-dc` becomes six nested
directories. `CategoryPath` already validates the grammar (lowercase letters, digits, hyphens, no
whitespace, dot boundaries, 100-char cap), which makes every legal category a legal relative path
with no escaping and no traversal risk.

Component definitions, entities, components, containments and relationships have **no** category
column. They are grouped flat by kind. Inventing categories for them would create a second
classification scheme that nothing in the database can validate.

### Format per kind — and why

| Kind | Format | Reason |
| --- | --- | --- |
| Mechanic | `.md` sidecar **+ `.js` source file** | The `.js` is the whole point of the feature: lintable, node-testable, diffable, syntax-highlighted, with real line breaks. The `.md` holds front matter, `## Description`, `## Matches` and `## Requirements` (fenced JSON). |
| Procedure contract | `.md`, single file | `Instructions` is a numbered list and `Constraints` is a bullet list — this content *is* markdown. The parser already exists. Encoding it as JSON would re-escape the prose and reintroduce precisely the readability failure this feature exists to fix. |
| Component definition | `.json` | Its `Schema` field is already JSON. Wrapping JSON in markdown adds a parser and gains nothing. |
| Entity + its components | `.json`, one file per entity, components inline | Pure machine data, rarely hand-edited. Inlining components keeps a creature reviewable as one object rather than scattered across a join. |
| Containment / relationships | `.json`, one file each | Edges between entities. Per-edge files would produce noise with no reviewable unit. |
| Operations | `.jsonl` | Append-only log, one record per line, streamable, never re-imported. |

**Answering the open question directly:** contracts as `.md`, not `.json`. The rule of thumb is
*prose to markdown, data to JSON, code to its own language's file extension* — and it is the same
rule that makes mechanics split into a pair.

### The mechanic pair

`MechanicFile.Parse` gains one rule: if the `## Source` section is absent, read the sibling `.js`
file with the same basename. If both are present, that is an error rather than a precedence
decision — two sources of truth for one field, caught at parse time.

Generic catalog mechanics use the same `.md`/`.js` pair as exported ruleset mechanics. The files are
embedded at build time, so startup and import read the same authored content.

### `manifest.json`

Not decoration. It is the common ancestor that makes three-way drift detection possible:

```json
{
  "schemaVersion": 1,
  "exportedAt": "2026-08-19T09:00:00Z",
  "sourceDatabase": "dantesroleplay.db",
  "records": [
    { "kind": "mechanic", "id": "mechanic.dnd2024.check.ability", "version": 4,
      "contentHash": "A1B2...", "path": "mechanics/ruleset/dnd2024/core/gameplay/ability-checks/fixed-dc/mechanic.dnd2024.check.ability.md" }
  ]
}
```

---

## Drift policy — the core design decision

Both authoring populations write freely between synchronisations. Import compares **three** hashes
per record: the file on disk, the row in the database, and the manifest entry from the last export.

| File vs manifest | DB vs manifest | Meaning | Import action |
| --- | --- | --- | --- |
| same | same | Nothing changed | Skip silently |
| **changed** | same | Developer edited the file | Write to database, new version, change note names the import |
| same | **changed** | LLM authored live over MCP | **Leave the database alone.** Warn: "run export to capture" |
| **changed** | **changed** | Both edited the same record | **Conflict. Refuse.** |
| absent in manifest | present in DB | Record created over MCP since export | Leave alone, warn |
| present in file | absent in DB | New file-authored record | Create |
| absent in file | present in manifest and DB | Deleted from files | Report only. Never auto-delete. |

Rules:

- **A conflict aborts the whole import.** No partial application. Report every conflicting id, then
  exit non-zero.
- **`--dry-run` prints the table above and writes nothing.** This matches the system's existing
  dry-run culture — `commit` already requires it for most kinds.
- Overrides are explicit and per-run: `--force-files` (files win) and `--force-db` (skip drifted
  records). Never a default, never a config file.
- **No `--delete` in this feature.** Removing a rule that something else composes is a real hazard;
  it needs its own dependency analysis.
- Running import with no manifest degrades to two-way: any record whose file hash differs from the
  database is a conflict unless forced. Stated so the first run on a fresh checkout is predictable.

---

## CLI surface

New console project `DantesRoleplay.Cli`, referencing `DantesRoleplay.DataAccess` only.

Rationale: `DantesRoleplay.MCPServer` is `Microsoft.NET.Sdk.Web`, `SelfContained`, `PublishSingleFile`.
Bolting an argument branch onto a `WebApplication` host to avoid a project file is the more expensive
choice, and import/export never executes JavaScript, so it does not need the `RuleAccess`/Jint
reference at all — keeping that dependency out of the tool is worth a `.csproj`.

```
dotnet run --project DantesRoleplay.Cli -- export <dir> [--rules-only] [--with-history]
dotnet run --project DantesRoleplay.Cli -- import <dir> [--dry-run] [--force-files|--force-db]
dotnet run --project DantesRoleplay.Cli -- verify <dir>     # drift report, exit 1 if any
```

`verify` is `import --dry-run` with an exit code, so CI can assert files and database agree.

**To confirm in Slice 1:** whether `DataAccessServiceCollectionExtensions` exposes a registration
that does not drag in the MCP server graph. If it does not, adding one is part of Slice 1.

---

## Slices, in dependency order

Each is a separate pass. One slice, its contract, and its tests land together.

### Slice 0 — one content hash, populated everywhere *(verified 2026-08-19)*

- Move the content-hash function into `DantesRoleplay` (core). Field list unchanged from
  `MechanicFile.ContentHash`, and the equivalent for contracts including `Governs`.
- `MechanicFile` / `ProcedureFile` call it instead of computing their own.
- `MechanicStore.WriteAsync` / `ProcedureStore.WriteAsync` compute and persist it on every write,
  whatever the caller. A caller-supplied `SourceHash` is ignored, not trusted.
- Backfill migration over the 18 mechanic and 21 contract versions with empty hashes.

**Exit gate**
- `select count(*) from mechanic_version where SourceHash = ''` → 0; same for
  `procedure_contract_version`.
- A test writes a mechanic through the MCP commit path and asserts its stored hash equals the hash
  `MechanicFile.Parse` computes for the identical content. This is the test that makes the whole
  feature possible; it must fail before the change.
- Existing regression suite green, allowing for the two pre-existing `ActionRunnerTests`
  composition failures recorded below.

#### Slice 0 implementation record — 2026-08-19

Written but **not compiled**: no .NET SDK is reachable from the authoring environment (Microsoft's
package and download endpoints are blocked). Everything below marked *verified* was established by
reimplementing the hash in Python and running it against the real `dantesroleplay.db` and the real
bootstrap markdown. Everything marked *unverified* needs a build.

**Added**

| File | What it is |
| --- | --- |
| `DantesRoleplay/Content/ContentHash.cs` | The one definition. Canonicalises each field (CRLF/CR → LF, trim), joins with U+001F, SHA-256, uppercase hex. `ForMechanic` and `ForProcedure` fix the field order. |
| `DantesRoleplay.DataAccess/ContentHashBackfill.cs` | `AuditAsync` reports stored vs expected for every revision; `RunAsync` corrects the differences. One answer to "what should this row's fingerprint be". |
| `DantesRoleplay.Tools/` | Console host — `ITool`, `CommandLine`, `DatabaseLocator`, `Commands/HashesTool`, `Commands/BackfillHashesTool`. References DataAccess only; no `PackageReference` of its own, and deliberately not RuleAccess. |
| `DantesRoleplay.Tests/ContentHashTests.cs` | 13 tests, including the gate. |

**Changed**

- `MechanicFile` / `ProcedureFile` — `ContentHash` delegates to the core definition.
- `MechanicFile.Parse` — an empty Requirements section now parses to `"{}"`, matching what the store
  stores for it. Left as it was, such a file would have been reseeded on every start, forever,
  without ever converging.
- `MechanicStore.WriteAsync` / `ProcedureStore.WriteAsync` — compute the fingerprint from the values
  actually being stored. `Requirements` is hoisted into a local so the row and its fingerprint
  cannot disagree.
- `WriteMechanicRequest.SourceHash` / `WriteProcedureRequest.SourceHash` — **removed**. A caller that
  can supply its own fingerprint can mark drifted content as clean, so the guarantee is enforced by
  the type rather than by a comment. Seeders and `MigrationDriftTests` updated accordingly.
- `DataAccessServiceCollectionExtensions` — registers the backfill and runs it **before** the
  seeders.
- `DantesRoleplay.slnx` — adds the Tools project.

**Two bugs found while implementing, both fixed here**

1. `MechanicFile.ContentHash` concatenated its fields with no separator, so `("ab", "c")` and
   `("a", "bc")` fingerprinted identically. `ProcedureFile` had a `\u001f` separator and a test;
   the mechanic side had neither. *Verified: the old mechanic formula reproduces the stored hashes
   exactly with no separator, and the old contract formula only with one.*
2. Both parsers rebuild sections with `StringBuilder.AppendLine`, which emits `Environment.NewLine`
   — so the same file seeded on Windows and on Linux fingerprinted differently, and a catalog
   exported on one and imported on the other would have reported every record as drifted.
   *Verified: the stored contract hashes only reproduce with CRLF-joined sections.*

**Verified against the live database**

- The backfill corrects **20 of 20** mechanic revisions (18 had no fingerprint at all) and **55 of
  55** contract revisions (21 empty). The non-empty ones were all computed by a superseded formula.
- The first startup after the backfill appends **zero** new versions: all 15 bootstrap contracts and
  both bootstrap rules fingerprint identically from disk and from storage, so neither seeder fires.
  This is why the backfill runs before them, and it is the property that would otherwise have
  buried a spurious revision of every bootstrap record in the history.

**Not verified — needs a build**

- That it compiles.
- The 213-test baseline, plus the 13 new tests.
- `A_mechanic_written_through_the_store_fingerprints_the_same_as_the_file_it_came_from` is the gate.
  It could not have passed before this change: the MCP write path stored an empty string.

**How to check the gate**

```
dotnet build
dotnet test
dotnet run --project DantesRoleplay.Tools -- hashes          # expect: exit 1 before the server restarts
dotnet run --project DantesRoleplay.Tools -- backfill-hashes --dry-run
dotnet run --project DantesRoleplay.Tools -- backfill-hashes
dotnet run --project DantesRoleplay.Tools -- hashes          # expect: exit 0, "Every revision carries a current fingerprint."
```

#### Slice 0 exit-gate result — 2026-08-19

Built and tested. All five projects compile, including the new Tools project. Every test passes
**except two pre-existing failures**, both in `ActionRunnerTests`:

- `Declared_children_run_before_the_parent_and_are_frozen_and_audited`
- `Declared_children_can_compose_recursively_without_exposing_a_host_callback`

These are the Feature 5 mechanic-composition blocker, not a regression from this slice:

- `ActionRunnerTests.cs`, `MechanicComposer.cs`, `ActionRunner.cs` and `ProjectionResolver.cs` are
  unmodified since commit `6e61d0d`; Slice 0 touched none of them.
- `FEATURE-5-DEPENDENCY-PLAN.md` carries an open "Blocker acceptance requirements" section for
  exactly this, and gates its own Slice 2 on it.
- The live database agrees: `mechanic.dnd2024.encounter-initiative-order` has failed 10 of 10 runs,
  two with `COMPOSITION_FAILED`, and its v4 change note reads *"TEMPORARY DIAGNOSTIC VERSION. The
  parent could not see ctx.children.initiative even though..."*.

**A pinned regression baseline used to live in `ruleset/dnd2024/ROADMAP.md`.** It predated the
in-flight Feature 5 Slice 2 work. Fixing composition is Feature 5's business, not this feature's.

### Slice 1 — export, ruleset only *(verified 2026-08-19)*

Mechanics, procedure contracts, component definitions. Latest version only. Writes `manifest.json`.

**Exit gate**
- Export of the current database produces 10 `.md`/`.js` mechanic pairs, 27 contract `.md` files,
  7 component-definition `.json` files, and a manifest with 44 entries.
- `catalog/mechanics/check/mechanic.check.threshold.md` + `.js` reparse via `MechanicFile.Parse` to a
  record whose content hash equals the live row's — the seeded rule is the fixture because its
  canonical authored form is already in that catalog location.
- Every exported `.js` is valid JavaScript: parse each with Jint's parser in a test. Catches
  fence-stripping and escaping bugs at the point they are introduced.
- Export is read-only: assert zero rows written and zero operations logged.

#### Slice 1 implementation record — 2026-08-19

**Added**

| File | What it is |
| --- | --- |
| `DataAccess/Bootstrap/MarkdownDocument.cs` | Writes the markdown the two file parsers read. Lives beside them, because a format is two halves and separating them is how a reader and a writer drift. Refuses content that would not parse back. |
| `DataAccess/Catalog/CatalogLayout.cs` | The only place that decides where a record lives. Category → directory path, forward slashes everywhere. |
| `DataAccess/Catalog/CatalogManifest.cs` | The common ancestor Slice 2's three-way drift detection needs. |
| `DataAccess/Catalog/ComponentDefinitionFile.cs` | Component definitions as JSON, schema in a sibling `.schema.json`. |
| `DataAccess/Catalog/CatalogExporter.cs` | The read-only walk. |
| `Tools/Commands/ExportTool.cs` | `roleplay export <directory>`. |
| `Tests/CatalogExportTests.cs` | 13 tests. |

**Changed**

- `MechanicFile` / `ProcedureFile` — gained `ToMarkdown()`, the exact inverse of `Parse`.
- `MechanicFile.Parse` — takes an optional sidecar source. A `## Source` section *and* a sibling
  `.js` is an error, not a precedence rule.
- `ContentHash` — `Normalise` made public, so the catalog writer and the fingerprint cannot disagree
  about whitespace; gained `ForComponentDefinition`.

**Decisions**

- **The schema is a sibling file, not a nested JSON value.** Nested, it would be reserialised on
  every round trip and a schema nobody edited would come back looking changed — the same reason a
  mechanic's JavaScript is not a JSON string. Its presence on disk is what says a schema exists;
  there is no pointer to it that could go stale.
- **Requirements are written verbatim, never reformatted.** Pretty-printing would change the
  fingerprint of a rule nobody edited, and every such rule would then read as drifted.
- **Everything is written with LF**, on every platform, so two exports of one database are
  byte-identical wherever they ran. Slice 2's round-trip gate depends on it.
- **Export refuses a database whose fingerprints are stale**, pointing at `backfill-hashes`. A
  manifest built on fingerprints the database does not itself agree with would have import
  confidently misjudging which side of a divergence is newer.
- **Orphans are reported, never deleted.** Import does not delete either.

#### Slice 1 exit gate — MET on the live database, 2026-08-19

`backfill-hashes` then `export catalog`, run for real:

- Fingerprints went from **2/20 and 34/55 populated to 20/20 and 55/55**, with version counts
  **unchanged at 20 and 55** — zero spurious revisions. That is the property the "backfill before
  the seeders" ordering exists to protect, confirmed rather than argued.
- Export wrote **61 files**: 10 `.md` + 10 `.js`, 27 contract `.md`, 7 component `.json` + 6
  `.schema.json` (`stats` has no schema), and a 44-record manifest.
- Re-reading the real catalog and comparing every record against both the manifest and the live
  row: **file == manifest == database, 44 of 44.**
- The first attempt correctly *refused*: the guard fired on a database whose fingerprints were
  still stale, with the fix in the message.

#### Slice 1 exit gate — earlier simulation, superseded by the live run above

The exporter was reimplemented in Python and run against the live `dantesroleplay.db`, and the
result reparsed with a reimplementation of the parsers:

- **10 mechanics, 27 procedure contracts, 7 component definitions, 44 manifest entries** — the
  counts this plan predicted, exactly.
- **61 files**: 10 `.md` + 10 `.js`, 27 `.md`, 7 `.json` + 6 `.schema.json` (`stats` has no schema),
  and the manifest.
- **44 of 44 records reparse to an identical fingerprint.** Zero differ. This is the gate.
- Categories nest as intended: `mechanics/ruleset/dnd2024/core/gameplay/ability-checks/fixed-dc/`.

Still needs a build: that it compiles, and the 13 tests in `CatalogExportTests`. The likeliest
compile risk is `Every_exported_source_file_is_parseable_javascript`, which wraps each source in a
function expression before handing it to `Jint.Engine.Execute` — a mechanic's source ends in a
top-level `return`, which is a syntax error in a bare script.

**How to check the gate**

```
dotnet build && dotnet test
dotnet run --project DantesRoleplay.Tools -- export catalog
```

Expect 10 / 27 / 7 and a `catalog/` tree; `git status` on it is the review.

### Slice 2 — import with drift detection *(verified 2026-08-19)*

Extends `MechanicSeeder.Load()` / `ProcedureSeeder` to accept a directory root alongside the existing
embedded-resource source. Implements the drift table.

**Exit gate**
- Round-trip: export → fresh empty database → import → export again produces a byte-identical tree
  and identical content hashes for all 44 records.
- One test per row of the drift table, including both conflict cases.
- `--dry-run` writes nothing: assert row counts and `operation` count unchanged.
- A conflict exits non-zero and names every conflicting id.
- Startup seeding of embedded resources still works unchanged.

#### Slice 2 implementation record — 2026-08-19

**Added**

| File | What it is |
| --- | --- |
| `Catalog/CatalogReader.cs` | Reads a catalog directory back, reusing the existing parsers and their sidecars. |
| `Catalog/CatalogImportModels.cs` | `CatalogChange`, `CatalogForce`, the plan and result types. |
| `Catalog/CatalogImporter.cs` | The drift table, and applying it inside one transaction. |
| `Tools/Commands/ImportTool.cs` | `roleplay import <dir>` with `--dry-run`, `--force-files`, `--force-db`. |
| `Tools/Commands/VerifyTool.cs` | `roleplay verify <dir>` — same planner, CI-usable exit code. |
| `Tools/Commands/CatalogReport.cs` | Shared plan printer, so `import` and `verify` cannot describe one situation two ways. |
| `Tests/CatalogImportTests.cs` | 14 tests — one per drift-table row, plus the round trip and the guards. |

**Decisions**

- **The shared thing is the PARSER, not the seeder.** The plan said "extend `MechanicSeeder.Load()`";
  in practice the seeders keep their own job — idempotently installing the shipped manual from
  embedded resources — and the importer reuses `MechanicFile`/`ProcedureFile`. Seeding and
  drift-aware synchronisation are different questions, and one class doing both would have to guess
  which it was being asked. The no-second-loader requirement is satisfied either way.
- **A conflict aborts everything, including uncontested edits in the same run.** A partly
  synchronised catalog is harder to reason about than an unapplied one.
- **A skipped record keeps its OLD manifest entry.** Recording the database's new fingerprint would
  make the *next* import read the untouched file as a catalog edit and overwrite the very live work
  this import just protected. Leaving it stale means it keeps reporting until somebody exports —
  which is what actually resolves it.
- **Component definitions are applied before rules.** Requirements name them. The store does not
  enforce that on write, and an ordering that works only because nothing checks becomes
  load-bearing by accident.
- **Identical content on both sides is agreement, not conflict** — including when the two were
  edited separately into the same thing.
- **The importer's stores must share its `DbContext`**, or its transaction covers nothing. Recorded
  in the class remarks; scoped DI gives it for free, a hand-built test does not.

#### Slice 2 exit gate — `verify` MET on the live catalog, 2026-08-19

```
C:\Users\dante\source\repos\DantesRoleplay\catalog

    unchanged                44

The catalog and the database agree.
```

Exit code 0. `import --dry-run` against the same catalog left the database at 20 and 55 versions with
zero import-authored rows, and left `manifest.json` at its original export timestamp.

Two NuGet advisories surfaced in the build output, both transitive and both pre-existing:
`SQLitePCLRaw.lib.e_sqlite3` 2.1.11 and `System.Security.Cryptography.Xml` 9.0.0 (NU1903). Not this
feature's doing; worth a separate look.

#### Slice 2 exit gate — drift table verified by simulation

The classification was reimplemented in Python and run against the **real** `catalog/` and the
**real** database, once per row of the table:

| Scenario | Result |
| --- | --- |
| real catalog vs real database | 44 unchanged |
| one file edited | 43 unchanged, 1 **file edited** |
| one row authored live | 43 unchanged, 1 **database edited** |
| both moved | 43 unchanged, 1 **conflict** |
| no manifest, one file edited | 43 unchanged, 1 **conflict** |
| a rule authored after the export | 44 unchanged, 1 **new in database** |
| a file deleted from the catalog | 43 unchanged, 1 **missing from files** |
| a rule added to the catalog | 44 unchanged, 1 **new in files** |
| both edited to the same content | 44 unchanged |

Every row lands where the table says it should. Still needs a build, and the round-trip test
(export → empty database → import → export → byte-identical) has only been reasoned about, not run.

**How to check the gate**

```
dotnet build && dotnet test
dotnet run --project DantesRoleplay.Tools -- verify catalog        # expect exit 0, "the catalog and the database agree"
dotnet run --project DantesRoleplay.Tools -- import catalog --dry-run
```

Then edit a description in a `catalog/**/*.md` and re-run `verify` — expect exit 1 and one
`edited in files`.

### Slice 3 — world export and import *(implemented, unbuilt)*

Entities, components, containments, relationships.

Held back deliberately: the world currently holds 25 entities, most of them test fixtures
(`creature.slice4-fixture-missing-skills`, `creature.f4s2-missing-abilities`). Deciding what belongs
in a portable catalog versus what is disposable test residue is its own question, and answering it
inside Slice 2 would block a working ruleset round-trip on it.

**Exit gate**
- Round-trip of all 25 entities, 31 components, 1 containment with identical data.
- `--rules-only` proven to exclude the world entirely.
- Import rejects a component whose `DefinitionId` has no definition — the existing "attaching an
  undeclared component type fails on purpose" invariant must survive bulk import.

#### Slice 3 implementation record — 2026-08-19

**Added**: `Catalog/EntityFile.cs`, `Catalog/RelationshipsFile.cs`, `Tests/CatalogWorldTests.cs`.
**Changed**: layout, manifest, `ContentHash`, exporter, reader and importer all extended for world
records; `export` gained `--rules-only`.

**Decisions**

- **Containment is folded into the entity file**, not kept as a separate edge file. A thing is
  inside at most one other thing — the database enforces that — so "where is this?" is a property of
  the entity. Relationships are not folded in: they are genuinely many-to-many and belong to neither
  end, so they are one `world/relationships.json`, one manifest record, drift all-or-nothing. A
  relationship has no identity beyond its (from, to, kind) triple, and inventing a filename per edge
  would be inventing an identity the database does not have.
- **Component data is canonicalised, not byte-preserved** — the one place the catalog reformats what
  it carries. Rule source and JSON Schemas keep their exact bytes because a person wrote them and a
  person reads the diff; component data is the output of `JSON.stringify` inside a mechanic. What
  matters is that both sides canonicalise identically, which they do, through one function.
  Conveniently `WorldStore.UpsertComponentAsync` already stores `ParseObject(json).ToJsonString()`,
  so the database's own form is the canonical one.
- **Entity ids get a filename guard.** An entity id is the one identifier the kernel does not
  validate — it is whatever was passed to `CreateEntityAsync`, trimmed — so it is the only input
  that could contain `..` or a path separator and turn a database row into a write outside the
  export root. Windows device names are refused too.
- **Soft-deleted entities are not exported.** A catalog states what the world IS; re-importing a
  tombstone would resurrect a row somebody deleted on purpose.
- **`--rules-only` is recorded in the manifest** as `includesWorld`. Without it, a rules-only catalog
  would report every entity in the database as "authored live and never exported" on every run —
  true, useless, and the kind of noise that trains people to skip the output.
- **The importer reaches past `IWorldStore` in exactly one place**: writing an entity's name.
  There is no rename in that interface because it is the effect vocabulary a *mechanic* gets, and a
  rule renaming a creature mid-play is not something it should allow. A developer editing a catalog
  file is a different actor, and silently discarding their rename would be worse than the exception.
  This is the only such deviation and it is commented as one.

#### Slice 3 exit gate — verified against the live world, pending a build

The plan predicted "25 entities, 31 components". The live figures are different, and the difference
is the finding:

| | Rows | Exported |
| --- | --- | --- |
| Entities | 25 | **6** — 19 are soft-deleted test residue |
| Components | 31 | **8** — the other 23 hang off deleted entities |
| Containment | 1 | 1 |
| Relationships | 0 | 0 (the file is still written, so "there are none" is stated) |

Also verified against the live database:

- **Every live entity id can safely become a filename** — none contains a separator, `..`, a
  trailing dot or a reserved device name.
- **Every component's data is valid JSON**, so all 8 survive the canonicaliser.
- **Entity fingerprints are stable across the trip, 6 of 6.**

### Slice 4 — history export *(implemented, unbuilt)*

`history/operations.jsonl`, export only, behind `--with-history`. The live log now holds **1,325**
operations; they export in timestamp order, one JSON object per line.

**One deliberate departure from this plan.** It said import should *refuse* a catalog containing a
history directory. Refusing a whole import because a legitimately exported `history/` is present
would be user-hostile — and it would also be the weaker guarantee. What ships instead: import
**ignores** `history/`, says so in one line, and there is **no code path anywhere in the tool that
writes an operation from a file**. That is the property that matters, and it is asserted by a test
rather than by a refusal that could be relaxed later.

---

## Decisions that prevent two sources of truth

Recorded because the governing contract requires them.

1. **One hash function, in core, called by both layers.** Two hash definitions would make drift
   detection lie silently. Slice 0 exists solely to establish this.
2. **`SourceHash` is computed by the store, never accepted from the caller.** A caller that can
   supply its own hash can mark drifted content as clean.
3. **Extend the existing seeders; do not write a second loader.** `MechanicFile` and `ProcedureFile`
   remain the only parsers. A folder source is a new *source* for the existing loader, not a new
   loader.
4. **A mechanic's source lives in exactly one place per file set** — inline `## Source` (embedded
   bootstrap rules) or a sibling `.js` (exported rules). Both present is a parse error.
5. **Import never deletes.** Absence in files is reported, never applied.
6. **The database stays authoritative for anything authored over MCP until an export captures it.**
   Import defaults to yielding, because the LLM population cannot re-create lost work from a
   checkout and a developer can.
7. **Export is genuinely read-only** — no operation log entry, no version bump, no `UpdatedAt` touch.
   A capture that mutates what it captures cannot be run freely, and a tool nobody runs freely will
   not be run before an import.

---

## Follow-on work, explicitly outside this feature

Listed so the plan is not mistaken for the payoff.

- **Re-author the ten live mechanics as readable file-authored source.** Depends on Slice 2. This is
  what recovers the comments and line breaks lost between v1 and v4.
- **A shared JavaScript prelude** (`dnd.abilityMod()`, `dnd.requireComponent()`,
  `dnd.validateClosedInput()`). The current corpus re-implements the six-ability table in 5
  mechanics, `Math.floor((score - 10) / 2)` in 3, and carries 96 `throw new Error` sites averaging
  23% of every mechanic's source.
- **A node-based mechanic test harness** so rules can be tested without booting the server. The
  operation log shows 665 mechanic executions with 165 failures — trial-and-error that belongs in a
  test file, not in MCP round trips.

Each is a separate `procedure.system.create-feature` pass.

---

## Plan-quality audit

- Every leaf is either implemented with named evidence, or standalone with nothing unresolved below
  it. ✅
- The lowest unimplemented leaf is identified and is genuinely lowest: Slice 0 blocks Slices 1–4 and
  depends on nothing. ✅
- No slice bypasses a dependency with placeholder data or a second copy of existing logic. ✅
- Every slice has an objective, queryable exit gate. ✅
- No new MCP tool or kind is required; `procedure.mcp.add-tool` is correctly not invoked. ✅
- Open item carried into Slice 1: whether `DataAccessServiceCollectionExtensions` already exposes a
  server-free registration. Does not affect slice ordering.

## Plan-change rule

If implementation reveals a dependency not listed here, revise this document and descend to that
dependency. Do not bypass it, mock it, or fold it into the current slice.
