---
id: procedure.system.create-feature
category: system
name: Add a feature
governs: adding a capability that does not exist yet
revised-by: Claude Opus 5, 2026-08-18 — the unit of surface extension is a kind, not a tool
status: active
---

## Description
How to add a capability that does not exist yet, deciding first whether it belongs in the kernel
at all.

## Instructions
1. Ask where it belongs before asking how to build it. If it can be expressed as data — a
   component definition, a contract — or as JavaScript, it does not go in C#.
2. Search for an existing capability that already covers most of it. Extending beats creating.
3. Retrieve `procedure.system.modify` and follow it for the code changes.
4. Write or update the procedure contract that describes how to USE the new feature, in the same
   change. A capability nobody can discover does not exist.
5. Add a test that would fail without the feature.
6. Record the operation.

## Constraints
- A feature and its contract land together, never in separate changes.
- If the feature needs a new query kind or commit kind, follow `procedure.mcp.add-tool` instead.
  There are three tools and there will not be a fourth, and a new kind is nearly always the wrong
  answer too — check first whether it fits behind an existing kind, or is a contract rather than a
  capability.
