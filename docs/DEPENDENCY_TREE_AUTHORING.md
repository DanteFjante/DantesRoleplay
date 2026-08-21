# Dependency-tree authoring for LLMs

Status: **Required when a feature crosses owners or has an unproven prerequisite**

## Purpose

A dependency tree answers one question: **what must already be true before one player-visible
capability can be implemented safely?** It establishes ownership and readiness; it does not
authorize code, catalog records, permanent IDs, migrations, or public-surface changes.

Use this guide before writing a feature implementation document. For a genuinely local change with
one existing owner, record the dependency boundary directly in the implementation document instead
of manufacturing a separate tree.

## Required inputs

Read according to [IMPLEMENTATION_DOCUMENT_READING.md](IMPLEMENTATION_DOCUMENT_READING.md), then
inspect only:

1. the owning roadmap row;
2. relevant catalog procedures, schemas, mechanics, and source records;
3. code/tests that implement the suspected owners; and
4. receipts needed to prove prerequisites.

Search `catalog/` and code before proposing an ID. Mark every dependency as `verified`, `ready`,
`planned`, `missing`, `blocked`, or `conflicting`, with a link or concrete evidence.

## D&D 5e 2024 alignment gate

Every tree declares one alignment class:

| Class | Meaning |
| --- | --- |
| `dnd2024-owned` | Implements a D&D 5e 2024 rule and must cite `source.dnd2024.srd-5.2.1` with an exact locator. |
| `dnd2024-compatible` | Is not itself a D&D rule but consumes D&D state/actions without redefining their meaning. |
| `ruleset-neutral` | Is generic engine/game infrastructure and contains no D&D vocabulary or assumptions. |

For `dnd2024-owned` work:

- use the repository scope/key conventions `dnd2024` and `ruleset.dnd2024.*`;
- derive rule meaning from SRD 5.2.1, not remembered 2014 rules, unofficial summaries, or another
  edition;
- name the exact source section/locator and paraphrase only the rule needed by the feature;
- model D&D concepts through their existing owners: abilities, D20 Tests, proficiency, action
  economy, conditions, damage, movement, equipment, rests, class/species/background/feat/spell
  content, and source provenance;
- identify any 2014 compatibility behavior, optional rule, or house rule explicitly and require
  confirmation rather than blending it into the 2024 rule; and
- keep calculations, eligibility, timing, and outcomes in catalog JavaScript. C# may provide only
  generic hosting/orchestration infrastructure.

`dnd2024-compatible` and `ruleset-neutral` features must not introduce a parallel rule model or
accept derived D&D authority from callers.

## Construction algorithm

1. **State the root outcome.** One testable player/GM capability, plus explicit non-goals.
2. **Find owners.** For every fact, calculation, transition, effect, projection, and authority
   decision, name the existing owner or mark it missing.
3. **Draw direct dependencies.** A child is required only if the parent cannot meet its acceptance
   contract without it.
4. **Expand recursively.** Continue until every leaf is implemented or independently implementable.
5. **Remove false dependencies.** A shared fixture, convenient sequence, UI consumer, or future
   enhancement is not automatically a prerequisite.
6. **Detect conflicts.** Flag duplicate state, two transaction roots, competing IDs/schemas,
   ruleset-specific C#, or callers supplying derived values.
7. **Order leaves.** Prefer the smallest useful leaf or seam proof; do not bundle siblings.
8. **Define evidence.** Each leaf needs positive, negative/no-change, boundary, replay,
   deterministic, rollback, and compatibility evidence as applicable.
9. **Identify semantic gates.** Permanent IDs, schema meaning, migrations, public kinds, house rules,
   cross-owner semantics, and completed-feature acceptance require confirmation.
10. **Stop at planning.** Produce or update the tree and the roadmap link; create no runtime
    artifact.

## Leaf readiness test

A leaf is `ready` only when all are true:

- one owner and one ruleset-alignment class are named;
- its authoritative source/state is available;
- proposed IDs are either existing or explicitly awaiting confirmation;
- caller input is closed and all derived values are identified;
- result, typed effects, transaction owner, failures, replay, and rollback are specified;
- the C#/catalog JavaScript boundary is explicit;
- tests can prove acceptance without relying on conversation history; and
- its exit gate does not depend on an unspecified sibling.

Otherwise the leaf remains `planned`, `missing`, `blocked`, or `conflicting`.

## Document format

```markdown
# <Feature ID> dependency tree — <capability>

Status: <planning only / awaiting confirmation / lowest leaf ready>
Ruleset alignment: <dnd2024-owned | dnd2024-compatible | ruleset-neutral>
Source: <source ID + exact locator, or not applicable>

## Outcome and non-goals
## Existing owners and evidence
| Concern | Owner | State | Evidence |

## Dependency tree
<compact tree; every node has a state>

## Conflicts and decisions
## Ordered leaves
| Order | Leaf | Depends on | Exit gate |

## Lowest ready leaf
<closed summary sufficient to create its implementation document>

## Confirmation gates
## Planning receipt
- Runtime artifacts created: none.
```

## Maintenance

The tree is prospective. When a leaf completes, link its receipt and collapse its detail to one
verified line. Do not append implementation diaries. Remove the tree when all work is complete and
the roadmap plus receipts preserve its result.
