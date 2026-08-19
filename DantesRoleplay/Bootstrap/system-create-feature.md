---
id: procedure.system.create-feature
category: system
name: Add a feature
governs: adding a capability that does not exist yet
revised-by: Codex, 2026-08-18 — dependency-first, one-slice feature delivery
status: active
---

## Description
How to add one capability safely: plan its dependencies recursively, implement the lowest
unimplemented dependency, verify it, and stop before starting the next slice.

## Instructions
1. Define exactly one target capability, its boundary, and explicit non-goals. Ask where it belongs
   before asking how to build it. If it can be expressed as data, a contract, or JavaScript, it
   does not go in C#.
2. Search for an existing capability that already covers any part of it. Extending beats creating.
3. Before implementation, write a repository plan that recursively lists the target's
   dependencies. Expand each unimplemented dependency until every leaf is either:
   - already implemented, with concrete verification evidence; or
   - one standalone, implementable prerequisite with no unresolved dependency below it.
4. Put the leaves in dependency order. For every planned slice record its artifacts, governing
   contract, invariants and failure behavior, tests, dry-run/query-back checks, and an objective
   exit gate. Record decisions that prevent two sources of truth or duplicated rule logic.
5. Select exactly one lowest unimplemented leaf as the current slice. When all of a parent's
   dependencies are verified, that parent may become the next slice. Do not implement a sibling
   or dependent slice in the same pass.
6. Retrieve `procedure.system.modify` for C# changes and every domain contract governing the
   selected slice. Write or revise the procedure contract that describes how to use the slice in
   the same change. A capability nobody can discover does not exist.
7. **Author runtime content in files first.** When the slice creates or revises a procedure,
   mechanic, component definition, or ruleset world fixture, edit its canonical file under
   `catalog/` (including a mechanic's `.md` and `.js` pair). Use C# only for kernel capabilities
   that cannot be expressed as catalog content. This preserves readable source, reviewable diffs,
   linters and tests, and avoids spending tokens repeatedly escaping source through MCP payloads.
8. Run `roleplay import catalog --dry-run` and inspect the plan. Resolve a reported conflict or
   export newer database-authored work before applying anything; do not use `--force-files` as a
   shortcut around unreviewed drift. Then run `roleplay import catalog`, which is the only normal
   path for moving developer-authored catalog changes into the runtime database. Commit the
   catalog, its manifest, and the changed database together.
9. Use MCP writes only for work that genuinely originates in a filesystem-less MCP session. After
   such a write, run `roleplay export catalog` before further file edits so the catalog becomes the
   shared reviewed baseline again.
10. Implement only the selected slice. Add a test that would fail without it and run the
    proportionate regression suite.
11. Use every supported dry run before writing. Query committed state back, record operation IDs
    and test evidence in the plan, and mark the slice complete only when its exit gate is met.
12. Stop for review before selecting the next slice. If implementation reveals a new dependency,
   revise the plan and descend to that dependency instead of bypassing or mocking it.

## Constraints
- The dependency plan must exist before feature implementation. Creating or revising the plan and
  this planning contract is planning work, not permission to implement its first game slice.
- One slice and its contract land together, never in separate changes. Dependencies are completed
  as sequential slices, not bundled into the parent feature.
- A slice is either verified against its exit gate or remains pending. "Mostly complete" does not
  authorize work on a dependent slice.
- Never bypass a dependency with caller-supplied derived values, duplicated state, placeholder
  data, or a second copy of an existing rule. If temporary scaffolding is unavoidable, it must be
  an explicit planned dependency with removal criteria.
- Evidence for an existing dependency must name the test, query result, operation ID, or repository
  artifact that proves it; assumption is not evidence.
- Do not treat the database as a second hand-edited source for developer work. A clean
  `roleplay verify catalog` is the release check that proves the files and runtime database agree.
- If the feature needs a new query kind or commit kind, follow `procedure.mcp.add-tool` instead.
  There are three tools and there will not be a fourth, and a new kind is nearly always the wrong
  answer too — check first whether it fits behind an existing kind, or is a contract rather than a
  capability.
