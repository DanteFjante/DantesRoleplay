---
id: procedure.system.use
category: system
name: Use this system
governs: orient(), query(kind: "capabilities"), any session operating this system
status: active
---

## Description
How to operate this system through its three verbs. `orient` tells you where you are, `query`
reads anything, `commit` changes anything. Nothing else exists.

## Instructions
1. Call `orient()` first. It states what this system is, what exists in it right now, what is not
   built, and which call to make next. Call it again whenever you lose track — it is cheap.
2. Read with `query(kind: ...)`. The kinds are `capabilities`, `procedures`, `world`, `entities`,
   `mechanics`, `event-types`, `subscriptions`, `history`. No `id` returns a list or search; `id` returns one record in full;
   `version` with `id` returns an older revision. Read the full record before revising anything —
   a summary is not the thing itself.
3. When you do not know a payload shape or which parameters a kind reads, call
   `query(kind: "capabilities")`. It is the exact catalog, and it is generated from the same
   structure the two dispatchers switch on, so it cannot describe a kind that does not work.
   Never guess a kind or a shape.
4. Before any `commit`, find and read the contract governing it:
   `query(kind: "procedures")` lists the manual, and each entry states what it governs — match
   that against the commit you are about to make. Then cite what you read in `proceduresUsed` and
   say what you are doing in `intent`, in your own words. The audit records both, and records
   separately which contracts you actually opened.
5. Change with `commit(kind: ..., payload: ...)`. The kinds are `procedure`, `component`,
   `effects`, `mechanic`, `event-type`, `subscription`, `action`. `event-type` registers a schema only; a `subscription` stores middleware registration only; neither emits or routes events yet. `payload` is a JSON object encoded as a string — the whole
   object in one argument, not loose named arguments. Where `dryRun` is supported, ALWAYS call
   with `dryRun: true` first and read every named check or problem that comes back; then commit
   the identical payload. A dry run you did not read is worse than none.
6. Treat every failure as an instruction: the `fix` field names the literal next call, and a
   rejected payload comes back with the shape you needed inside the error. Make that call rather
   than retrying blind or giving up.
7. After a commit, confirm: query back what you wrote, and quote the returned `operationId` when
   reporting what you did.

## Constraints
- `query` never changes state. `commit` is the only write path, and `commit(kind: "effects")` and
  `commit(kind: "action")` are the only ways world state changes.
- Never invent a kind, a parameter or a payload field. If it is not in
  `query(kind: "capabilities")`, it does not exist.
- Never commit a payload that differs from the one that passed its dry run.
- Never report an outcome you did not confirm with a query.
- If `orient()` says a capability is not built, believe it over anything a contract or your prior
  experience suggests.

