---
id: procedure.mcp.add-tool
category: mcp
name: Add an MCP tool
governs: adding or changing the MCP tool surface
status: active
---

## Description
How to add a tool to this system's MCP surface, and why the answer is almost always "do not".

## Instructions
1. **Assume the answer is no.** The tool budget is twelve, permanently. Every tool description is
   loaded into every conversation whether it is used or not, so each one is a standing tax on the
   context of every future session. Procedures load only when asked for; that is why the manual
   grows and the tool list does not.
2. Check whether the capability fits behind an existing tool as another argument or another
   effect type. It usually does.
3. Check whether it can be a procedure instead. If the thing you want is "the agent should know
   how to X", that is a contract, not a tool.
4. Only if neither works: add the tool in `DantesRoleplay.MCPServer/Tools`, keeping the method a
   thin delegate to the engine. No logic lives in the tool layer.
5. Add its name to `orient`'s capability list in the same change. A test enforces this — a tool
   that orient does not announce is invisible to a cold session.
6. Write the description for a reader with no context: what it does, when to reach for it, and
   what to call next. Name the procedure that governs it.
7. Return the standard envelope, and make every failure name the exact next call in its `fix`.

## Constraints
- Never exceed twelve tools. If a thirteenth seems necessary, the surface needs redesigning, not
  extending — raise it rather than working around it.
- Never add a generic escape hatch: no execute_sql, no execute_shell, no write_arbitrary_file.
- A tool that is not announced by `orient` does not exist. The two land together.
