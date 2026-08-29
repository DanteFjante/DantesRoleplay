# Reading implementation documents as an LLM

Status: **Required read protocol for planning and implementation work**

## Goal

Read the smallest authoritative set that makes the assignment safe. More documents do not mean more
authority; loading unrelated plans increases the chance of following stale instructions.

## First classify the task

- **Dependency planning:** use [DEPENDENCY_TREE_AUTHORING.md](DEPENDENCY_TREE_AUTHORING.md); do not
  implement runtime artifacts.
- **Feature-document authoring:** use
  [FEATURE_IMPLEMENTATION_AUTHORING.md](FEATURE_IMPLEMENTATION_AUTHORING.md); do not implement until
  its semantic gates are closed.
- **Feature implementation:** read one active implementation document and its named owners/evidence.
- **Verification/review:** read the active document's acceptance matrix plus the produced diff,
  catalog records, tests, and receipt.
- **Status question:** read `STATUS.md`, the owner roadmap row, and the latest relevant receipt; do
  not reopen every plan.

## Authority order

When documents disagree, use this order:

1. `AGENTS.md` and durable architecture boundaries;
2. authoritative catalog contracts/schemas/mechanics/source records during development;
3. current code and tests for implemented behavior;
4. confirmed decisions, migrations, and completion receipts/validations/ratifications;
5. one active dependency tree and one active implementation document;
6. owning subsystem/ruleset roadmap;
7. `STATUS.md` and `KNOWN_ISSUES.md` as navigation/current summaries;
8. old plans, handoffs, historical notes, and conversation context.

SQLite replaces catalog files as authority only for a running game's live state or MCP-only authored
content. Synchronization follows `CATALOG_HANDOVER.md`.

## Read order for one implementation slice

1. Read `AGENTS.md` completely.
2. Read the header/status of this guide, the owning roadmap row, and candidate feature documents.
3. Select exactly one document whose status is `active` and whose feature/slice matches the request.
4. Read that selected implementation document completely.
5. Read its dependency-tree path from root to selected leaf; do not read unrelated branches.
6. Read every governing catalog procedure and existing owner explicitly named by the slice.
7. Read the exact SRD 5.2.1 source record/locator for `dnd2024-owned` work.
8. Inspect the relevant licensed [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) implementation for `dnd2024-owned` mechanics; use it only as an engineering reference and record any behavior or edge case adopted.
9. Read prerequisite receipts only to verify claims used by the slice.
10. Inspect allowed code/catalog files and focused tests, then restate the boundary, ruleset class,
   owner, source, allowed changes, tests, and stop point before editing.

Do not read all feature directories, all receipts, the whole architecture history, or every
subsystem roadmap “for context.” Follow links only when the selected document makes them relevant.

## Status interpretation

| Document status | LLM action |
| --- | --- |
| `draft` | May revise planning only. |
| `awaiting confirmation` | Stop before the gated semantic/runtime change. |
| `active` | Implement only its named slice and allowed boundary. |
| `blocked` | Verify the blocker; do not bypass or broaden scope. |
| `accepted` / `verified` / `complete` | Use its receipt/catalog/tests as evidence; do not implement it again. |
| `superseded` | Do not follow it; use the named replacement. |
| missing/ambiguous | Resolve through local evidence or request confirmation before a semantic change. |

If a plan says “planned” but code, catalog records, passing tests, and a receipt prove completion,
the implementation evidence wins and the stale plan should be corrected or removed. A receipt never
overrides current code/catalog truth when they have since changed.

## D&D 5e 2024 reading checks

Before implementing `dnd2024-owned` work, confirm:

- the source is `source.dnd2024.srd-5.2.1` with an exact locator;
- the relevant Foundry dnd5e implementation has been checked for design/edge-case evidence, without treating it as the rules authority or a direct dependency;
- the document does not rely on remembered 2014 terminology or behavior;
- any compatibility/optional/house rule is labeled and confirmed separately;
- existing D&D owners are referenced rather than copied;
- derived rule values are not caller authority or duplicated state; and
- rule logic is assigned to catalog JavaScript, not feature-specific C#.

If any check fails, the document is not implementation-ready.

## Stop and escalate

Stop before editing when:

- two active documents claim the same feature/slice or owner;
- the selected document conflicts with catalog/code/test evidence;
- a required source locator or prerequisite evidence is absent;
- a new permanent ID, schema meaning, migration, public kind, house rule, or cross-owner semantic
  decision is unconfirmed;
- the implementation would put D&D rules in C# or duplicate an existing mechanic/state owner; or
- the requested outcome cannot fit one coherent slice/root transaction.

State the conflicting evidence and the smallest decision needed. Do not resolve semantic ambiguity
by reading more unrelated documents or inventing a compromise.

## Pre-edit reading receipt

Before changing files, the LLM should be able to state in a few lines:

- selected feature/slice and document status;
- ruleset alignment and source locator;
- authoritative owners and dependency leaf;
- allowed files/artifacts and forbidden work;
- acceptance commands/evidence; and
- exact stop point.

If it cannot, it has not finished reading.
