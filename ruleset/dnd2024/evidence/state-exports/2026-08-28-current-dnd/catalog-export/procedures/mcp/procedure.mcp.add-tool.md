---
id: procedure.mcp.add-tool
category: mcp
name: Extend the MCP surface
governs: adding or changing the MCP surface, adding a query kind or a commit kind
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to extend this system's MCP surface, and why the answer is almost always "do not".

## Instructions
1. **Assume the answer is no.** There are exactly three tools — `orient`, `query`, `commit` — and
   there will not be a fourth. Every tool description is loaded into every conversation whether it
   is used or not, so each one is a standing tax on the context of every future session.
   Procedures load only when asked for; that is why the manual grows and the tool list does not.
2. Check whether the capability fits behind an existing kind as another argument, another effect
   type, or another field in a payload. It usually does.
3. Check whether it can be a procedure instead. If the thing you want is "the agent should know
   how to X", that is a contract, not a change to the surface.
4. Only if neither works, the unit of extension is a KIND, not a tool. Add it to
   `VerbSurface.QueryKinds` or `VerbSurface.CommitKinds` in `DantesRoleplay.MCPServer/Tools`, with
   the parameters it reads or its full payload shape and a complete example, and add the matching
   case to that verb's dispatch switch. Both, in the same change: a guard test compares the two
   lists in both directions and fails the build if either side is missing.
5. Keep the handler a thin delegate to the kernel. No logic lives in the tool layer.
6. Write the kind's description for a reader with no context: what it returns or changes, when to
   reach for it, and what to call next. Name the contracts that govern it in `Contracts`.
7. Return the standard envelope, and make every failure name the exact literal next call in its
   `fix` — beginning with the call, not with prose about it. Inside the MCP project, build that
   call with `VerbSurface.CommitCall(...)` rather than by hand, so it stays a call that can
   actually be made. Kernel code cannot: `DantesRoleplay.DataAccess` does not reference the MCP
   project, and must not start to. Write those by hand and rely on the guard tests, which read
   the kernel's strings too.
8. Keep the list of kinds FLAT. A bounded list of kinds costs a few tokens each; do not create
   sub-kinds. Records may use hierarchical category paths, however: browse procedures with
   `query(kind: "categories", catalog: "procedures")` or mechanics with
   `query(kind: "categories", catalog: "mechanics")`, and use the returned branch as the
   category filter on the corresponding record list.

## Constraints
- Never add a fourth tool. If one seems necessary, the surface needs redesigning, not extending —
  raise it rather than working around it.
- Never add a generic escape hatch: no execute_sql, no execute_shell, no write_arbitrary_file.
- A kind that `query(kind: "capabilities")` does not list does not exist, and a kind listed there
  that nothing dispatches is worse — it is a promise a session will act on. The two land together.
- Never describe a capability the code does not have. This is the failure that crippled the
  previous system: the manual advertised operations that had never been built, and every session
  planned around them.

