---
id: procedure.system.modify
category: system
name: Modify the application
governs: changing this application's C# code or configuration
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to change this application's own code or configuration.

## Instructions
1. Inspect the relevant subsystem — see `procedure.system.inspect`.
2. Retrieve its governing contracts.
3. Prefer extending an existing abstraction over adding a parallel one.
4. Preserve backward compatibility unless you are changing it deliberately, and say so if you are.
5. Add or update tests.
6. Record what changed, with the intent you were given in your own words.

## Constraints
- Never bypass the persistence APIs with arbitrary SQL.
- Never modify a core invariant without an explicit architecture decision recorded first.
- Never put a game concept into the kernel. If C# would need to learn what a hit point, a spell
  or an initiative order is, the change belongs in JavaScript instead.

