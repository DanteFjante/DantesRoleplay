---
id: procedure.contract.create
category: contracts
name: Create or revise a procedure contract
governs: commit(kind: "procedure")
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to author the instructions that govern this system—including this one. `commit(kind:
"procedure")` is the live MCP route; a repository agent edits the canonical markdown under
`catalog/procedures/` and validates the catalog instead.

## Instructions
1. Search for an overlapping contract first with `query(kind: "procedures", query: "...")`. Revise
   it rather than adding a near-duplicate. The write itself warns you when something looks
   similar; do not ignore it.
2. Decide its category — reuse an existing one where you can — and keep the scope narrow. One
   contract answers one question.
3. State what it `governs` — the calls or operations it applies to. Write calls as literal
   fragments, e.g. `commit(kind: "effects")` or `query(kind: "mechanics")`, so that matching a
   contract to an operation is a lookup rather than an interpretation. Without this, a later agent
   cannot tell whether your contract is the relevant one, and the system's central rule collapses
   into guesswork.
4. Separate enforceable invariants from prose guidance: instructions are how to proceed,
   constraints are what must not happen.
5. Add an example whenever the wording could be read two ways.
6. Write it for a reader with no prior context. Assume nothing about what came earlier.
7. In repository mode, edit the canonical file and run `.\roleplay validate catalog`; do not mirror
   the edit through MCP. In MCP-only mode, call `commit(kind: "procedure", payload: {...}, dryRun:
   true)` first, read every named check, then send the identical payload without `dryRun`.
8. Writing an existing id appends a revision — it never overwrites. Say why in `changeNote`.

## Constraints
- Ids are permanent. There is no rename and no delete; the only retirement is
  `status: deprecated` or `status: archived`. Choose the id as carefully as a public API name.
- Never write a constraint you cannot state as a checkable condition; if it is advice, it is an
  instruction, not a constraint.
- Reuse an existing category unless the contract genuinely opens a new area.
- Never name a call in `governs` or in the body that `query(kind: "capabilities")` does not list.
  A contract that instructs a session to make a call that does not exist is worse than no
  contract: it is followed.
