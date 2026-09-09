---
id: procedure.system.namespace
category: system
name: Place an authored identity in a namespace
governs: query(kind: "namespaces"); commit(kind: "system.namespace.register"); where a new mechanic, procedure, component definition, event type or subscription id may live
status: active
createdBy: "llm"
changeNote: "Added after a session spent an afternoon diagnosing an unregistered prefix that no call could list and no call could create."
---

## Description
Every authored identity is placed by its prefix. `mechanic.game.core.world.quest.register` lives in
`mechanic.game.core.world.quest`, and if that namespace is not registered the write is refused —
not by the dry run, which checks the record, but by the commit, which checks the identity. This
contract is how you find out where you are allowed to write, and how to open somewhere new.

## Matches
where can I put this mechanic
what namespaces exist
my mechanic write was refused
namespace unknown
register a new namespace
why did my commit fail when the dry run passed
what prefix should this id have

## Instructions
1. Read `query(kind: "namespaces")` before choosing an id. It lists every registered namespace,
   who owns it, which record kinds it accepts, and whether it has been reviewed. Pass `query` to
   search it, or `id` to read one.
2. Prefer an existing namespace. A new one is new identity space, and identity space is the thing
   this system is least able to take back — ids are permanent and there is no rename.
3. When none fits, register it with `commit(kind: "system.namespace.register")`, supplying `id`,
   `owner`, `description` and `allowedKinds`. Dry-run it first, as with every commit.
4. A registered namespace arrives needing review. Nothing may be written into it until a person
   reviews it, and the caller cannot review its own request — that is the whole point of the gate.
   Say what you registered and what it is for, and let the operator review it.
5. Only then write the record. Its prefix must match the namespace exactly, and the namespace must
   accept that record kind.

## Constraints
- A namespace is never created as a side effect of using it. Writing into an unregistered prefix
  fails; it does not register anything.
- `allowedKinds` is a whitelist. A namespace registered for `mechanic` will refuse a `procedure`
  with the same prefix.
- A namespace whose ancestor is disabled is unusable regardless of its own state.
- Review status is not a caller field. Registering does not grant use; a person reviewing does.
