# Feature 5 system dependency plan — roster projection and mechanic composition

Status: **Leaf 1 verified — safe declarative composition is ready for the encounter-order parent**
Last updated: 2026-08-19

## Purpose

Finish Feature 5 without duplicating Initiative logic. The encounter-order parent must invoke the
verified individual Initiative mechanic for every participant, retain no copied ability data, and
commit one authoritative order snapshot. The current kernel exposes only fixed named roles and no
child-mechanic API, so the dependency expands as follows:

```text
encounter order
├─ existing declared encounter contents projection      [verified reusable roster identity]
├─ child mechanic invocation with frozen projections   [lowest leaf]
│  ├─ explicit permitted child id/version resolution
│  ├─ deterministic child seed derivation
│  ├─ depth/cycle/child-count limits
│  └─ child output/log/provenance, effects unapplied
└─ parent-owned encounter order component               [Feature 5 Slice 2]
```

Existing `RoleRequirement.IncludeContents` supplies a declared encounter/container's arbitrary
contained entity identities without giving JavaScript database access. The composition host, not a
new roster projection, resolves each child rule's declared components for those identities. This
avoids a parallel collection schema and keeps child visibility truthful.

## Leaf 1 — safe child execution

Add a kernel-owned composition service. A parent declares the child mechanic IDs and child role
bindings it may invoke; the host resolves each active child/version and its frozen projection,
derives child seeds from the parent seed and declared invocation index, then runs child source
without applying child effects. The parent receives immutable child data/log/effects only. The
service rejects inactive/undeclared children, depth/cycle breaches, invalid bindings, child limits,
and any child failure atomically. The parent remains the only writer through its returned effects;
the action audit records every child id/version/seed/projection.

Acceptance: a non-D&D parent demonstrably reuses one child without reimplementing it; child
effects are not applied; replay is exact; a cycle/depth breach and one failing child produce no
parent effects; no JavaScript receives CLR/store access.

### Implementation record — 2026-08-19

Architecture decision (user, 2026-08-19): preserve the strict string-only sandbox boundary. Child
composition therefore is **declarative host orchestration**, not a CLR callback in
`ctx.mechanics.run`. A parent declares permitted child calls/bindings in persisted metadata; the
host resolves, runs, and freezes children before the parent JavaScript executes. The parent reads
only serialized child results, never a store, delegate, or asynchronous host object.

The complete kernel layer is implemented in `IMechanicComposer`, `MechanicComposer`,
`MechanicRequirements`, `ActionRunner`, and the Jint harness. A persisted `children` declaration
names a child mechanic and binds its roles only from parent roles; `forEachContentsOf` plus `$item`
fans out over an `includeContents` parent role. A declaration may select one named parent-input
object, or the object keyed by each `$item` identity, so a closed child input is not polluted by
parent-only metadata. The host resolves active child mechanics, derives stable child seeds,
recursively composes them, and runs them before the parent. It rejects invalid
declarations, inactive children, invalid bindings, cycles, depth at or above eight, fan-out above
100, and child failures. Child effects remain unapplied proposals.

The enriched projection records child id/version/seed/declared-role-identities/output/log and is
serialised into the action audit. The Jint harness turns it into deep-frozen `ctx.children`; it does not receive a delegate,
CLR object, store, or callback. Focused action tests cover roster fan-out, deterministic distinct
child seeds, recursive composition, deep freezing, audit provenance, per-item closed input,
declaration rejection, and failure before parent execution. Focused composition/store tests pass
34/34 and full regression passes 232/232 under the local self-contained-disabled test setting;
`git diff --check` is clean apart from existing line-ending warnings.
setting.

## Parent gate

Only after both leaves are verified, revise the Feature 5 plan with the live encounter component,
tie-decision input, lifecycle/correction rules, and multi-participant matrix. The parent must not
start until that revised plan is complete.
