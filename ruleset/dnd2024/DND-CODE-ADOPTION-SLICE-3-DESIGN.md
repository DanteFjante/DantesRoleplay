# D&D code-adoption Slice 3 design — test-only adapter seam

Status: **accepted 2026-08-25; Slices 3A–3C complete**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree: [D&D code-adoption dependency plan](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Selected cohort: [Slice 2C receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-2C-RECEIPT.md)
Ruleset alignment: **mixed by child** — 3A/3C are `dnd2024-compatible`; 3B is
`dnd2024-owned`
Source review: [Slice 3 source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md)

## Outcome and non-goals

Prove, without production activation, that canonical component state can be materialized through a
dependency-aware structural projection, handed to one bounded JavaScript rule with kernel-owned
seeded randomness, and normalized into a deterministic effect-free result. The chosen rule is one
raw fixed-DC ability check.

This slice does not register or activate `dnd2024.abilities`, a D&D projection, procedure, mechanic,
source, action, or public operation. It does not import donor packages, donor campaign state,
reducers, persistence, events, RNG, or Foundry code. It excludes skills, proficiency, character
level, Advantage/Disadvantage, conditions, saves, Initiative, attacks, consequences, and all state
writes.

## Closed architecture

~~~text
disposable component state
  -> structural ability-score projection
  -> dependent raw-check operation-view projection
  -> frozen MechanicProjection role + closed { ability, dc } input + seed
  -> test-only catalog JavaScript in Jint
  -> normalized result + zero effects/events/notifications
  -> archived/donor/SRD vectors and boundary assertions
~~~

The structural projection copies data only. The D&D ability-modifier formula, roll, total, and
success branch stay in JavaScript. The generic C# test harness knows only manifest-declared roles,
component/projection references, input, seed, and expected envelopes; it contains no D&D IDs,
vocabulary, formula, or outcome branch.

## Existing owners and evidence

| Concern | Existing owner | State for Slice 3 |
| --- | --- | --- |
| Versioned structural mappings, dependency cycles, exact source versions, materialization, and reverse impact | `projection-materialization` | verified and reused unchanged unless 3A proves a generic defect |
| Exact application mechanic projection and read-only evaluation | `application-execution` | verified; production code is out of scope |
| Frozen JavaScript context, execution limits, and seeded `ctx.randomInt` | `mechanics` / `JintMechanicEngine` | verified and reused unchanged |
| Ability state shape | archived `dnd2024.abilities` component/schema | first-party recovery evidence only |
| Raw fixed-DC ability-check behavior | archived `mechanic.dnd2024.check.ability` plus SRD review | selected recovery candidate, not active |
| Rule authority | `source.dnd2024.srd-5.2.1` exact headings and PDF pages 5–7 | verified for this probe only; runtime source registration remains later work |
| Foundry engineering reference | pinned `module/dice/d20-roll.mjs` | reviewed, reference-only, no copied bytes |
| Effects and transaction | none | the evaluator never invokes an effect applier or opens a game transaction |

## Subslice and model plan

One effort point (EP) is approximately one focused model-day including its tests and review fixes.

| Subslice | Closed deliverable | Depends on | Effort | Implementation model | Review model | Exit gate |
| --- | --- | --- | ---: | --- | --- | --- |
| 3A | Manifest-driven operation-view mapping over a disposable component and a two-node projection dependency chain | Slice 2C and source review | 2 EP | `gpt-5.6-terra` high | `gpt-5.6-sol` high | exact view materializes; reverse impact is correct; undeclared/stale inputs reject |
| 3B | One test-only first-party-recovery JavaScript wrapper for a raw ability check | 3A | 3 EP | `gpt-5.6-terra` high | `gpt-5.6-sol` xhigh | fixed inputs and seed produce the exact explained effect-free result; malformed input fails before RNG |
| 3C | Neutral vectors, archive/donor normalization, parity, replay, and isolation proof | 3A–3B | 2–3 EP | `gpt-5.6-terra` high; `gpt-5.6-luna` medium may fill frozen vectors | `gpt-5.6-sol` xhigh | parity/intentional differences are explicit; no runtime registration or undeclared access exists |

Total: **7–8 EP**. These are already the implementation subslices. Within each, fixture generation,
implementation, and verification are work packets rather than further subslices because they share
one owner and one exit gate. Split again only if 3A discovers a necessary production-kernel change
or 3C discovers an intentional rule difference requiring confirmation.

## Slice 3A — operation-view mapping

Delivered assignment: [Slice 3A implementation](DND-CODE-ADOPTION-SLICE-3A-IMPLEMENTATION.md) and
[receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-3A-RECEIPT.md).

3A creates a development-only probe manifest outside `catalog/`. In a disposable database, a
generic test runner registers an ephemeral application, state space, ability-state component type,
and two structural projections. The first projection exposes the six declared scores. The second
depends on the first and produces the operation view consumed by the later wrapper. Keeping all six
scores is the smallest static view for a mechanic whose closed action input chooses any one of six
abilities; no level, skill, condition, content, relationship, containment, or campaign state enters
the view.

The test must prove exact role binding, component version/schema hash, projection content hash,
source revisions, dependency ordering, and reverse impact from an ability-score field through both
projection nodes. Missing fields/components, a stale reference, wrong state space, an extra role,
or an undeclared/cyclic dependency fails without a partial view.

## Slice 3B — pure JavaScript wrapper

3B adds one development-only JavaScript candidate and result schema under the probe directory. It
is an adapted first-party recovery, not a direct copy or active catalog mechanic. Its provenance row
must name the exact archived source path/hash, target hash, narrowed transformation, verified SRD
locators, and Foundry reference review.

Closed input is exactly:

```json
{"ability":"str|dex|con|int|wis|cha","dc":0}
```

`dc` is a finite nonnegative integer. The frozen operation view contains exactly the six integer
ability scores, each 1–30. The seed is supplied only through `MechanicProjection.Seed`; neither the
input nor state may supply a roll, modifier, total, result, source identity, or RNG.

The wrapper validates the complete view and input before its single `ctx.randomInt(1, 20)` call,
selects the requested score, derives `floor((score - 10) / 2)`, adds it to the roll, and succeeds
only when the total equals or exceeds the DC. It returns one normalized `ability-check` data object
with ability, score, DC, die, roll, one auditable modifier, total, succeeded, and fixed source
references. Effects, events, and notifications are empty. Natural 1 and 20 receive no special
ability-check branch.

## Slice 3C — parity and isolation proof

3C freezes a small neutral scenario set and a generic comparison runner. At minimum it covers score
1, 8, 10, 16, and 30; DC below/equal/above the total; all six ability IDs; seed replay; seed 7
(first d20 is 1); seed 36 (first d20 is 20); malformed/extra input; missing/stale projection data;
and mutation attempts against frozen context.

Normalization compares only shared semantics: ability, score, DC, roll, ability modifier, total,
and success. It may use the archived Feature 1 output and the pinned donor
`src/derive/ability.ts::abilityModifier` as engineering comparators. The donor's whole
`computeAbilityCheck` path is deliberately excluded because it requires character, content, item,
effect-stack, proficiency, condition, and optional consumer facts outside this cohort. Foundry is
reference-only and is never executed by the runner.

Any difference must be classified as wrapper defect, historical defect, source correction, or
intentional difference. Source correction or intentional difference stops for confirmation. The
final proof must also show byte-stable projection/result for the same state/input/seed, changed
results for a controlled score or seed change, zero effects/events/notifications, unchanged ECS
state/revisions, no application/catalog/projection registration after fixture disposal, and no
network/package/runtime donor dependency.

## Failure, replay, and rollback contract

- Validate state, projection references, roles, input object, exact keys, value types/ranges, and
  output schema before declaring success.
- A malformed or incomplete declaration fails closed with no sandbox run. Malformed D&D input or
  view fails before the first RNG draw.
- The same component revisions, projection versions/hashes, input, and seed produce byte-identical
  normalized data. A replay does not write or apply anything.
- There is no rollback path because Slice 3 runs the read-only materializer/evaluator only. The
  no-change assertion covers ECS rows/revisions and registration tables before and after the run.
- If the proof requires a production code change, permanent ID, schema meaning, public surface, or
  test-only bypass of a kernel invariant, stop and design a separately confirmed leaf.

## Acceptance matrix

| Concern | 3A | 3B | 3C |
| --- | --- | --- | --- |
| Positive | dependency view materializes | one seeded check returns explained output | normalized vectors agree |
| Negative | missing/stale/wrong-scope/extra-role rejects | missing/extra/wrong type/range rejects before RNG | every rejected case leaves state unchanged |
| Boundary | only ability state is visible | scores 1/30, DC 0, seeds 7/36 | natural 1/20 follow total comparison |
| Determinism | canonical output and source revisions | same seed/input/view, same result | repeated full pipeline is byte-identical |
| Dependency awareness | exact forward/reverse graph | consumes only top operation view | field impact names both projection consumers |
| Isolation | no catalog/runtime registration | no store/network/CLR/donor access | zero effects/events/notifications and unchanged DB |
| Compatibility | generic harness has no D&D rule logic | catalog-compatible JS/closed Jint context | archived/donor differences classified |

## Sequence, confirmations, and stop

1. Activate and implement 3A only.
2. Accept 3A with focused/full tests and a receipt, then author the final 3B implementation document.
3. Implement/accept 3B, then author the final 3C implementation document.
4. Complete 3C only after Sol xhigh rule/boundary review and user confirmation of any intentional
   difference. Slice completion itself is a feature-acceptance confirmation gate.

No confirmation is required to implement 3A as designed because it creates only disposable fixture
IDs and development-only files. Confirmation is required before any permanent application/source/
component/projection/mechanic ID, catalog record, schema-meaning change, production registration,
public operation, migration, intentional difference, or production activation.

Completion: Slices 3A–3C are accepted. The test-only operation view, wrapper candidate, retained-
source parity vectors, and isolation evidence are recorded by their receipts. Runtime catalog,
application, database, and archive state changed: none.
