# Feature-implementation document authoring for LLMs

Status: **Required before implementing a new feature slice**

## Purpose

A feature implementation document turns one ready dependency-tree leaf into one bounded, reviewable
assignment. It states exactly what may change, what behavior must result, how D&D 5e 2024 alignment
is proved, and where the implementation must stop.

It is prospective authorization, not runtime authority and not a completion receipt.

## Preconditions

Do not author an implementation document until:

- the relevant dependency leaf passes the readiness test in
  [DEPENDENCY_TREE_AUTHORING.md](DEPENDENCY_TREE_AUTHORING.md);
- existing catalog/code owners have been searched;
- every semantic boundary is either confirmed or explicitly marked as blocking; and
- the assignment contains only one coherent slice and one root transaction owner.

If those conditions fail, revise the dependency tree instead of asking the implementation model to
guess.

## Mandatory identity

Every document begins with:

- feature and slice ID;
- status: `draft`, `awaiting confirmation`, `active`, `blocked`, or `accepted`;
- owning roadmap and dependency tree;
- ruleset alignment: `dnd2024-owned`, `dnd2024-compatible`, or `ruleset-neutral`;
- SRD 5.2.1 source ID and exact locator for `dnd2024-owned` work;
- requested outcome and explicit exclusions;
- allowed files/areas and exact stop point; and
- confirmation decisions already granted or still required.

An `active` document has no unresolved semantic gate.

## D&D 5e 2024 implementation contract

For ruleset-owned work, include a compact alignment table:

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |

The document must:

- use the 2024 rules and vocabulary consistently; never silently import a 2014 rule;
- distinguish source rule, repository design decision, optional rule, and house rule;
- reuse canonical ability, proficiency, D20 Test, action-economy, condition, damage, movement,
  equipment, rest, advancement, and content owners where relevant;
- derive modifiers, DCs, ranges, eligibility, resource costs, and outcomes from authoritative state;
- keep D&D formulas/branches/outcomes in catalog JavaScript and immutable content in catalog data;
- keep C# limited to generic context materialization, sandboxing, typed effects, transactions,
  composition plumbing, audit, retrieval, and protocol handling; and
- reject ruleset drift when source content/version or required owner state is missing.

If exact SRD behavior is unavailable in the registered source or conflicts with an accepted owner,
stop for source/semantic confirmation. Do not fill the gap from model memory.

## Required sections

1. **Outcome and boundary** — one capability, inclusions, exclusions, and user-visible acceptance.
2. **Prerequisite evidence** — only dependencies used by this slice, with receipt/test/catalog links.
3. **Artifacts** — existing/revised/new IDs, schemas, mechanics, procedures, fixtures, migrations,
   public kinds, and code seams. New permanent/public/schema items must show confirmation.
4. **Authoritative state and input** — closed request shape, role bindings, values resolved by the
   backend, and values callers may never supply.
5. **Behavior** — ordering, missing/null/empty semantics, calculation/transition algorithm,
   deterministic seed use, result shape, typed effects, and transaction ownership.
6. **Failure contract** — malformed, missing, wrong-scope/state, unauthorized, stale, replayed,
   blocked, and injected-failure behavior with no-change expectations.
7. **Implementation sequence** — catalog/contracts first, smallest generic host change second,
   tests/evidence last; no unrelated cleanup.
8. **Acceptance matrix** — positive, negative, boundary, deterministic, replay, rollback,
   fresh-import, compatibility, and surface cases as applicable.
9. **Verification** — focused commands, catalog validation, full-suite acceptance, and protocol walk
   only for MCP/dependency-registration changes.
10. **Receipt and stop** — where evidence will be recorded and the exact point at which work ends.

## Compact template

```markdown
# <Feature> <Slice> implementation — <capability>

Status: <draft | awaiting confirmation | active | blocked | accepted>
Owner/roadmap:
Dependency tree/leaf:
Ruleset alignment:
Source ID and locator:
Outcome:
Exclusions:
Allowed files/areas:
Stop point:

## Confirmed decisions
## D&D 5e 2024 alignment
| Rule concern | Source meaning | Existing owner | Consequence |

## Prerequisite evidence
## Runtime artifacts
## Authoritative state and closed input
## Behavior, result, and typed effects
## Failure, replay, and rollback contract
## Implementation sequence
## Acceptance matrix
## Verification commands
## Completion receipt and exit gate
```

## Completion lifecycle

After implementation:

1. query/read back or otherwise inspect every authored artifact;
2. run the stated verification against the same worktree;
3. write a short receipt containing delivered boundary, evidence, and exclusions;
4. change roadmap/dependency status once; and
5. remove completed implementation prose when catalog/code/tests/receipts preserve it.

Never turn the implementation document into a dated diary or a second copy of executable source.
