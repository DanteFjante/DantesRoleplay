# D&D 2024 low-magic two-continent world implementation map

Status: **authoring Leaves 1–5 and parallel Slice 2 complete as review content; Leaf 6 ready; runtime prerequisites remain blocked**
Ruleset alignment: **dnd2024-compatible**
Source: **not applicable to the setting root**; any later `dnd2024-owned` rule behavior must cite
`source.dnd2024.srd-5.2.1` with an exact locator and record the relevant Foundry dnd5e engineering
review.
Roadmap owner: [World and lore roadmap](../WORLD_AND_LORE_PLAN.md)
Active product dependency: [D&D 2024 complete-campaign dependency graph](../ruleset/dnd2024/DND2024-COMPLETE-CAMPAIGN-DEPENDENCY-GRAPH.md)
Requested: 2026-08-30

Creative direction: [Caldris creative charter](CALDRIS-CREATIVE-CHARTER.md)
Expanded content pack: [Caldris authoring index](caldris/CALDRIS-AUTHORING-INDEX.md)

## Outcome and non-goals

Create one original, persistent D&D 5e 2024 setting and an initial campaign in it. The setting has
two continents, many kingdoms, a long pre-kingdom history, grounded medieval material life,
uncommon but real magic, a few consequential dragons and other mythical creatures, prepared cities,
interlocking long-form plots, memorable NPC interactions, robust mysteries, and quests that reward
creative solutions. Live narration should feel warm, intimate, and storybook-like while preserving
danger, uncertainty, and player agency.

This map plans the content and its dependencies. It creates no World, campaign, permanent content
ID, schema, migration, public operation, map asset, or live database record.

The first delivery is not:

- a procedural setting generator or permission for a model to write directly to SQLite;
- a complete description of every village, noble, road, or historical day before play begins;
- a high-magic economy with routine teleportation, resurrection, enchanted street lighting, or
  magic-item shops in every city;
- a grimdark simulation in which historical realism is used to remove hope, agency, or playability;
- a scripted novel with fixed scenes, required dialogue, protected NPCs, or one correct solution;
- a new puzzle engine, dialogue engine, economy simulator, or D&D rules fork; or
- a change to D&D 2024 class, spell, monster, rest, travel, or combat rules without a separately
  sourced and confirmed implementation slice.

## Creative promise

The setting should consistently deliver five player-facing promises:

1. **The world feels inhabited.** Food, weather, work, faith, law, distance, family, trade, and
   seasonal rhythms matter even when no adventure is happening.
2. **Magic is wondrous because it is uncommon.** People know that magic exists, but most lives and
   institutions function without routine access to it.
3. **History remains present.** Borders, customs, grudges, ruins, titles, roads, and songs have
   traceable causes, including causes older than the kingdoms that now explain them.
4. **Problems have more than one human answer.** Violence, negotiation, investigation, sacrifice,
   trickery, service, and unexpected combinations of abilities can all change the situation.
5. **There is somewhere worth returning to.** Hospitality, friendship, meals, festivals, craft,
   small kindnesses, and recurring safe places provide emotional warmth without erasing peril.

## Adopted authoring scale

These content-budget defaults are adopted under the user's authorial delegation, but they are not
authorized permanent records. They keep “many
kingdoms” and “lots of lore” concrete without requiring every place to receive equal detail.

| Layer | Recommended reviewed target | Depth rule |
| --- | ---: | --- |
| Continents | 2 | Both receive geography, cultural regions, trade, historical roles, and current tensions. |
| Sovereign polities | 12 total | Prefer 9–10 kingdoms plus 2–3 contrasting polities such as a confederacy, city-state, or marcher realm. |
| Historical eras | 6–8 | At least two eras predate the present kingdoms; each later era changes borders, institutions, or remembered truth. |
| Kingdom histories | 12 | Each has 4–6 turning points, one founding story, one contested account, one inherited scar, and one unresolved consequence. |
| Prepared cities | 3 per polity | One capital and two secondary cities receive identity, economy, districts, tensions, landmarks, and adventure use: about 36 cities. |
| Deep starting settlements | 3 | One starting city or large town plus two nearby settlements receive street-level playable detail. |
| Other named places | 30–50 | Ports, monasteries, forts, ruins, crossings, forests, mines, islands, and pilgrimage sites receive gazetteer depth first. |
| Major factions | 24–36 | Usually 2–3 per polity, with several crossing borders; every faction has plausible goals and internal disagreement. |
| Recurring NPCs | 40–60 | Twelve to fifteen are fully playable in the starting region; the rest begin as concise anchors. |
| Dragons | 4–6 known individuals | Only 1–2 are presently active near settled lands; each has a domain, desire, historical tie, and reason it has not conquered society. |
| Other mythical creature traditions | 15–25 | Separate folklore, confirmed presence, ecology, and encounter use; not every legend is true. |
| Long campaign threads | 4 | Local/community, kingdom/political, ancient/continental, and personal/cozy threads intersect without all being active at once. |
| Initial quest cluster | 4 quests | Each quest fits the current three-objective owner and contributes to at least two campaign threads. |
| Adventure seeds | 18–24 | Seeds identify pressure, interested actors, discovery paths, and consequences rather than a required plot. |

Breadth and depth are intentionally asymmetric. Every kingdom should be coherent before play, but
only the starting region needs complete street-level detail for the first playable release.

## Requirements the setting brief must close

### 1. Table and campaign contract

Before lore authoring, confirm:

- intended campaign length and likely level range;
- starting level and whether characters begin as locals, travellers, or a mixed group;
- balance among social play, exploration, mystery, combat, politics, and downtime;
- desired lethality, moral darkness, horror intensity, romance boundary, and treatment of war,
  disease, poverty, prejudice, harm to children, and other sensitive material;
- milestone or XP advancement, if the applicable rules capability exists;
- whether the campaign is open-world, guided, or a hybrid with one strong starting situation;
- how much travel between continents should happen in the first campaign; and
- which character options, species, classes, and supernatural origins should feel common,
  uncommon, exceptional, foreign, hidden, or unknown without changing their D&D mechanics.

### 2. Grounded medieval baseline

“Realistic medieval” needs explicit choices rather than a vague aesthetic. Define:

- technology band, metallurgy, armor, ships, mills, roads, siege craft, and availability of paper;
- settlement sizes, urbanization, sanitation, fire risk, water supply, and food storage;
- agriculture, harvest calendar, winter scarcity, labour obligations, grazing, forests, and land use;
- literacy, schooling, archives, messengers, heraldry, news speed, and the reliability of records;
- coinage, barter, credit, tolls, guilds, markets, trade routes, monopolies, and price differences;
- land tenure, succession, taxation, courts, customary law, sanctuary, oaths, and who can appeal;
- armies, levies, mercenaries, castles, naval power, supply limits, and the cost of campaigning;
- religion as daily practice, charity, burial, pilgrimage, calendar, legitimacy, and social care;
- kinship, households, marriage, inheritance, migration, adoption, and community obligations; and
- travel times, seasons, terrain, border formalities, languages, dialects, and hospitality customs.

Historical grounding must support good play. Cultures should contain class, regional, religious,
generational, and political differences rather than behaving as uniform stereotypes. “Realism” is
not automatic permission for gratuitous misery or direct caricatures of real peoples.

### 3. Low-magic social model

Low magic should constrain access and social prevalence while leaving confirmed D&D 2024 rules
intact. The default recommendation is:

| Layer | Recommended meaning |
| --- | --- |
| Folk belief | Charms, saints, omens, hedge remedies, taboos, and supernatural stories are common; most are not reliable spellcasting. |
| Minor practitioners | Known in some districts and institutions but uncommon enough to be personally notable. |
| Trained spellcasters | Concentrated in a few courts, temples, colleges, secluded orders, military offices, or itinerant traditions. |
| Powerful spellcasters | Rare historical or political actors whose presence changes diplomacy and attracts scrutiny. |
| Magic items | Named, inherited, excavated, commissioned at great cost, or held by institutions; routine open retail is unusual. |
| Divine miracles | Faith is ordinary; mechanically powerful miracles are not routine public services. |
| Monsters and dragons | Known through evidence and tradition, but sparse enough that most people have never seen one. |

For every setting-relevant form of magic, the world bible must answer:

- Who can learn or perform it, and how long does that take?
- What money, materials, patronage, vows, danger, or social permission does it require?
- Who regulates, fears, employs, taxes, studies, or persecutes it?
- How do witnesses distinguish a spell from fraud, faith, craft, poison, or folklore?
- What prevents it from replacing ordinary farming, transport, medicine, communication, warfare,
  policing, inheritance, and construction?
- What consequences follow from healing, resurrection, divination, mind influence, summoned
  creatures, created food, teleportation, flight, and magical communication?
- How does society react when player characters use abilities that are exceptional in this world?

Scarcity alone is not enough. The design needs a **magic impact audit** so a low-magic claim remains
consistent once D&D spells and exceptional player characters enter play. Restrictions that alter a
player option or spell are house rules and require separate confirmation and source treatment.

### 4. Geography and political causality

The two continents need more than outlines on a map. Author:

- tectonic and watershed logic sufficient to place mountains, rivers, farmland, ports, forests,
  deserts, passes, islands, and climate bands coherently;
- sailing seasons, prevailing routes, dangerous crossings, chokepoints, and reasons for exchange;
- food-producing cores, resource frontiers, trade corridors, pilgrimage routes, and contested
  borderlands;
- political borders explained by terrain, inheritance, conquest, treaty, culture, and logistics;
- at least three cross-continental relationships that are neither war nor simple trade; and
- places that remain difficult to reach so distance continues to matter despite magic.

Repository topology uses containment and explicit adjacency/routes. Coordinates and map art are
presentation aids, never setting or travel authority.

### 5. Eras and history

History should use two complementary forms:

- **era synthesis:** a readable account of the broad age, its institutions, technologies, beliefs,
  migrations, and turning point; and
- **dated chronology:** discrete events with date, audience, World scope, and optional subjects once
  the W19 chronology owner is confirmed and implemented.

Each era must leave at least three present-day residues: a border, law, ruin, title, road, ritual,
language feature, disputed relic, ecological change, debt, taboo, or living institution. Each
kingdom history must distinguish public memory, scholarly dispute, popular folklore, and hidden
truth. Conflicting accounts should be intentional and traceable, not continuity mistakes.

Recommended era pattern:

1. mythic or poorly recorded age before centralized kingdoms;
2. first durable settlements, roads, temples, or writing traditions;
3. an expansive empire, league, faith, or trade order linking both continents;
4. fracture, migration, disaster, or succession crisis that creates the modern political field;
5. kingdom-foundation and consolidation period;
6. a long peace or uneasy order that shaped living grandparents and institutions;
7. the recent rupture that creates the campaign's present tensions.

The charter fixes seven eras. Their names, dates, public accounts, disputed interpretations, and
hidden truths are authored in the macro World bible and remain review content until runtime import.

### 6. Kingdom and polity dossier

Every sovereign polity needs a consistent dossier:

- name, demonym, symbols, capital, borders, terrain, climate, population pattern, and languages;
- form of rule, succession, legitimacy story, administrative reach, courts, taxes, and local power;
- social orders, households, faiths, festivals, funerary customs, food, dress, crafts, and humour;
- major exports, imports, shortages, trade partners, currencies, roads, ports, and labour relations;
- military obligations, defensive geography, strategic fears, allies, rivals, and treaty burdens;
- policy toward magic, magical institutions, known supernatural sites, and practical scarcity;
- three prepared cities and the different role each plays in the realm;
- two or three factions with incompatible but understandable aims;
- three anchor NPCs: one public authority, one local connector, and one disruptive or hidden actor;
- four to six historical turning points and how each still affects the present;
- one public national story, one credible counter-story, and one GM-only secret;
- one current pressure that worsens if ignored and one opportunity that could improve life; and
- hooks into at least two other polities and two of the campaign's long threads.

No polity should be only “the war kingdom,” “the merchant kingdom,” or another single trait. Its
institutions, regions, factions, and ordinary residents should disagree with one another.

### 7. City dossier

Every prepared city receives:

- reason for existing in that exact place and how it feeds, drinks, trades, and disposes of waste;
- governance, law enforcement, courts, gates, tolls, curfews, fire response, and informal power;
- three to seven districts with different functions, social textures, and travel relations;
- two sensory signatures, daily and weekly rhythms, seasonal change, and one characteristic food;
- major faith, guild, market, port, archive, fortification, school, hospital, or charitable place;
- at least three useful landmarks, one welcoming refuge, and one place people avoid;
- six anchor NPC roles before full NPC authoring: authority, host, worker, broker, rival, outsider;
- one public tension, one concealed tension, one old wound, and one piece of joyful local life;
- at least three adventure entries that can be approached socially, physically, or through research;
  and
- enough containment/adjacency to navigate it without treating map coordinates as authority.

### 8. Factions and NPC interactions

Factions use the existing World faction, front, territory, alliance/opposition, and agenda owners.
Every major faction needs:

- a worthy goal, a questionable method, a material limitation, internal disagreement, and a line
  it believes it will not cross;
- assets that actually explain its reach;
- public reputation, member belief, rival interpretation, and hidden pressure; and
- at least one relationship that can change through player action rather than a prewritten ending.

Every recurring NPC should have an interaction card with:

- immediate want, long-term want, fear, obligation, boundary, and current pressure;
- public face, private contradiction, one competence, one vulnerability, and one kindness;
- voice rhythm and conversational habit without pages of scripted dialogue;
- knowledge they know, suspect, doubt, hide, and misunderstand;
- faction/location ties, attitude toward magic, and two relationships with other NPCs;
- what earns trust, what costs trust, and how their behaviour changes after a meaningful event; and
- a reason to interact that is not merely handing out information or quests.

The current repository owns motives and knowledge state but does not yet have a confirmed complete
narrative-NPC profile/disposition owner. The interaction-card format is therefore review content
until W8 closes that runtime boundary; it must not be forced into unrelated components.

### 9. Dragons and mythical creatures

Each known dragon is an individual historical actor, not a random encounter label. Its dossier
must define age and capabilities, habitat, territory, desire, fear, hoard meaning, relationships,
past interventions, current signs, and the political/ecological reason it has not conquered the
setting. At least one dragon should permit negotiation, misinterpretation, or mutual interest;
“dragon” must not automatically mean a mandatory boss fight.

Other mythical creatures need:

- folklore name and the cultures that tell the story;
- whether the creature is confirmed, disputed, extinct, transformed, or wholly legendary;
- habitat, food, reproduction or origin, seasonality, and effect on ordinary ecology;
- what evidence people mistake for it and what genuine signs exist;
- relationship to magic, religion, local livelihoods, and historical events; and
- discovery, negotiation, avoidance, rescue, research, and conflict possibilities.

Only creatures with required executable D&D behaviour should bind to stat blocks. A narrative
creature record and a rules stat block are separate concerns.

### 10. Campaign and multilayered plot architecture

The campaign bible should contain four interlocking threads:

| Thread | Typical scale | Function |
| --- | --- | --- |
| Hearth | home, friendship, craft, neighbourhood, local promise | Gives the party people and places worth protecting and returning to. |
| Crown | succession, law, faction, trade, border, legitimacy | Makes kingdom choices consequential without requiring a single correct allegiance. |
| Road | travel, cultural contact, missing people, ruins, monsters | Connects the setting and allows episodic adventures with durable consequences. |
| Deep history | ancient truth, dragon, relic, forgotten compact, pre-kingdom cause | Reveals why several present conflicts rhyme without making every problem one conspiracy. |

For each thread define the visible situation, hidden pressure, deeper truth, interested factions,
escalation if ignored, opportunities for intervention, and at least three possible end states. At
least two apparently separate adventures should share consequences before their common historical
connection becomes clear.

Plot must be situation-based:

- no required scene order beyond immediate causal dependencies;
- no essential NPC is protected from departure, refusal, exposure, or death;
- no revelation depends on one NPC, one clue, one roll, or one location remaining available;
- antagonists and allies continue pursuing understandable goals when ignored;
- failures change cost, time, trust, danger, or opportunity instead of stopping the campaign; and
- player-created plans can replace anticipated methods while preserving world constraints.

The current campaign owner supports one active arc and one active chapter. Multiple authored
threads may exist in the campaign bible, but durable parallel/branching activation waits for the
separately confirmed C12 boundary.

### 11. Quest design

Every prepared quest should state:

- initiating pressure and why it matters now;
- interested actors and what each wants;
- exactly three initial objectives for compatibility with the current quest owner, including an
  optional or GM-facing objective when appropriate;
- at least three materially different approaches and a way to combine them;
- clues, locations, NPCs, factions, and items it references without copying their mutable state;
- complications that respond to choices rather than enforcing a predetermined route;
- success, partial success, refusal, delay, and failure consequences;
- what changes in the World, campaign, relationships, or later opportunities after resolution;
- how a peaceful, cunning, scholarly, magical, exploratory, and forceful contribution might help;
  and
- one small human reward or moment of warmth in addition to money or mechanical reward.

Rewards and consequences must use their eventual owners. Until the general reward/consequence
transaction exists, the quest bible may propose outcomes but cannot claim that narration granted
them.

### 12. Secrets, clues, riddles, and creative solutions

Use the existing fact, rumour, secret, clue, classification, support, reveal, contradiction, and
actor-knowledge owners. Apply these authoring rules:

- Every essential conclusion has at least three independent clues, preferably across conversation,
  place/object examination, records/folklore, and observed consequence.
- A clue points to evidence or a next question; it does not silently disclose a GM-only secret.
- Red herrings are limited, fair, and useful even when disproved. They must not exist only to waste
  time or punish attention.
- A failed check yields lesser quality, extra time, exposure, cost, or a complication when the
  fiction allows progress; it does not erase an observable clue.
- Every riddle is grounded in lore available before or at the riddle, has a testable answer, has a
  two-step hint ladder, and defines what happens after a wrong answer.
- A riddle gate has another route or a recoverable consequence unless it protects purely optional
  material.
- Creative solutions are judged against constraints and intended stakes, not whether they match the
  author's predicted verb. Equivalent solutions can earn a different cost or consequence.
- Mystery truth is authored before the clue trail. The ending may change because of player action,
  but hidden truth is not invented retroactively to fit a dramatic guess.

No durable puzzle schema is needed for the first campaign. Riddle text and discoverable evidence
can be review content and World knowledge; the trusted GM adjudicates novel solutions through the
applicable rules and state owners.

### 13. Cozy storybook narration

The narration brief extends the existing storytelling procedure without becoming state authority.
It should specify:

- warm, clear prose with concrete sensory detail, varied sentence rhythm, and restrained metaphor;
- present-tense scene narration and dialogue driven by stored NPC wants;
- recurring motifs such as bread, rain on shutters, bells, smoke, gardens, river light, tools,
  animals, songs, mending, and seasonal food, varied by place rather than repeated mechanically;
- a “warmth budget” for most adventures: one welcoming place, one kind or repairable relationship,
  one ordinary delight, and one moment of wonder;
- danger described vividly without dwelling gratuitously on cruelty or bodily suffering;
- quiet scenes that allow conversation, work, meals, travel, festivals, recovery, and consequences;
- humour arising from character, custom, timing, and affection rather than constant quips; and
- an ending decision point that never chooses a player character's words, feelings, beliefs, or
  voluntary action.

Cozy means emotionally hospitable, not consequence-free. Safe places can come under pressure, but
the setting should not repeatedly destroy every source of attachment merely to prove seriousness.

### 14. Continuity, secrecy, and change tracking

The authored package needs ledgers for:

- names, aliases, titles, demonyms, languages, currencies, dates, distances, and calendars;
- location containment and adjacency distinct from political control and travel routes;
- NPC/faction relationships and who knows, suspects, doubts, or hides each relevant claim;
- public facts, rumours, secrets, clues, contradictions, and superseded historical claims;
- unresolved promises, debts, injuries, absences, offices, heirs, and current faction fronts;
- campaign thread state, quest dependencies, consequences, and unused opportunity seeds; and
- player-facing versus GM-only text, with no secret IDs, counts, relationship existence, or media
  keys leaking through player projections.

SQLite is authoritative after reviewed content becomes live. Review files are not a parallel live
state store; synchronization must occur only at an explicit, backed-up import boundary.

## Existing owners and dependency state

| Concern | Current owner/evidence | State for this world |
| --- | --- | --- |
| World root, nested Regions, settlements, sites, interiors | World location contracts and containment | `verified` as a model; scalable live creation remains blocked |
| Adjacency, routes, clock, travel, factions, fronts | World W1–W16 owners and receipts | `verified` for bounded use |
| Facts, rumours, secrets, clues, epistemic state | World W4 plus knowledge extensions | `verified`; final player-byte secrecy still depends on audience work |
| Dated setting history | W19 proposed chronology owner | `blocked` awaiting confirmation and implementation |
| Narrative NPC profile and changing social disposition | Complete-campaign W8 boundary | `missing` as a complete owner; motives/knowledge may be reused without overloading them |
| Campaign root and one active arc/chapter | Campaign create/chapter owners | `verified` for the narrow model |
| Parallel and branching durable arcs | Campaign C12 plan | `blocked` by prerequisites and semantic confirmation |
| Three-objective quest and manual lifecycle | Quest owners and procedures | `verified` for a bounded quest |
| General reward/consequence handling | Complete-campaign D1–D3 | `missing`/`blocked` |
| Story-first narration | `procedure.play.storytelling` | `verified` as a trusted-host conduct contract |
| Scalable World validate/create/select/archive | Complete-campaign W2/G9 | `blocked` by live-authoring prerequisites |
| Atomic fixed small-world plus campaign creation | W17/C10 | `partial`; current shape is far too small and C10 preview/create remains incomplete |
| D&D rules and content | D&D catalog mechanics/content | mixed; every rule-dependent playable slice must use only accepted capabilities |

## Dependency tree

```text
Reviewed two-continent setting and playable first campaign                         [planned]
├── A. Creative charter and table contract                                        [verified as review direction]
│   ├── Caldris working name and grounded regional naming style                   [verified]
│   ├── twelve-polity content budget                                               [verified]
│   ├── low-magic social model and magic impact policy                            [verified]
│   ├── late-medieval baseline and default tone/sensitivity boundaries            [verified]
│   └── level 1–10 mixed-origin campaign and play mix                             [verified]
├── B. Reviewed setting bible, no runtime write                                    [authored for review]
│   ├── physical geography, climate, seas, and travel scale                       [authored]
│   ├── eras, broad history, and continuity ledger                                [authored]
│   ├── polities, cities, trade, law, faith, and cultures                         [authored]
│   ├── magic institutions and impact audit                                       [authored]
│   ├── factions, NPC interaction cards, dragons, and creatures                   [authored]
│   └── public/rumour/secret/clue matrix                                           [authored editorially]
├── C. First playable vertical                                                     [authored for content review]
│   ├── one starting kingdom and three settlements at playable depth              [authored]
│   ├── routes, local factions, 12–15 recurring NPCs, and local knowledge         [authored]
│   ├── one campaign, one active arc, and one active chapter                      [verified owner]
│   ├── four interlinked three-objective quests                                   [verified owner; 30 packets authored]
│   ├── robust clue/riddle matrix and alternate-solution review                   [authored]
│   └── storybook narration and one disposable played-session proof               [planned]
├── D. Full setting breadth                                                        [authored for review]
│   ├── remaining polity and city dossiers                                        [authored]
│   ├── cross-continent routes, relationships, and opportunity seeds              [authored]
│   ├── historical chronology records                                             [blocked by W19]
│   ├── narrative NPC authoring and player-safe projection                        [blocked by W8/A5]
│   └── parallel/branching durable campaign threads                               [blocked by C12]
└── E. Live creation and acceptance                                                [blocked]
    ├── G7N namespace-containment acceptance                                      [active prerequisite]
    ├── backup/restore and authorized live authoring                              [blocked P1/P3/G9]
    ├── World validate/create/select and scalable reviewed import                 [blocked W2]
    ├── atomic or explicitly staged World/campaign creation with rollback         [missing design]
    ├── player-secret canaries, replay, rollback, restart, and readback           [blocked]
    └── played acceptance and explicit synchronization boundary                   [blocked]
```

## Ordered implementation leaves

Each row is one reviewable leaf. Completing one row does not authorize the next.

| Order | Leaf | Depends on | Exit gate |
| ---: | --- | --- | --- |
| 0 | Confirm creative charter | User's delegated creative direction | **Complete:** the Caldris charter fixes working name, scale, magic baseline, historical tone, campaign start, safety defaults, humour, and personality range; no runtime IDs. |
| 1 | Author consistency ledgers and dossier templates | Leaf 0 | **Complete as review content:** repeated polity, city, NPC, faction, plot, quest, and visual shapes plus structural checks now exist in the expanded content pack. |
| 2 | Author macro World bible | Leaf 1 | **Complete as review content:** both continents, 12 polities, 7 eras, trade/travel logic, magic institutions, 6 dragons, creatures, and current tensions are authored. |
| 3 | Author one deep starting region | Leaf 2 | **Complete as review content:** Alderwick, three settlements, twelve Bramblebridge sites, factions, NPC cards, warmth anchors, secrets, and clues are authored. |
| 4 | Author campaign spine | Leaves 2–3 | **Complete as review content:** four threads, six volumes, consequence paths, four boss ladders, and multiple end-state choices are authored. |
| 5 | Author and test the initial quest cluster | Leaves 3–4 | **Complete as editorial content:** thirty three-objective packets include multiple approaches, redundant clues, riddles where useful, and fail-forward consequences. Human table testing remains Leaf 6. |
| 6 | Run a content-only tabletop review | Leaves 2–5 | Geography, history, low-magic impact, clues, NPC motives, and plots pass contradiction, stereotype, brittleness, and tone review. |
| 7 | Close runtime prerequisite slices | G7N, W19, W8/A5, G9/W2, applicable D&D mechanics | Each owner is implemented and accepted separately; no setting content is used to smuggle in a new schema or rule. |
| 8 | Preview reviewed live package | Leaves 6–7 | Proposed IDs, components, relationships, visibility, counts, collisions, references, fingerprint, and problems are deterministic with zero writes. |
| 9 | Create World and initial campaign | Leaf 8 plus backup/import confirmation | The reviewed package commits atomically or through an explicitly ratified staged boundary; every injected failure leaves no partial state. |
| 10 | Fresh-host played acceptance | Leaf 9 | A new host retrieves only stored state, runs social/exploration/mystery play, rejects one invalid action without change, resolves one quest consequence through owners, and resumes after restart. |
| 11 | Expand remaining depth | Leaf 10 | Add later kingdoms, cities, quest clusters, maps, and cross-continent arcs only when play approaches them or their detail is needed for continuity. |

## Lowest ready leaf

The creative-direction leaf and review authoring Leaves 1–5 are complete through the
[Caldris authoring index](caldris/CALDRIS-AUTHORING-INDEX.md). The pack now contains a World bible,
gazetteer, faction and NPC atlases, interwoven campaign tapestry, forty-eight quest packets, a broad
feature backlog, forty-eight second-ring locations, additional history and living lore, forty
reusable story patterns, two illustrative maps, and character, location, and item concept art.

The lowest ready leaf is **content-only tabletop review**. A human reviewer or table should test
the opening cluster for contradictory geography, brittle clues, unwanted stereotypes, pacing,
NPC distinctness, humour, emotional range, and whether creative approaches genuinely work. This
review still creates no permanent IDs, catalog records, or live state. Runtime prerequisite work
remains Leaf 7 and requires its own owners and confirmations.

## Content review gates

Before a setting bundle is considered ready for runtime preview, verify:

- **Causality:** every border, city, trade route, ruin, and current crisis has an intelligible cause.
- **Low-magic integrity:** the magic impact audit explains why ordinary institutions remain mundane
  without silently changing player rules.
- **Historical continuity:** every era leaves present consequences; dates and successions do not
  conflict accidentally.
- **Political depth:** every polity and faction contains internal disagreement and at least one
  relationship not reducible to ally/enemy.
- **Cultural depth:** no culture is a monoculture, costume, alignment, or direct real-world caricature.
- **Ecology:** dragons and creatures have food, habitat, limits, signs, and effects on neighbouring
  communities.
- **NPC playability:** recurring NPCs want something, know limited things, can change, and are not
  merely quest dispensers.
- **Mystery fairness:** essential revelations have three-clue coverage, recoverable failure, and no
  hidden information leak.
- **Agency:** every prepared problem has multiple viable approaches and consequences for refusal or
  delay.
- **Cozy tone:** warmth anchors and ordinary life are durable parts of the setting, not disposable
  prologue decorations.
- **Repository ownership:** World, campaign, quest, knowledge, session, item, character, and rule
  state remain with their existing owners.
- **Audience safety:** player projections disclose no secret text, IDs, counts, relationships, or
  media references.
- **Operational safety:** preview is zero-write; creation is replay-safe; forced failure rolls back;
  restart reconstructs the same visible state from SQLite.

## Confirmation gates

Explicit confirmation is still required before:

- choosing the final World/campaign names or permanent IDs;
- fixing the content budget if it differs from the recommendations;
- defining or restricting character options, spell access, resurrection, magic items, languages,
  religion/cosmology, or other rules-adjacent meanings;
- approving the W19 chronology component and relationship meanings;
- introducing a narrative-NPC profile, relationship/disposition, puzzle, or reward/consequence
  schema;
- changing campaign arc cardinality or durable branching semantics;
- creating a scalable import/public authoring surface or live World lifecycle operation;
- importing into or mutating a live SQLite World/campaign; and
- declaring the complete World/campaign accepted.

## Planning receipt

- Runtime artifacts created: **none**.
- Permanent World, campaign, location, faction, NPC, creature, knowledge, chronology, quest, and
  item IDs created: **none**.
- Catalog, schema, mechanic, source, migration, public operation, and live database changes: **none**.
- Existing World/campaign/quest/knowledge/storytelling owners are reused rather than duplicated.
- The user delegated ordinary creative decisions; the Caldris charter records adopted defaults and
  the tone/personality contract without creating runtime authority.
- The expanded review pack contains 93 feature candidates, 12 polities, 36 primary cities, 34
  factions, 95 local and polity NPC anchors, 48 quest packets, 48 second-ring locations, 24
  additional historical incidents, 24 living-lore entries, 40 reusable story features, and 9
  presentation-only images.
- The roadmap link records this prospective setting plan without assigning a new runtime feature ID.
