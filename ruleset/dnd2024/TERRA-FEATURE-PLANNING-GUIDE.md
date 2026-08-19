# Terra guide — planning future D&D features

Status: **Active planning instructions**
Last updated: 2026-08-18

## Purpose and authority

This guide tells Terra how to produce future feature plans at the same quality bar as the existing
Feature 3 and Feature 4 plans. It supplements the live `procedure.system.create-feature` contract;
it does not replace or weaken that contract.

Authority order is:

1. Catalog files and their verified live database import.
2. `procedure.system.create-feature`.
3. The current feature's repository dependency plan.
4. `ROADMAP.md` for feature order and broad boundaries.
5. `TERRA-IMPLEMENTATION-HANDOFF.md` for the current assignment.
6. `STATUS.md` for kernel/repository context only.

Catalog files are the canonical development source for runtime game contracts, component
definitions, entities, and mechanics; the imported database is the runtime copy. Repository
feature plans contain decisions and evidence, never duplicate runtime payloads or JavaScript
source.

## Planning-pass contract

A planning pass is separate from an implementation pass.

During a planning pass Terra may:

- query the live database;
- inspect repository documentation and kernel code read-only;
- consult the official rules source;
- create or revise planning, roadmap, handoff, and status documents;
- run read-only repository verification.

A planning pass must not create or revise a live game procedure, component, entity, or mechanic,
and must not run state-changing game actions. Planning ends with one lowest missing slice named as
the next assignment. Implementation begins only in a later reviewed pass.

## Required planning inputs

Before writing a plan, Terra must have all of the following:

1. One feature row or one plainly stated target capability.
2. The live `procedure.system.create-feature` contract, read in the current session.
3. The current roadmap and the immediately preceding verified feature plan.
4. The official SRD source identity and exact relevant section locators.
5. A live inventory of existing procedures, component definitions, mechanics, representative
   entities, and relevant audit history.
6. Search results proving whether an apparent dependency or owner already exists.

If an input is unavailable, record that as a planning blocker. Do not replace evidence with an
assumption.

## Planning algorithm

Follow these phases in order. Do not draft artifacts first and reverse-engineer dependencies
afterward.

### Phase 1 — state the capability and boundary

Write one outcome sentence from the caller/player perspective. It must say what becomes possible,
not how it will be coded.

Then write explicit included and excluded behavior. Boundaries must answer:

- Which actors or objects are supported?
- Which inputs are authoritative?
- Which state is read or changed?
- Is the result descriptive, state-changing, or both?
- Which nearby SRD rules are deliberately deferred?
- What would a future feature own instead?

If two independent player outcomes are joined by “and,” consider whether they are separate
features or sequential slices.

### Phase 2 — establish the official rule basis

Use the live source registry first. Verify the exact official document version, then consult the
official SRD rather than memory or a secondary summary.

Record section headings and stable PDF pages when available. Paraphrase only the rules necessary
to establish formulas, vocabulary, branches, and non-goals. Do not copy large blocks of source
text into the plan.

Separate three things that are often confused:

- a fact stored on an actor or item;
- a value derived from other authoritative facts;
- a transient circumstance supplied for one resolution.

This distinction determines component ownership and prevents duplicated state.

### Phase 3 — inventory the live system

Read before searching broadly:

1. `procedure.system.create-feature`;
2. `query(kind: "capabilities")` when payload or dry-run behavior is uncertain;
3. `query(kind: "world")`;
4. the source registry and directly relevant domain contracts;
5. governing world/mechanic/action contracts;
6. the live artifacts named as dependencies by the roadmap or prior plans;
7. representative entity state and relevant history.

Search procedures and mechanics using several classes of terms:

- the proposed id;
- the official rule name;
- synonyms a player or GM would say;
- likely match phrases;
- the broader category and the narrower action;
- adjacent rules that might already own part of the behavior.

Search results are not proof of absence when token matching returns nearby artifacts. Read the
candidate records and state why each does or does not own the new capability.

Record operation IDs for the workflow read, decisive dependency reads, overlap searches, world
inventory, and representative actor baseline. Avoid dumping entire history responses into the
plan.

### Phase 4 — decide ownership before decomposition

For every concept, ask these questions in order:

1. Is it permanent identity or a thing that exists? Use an entity.
2. Is it authoritative data attached to a thing? Use or extend a component.
3. Is it derived from authoritative data? Compute it; do not store it.
4. Is it one-roll or one-action context? Keep it in validated action input; do not persist it.
5. Is it uncertain resolution or reusable transformation? Use a mechanic.
6. Is it discoverable behavioral policy? It needs a live procedure contract with its slice.
7. Does an existing artifact already own it? Revise the owner instead of creating a sibling.

For every proposed component, explicitly decide:

- missing versus explicit empty semantics;
- complete replacement versus merge behavior;
- fixed vocabulary and canonical ordering;
- source-reference ownership;
- fields that must remain derived;
- normal creation/correction path;
- future migration or retirement constraints.

For every proposed mechanic, explicitly decide:

- one reusable owner versus one-per-case sprawl;
- exact roles and declared component requirements;
- closed input and rejection behavior;
- deterministic/random behavior and seed use;
- structured result shape;
- exact proposed effects and transaction boundary;
- player intent phrases and overlap risk;
- which consequences belong to another mechanic.

Record ownership decisions in the plan. These decisions matter more than proposed names because
they prevent two sources of truth.

### Phase 5 — build and recursively expand the dependency graph

Start with the target capability as the root. Add every prerequisite needed for authoritative
input, state, derivation, execution, routing, and verification.

For every node, assign exactly one status:

- **implemented** — cite a live artifact plus concrete behavioral/query evidence;
- **missing leaf** — standalone, implementable, and has no unresolved dependency below it;
- **blocked parent** — depends on one or more missing nodes;
- **excluded** — deliberately outside the feature boundary, with an owner or future feature named.

Recursively descend through every missing dependency. “The mechanic needs this field” is not a
leaf if the authoritative source, storage shape, or validated write path for that field is still
undefined.

Common hidden dependencies to look for:

- stable ids and source attribution;
- state creation/correction paths;
- missing-versus-empty semantics;
- derived-value formulas and their boundary bands;
- shared roll conventions;
- ordering and tie policies;
- intent routing among existing mechanics;
- downstream effect ownership;
- fixtures capable of representing missing or corrupt state;
- restoration of the shared test actor.

A dependency graph is complete only when every leaf is evidenced or independently implementable.

### Phase 6 — convert leaves into sequential slices

Put missing leaves before their parents. One slice should introduce one coherent lowest-level
capability and its contract, not every artifact that happens to share a theme.

A good slice:

- can be implemented and acceptance-tested without a later slice;
- leaves the live system internally valid and discoverable;
- has one clear owner;
- has a bounded artifact set;
- has a final state or cleanup obligation;
- can stop safely after its exit gate.

Usually combine a state contract, its component definition, and its validated administrative
recorder in one slice because the component should not be left without a normal safe write path.
Usually keep a consumer/resolution mechanic in a later slice after its state dependency is
verified.

Do not bundle siblings merely because one model call could write them. Do not split a procedure
contract away from the capability it governs.

### Phase 7 — fully specify every slice

Each slice section must include:

1. **Status and prerequisite:** what must already be verified.
2. **Runtime artifacts:** exact proposed ids, categories/scope, and whether each is new or revised.
3. **Governing contracts:** every live procedure Terra must re-read immediately before writing.
4. **Source locator:** exact live source entity and official section.
5. **Data/input contract:** closed fields, types, ranges, stable ids, ordering, null/missing/empty
   semantics, and caller-forbidden derived fields.
6. **Required state:** roles, component requirements, source-reference checks, and corrupt-state
   behavior.
7. **Resolution or recording algorithm:** formulas and branch order precise enough to test without
   embedding the final runtime source.
8. **Result and effects:** structured fields, effect count/types, and state boundaries.
9. **Invariants and non-goals:** facts that must remain true and nearby behavior not implemented.
10. **Implementation sequence:** live reads, searches, supported dry runs, identical commits,
    query-backs, real actions, restoration, documentation, and stopping point.
11. **Acceptance matrix:** objective positive, negative, boundary, replay, routing, state, and
    cleanup assertions.
12. **Exit gate:** an all-or-nothing definition of verified completion.

Do not include executable JavaScript, full commit payloads, or a duplicate JSON schema in the
repository plan. Describe the shape and behavior precisely; the live contract remains authority.

### Phase 8 — design an acceptance matrix that can find bugs

Use every applicable test class below. If one does not apply, say why rather than silently omitting
it.

| Test class | What the plan must prove |
| --- | --- |
| Happy path | Representative valid input returns exact structured data and expected effects |
| Boundaries | Minimum, maximum, formula transition points, empty/full collections, ties, or zero |
| Differential | Two cases differing in one fact differ by exactly the expected amount |
| Closed input | Missing, null, wrong type/case, unknown id, duplicate, extra, and caller-derived fields fail |
| Missing state | Required absent component/entity fails rather than defaulting |
| Corrupt state | Invalid stored shape/source/order/range fails before randomness or effects |
| Determinism | Same seed, input, state, and mechanic version replay identical structured results |
| Random selection | Correct call count, generation order, selected die, unequal dice, and ties |
| Natural rolls | Rules for natural minimum/maximum are proven with a counterexample DC when needed |
| Routing | Player phrases select exactly the intended scoped mechanic; administrative phrases stay separate |
| Effects | Exact effect count/type/data, atomic rejection, or explicit zero effects |
| State integrity | Rejections and effect-free actions leave exact bytes/revisions unchanged |
| Restoration | Shared actor returns to the declared baseline; disposable fixtures are queried then deleted |
| Readback | Every created/revised artifact is queried live at the intended version/status/scope |
| Repository | Full expected test count and `git diff --check` pass |

“Run a valid case” is not an acceptance assertion. Include expected numbers, deltas, order,
selected ids, effect counts, or byte comparisons wherever the rule permits them.

For formulas, test every transition boundary, not one representative level. For random rules,
choose fixed seeds that expose unequal rolls and ties. For natural-roll rules, use target numbers
that distinguish total comparison from automatic success/failure. For invalid inputs, prove no
effect and unchanged state; the error message alone is insufficient.

### Phase 9 — define evidence before implementation

Every acceptance group must be capable of recording:

- operation id;
- selected artifact id and version where relevant;
- exact structured values or state bytes;
- applied effect count and affected ids;
- error code plus domain-specific reason for expected failures;
- before/after actor revision or exact component data;
- repository test total and diff-check result.

Plan evidence must be concise. Keep one or two decisive operation IDs per assertion group rather
than listing exploratory noise. A dry-run success proves only that a payload is admissible; it does
not prove runtime behavior.

### Phase 10 — run the plan-quality audit

Before calling the plan ready, answer every question below with “yes” and point to a section:

1. Is there exactly one target capability with explicit non-goals?
2. Is the official source/version/locator concrete?
3. Were live owners and overlaps searched using multiple phrasings?
4. Does every implemented dependency have evidence rather than assumption?
5. Was every missing dependency expanded to a standalone leaf?
6. Is ownership of state, derived values, transient input, resolution, and consequences explicit?
7. Does every slice include its contract and leave a usable, valid system when stopped?
8. Is exactly one lowest slice named as next?
9. Are inputs closed and missing/null/empty semantics explicit?
10. Are formulas, canonical ordering, effects, and result fields testable without guessing?
11. Does the matrix cover boundaries, invalid input, missing/corrupt state, replay/routing, and
    final-state integrity proportionately?
12. Are dry-run limitations and query-back steps correct for each MCP commit kind?
13. Are shared-actor restoration and fixture deletion explicit?
14. Does the exit gate require objective evidence and repository checks?
15. Does the plan avoid runtime payload/source duplication in the repository?
16. Does the planning pass stop before implementation?

Any “no” means the plan remains draft.

## Red flags requiring plan revision

Stop and revise instead of proceeding when the plan contains any of these:

- a caller supplies a modifier, total, selected die, outcome, or other value the system can derive;
- the same fact would be stored in two components;
- one mechanic is proposed per ability, item, creature, spell, or situation without a structural
  reason;
- a generic example mechanic is treated as the D&D rule;
- a missing component silently means an empty list or zero;
- a plan creates a component but postpones its safe normal write path;
- a resolution mechanic also applies consequences owned by its cause or a later feature;
- a test asserts narration or `ok: true` but not structured data/effects/state;
- only happy paths are specified;
- invalid tests do not compare state before and after;
- temporary fixtures have no explicit deletion path;
- two slices are authorized in one pass;
- a new MCP tool/kind, database migration, or C# rule helper is proposed for game data/JavaScript;
- a repository file is proposed as runtime authority;
- an overlap is rationalized instead of resolving ownership;
- a parent is marked ready while any dependency is merely “assumed available.”

## Efficient research without lowering quality

Terra should conserve tokens by reducing duplication, not evidence:

- query exact ids for known dependencies and use broad search only for ownership/overlap discovery;
- request bounded history around the relevant actor or artifact;
- record compact operation ids and numerical results rather than full response bodies;
- read only the current plan plus the immediately relevant prior plan;
- reuse verified formulas and vocabularies by citing their live contracts;
- do not restate the kernel architecture inside every game-feature plan;
- use tables for dependency evidence and slice order, and a tree for recursive dependencies;
- write one complete plan pass, then run the quality audit once before editing.

Token efficiency never justifies skipping live reads, official sources, negative cases, restoration,
or stop gates.

## Required repository updates after a planning pass

When the plan passes the audit:

1. Create `ruleset/dnd2024/feature-XX/FEATURE-XX-DEPENDENCY-PLAN.md`.
2. Update `ROADMAP.md` status and the feature row/graph without claiming implementation.
3. Update `TERRA-IMPLEMENTATION-HANDOFF.md` so its current assignment names only the first slice.
4. Add one concise `STATUS.md` planning bullet.
5. Run the expected repository test suite and `git diff --check`.
6. Report that no runtime game artifact was created and stop.

Do not mark a feature or slice implemented during a planning-only pass.

## Copyable feature-plan skeleton

Use this structure, deleting only sections proven irrelevant:

````markdown
# Feature <N> dependency plan — <capability>

Status: **Planned; Slice 1 is the next and only authorized implementation pass**
Last updated: <date>

## Execution rule
<file-first catalog workflow authority, one-slice stop rule, and import/verify runtime agreement>

## Target capability
<one caller/player outcome sentence>

### Included
- ...

### Excluded
- ...

## Official source basis
<source entity, official document/version, locators/pages, concise rules basis>

## Verified existing dependencies
| Dependency | Current evidence |
| --- | --- |
| ... | <live id/version plus operation/test/query evidence> |

## Recursive dependency analysis
```text
<root>
├─ <dependency> [implemented + evidence]
├─ <dependency> [missing: Slice 1 leaf]
└─ <parent> [blocked: Slice 2]
```

## Dependency and ownership decisions
1. <where authoritative state belongs and why>
2. <what is derived/transient and must not be stored>
3. <which existing artifact is revised or why a new owner is distinct>
4. <which downstream consequence belongs elsewhere>

## Slice order and stop gates
| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | ... | plan reviewed | ... |

## Slice 1 — <lowest leaf>
### Runtime artifacts
### Governing contracts and source locator
### Data/input contract and required state
### Resolution/recording behavior
### Result and effects
### Invariants, failure behavior, and non-goals
### Slice 1 implementation sequence
### Slice 1 acceptance matrix
### Slice 1 exit gate

## Slice 2 — <dependent parent>
<same complete structure; explicitly blocked until prior review>

## Plan-quality audit
<record any feature-specific audit findings>

## Plan-change rule
<stop and descend when a new dependency or ownership conflict appears>
````

The skeleton is a minimum structure, not permission to fill sections with vague prose. A plan is
ready only when another agent can implement the first slice without inventing ids, semantics,
failure behavior, tests, cleanup, or the definition of done.
