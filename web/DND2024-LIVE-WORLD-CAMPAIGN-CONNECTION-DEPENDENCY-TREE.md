# DND2024 live World and Campaign connection dependency tree

Status: **leaves 1–3 verified; visits remain blocked on a canonical owner**
Ruleset alignment: **dnd2024-compatible presentation**
Source: authorized application ECS state and campaign knowledge projection; no D&D rule source applies
Owner/roadmap: `web/WEB-INTERFACE-ROADMAP.md`, Feature 5

## Outcome and non-goals

Finish the existing World and Campaign information pages by explaining the already projected map
layers, connecting existing World people/faction/creature/holding records, and projecting existing
Campaign recaps, outcomes, and clues. The UI must stay useful when the live database has no records.

This work does not invent visits, clues, outcomes, people, faction territory, or holdings from prose.
It does not add permanent component or relationship IDs, mutate SQLite, calculate D&D rules, or make
prototype map geometry authoritative.

## Dependency tree

~~~text
Live World and Campaign information completion                         [verified]
├─ 1. Audience-safe map controls and legend                           [verified]
│  ├─ explain only projected layers and visible marker states        [verified]
│  └─ expose no hidden layer names, counts, or DM annotations        [verified]
├─ 2. Existing live World directories                                 [verified]
│  ├─ DM projection from exact actor/creature containment            [verified]
│  ├─ factions from the existing faction component and relationships [verified]
│  └─ holdings only from exact location containment                  [verified]
└─ 3. Existing live Campaign records                                  [verified except visits]
   ├─ session recaps and terminal arc outcomes                        [verified]
   ├─ authorized clue presentation                                   [verified]
   └─ visits                                                         [blocked: no canonical visit owner]
~~~

## Security and authority decisions

- Layer controls and the legend consume only the audience-projected `MapDocument` and overlays.
- Structured World directories are admitted only for DM perspective until an audience-safe World
  directory projection exists. Player mode continues to use the authorized knowledge endpoint.
- Campaign clues consume only clue entries already admitted by that same knowledge endpoint.
- A local DM's Player preview does not reinterpret DM-only structured records as player-safe.
- Empty arrays are truthful when the live state lacks records; fixtures are never fallback game data.

## Ordered leaves and exit gates

| Order | Leaf | Exit gate |
| ---: | --- | --- |
| 1 | Map legend | Every displayed legend entry corresponds to currently projected layers or visible marker states; Player bytes contain no DM-only labels/counts. |
| 2 | World directories | Exact live containment/components populate DM people, creatures, factions, and holdings; Player/preview stays fail-closed. |
| 3 | Campaign records | Existing session recaps, terminal outcomes, and authorized clues project without inference; visits remain an explicit empty state until a canonical owner exists. |

## Completion evidence

- Slice 14: `DND2024-MAP-LEGEND-SLICE-14-RECEIPT.md`
- Slice 15: `DND2024-LIVE-WORLD-DIRECTORIES-SLICE-15-RECEIPT.md`
- Slice 16: `DND2024-LIVE-CAMPAIGN-RECORDS-SLICE-16-RECEIPT.md`
