# D&D 2024 G4 Mechanic Operation Identity Implementation

Status: accepted

## Boundary

This slice closes complete-campaign dependency G4 without adding campaign-specific behavior to the
kernel. The interaction coordinator remains the owner of the already-authorized root operation
identity. Application execution carries that identity into the immutable mechanic projection, and
the host derives every child identity. Catalog JavaScript may observe identity but cannot provide or
replace it.

Included owners:

- `src/system/interaction-orchestration/hosting/InteractionExecutionCoordinator.cs` as the root
  identity issuer already accepted at G1;
- `src/system/application-execution/` for propagation and deterministic child derivation;
- `src/system/mechanics/` for the immutable sandbox projection;
- focused application-execution and mechanic sandbox tests;
- the complete-campaign dependency graph and a completion receipt after acceptance.

Excluded:

- new public interaction or MCP request fields;
- campaign record, clock, session, arc, or website behavior;
- live database or catalog-state mutation;
- using operation identity as a substitute for application-owned entity-id policy.

## Contract

An executing mechanic receives a frozen `ctx.execution` value with:

- `rootOperationId`: the interaction coordinator's immutable root action operation id;
- `operationId`: the current mechanic invocation id;
- `parentOperationId`: null for the root and the immediate parent id for a child;
- `invocationOrdinal`: zero for the root and the host-assigned child ordinal otherwise.

The root identity is copied from `ApplicationEcsExecutionIdentity`, which the interaction
coordinator derives from authorized, verified execution evidence. A caller-supplied input property
named `execution` has no authority over this context.

For child invocation ordinal `n`, the host derives the child operation id as the first 16 bytes of
SHA-256 over this UTF-8 canonical sequence, rendered as 32 lowercase hexadecimal characters:

```text
mechanic-child-operation-v1\n
<root operation id>\n
<parent operation id>\n
<ordinal>\n
<qualified child mechanic id>\n
<exact child content fingerprint>
```

The qualified child mechanic id and exact active content fingerprint are resolved before
derivation. This makes sibling and nested identities deterministic, stable under replay, and
unforgeable through mechanic input. The parent receives each child's execution context with the
frozen child result. Read-only mechanic evaluation that has no authorized execution identity keeps
`ctx.execution` null.

The typed root effect batch continues to use the same root `ApplicationEcsExecutionIdentity`; this
slice does not create an independent commit boundary for child data-only mechanics.

## Failure and transaction behavior

- A malformed execution context is rejected before JavaScript evaluation.
- Child identity derivation is pure and cannot write or reserve state.
- Existing action replay/conflict behavior remains authoritative at the root operation boundary.
- Children remain data-only and cannot commit effects independently.
- No new transaction owner or migration is introduced.

## Acceptance

- root JavaScript observes the exact host-issued identity;
- child JavaScript observes deterministic root/current/parent identities and its ordinal;
- the parent observes the same immutable child execution context in `ctx.children`;
- nested and sibling invocations receive distinct deterministic ids;
- caller input cannot replace or mutate `ctx.execution`;
- evaluation without execution authority observes null;
- the existing typed-effect replay/conflict acceptance still passes;
- focused application-execution and mechanics suites pass.
