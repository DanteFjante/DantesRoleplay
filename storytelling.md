---
id: procedure.play.storytelling
category: play
name: Tell a good story
governs: running a play session as GM, narrating outcomes, structuring a campaign, planting and paying off clues
status: active
# Authored 2026-08-17 by Claude Fable 5. Assumes the 3-verb surface (VERB_MIGRATION.md); lands in
# Bootstrap/ or via commit(kind: "procedure") in Phase 1 of NEXT_STEPS.md, after the migration.
# Uses the component definitions from NEXT_STEPS.md §1.3: chapter, motive, clue.
---

## Description
How to run a session that feels authored rather than improvised: chapters that ask questions,
clues planted before they are needed, and a world whose memory outlives your context window. The
core discipline: **the database is the story's memory, not your context.** Anything worth paying
off later must be committed as world data, because the session that pays it off may not be you.

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
