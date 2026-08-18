# DantesRoleplay — what to do next

Written 2026-08-17 by Claude Fable 5, after the contract review, the 3-verb migration design, and
the MVP assessment. Ordered: each phase assumes the one before it. Update `STATUS.md` in the same
change that moves an item, as usual.

The one-sentence goal this document serves (STATUS.md, M6): *you can play a session through an
MCP client, and the LLM can add a new game mechanic mid-session that survives and gets reused
next time.* Phases 0–2 make that sentence true and demonstrated. Phase 3 makes the system good
beyond it.

---

## Phase 0 — the surface, finished (≈ 2 days)

**0.1 Run the 3-verb migration.** Follow `VERB_MIGRATION.md` exactly — decisions D1–D12 are
pinned, batches B1–B7 are ≤5 files each with a build between.
*Why now:* every cold walk, contract and play-test performed against the twelve-tool surface is
invalidated by the migration; it is cheapest while there is no content and no players.

**0.2 Fold the orient fixes into batch B2.** Orient reports mechanic counts by status and scope,
and the static NotYetBuilt line "any rule at all — not built" becomes conditional on the count.
*Why:* the moment the first active mechanic exists that line is false, `system-inspect` tells
agents to believe orient over everything, and a mid-campaign session that cannot discover rules
exist will bypass them. This is the "gets reused next time" half of the MVP sentence.

**0.3 Sweep the remaining review findings into batches B4–B6** (they are contract edits, and the
governs/verb rewrite touches every file anyway): world-change and mechanic-run gain one
cross-referencing paragraph each on when to prefer the mechanic path over direct effects;
world-model gains one line noting that committing an existing component-definition id updates it
in place — the single in-place write in the system.

**Exit test:** guard tests green both directions, full suite green, `system-use.md` seeded, and
one scripted protocol walk (orient → query → commit) against the new surface.

---

## Phase 1 — the first content, through the front door (≈ 1 day)

**1.1 Author a starter ruleset of 3–5 mechanics via `commit(kind: "mechanic")`, not via seed.**
Suggested set: an ability check (seeded random vs a component value), a damage/consequence
mechanic (writes a component field), a movement mechanic (containment), and one flavour mechanic
that proposes zero effects — narration only, which the corrected `procedure.mechanic.run` now
explicitly blesses.
*Why through the front door:* authoring the first real content IS the acceptance test of the
authoring surface. Every friction point met here is one a mid-session LLM would have met later,
with a player waiting.

**1.2 Land the play contracts.** `procedure.play.storytelling` (drafted alongside this document —
see `storytelling.md`) and, if 1.1 revealed the need, a short `procedure.play.session` covering
session start (orient → query world → resume the open chapter).
*Why contracts and not prompt text:* the GM's craft guidance must survive session boundaries and
reach a cold model the same way everything else does — retrieved, on demand, from inside the
system.

**1.3 Model the campaign frame as world data.** Using only existing structures: a `chapter`
component definition (question, status, summary), a `motive` definition for NPCs, a `clue`
definition (what it points to, planted/found). No kernel change, no schema change — this is the
entity-component model doing what it was built for.
*Why:* this is what lets session two resume session one's story from queries alone. It is the
storytelling contract's memory substrate.

**Exit test:** a fresh session can answer "what rules exist and what story is in progress?" using
only orient and query.

---

## Phase 2 — proof by play (two evenings)

**2.1 The played session.** One uncoached LLM as GM over MCP, one human player, one short
scenario. No coaching, same rules as a cold walk: when you want to intervene, write the sentence
down — it is a finding about the surface or the contracts, not about the model.

**2.2 The reuse session.** A second session, fresh context, same database. It must resume the
chapter, reuse at least one mechanic authored before or during session one, and ideally author
one new mechanic mid-play because the story needed it.

**2.3 Review `history()` afterward** — cited-vs-read, failures, wrong turns — and write the
findings into `COLDWALK.md` as runs 6 and 7. Fix what the findings say; nothing else.

**Exit test:** the M6 sentence is demonstrated. Declare MVP in `STATUS.md`.

---

## Phase 3 — beyond MVP, in value order

**3.1 The supervision view.** The control room: recent operations, mechanic diff between
versions, projection/seed replay for any action. The TravelRoleplay view-layer prior art
(ARCHITECTURE.md §9.8 — declarative view specs, closed hint vocabulary, no AI-authored raw HTML)
is load-bearing here; read it before designing.
*Why first in phase 3:* the premise of the whole system is a human approving code an AI wrote,
and approval needs a room to happen in.

**3.2 Multi-step actions.** A mechanic that needs an answer mid-run ("which door?") currently
cannot ask. TravelRoleplay's stateless ask/answers pattern — the mechanic returns questions,
the caller re-runs with answers attached, no resume token, no server state — fit this kernel's
statelessness and is the design to start from, including its known actionStack trap.
*Why:* real table play hits "it depends — ask the player" within the first hour.

**3.3 Events and subscriptions.** "When X changes, Y happens" without the GM polling. Currently
listed in orient's NotYetBuilt; keep it there until designed. Start from the lesson that made
TravelRoleplay's version workable: a closed registry of event names, so a bad trigger fails
loudly at write time rather than never firing.

**3.4 Campaign lifecycle.** Snapshot = copy the SQLite file (already true — document it as the
supported mechanism); plus a scope-aware "new campaign from shared rules" path. Cheap, and it
protects the play-test databases that are about to become precious.

**3.5 Semantic search — only when its trigger fires.** ARCHITECTURE.md §8.3 names the revisit
conditions. The evidence that fires them: `history()` showing intent searches that missed a
mechanic which existed. Embedded options (sqlite-vec-style) preserve the one-file-copy
constraint; hosted stores remain ruled out by the sync-store decision.

**Deliberately not listed:** a second model provider, auth/multi-user, a public deployment, a
SPA. All premature until people other than Dante play it.
