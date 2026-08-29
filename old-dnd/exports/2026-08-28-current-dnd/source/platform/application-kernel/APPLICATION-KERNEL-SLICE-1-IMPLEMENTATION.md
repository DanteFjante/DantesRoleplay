# Application kernel Slice 1 implementation — read-only legacy inventory

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md), leaf B  
Ruleset alignment: **ruleset-neutral**  
Source ID and locator: **not applicable**  
Outcome: Produce one machine-readable, evidence-backed inventory of the current legacy catalog,
component/schema compatibility boundary, source paths, and public protocol kinds.  
Exclusions: Any runtime behavior change; database access or mutation; application/source/state-space
registration; schema/type migration; catalog move/edit; alias; public kind registration; projection
definition; test fixture change; and classification by unsupported inference.  
Allowed files/areas: This document; the new read-only report under
`platform/application-kernel/inventory/`; Slice 1 receipt; and status/link-only changes in the
owning application-kernel plan and platform roadmap. Existing code and `catalog/` are read-only.  
Stop point: Verify the report against the present worktree, record the receipt, mark leaf B's
inventory sub-leaves accepted, and stop before any registry, persistence, source registration,
alias, or migration work.

## Confirmed decisions

- S0.1–S0.10 are accepted in the [Slice 0 semantic contract](APPLICATION-KERNEL-SLICE-0-IMPLEMENTATION.md)
  and its [receipt](receipts/APPLICATION-KERNEL-SLICE-0-RECEIPT.md).
- `system` is reserved for generic platform behavior; an ambiguous legacy record is not classified
  as system merely because it is shared or currently has a `game.core.*` name.
- `dnd2024` is the first target application, but no legacy record receives that owner without
  direct repository evidence or a later explicit confirmation.
- This slice is read-only and introduces no permanent runtime ID, schema, table, public kind,
  migration, or application registration.

## D&D 5e 2024 alignment

Not applicable. This inventory records existing evidence and deliberately does not decide D&D
ownership, change game rules, or use an SRD source.

## External implementation reference

No Foundry dnd5e review applies: the slice implements no D&D behavior and performs no rule
classification from external sources.

## Prerequisite evidence

- [Application-kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md): Slice 0 is accepted;
  Slice 1 requires a zero-mutation map of IDs, schemas, values, sources, and public kinds.
- [Application-kernel agent guide](APPLICATION-KERNEL-AGENT-GUIDE.md): legacy whole-component reads
  remain conservative, unowned records must not be guessed, and source registration cannot be
  inferred from the local-AI scanner.
- [Catalog manifest owner](../../src/system/catalog/persistence/CatalogManifest.cs) and
  [catalog reader](../../src/system/catalog/persistence/CatalogReader.cs): the existing catalog
  manifest is an authored record list, not an application/source registry.
- [World store contract](../../src/system/state/domain/IWorldStore.cs),
  [component definition](../../src/system/state/domain/ComponentDefinition.cs), and
  [WorldStore persistence](../../src/system/state/persistence/WorldStore.cs): component definitions
  are currently unscoped and mutable; writes only accept JSON objects and do not enforce schemas.
- [VerbSurface](../../DantesRoleplay.MCPServer/Tools/VerbSurface.cs): the existing public protocol
  has `orient`, `query`, and `commit`, with flat unqualified kind lists.
- The checkout contains no `.db`, `.sqlite`, or `.sqlite3` file. This report therefore inventories
  authored catalog component values/shapes and runtime persistence capability, not a live campaign
  database.

## Runtime artifacts

New documentation-only artifact:

- `platform/application-kernel/inventory/LEGACY-APPLICATION-KERNEL-INVENTORY.json` — deterministic
  report format version 1.

The report is not a catalog record, runtime schema, migration input, registration request, or
application manifest. Its filename is descriptive only and does not establish a platform ID.

## Authoritative state and closed input

The inventory reads only the current worktree:

1. `catalog/manifest.json` and the files it names;
2. component definition and schema sidecars under `catalog/components/`;
3. entity fixture component payloads and `catalog/world/relationships.json`;
4. existing catalog reader/layout/import boundaries;
5. `VerbSurface`'s query and commit specifications; and
6. source-scanning/configuration call sites needed to identify the current unregistered sources.

The classifier receives only a record's kind, ID, path, declared category/scope where present, and
direct code/catalog evidence. It may assign exactly one of `system`, `dnd2024`,
`other-application`, or `unresolved` as `recommendedOwner`.

It may assign `system` only when the record is demonstrably generic platform administration or
generic structural state behavior. It may assign `dnd2024` or `other-application` only when an
existing explicit owner proves it. All `game.core.*` and other non-generic records without that
proof remain `unresolved`. A classification is a migration recommendation, never a runtime write.

## Behavior, result, and typed effects

The report must contain:

- reproducibility metadata: report format, UTC observation time, source commit if available, catalog
  manifest hash, and a clear statement that no live database was present;
- one exact coverage group for every in-scope component definition, procedure, mechanic, event
  type, and subscription. Each group lists its member IDs, owner recommendation, confidence, and
  concise evidence; the catalog manifest fingerprint and path findings provide the immutable
  version/hash/path evidence without copying the manifest into a second authority;
- catalog files omitted from the manifest, manifest entries whose files are missing, duplicate IDs,
  and definition/schema sidecar pairing findings;
- each component definition's schema presence and JSON shape findings, plus fixture usage counts and
  observed payload root kinds without opening/mutating a live database;
- the legacy source map: catalog root, layout/reader owner, configured/bootstrap import paths, and
  generic local-AI scanning seams, each marked registered, unregistered, or not a catalog source;
- all existing query and commit kind names, their governing procedure IDs, and classification as
  generic-system, application-owned candidate, or migration/ownership unresolved;
- no proposed aliases. A missing or unqualified future replacement is recorded as a compatibility
  finding, not invented.

Rows sort by `(record kind, ID, path)` using ordinal comparison. Lists of public kinds sort by
verb then kind. JSON is indented UTF-8 with a trailing newline. Re-running the collection against
the same worktree produces equivalent ordered content except `observedAtUtc` and source-control
working-tree status.

No typed effects are produced. The transaction owner is **none**.

## Failure, replay, and rollback contract

- Missing, malformed, or duplicate catalog records become explicit report findings; the inventory
  neither repairs nor excludes them silently.
- A record with insufficient direct evidence is `unresolved`, never assigned by naming convention
  or presumed future application.
- No database connection is opened. If a database is discovered, the report records it as an
  out-of-scope live-state finding and stops before reading it.
- Missing source/configuration evidence is reported as `unregistered` or `unknown`; no directory is
  created, scanned outside the checked-out source tree, or registered.
- Re-running produces a new observation only; no runtime state, active manifest, public contract,
  catalog file, or source registration changes. There is no rollback because the slice has no
  mutable runtime effect.

## Implementation sequence

1. Read the named current catalog, state, and protocol owners; inspect the dirty worktree.
2. Enumerate catalog manifest records and file/sidecar/fixture evidence deterministically.
3. Enumerate legacy source and public kind evidence from named owners.
4. Apply the closed conservative classification rule and write the JSON report.
5. Validate report JSON, paths, counts, and ordering; run the disposable catalog validator.
6. Write one receipt, update Slice 1/leaf B status once, and stop.

## Acceptance matrix

| Case | Required evidence |
| --- | --- |
| Complete catalog inventory | Every in-scope authored ID appears in exactly one machine-readable coverage group with kind, classification, and evidence; manifest hash/counts and path findings identify the source snapshot. |
| Component/schema boundary | Every catalog component definition reports schema-sidecar state; fixture values report root JSON kind and usage without a live DB. |
| Ambiguous ownership | Every `game.core.*` record and every non-generic executable record without direct owner evidence is `unresolved`. |
| Generic ownership | Structural `world.*` event types and confirmed generic system procedures may be `system` only with direct owner evidence. |
| Source map | `catalog/` is shown as a legacy unregistered authored source; local-AI scanning is not misreported as an application registry. |
| Public surface | All current `VerbSurface` query/commit kinds and their governing procedures are represented; no new kind/alias is declared. |
| File integrity | Missing manifest paths, unmanifested relevant files, duplicate IDs, and unmatched schema sidecars are explicit findings. |
| Determinism | Record and kind lists use documented stable ordering; report JSON parses successfully. |
| No change | Git diff shows only Slice 1 documentation/report/status evidence. No database/catalog/code/public-surface mutation exists. |
| Fresh import | `roleplay validate catalog` succeeds against a disposable database without touching live state. |

## Verification commands

```powershell
Get-Content platform/application-kernel/inventory/LEGACY-APPLICATION-KERNEL-INVENTORY.json -Raw |
  ConvertFrom-Json | Out-Null
.\roleplay validate catalog
git diff --check -- platform/application-kernel platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md
git diff --name-only -- platform/application-kernel platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md
```

The final path list may contain only the Slice 1 document, the inventory report, its receipt, and
the permitted owner status/link edits, in addition to pre-existing unrelated dirty files.

## Completion receipt and exit gate

The [completion receipt](receipts/APPLICATION-KERNEL-SLICE-1-RECEIPT.md) records report hash/counts,
validation evidence, deliberate exclusions, and unresolved ownership totals. Slice 1's inventory
row and leaf B inventory sub-leaves are accepted; `game.core.*` ownership and all
migration/public-alias enforcement gates remain open. The slice stops before Slice 2.
