# World Feature 17 Slice 1 handoff — effect-free small-world composer

Status: **Complete and awaiting review — W17 Slice 1 verified.**
Last updated: 2026-08-21

## Assignment identity

- Assignment ID: W17-S1
- Subsystem: World
- Owning plan: `WORLD-FEATURE-17-SMALL-WORLD-COMPOSER-PLAN.md`
- Exact slice: Effect-free closed small-world child composer
- Target model profile: standard implementation after ratification
- Requested outcome: Return one valid C10 fixed World graph as deterministic typed World-only
  effects and review evidence with zero durable writes.
- Explicitly excluded work: C10 preview/create, campaign effects, public transport, catalog edits,
  transaction/audit/event/notification ownership, fixtures, alternate world shapes, and generated
  content.
- Stop point: W17 Slice 1 receipt after all focused evidence; do not begin C10 R5/R6.
- Reviewer/authority: C10 R3 cross-root ratification record.

## Activation gate

The [R3 record](../../campaign/feature-10/CAMPAIGN-FEATURE-10-R3-CROSS-ROOT-RATIFICATION.md) freezes
the namespace source/syntax, outer coordinator, exact child effect/result representation, preview
visibility, fingerprint ownership, and failure/audit correlation. If a later change differs from
that record, revise W17 before implementation; this handoff does not permit an implementation
model to choose a replacement.

## Required reads

Read in order: `AGENTS.md`; the W17 plan; the R3 ratification record; W1/W3/W4 plans and their
governing location/faction/knowledge procedures; `DantesRoleplay/World/IStagedWorldComposer.cs`;
`DantesRoleplay.DataAccess/StagedWorldComposer.cs`; C10 dependency and execution plans; then only
the ratified allowed source/test files.

## Verified baseline and allowed files

W1, W3, W4, and the generic staged overlay are implemented. The staged overlay starts one root
entity, permits only a declared fixed ID boundary, dry-runs the accumulated effects, and exposes a
read-only virtual World. It does not write state.

After activation, the allowed implementation set is exactly the files named by R3, expected to be:

- `DantesRoleplay/World/SmallWorldComposition.cs` — child-only closed types/interface;
- `DantesRoleplay.DataAccess/SmallWorldCompositionPlanner.cs` — deterministic effect-free planner;
- `DantesRoleplay.Tests/WorldFeature17SmallWorldCompositionTests.cs` — focused coverage;
- `DantesRoleplay.DataAccess/DataAccessServiceCollectionExtensions.cs` only if R3 requires DI;
- `world/feature-17/WORLD-FEATURE-17-SLICE-1-RECEIPT.md`.

No catalog file, campaign source, MCP source, migration, public command, or generic staged-overlay
implementation may change.

## Closed implementation contract

Implement the exact request/result, local-key mapping, ID derivation, fixed 14-entity graph,
20-component/4-containment/20-relationship counts, canonical effect order, and stable error codes
in the W17 plan. Use only the ratified namespace input and existing World state. Derive all IDs,
statuses, visibility, relationship endpoints, and `{}` relationship data; callers cannot supply
them. A valid output is effect-free; an invalid output has no effects/mapping/counts.

Call `IStagedWorldComposer.StartAsync` once for the root and `AppendAsync` once with every remaining
effect in canonical order. Do not call `IEffectApplier` directly. Do not begin, commit, or roll
back a transaction. Do not record an operation/audit/event/notification. The staged virtual World
may be returned only through the ratified internal child result; it is never exposed through MCP.

## Acceptance matrix

| Test class | Exact assertion |
| --- | --- |
| Valid graph | 14/20/4/20 counts, all derived IDs/map entries, exact effect type/order/data, fixed links, and zero writes. |
| Closed input | Null/missing/empty/unknown/derived/raw/permanent-ID/local-key/invalid W1-W4/classification input returns the stated stable code with no effects or state delta. |
| Graph integrity | Wrong scope/about/support target, visibility/status override, adjacency orientation, faction endpoint, or containment slot is rejected before staging. |
| Collision/definition state | Existing derived ID, duplicate derivation, inactive/missing definition, guard failure, and staged validation failure return the stated invalid result without reservation. |
| Determinism | Two calls over identical state are byte-equivalent; a competing claimed ID changes only the collision outcome. |
| State integrity | Byte-compare entities, components, containment, relationships, events, notifications, and operations before/after every valid and invalid call. |
| Repository | Focused tests pass; full suite at feature acceptance; `git diff --check` passes. Catalog validation is N/A unless an authorised catalog edit exists. |

## Escalation and completion

Stop if R3 is absent/different, a required existing component contract is unavailable, an effect
requires a new vocabulary, the staged overlay cannot validate the fixed boundary, or unrelated
dirty work overlaps an allowed file. Record only the smallest blocking evidence. On success, write
the W17 receipt with test results and zero-write proof, mark this handoff Complete and awaiting
review, and stop.
