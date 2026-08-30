# DND2024 contract and recipe cutover dependency tree — activate the canonical prototype contracts

Status: **active; mechanic contract-owner repair remains a prerequisite**
Ruleset alignment: **dnd2024-compatible**
Source: **not applicable; this slice changes application activation, not D&D rule meaning**
Owner: [D&D 2024 roadmap](ROADMAP.md)
Platform owner: [Interaction orchestration dependency tree](../../platform/interaction-orchestration/INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md)

## Outcome and non-goals

Activate the reviewed `catalog/applications/dnd2024` snapshot so its current mechanic contracts,
JavaScript mechanics, procedures, component contracts, and authored recipe content replace the stale
pre-cutover application snapshot used for discovery. Verify old contract identities against the
current catalog before activation.

This tree does not recreate retired compatibility behavior, rewrite historical receipts, invent
learned interaction recipes, change D&D rules, add permanent IDs, or change schemas. It permits one
ruleset-neutral kernel correction: raise the already-bounded retrieval document ceiling from
32,000 to 64,000 characters so the current 49,694-character character-creation contract can be
returned intact. Documents above the new ceiling still fail closed.

## Existing owners and evidence

| Concern | Owner | State | Evidence |
| --- | --- | --- | --- |
| D&D mechanic contracts and JavaScript | `catalog/applications/dnd2024/mechanics/` | verified source | 69 current mechanic contracts; old D&D contract identities are either retained and reshaped or deliberately superseded |
| D&D procedures | `catalog/applications/dnd2024/procedures/` | verified source | 57 current procedures; 18 old D&D procedure identities are retained and reshaped; `procedure.character.playtest-bootstrap` is deliberately absent |
| Crafting recipes | D&D content and crafting component/archetype owners | verified source | 18 authored recipe records plus the current component and archetype contracts |
| Learned interaction recipes | interaction orchestration / SQLite | verified empty | both the archived and current live databases contain zero interaction recipe, revision, and evidence rows |
| Historical receipts | interaction orchestration / SQLite | verified preserved | seven resolution receipts already exist in both archived and current databases; evidence is not rewritten |
| Source overlay and activation | application source registry and activation | ready | `dnd2024-core` already owns `catalog/applications/dnd2024/**/*`; current preview is valid with zero source problems |

## Dependency tree

```text
Activate current D&D application contracts [blocked]
├── inventory archived runtime rows [verified]
│   ├── mechanic/procedure identities [verified]
│   ├── learned interaction recipes [verified empty]
│   └── historical receipts [verified preserved]
├── repair current mechanic contract owners [in progress]
│   └── see DND2024-MECHANIC-CONTRACT-REPAIR-DEPENDENCY-TREE.md
├── verify current authored catalog [blocked by repair]
│   ├── prototype record/schema suite [ready]
│   ├── JavaScript syntax [ready]
│   └── source preview with no problems [verified]
├── expose complete current contracts through retrieval [in progress]
│   ├── retain exact contract JSON and mechanic source [verified]
│   ├── accept current 49,694-character contract [ready]
│   └── reject documents above 64,000 characters [ready]
├── keep generated project artifacts outside catalog authority [in progress]
│   ├── redirect Visual Studio intermediate output [ready]
│   └── remove the generated `obj` document from source scan [ready]
└── activate exact preview [blocked by repair]
    ├── optimistic expected-active fingerprint [verified]
    ├── read-back exact activated fingerprint [ready]
    └── feature discovery smoke test [ready]
```

## Conflicts and decisions

- The active revision predates the current mechanic/procedure files and contains only 2,775
  `dnd2024-core` documents. The current valid preview contains 2,971 source documents.
- Activation revisions 8 through 10 are immutable evidence from the cutover attempts. Revision 10
  contains 3,089 winners, but subsequent contract-owner repairs correctly put runtime discovery in
  `SOURCE_FILE_DRIFT` until one final post-repair activation is accepted.
- The archived and current databases have no learned interaction recipes to migrate. Creating any
  would fabricate evidence and is forbidden.
- The old live mechanic/procedure tables remain historical catalog state. They do not override the
  application-scoped current activation and are not deleted in this slice.
- Historical resolution receipts are evidence, not templates. Their equality between archive and
  current database is preserved rather than rewritten to newer contracts.

## Ordered leaves

| Order | Leaf | Depends on | Exit gate |
| --- | --- | --- | --- |
| 1 | Verify current files and exact preview | existing source registration | tests/syntax pass; preview valid; zero source problems |
| 2 | Repair the bounded generic retrieval ceiling | leaf 1 | current complete contract is retrievable; documents above 64,000 characters still fail closed |
| 3 | Remove generated build input from catalog authority | leaf 1 | Visual Studio intermediates are emitted outside `catalog/applications/dnd2024` |
| 4 | Activate the post-repair preview | leaves 1-3 and mechanic repair | one activation transaction succeeds against the expected active fingerprint |
| 5 | Read back and smoke test | leaf 4 | current revision/fingerprint and D&D contract discovery agree with the activated snapshot |

## Lowest ready leaf

Repair the generic retrieval ceiling while the independent mechanic contract-owner tree completes.
Do not reactivate after further mechanic edits until that tree's owner audit passes.

## Confirmation gates

The user explicitly requested verified migration and adaptation to the current system. No remaining
semantic gate exists because this slice creates no new ID, schema meaning, public kind, migration,
house rule, or cross-owner rule decision.

## Planning receipt

- Runtime artifacts created: none.
- Existing owners reused: application source registry, application activation, interaction
  orchestration, and the canonical D&D application catalog.
