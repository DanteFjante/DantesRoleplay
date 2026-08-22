# Storytelling Feature 1 dependency plan — publish the trusted-host narration procedure

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Implemented; global acceptance pending unrelated repository failure.**  
Last updated: 2026-08-21

## Target and boundary

Publish the one canonical `procedure.play.storytelling` contract so a trusted host can translate
already verified campaign and world state into interactive fantasy narration. It owns GM conduct
and state-to-fiction discipline, never durable story state or a new runtime capability.

Included: canonical C3/W4 terminology; trusted-host read order; agency, clue, secrecy, and
mechanic-result boundaries; turn structure and literary guidance; catalog seeding and fresh
retrieval proof.

Excluded: query/commit kinds, C# code, components, mechanics, events, subscriptions, migrations,
session lifecycle/recaps, player authorization, quest/objective changes, combat rules, generated
prose storage, fixtures, and persistent-database import.

## Dependency analysis

```text
S1 storytelling publication
├─ C3 campaign resume and chapter/arc ownership                 [implemented]
├─ W4 motive and knowledge owners                                [implemented]
├─ canonical procedure catalog/seed pipeline                     [implemented]
├─ procedure.play.storytelling                                   [this slice]
│  └─ fresh seeded retrieval                                     [this slice test]
└─ Q3.2 quest-summary handoff                                    [dependent; separate accepted slice]
```

The contract may refer only to canonical state already owned by `procedure.campaign.chapter`,
`procedure.game.core.world.knowledge`, and `procedure.game.core.world.faction`. It does not make
descriptive visibility into access control and does not claim that a narration call changes world,
campaign, quest, session, or combat state.

## Slice and acceptance

1. Add `catalog/procedures/play/procedure.play.storytelling.md` with the permanent existing
   `procedure.play.storytelling` ID and canonical component/query names.
2. Retire the root prose draft to a pointer so the catalog procedure is the only current contract.
3. Add one focused fresh-database seed/retrieval test that proves the contract is embedded,
   retrievable, and contains the canonical owner IDs and no retired shorthand claim.
4. Run focused tests and `roleplay validate catalog`; at feature acceptance run the full suite and
   record a receipt. Stop before Q3.2, which is separately owned by the Q3 plan.

## Exit gate

A fresh host can retrieve `procedure.play.storytelling` from a seeded database and follow one
trusted-host narration contract whose state references are canonical, whose player/hidden-data
limits are explicit, and whose prose cannot be mistaken for a state mutation or authorization
rule.
