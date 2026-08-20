---
id: procedure.play.storytelling
category: play
name: Tell a good story
governs: running a play session as GM, narrating outcomes as interactive fantasy prose, structuring a campaign, transitioning between chapters, planting and paying off clues
status: active
# Authored 2026-08-17 by Claude Fable 5. Assumes the 3-verb surface (VERB_MIGRATION.md); lands in
# catalog/procedures/ in Phase 1 of NEXT_STEPS.md, then validated before runtime import.
# Uses the component definitions from NEXT_STEPS.md §1.3: chapter, motive, clue.
# Revised 2026-08-20 to define the literary voice, interactive turn shape, pacing, dialogue, and
# mechanical-result presentation expected during play.
# Revised 2026-08-20 to add entertaining in-world chapter recaps and next-chapter introductions.
---

## Description
How to run a session that feels authored rather than improvised: chapters that ask questions,
clues planted before they are needed, and a world whose memory outlives your context window. The
core discipline: **the database is the story's memory, not your context.** Anything worth paying
off later must be committed as world data, because the session that pays it off may not be you.

The player's experience should feel like participating in a well-paced fantasy novel: concrete,
evocative prose; characters who speak with intention; consequences that matter; and an open place
for the player to act. Mechanical truth remains visible when it matters, but the normal response is
enjoyable fiction rather than a status report.

Between chapters, the story may briefly widen its lens. The closing chapter becomes a tale people
remember, misunderstand, celebrate or fear, and the next chapter opens with a fresh sense of
place, consequence and possibility. These transitions should be enjoyable to hear aloud as well
as read.

## Instructions

### Structure — chapters are questions
1. A campaign is a chain of chapters, and a chapter is one dramatic question: *who is poisoning
   the wells?* — not a location or a scene list. The chapter ends when its question is answered,
   and the answer should raise the next question.
2. Model each chapter as an entity carrying a `chapter` component: its question, its status
   (`open`, `answered`), and a two-sentence summary written when it closes. At session start,
   query for the open chapter before narrating anything — resuming the story is a query, not a
   recollection.
3. End every session on a change the player can see — something answered, lost, gained or
   revealed. Update the chapter summary in the same breath, while it is cheap.

### Clues — plant early, pay off later, never gate on one
4. Decide the chapter's hidden truth first and commit it to the world (a component on the
   culprit, a `motive` on the NPC). A truth that exists only in your narration will drift; a
   committed truth cannot contradict itself next session.
5. Plant at least **three** clues pointing at every conclusion the player must reach. Players
   miss clues, and one missed clue must never stall the chapter. Record each planted clue as
   `clue` data — what it points at, where it was planted, whether it has been found — so a later
   session knows what is already in play.
6. Plant before you need. A name dropped in chapter one and paid off in chapter three feels
   authored; the same name invented at the moment of payoff feels like what it is. When you need
   a payoff, query your planted clues FIRST and prefer paying off an existing one over inventing.
7. Reveal at the pace of discovery: narrate only what the characters perceive, even though you
   can query everything. The hidden component is the truth; the narration is what the lantern
   light shows.

### Agency — consequences, not walls
8. Never silently negate a declared action. If it cannot work, say what the character perceives
   that shows why; if it can, resolve it — through a mechanic when the outcome is uncertain and
   failure would be interesting, by narration when it is neither. Do not roll for the trivial.
9. Fail forward: a failed action changes the situation rather than ending it. Failure that only
   subtracts is a wall; failure that complicates is a story.
10. When the player does something the story did not anticipate, let the world's committed facts
    — motives, clues, relationships — decide the reaction. Consistency under surprise is what
    reads as intelligence.

### Craft — the small habits that read as smart
11. NPCs want things. Give every recurring NPC a `motive` component and let it drive their every
    appearance; an NPC who wants something offscreen feels alive onscreen.
12. Call back: reference committed past events in narration ("the innkeeper remembers the
    lantern you sold him"). Query history and the world for material — a callback the player
    verifies as true is worth ten invented flourishes.
13. Alternate tension and rest. After a dangerous scene, give a quiet one — that is where clues
    land best, because the player is listening instead of surviving.
14. Cut scenes when their question is answered. Lingering after the answer dissolves the
    tension you built.

### Voice — write fantasy prose, not a game log
15. Use present tense and second person for the player character by default. Keep viewpoint close
    to what the character can perceive. Follow an established campaign voice if one has been
    recorded, but do not imitate a living author or turn style into parody.
16. Prefer concrete nouns and active verbs. Give each scene two or three telling sensory details,
    chosen for mood or usefulness: wet ash under a boot, beeswax guttering in a chapel, iron on
    the wind. Do not inventory every sense or decorate every noun.
17. Vary sentence rhythm. Short sentences carry danger, surprise and decision. Longer sentences
    may open a landscape, memory or moment of wonder. Paragraph breaks are part of pacing.
18. Use figurative language sparingly and specifically. One fresh image is stronger than a page
    of mist, shadows, ancient whispers and unnamed dread.
19. Let the world be beautiful, funny, tender or strange as well as threatening. Constant grimness
    flattens tension because there is nothing left to lose.
20. Never answer ordinary play with a sterile report such as “the check succeeds; the door
    opens.” Show the effort, resistance, sound, consequence and changed situation.

### Turn shape — every response moves and opens
21. Begin with the immediate consequence or most vivid change, not a recap of the player's own
    words. Reorient with one brief detail only when the location, time or participants changed.
22. A normal turn has three movements: consequence, development, opening. First show what the
    declared action causes; then reveal a reaction, detail or complication; finally stop where the
    player's next decision matters.
23. Change at least one meaningful thing each turn: position, knowledge, danger, relationship,
    opportunity, cost or time. A response that changes nothing should be a deliberate quiet beat.
24. End on an actionable image, question, threat, offer or uncertainty. Do not close the scene and
    then ask a generic “What do you do?” when the prose can make the decision point clear.
25. Do not advance past the player's authority. Stop before choosing their words, intentions,
    beliefs, emotions or voluntary actions. You may describe involuntary perception and bodily
    reaction, but leave interpretation to the player.
26. Do not provide a menu every turn. Free action is the default. Offer two to four illustrative
    options only when the situation is unusually complex, the player asks for help, or play has
    stalled; always leave room for another approach.

### Character and dialogue — people want things
27. Give each recurring NPC a distinct verbal habit, attitude and immediate want grounded in
    stored motive and history. Distinction comes from priorities and word choice, not exaggerated
    accents or repeated catchphrases.
28. Dialogue is action. An NPC speaks to persuade, conceal, test, delay, comfort, threaten or obtain
    something. Avoid conversations that exist only to recite setting notes.
29. Break dialogue with brief physical behavior and environmental response. A hand covering a
    seal, a glance toward a locked stair or a cup left untouched can carry information without an
    explanatory paragraph.
30. Let NPCs misunderstand, evade and make imperfect choices while remaining consistent with known
    facts. Do not make every NPC omniscient, cryptic or conveniently helpful.

### Mechanics — preserve truth and translate it into fiction
31. Resolve genuine uncertainty before narrating its outcome. Never write the attractive result
    first and then search for a mechanic that permits it.
32. Treat the structured mechanic result as a hard boundary. Preserve success/failure, effects,
    resources, positions, damage, conditions, events and costs exactly. Add interpretation and
    sensory consequence, never a contradictory mechanical change.
33. Mention a decisive roll or total briefly when fairness, suspense or player choice benefits
    from it. Do not dump JSON, projections, candidate lists or internal operation details into the
    story unless the player asks for the mechanical record.
34. Integrate mechanics at the moment they become visible: the arrow glances from the shield, then
    a short parenthetical may state the attack missed; the poison takes hold, then name the applied
    condition. Keep fiction primary and evidence available.
35. In combat, narrate only the resolved action and immediate reactions. Keep spatial facts,
    targets, injuries, resources and turn ownership exact. Do not choreograph future attacks or
    decide another character's response.

### Pacing and length — spend words where choices matter
36. Match length to dramatic weight. Use roughly 80–180 words for quick exchanges and transitions,
    180–450 for a standard consequential turn, and 450–800 only for earned set pieces, chapter
    openings, revelations or endings. A player request may override these defaults.
37. Compress routine travel, repeated searches and bookkeeping into a vivid transition plus the
    facts that changed. Expand decisions, discoveries, reversals, intimate dialogue and costly
    consequences.
38. Exposition arrives through what characters notice, ask, uncover or remember from committed
    knowledge. Give only what matters now and let deeper lore remain discoverable.
39. Do not repeat the same description, stakes or available choices merely to fill space. Trust
    the reader to remember the previous turn; query the database when you cannot.
40. A chapter opening establishes place, pressure and one immediate possibility. A chapter ending
    pays off a question or changes its meaning, records the summary, and leaves a resonant image
    rather than an administrative checklist.

### Interactivity — protect surprise without hiding agency
41. Telegraph consequential danger through perceivable evidence before demanding a choice, unless
    the danger was already established or surprise was mechanically resolved.
42. Let clever plans change position, difficulty, available mechanics or consequences when world
    facts support them. Do not reduce every approach to the same roll wearing different prose.
43. On failure, preserve the attempted action's importance. Reveal information at a cost, worsen
    position, consume time, attract attention or create a difficult choice; never invent an effect
    the mechanic did not produce.
44. When no rule is needed, allow reasonable actions to work and move quickly to their interesting
    consequence. Interactivity comes from meaningful response, not from rolling against every
    door, conversation or patch of road.

### Chapter transitions — let the world tell the tale
45. When a chapter closes, create a short **chapter interlude** with two connected movements:
    first an entertaining retelling of the chapter just completed, then an introduction to the
    circumstances of the next chapter. It is a bridge in the story, not an administrative recap.
46. Before writing the interlude, query the closed chapter summary, important committed events,
    character actions, discoveries, losses, relationships, unresolved consequences and the next
    chapter's known setup. Prefer a few memorable turning points over a chronological list of
    everything that happened.
47. Choose a viewpoint that makes the retelling feel newly alive. Suitable lenses include tavern
    gossip, the people of the affected settlement, a key ally, a resentful rival, an antagonist,
    a travelling singer, a soldier's letter, a scholar's chronicle, a child's embellished version
    or a neutral storybook narrator. Ground the choice in people and places that actually exist
    when possible.
48. Let the viewpoint reshape emphasis and tone. Villagers may turn terror into a ridiculous
    heroic legend; an enemy may describe mercy as cowardice; a friend may remember the quiet act
    nobody else noticed. Bias, error and exaggeration are welcome as presentation, but they do not
    rewrite the committed facts beneath the telling.
49. Past player-character actions that were actually resolved may be described as part of the
    story, including their physical manner and visible effect. Do not invent an unrecorded action,
    dialogue line, decision, relationship or consequence just to make the recap smoother.
50. A transition narrator may humorously guess at a character's reason for a completed action—even
    guess wrongly—when the wording clearly marks it as gossip, interpretation or playful narrative
    speculation: “perhaps for honour, perhaps because nobody had mentioned the stairs.” Never
    present the invented motive as canonical access to the character's private mind. The player
    remains free to confirm, correct or enjoy the mistake.
51. Write the recap for pleasure as well as memory. Give it a small dramatic arc: the situation
    people faced, the choices that changed it, the cost or surprise, and the image by which the
    chapter will be remembered. Call back to specific details instead of saying only that the
    heroes completed a quest.
52. Vary the device between chapters. Do not turn every transition into the same “previously”
    speech, tavern song or omniscient summary. Reuse a viewpoint only when its evolving account is
    itself a meaningful campaign thread.
53. Pivot from memory into the next chapter through a natural hinge: a consequence arrives, a
    rumor travels ahead, the season turns, a letter is opened, an enemy reacts or the camera moves
    beyond the celebration to somewhere trouble is beginning. Make the connection between chapters
    felt even when the next problem is unexpected.
54. The next-chapter introduction establishes where and when play resumes, the local atmosphere,
    what has changed because of prior events, the immediate circumstances and the first perceivable
    pressure or opportunity. Reveal only knowledge the chosen narrative mode and player viewpoint
    are allowed to reveal.
55. The introduction may briefly describe the player characters arriving, travelling, recovering
    or performing other already established transition actions. Stop before any new voluntary
    choice, dialogue, belief or emotion is required. End in the live present at a concrete moment
    where control clearly returns to the players.
56. Aim for roughly 300–700 words for the complete interlude, with a shorter version when the
    completed chapter was brief. It may be read aloud. Favor memorable cadence, clean sentences
    and a few strong details over dense lore or a ledger of rewards.
57. After presenting a biased or comic retelling, preserve the factual chapter summary separately
    in structured story state. If a claimed motive or disputed detail could be mistaken for canon,
    label it in narration or presentation metadata as rumor, opinion, embellishment or speculation.

## Constraints
- The world store is canon. Before narrating any fact about the world, if you are not certain,
  query — never contradict committed data, and never "remember" what you did not query.
- Never reveal a hidden truth in narration by accident: what is stored as hidden stays hidden
  until a found clue or a mechanic outcome reveals it.
- Never let the chapter's progress depend on a single clue, a single roll, or a single NPC
  surviving.
- Never resolve genuine uncertainty by fiat when an active mechanic covers it — the mechanic's
  seed is what makes the outcome fair and replayable. Fiat is for the trivial and the purely
  narrative.
- Story state lives in components (`chapter`, `clue`, `motive`), never only in the transcript. A
  session that ends without committing its story state has quietly erased its own ending.
- Never decide the player character's dialogue, voluntary movement, plan, belief or emotion. End
  where the player can supply those things. A chapter interlude may retell completed, committed
  actions and visibly speculative motives, but cannot establish new private intent as fact.
- Never let beautiful prose obscure information the character would plainly understand: who is
  present, what changed, where immediate danger lies, and what decision is open.
- Never add damage, items, clues, relationships, conditions, movement, quest progress or other
  persistent facts merely because they make the narration more dramatic.
- Never expose GM-only truth through omniscient narration. Foreshadow through committed,
  perceivable evidence.
- Never imitate a living author's distinctive style. Use the campaign's own recorded tone and
  broad fantasy-story qualities instead.
- Never force every response into identical paragraphs, a choice menu or a mechanical footer.
  Rhythm should follow the scene while state remains inspectable on request.
- Never store an interlude narrator's bias, joke, rumor or invented motive as objective world
  truth. Store the canonical summary and, only if useful, the in-world account as a separately
  attributed belief or text.

## Chapter transition example

The completed chapter established that the party broke the miller's floodgate during a rescue,
saved six villagers and exposed the reeve's smuggling. The next chapter begins three weeks later
as the party reaches Dunmarrow during its autumn fair.

Suitable transition excerpt:

> By the end of the week, everyone in Bellweather knew who had saved the riverside cottages.
> Unfortunately, nobody agreed on how.
>
> The miller's version involved a wall of black water, six drowning children and Ser Caldus
> holding back the river with his shoulders. His wife insisted there had been only one child,
> that Caldus had mostly shouted at a gate, and that Mira Vale had opened it with an axe. At this
> point the miller would lower his voice and explain that Mira had destroyed the floodgate out of
> stern tactical necessity—or possibly because the gate had offended her. Mira never settled the
> matter.
>
> What endured beneath the embroidery was true enough: six people came home alive, the reeve's
> ledgers came out of the flour cellar, and every smuggler along the Greywater learned that their
> safest road was safe no longer. On the first cold morning of autumn, someone nailed the broken
> gate's iron latch above the village hearth. That was how Bellweather chose to remember the
> affair.
>
> Three weeks later, the same latch is being sold in miniature at the gates of Dunmarrow.
>
> The autumn fair has swallowed the hill town in red cloth, woodsmoke and the clang of little
> pilgrim bells. Your road-stained party has barely passed beneath the arch when the bells stop,
> one row after another. Across the market, a rider in the reeve's green livery slides from an
> exhausted horse. He carries no weapon. Tied around his wrist is a strip of Bellweather flour
> sacking, dark with blood.
>
> He sees you—and whatever speech carried him this far deserts him at last.

The miller's account makes the recap entertaining, but its errors remain attributed to him. The
characters' resolved actions and consequences remain intact. The final paragraphs establish the
new place and circumstances, connect them to the previous chapter, and return control before the
players decide how to respond.

## Narrative turn example

Player intent: “I put my shoulder to the swollen crypt door before the footsteps reach us.”

Mechanic result: the Strength check succeeds; the door opens; no damage or additional effect is
recorded.

Suitable response:

> The first impact jars your teeth. The oak gives less like wood than a wall buried beneath the
> hill, its iron bands cold enough to burn through your sleeve.
>
> Then something cracks inside the frame.
>
> You drive forward again, boots grinding old salt across the flagstones, and the door tears free
> with a groan that rolls away into the dark. Beyond it, narrow steps descend between walls painted
> with faded golden stars. The air that rises from below is dry, still—and touched with the faint
> sweetness of myrrh. (Strength 17 against 14: success.)
>
> Behind you, the approaching lantern-light reaches the last turn in the passage. Ahead, halfway
> down the star-painted stair, a small bronze bell begins to sway though there is no wind.

The response preserves the exact success, invents no damage or movement beyond opening the door,
and ends with two visible pressures without choosing what the player does next.

## Example
```text
Chapter open: "Who is poisoning the wells?"
Committed at chapter start: culprit NPC carries motive {"wants": "revenge on the guild"},
  hidden component {"secret": "poisoner"}; three clue entities planted (a stained glove at the
  well, a herbalist's missing ledger, a witness who heard singing at night).
Session 2 (fresh context): query open chapter -> query its clues -> two found, one not.
Player accuses the wrong NPC: query that NPC's motive -> they deflect toward the truth,
  and the unfound clue (the ledger) surfaces in their defence — planted in session 1,
  paid off in session 2, by an LLM that was not there in session 1.
```
