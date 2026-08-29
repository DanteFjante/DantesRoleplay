# Application kernel Slice 11G implementation — legacy action-catalog metadata readiness

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / described catalog directory nodes](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible legacy metadata only**  
Source ID and locator: **not applicable** — no D&D rule, formula, or content identity is introduced.  
Outcome: Close the existing blank-description blocker for the ratified legacy `dnd2024` procedure
and mechanic set, then prove all 34 action-oriented records have authored names/descriptions and
valid category paths before described-node materialization begins.  
Exclusions: New IDs or categories; instruction/constraint/governs/mechanic changes; catalog-node
serialization; public-provider/runtime integration; projections; state/state-space migration;
aliases; vectors; and AI orchestration.  
Allowed files/areas: The four existing blank-description procedure Markdown files; one focused
catalog-readiness test; this plan/receipt and
concise dependency/roadmap status links.  
Stop point: Stop after the exact ratified 20 procedures and 14 mechanics all parse with nonblank
authored names/descriptions and categories that map losslessly to valid logical `/` paths.

## Confirmed decisions

- The ownership ratification assigns these existing campaign, quest, play, `game.core`, check, and
  change records to `dnd2024`; no identity or ownership is changed here.
- Slice 0 requires application-authored catalog metadata and prohibits the kernel from inventing
  descriptions. Slice 9 enforces that boundary. Four existing procedures are the only blank
  descriptions in the ratified action-oriented source set.
- The added sentences summarize only each procedure's already-authored instructions and
  constraints. They do not alter the closed request, behavior, lifecycle, or failure contract.
- The user's continuation after Slice 11F authorizes this next bounded adoption prerequisite. No
  live database synchronization or public-surface change is authorized or performed.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Procedure behavior | Existing instructions and constraints remain unchanged. | Four existing catalog procedure files | Add summary metadata only. |
| D&D rules | None interpreted or changed. | Existing catalog mechanics/procedures | No SRD locator or Foundry comparison applies. |
| Application ownership | Ratified legacy action records belong to `dnd2024` migration. | `LEGACY-OWNERSHIP-RATIFICATION.md` | Test only the exact approved source-directory boundary. |

## External implementation reference

No Foundry dnd5e review applies because this slice adds no D&D rule behavior, data shape, or edge
case. It supplies missing prose metadata for repository-specific campaign and quest procedures.

## Prerequisite evidence

- [Slice 11F receipt](receipts/APPLICATION-KERNEL-SLICE-11F-RECEIPT.md) proves all 33 component
  contracts are adopted and leaves described catalogs as an independent remaining leaf.
- [Slice 9](APPLICATION-KERNEL-SLICE-9-IMPLEMENTATION.md) requires nonblank authored record
  descriptions and prohibits generated fallback prose.
- Current catalog parsing shows exactly four blank descriptions among the 34 ratified procedure
  and mechanic records selected by Slice 11A's source boundary.

## Runtime artifacts

- Add one `## Description` section to each of the four existing procedures.
- Preserve `catalog/manifest.json` as the last database/file common ancestor; reviewed import is a
  later explicit synchronization boundary.
- Add one repository test that enumerates the exact ratified action-source directories, parses
  every Markdown record with its existing parser, asserts the 20/14 split and unique IDs, and
  verifies authored metadata plus lossless category-to-logical-path conversion.
- Add no runtime code, schema, database row/table, migration, public kind, or source registration.

## Authoritative state and closed input

The existing procedure instructions/constraints govern meaning; descriptions are authored catalog
summaries. Existing category front matter owns logical paths. The test discovers only the closed
directories already accepted by Slice 11A. No caller or model supplies metadata or validation truth.

## Behavior, result, and typed effects

All 34 records parse through `ProcedureFile` or `MechanicFile`. Their IDs are unique; names,
descriptions, and categories are nonblank; and each dotted category round-trips exactly through
`CatalogLayout.CategoryDirectory` to a normalized logical `/` path. Typed effects: none.

## Failure, replay, and rollback contract

A missing/blank summary, duplicate ID, malformed file, absent mechanic sidecar, invalid category,
or non-lossless path conversion fails validation without changing files or any database. There is
no commit, replay, or transaction path.

## Implementation sequence

1. Add the exact readiness test over the ratified source boundary.
2. Author the four missing one-sentence summaries without rewriting synchronization history.
3. Run focused/full/local-AI/catalog/build/diff checks; record receipt and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Closed scope | Exactly 20 procedures and 14 mechanics are selected. |
| Authored metadata | Every selected record has a nonblank name and description. |
| Identity | Existing IDs/categories are unchanged and unique. |
| Directory readiness | Every category maps losslessly to a valid normalized logical path. |
| Semantic isolation | Only four description sections change in authored catalog content. |
| Repository | Focused/full/local-AI/catalog/build and `git diff --check` pass. |

## Verification commands

- Focused catalog validation/readiness tests.
- `roleplay validate catalog`; full shared/local-AI suites; warning-free isolated solution build;
  `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11G-RECEIPT.md`, mark this document
accepted, update Slice 11 status links, and stop before catalog-node materialization or publication.
