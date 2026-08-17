---
id: procedure.system.create-feature
category: system
name: Add a feature
governs: adding a capability that does not exist yet
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
- If the feature is a new MCP tool, follow `procedure.mcp.add-tool` instead — the tool budget
  applies and it is nearly always the wrong answer.
