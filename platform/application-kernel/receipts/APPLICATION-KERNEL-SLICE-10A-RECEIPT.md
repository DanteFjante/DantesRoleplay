# Application kernel Slice 10A receipt — public read-only catalog protocol

Status: **accepted**  
Completed: 2026-08-24  
Accepted implementation: [Slice 10A](../APPLICATION-KERNEL-SLICE-10A-IMPLEMENTATION.md)

## Delivered

- Added an explicit public-catalog provider port with an empty/deny production default and a
  bounded in-memory implementation for host composition and tests.
- Added `system.catalogs`, `system.catalog.browse`, `system.catalog.search`, and
  `system.catalog.record` as read-only query kinds without adding a fourth MCP tool or any
  `system.*` commit.
- Added bounded request fields, uniform envelopes, literal traversal/inspection calls, stable
  typed failures, snapshot cursor continuation, and public query audit subjects.
- Updated the authored system-use procedure and MCP component ownership metadata.
- Added a live JSON-RPC protocol walk proving tool discovery, all four catalog operations,
  pagination, stale-cursor recovery, and audit lookup against a prefiltered public fixture.
- Fixed host composition so an absent optional local-completion provider produces an explicit
  unavailable information-answer coordinator instead of preventing every MCP query from being
  activated. A supplied provider still selects the real coordinator; the host does not select or
  start a model.

## Evidence

- Focused direct and live system-catalog protocol tests: 3 passed, 0 failed.
- Catalog validation: 144 records valid; 17 existing near-duplicate warnings; no live data touched.
- Full shared suite: 547 passed, 0 failed.
- Standalone local-AI suite: 19 passed, 0 failed.
- Solution build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed; Git emitted line-ending notices only.

## Deliberate exclusions

This slice does not expose application/source/dependency administration, preview or activation,
state-space administration, any `system.*` commit, authorization implementation, private catalog
content, manifest persistence, vector search, local-AI planning, remote planning, interaction
orchestration, or game rules/content. Those remain blocked on their separately confirmed owners,
especially the E9 administrative authorization gate.
