# DantesRoleplay — what is left

Last updated 2026-08-18. Update `STATUS.md` in the same change that moves an item.

**The goal this document serves** (`ARCHITECTURE.md` §10, M6):

> You can play a session through an MCP client, and the LLM can add a new game mechanic
> mid-session that survives and gets reused next time.

Everything below is scoped to making that sentence **demonstrated**, not asserted. The machinery is
built and exercised: a rule has been written, found by what a player would say, run in the sandbox,
and had its effects applied in one transaction — over real JSON-RPC. What is missing is content, a
story frame to hang it on, and a stranger driving it without being told how.

---

## The checklist

| # | What | Who | Size | Blocks MVP? |
| --- | --- | --- | --- | --- |
| 1 | **Cold walk on the three-verb surface** — runs 1–5 re-run, recorded as run 6 | Dante | ~40 min | **Yes** — the migration's acceptance test |
| 2 | Author 3–5 mechanics through `commit`, not the seeder | either | ~half a day | **Yes** — nothing to play with otherwise |
| 3 | Land `procedure.play.storytelling` | either | ~1 hour | **Yes** — the GM's craft has to survive a session boundary |
| 4 | Model the campaign frame as world data (chapter / motive / clue) | either | ~2 hours | **Yes** — this is how session two resumes session one |
| 5 | Document campaign snapshot = copy the file | either | ~15 min | **Yes**, before play — it protects the evidence |
| 6 | **The played session** — uncoached GM, one human player (run 7) | Dante | one evening | **Yes** |
| 7 | **The reuse session** — fresh context, same database (run 8) | Dante | one evening | **Yes** — this is the milestone |
| 8 | Act on what runs 6–8 find, then declare MVP in `STATUS.md` | either | depends | **Yes** |

Items 2–5 can happen in any order and partly in parallel. 1 should come first because it tests the
surface everything else is built on; 6 and 7 must be last, and 7 must follow 6 on the same database.

Everything not in that table is **after** MVP. The list of what is deliberately excluded is at the
bottom, so it stays excluded.

---

## Phase 0 — the surface ✅ done 2026-08-18

The twelve-tool surface became `orient`, `query`, `commit`. `VERB_MIGRATION.md` holds the decisions,
the deviations and the twelve defects the work found; `STATUS.md` summarises. 182/182 tests green,
including a protocol walk that speaks real JSON-RPC to a running server.

**Two findings carried forward, because they change what the next phases can assume:**

- **`commit(kind: "action")` selects a rule by intent and RUNS it.** There is no way to name a rule
  and no caller-facing dry run. Every contract and `orient` had claimed otherwise, inherited from an
  unreachable older implementation that is now deleted. See item **B** under *Risks* below.
- **`governs` is matched literally, so two contracts governing one call is the same as none.** Two
  pairs had drifted into that state and were split into a caller-facing and a kernel-facing half.
  Watch for it as the manual grows.

**Still open from Phase 0 — this is checklist item 1.** The cold walk was batch B7 of the migration
and has not been run. Runs 1–5 in `COLDWALK.md` tested a surface that no longer exists, so they are
history, not evidence. Re-run all five against the three verbs, uncoached, in a session with no
access to this repository, and record it as **run 6**.

*What good looks like:* a cold session navigates orient → query → commit without inventing a kind
or a payload shape, and without needing `query(kind: "capabilities")` explained to it.
*What matters more:* wherever it guesses. A guess that happened to be right is still a finding.

---

## Phase 1 — the first content, through the front door

**1.1 Author 3–5 mechanics via `commit(kind: "mechanic")`, not via the seeder.** Two ship seeded
already — `mechanic.check.threshold` and `mechanic.value.adjust` — deliberately generic, as worked
examples to copy. What is missing is rules with a game in them.

Suggested set, chosen to exercise different parts of the effect vocabulary rather than to be a
game: something that moves a thing into another thing (`containment.move`), something that relates
two entities (`relationship.create`), something that removes a component, and one flavour rule that
proposes **zero** effects — narration only, which `procedure.mechanic.run` explicitly blesses.

*Why through the front door:* authoring the first real content IS the acceptance test of the
authoring surface. Every friction point met here is one a mid-session LLM would have met later,
with a player waiting. Do it as the LLM would: search first, dry-run, read the checks, commit,
then run the rule.

**1.2 Land `procedure.play.storytelling`.** Drafted at the repo root as `storytelling.md`. It
assumes the three-verb surface (it already reads correctly against it) and the definitions from 1.3.
Move it into `DantesRoleplay/Bootstrap/` or commit it through `commit(kind: "procedure")` — either
path works; the file path is the one with a build-time check behind it now.

If 1.1 reveals the need, add a short `procedure.play.session` covering session start: orient →
query world → resume the open chapter.

*Why a contract and not prompt text:* the GM's craft has to reach a cold model the same way
everything else does — retrieved, on demand, from inside the system.

**1.3 Model the campaign frame as world data.** Using only what exists: a `chapter` definition
(question, status, summary), a `motive` definition for NPCs, a `clue` definition (what it points to,
planted/found). No kernel change, no schema change — the entity-component model doing what it was
built for.

*Why:* this is what lets session two resume session one's story from queries alone. It is the
storytelling contract's memory substrate, and without it "the story survives" is a claim about the
context window rather than about the database.

**1.4 Write down how to snapshot a campaign.** It is already true — copy the SQLite file — but it is
written down nowhere, and the databases from items 6 and 7 are the evidence the whole project rests
on. One paragraph in `STATUS.md` or a short `procedure.system.snapshot`. Do this *before* the played
session, not after it.

**1.5 Build the D&D SRD 5.2.1 ruleset as small vertical slices.** The implementation shape, research
findings, component order, and verification gates are in
[DND_RULESET_IMPLEMENTATION_PLAN.md](DND_RULESET_IMPLEMENTATION_PLAN.md). It extends the active
contract-first ruleset track; it does not authorize a bulk content import or a new engine.

**Exit test:** a fresh session can answer *"what rules exist and what story is in progress?"* using
only `orient` and `query`.

---

## Phase 2 — proof by play

**2.1 The played session (COLDWALK run 7).** One uncoached LLM as GM over MCP, one human player,
one short scenario. Same discipline as a cold walk: when you want to intervene, write the sentence
down instead — it is a finding about the surface or the contracts, not about the model.

**2.2 The reuse session (COLDWALK run 8).** A second session, fresh context, **same database**. It
must resume the chapter, reuse at least one mechanic authored before or during session one, and
ideally author a new one mid-play because the story needed it.

**This is the milestone.** If it writes a second near-duplicate rule instead of finding the first,
retrieval is not doing its job and neither is the near-duplicate check.

**2.3 Review the audit afterward** — `query(kind: "history")`: cited-vs-read, failures, wrong turns.
Write the findings into `COLDWALK.md`. Fix what the findings say and nothing else.

**Exit test:** the M6 sentence is demonstrated. Declare MVP in `STATUS.md`.

---

## Risks to Phase 2, in the order they are likely to bite

These are not tasks. They are the three ways the played session can fail for a reason that is the
system's fault, listed so that when one happens it is recognised rather than debugged from scratch.

**A. A mechanic needs to ask the player something mid-run** ("which door?"). It cannot. Real table
play hits this within the first hour. The workaround during a play-test is to resolve it as two
actions with the GM asking in prose between them; the fix is Phase 3's stateless ask/answers.

**B. The wrong rule answers.** Selection is the best-ranked active match for the intent, with no way
to see the alternatives or override them. With two rules this is invisible; with the 5–7 rules
Phase 1 adds, "check" could match three. If this happens, record the intent and the rule that
answered — that transcript is the evidence for which fix in Phase 3 is right.

**C. The GM narrates an outcome the system did not produce.** The contracts forbid it in three
places, which means it is expected to be tempting. It is the failure that matters most, because it
makes the audit log and the story disagree, and only one of them survives the session.

---

## Phase 3 — after MVP, in value order

**3.1 The supervision view.** Recent operations, mechanic diff between versions, projection/seed
replay for any action. The TravelRoleplay view-layer prior art (`ARCHITECTURE.md` §9.8 — declarative
view specs, closed hint vocabulary, no AI-authored raw HTML) is load-bearing; read it before
designing. *Why first:* the premise of the whole system is a human approving code an AI wrote, and
approval needs a room to happen in.

**3.2 Choosing which rule runs** — risk B above, once there is a transcript. Two candidate shapes:
return candidates and require a second call to commit one (a round trip, and what the deleted
implementation did), or accept an optional `mechanicId` in the action payload that must be one of
the ranked candidates. Do not design it before the play-test.

**3.3 Multi-step actions** — risk A above. TravelRoleplay's stateless ask/answers pattern: the
mechanic returns questions, the caller re-runs with answers attached, no resume token, no server
state. Fits this kernel's statelessness. Includes its known `actionStack` trap.

**3.4 Events and subscriptions.** "When X changes, Y happens" without the GM polling. Keep it in
orient's `notYetBuilt` until designed. Start from the lesson that made TravelRoleplay's version
workable: a closed registry of event names, so a bad trigger fails loudly at write time rather than
never firing. The implementation design, chain limits, transactional behavior, and acceptance
tests are in [EVENTS_AND_SUBSCRIPTIONS_PLAN.md](EVENTS_AND_SUBSCRIPTIONS_PLAN.md).

**3.5 Campaign lifecycle beyond snapshot** — a scope-aware "new campaign from shared rules" path.

**3.6 Semantic search — only when its trigger fires.** `ARCHITECTURE.md` §8.3 names the revisit
conditions. The evidence that fires them: history showing intent searches that missed a mechanic
which existed. Embedded options (sqlite-vec-style) preserve the one-file-copy constraint; hosted
stores remain ruled out by the sync-store decision.

**3.7 Small known gaps**, none of which block anything:

- `IProcedureStore.GetVersionsAsync` and `IWorldStore.GetEntityAsync` have no production caller. The
  capabilities are reachable another way (`LatestVersion` on the detail; the plural getter), so this
  is tidiness, not a gap in the surface.
- `kind` is a `string` in the JSON schema rather than an enum, so an invented kind returns
  `UNKNOWN_KIND` at runtime instead of failing schema validation. Deviation from `VERB_MIGRATION.md`
  D2, recorded there.
- The `GuardTests` dispatch check verifies which kinds a switch handles, not which handler each arm
  calls. The protocol walk covers the routing behaviourally.

**3.8 Hierarchical catalog navigation.** Add hierarchical category paths, multiple-category and
recursive filtering, and a category-browser query for procedures and mechanics. The design and
acceptance criteria are in [HIERARCHICAL_CATALOGS_PLAN.md](HIERARCHICAL_CATALOGS_PLAN.md). Do this
only when real ruleset work shows that flat categories plus intent search are insufficient. D&D
contracts can adopt the documented dot-path naming convention immediately; recursive behavior
waits for this feature.

**3.9 Local intent routing and safe action pipelines.** Use deterministic retrieval first, then
optionally add Ollama re-ranking and local vector retrieval only when the documented scale or
retrieval-miss triggers fire. The router may prepare typed actions but must not silently execute
writes; arbitrary multi-commit pipelines remain out of scope. See
[LOCAL_INTENT_ROUTING_PLAN.md](LOCAL_INTENT_ROUTING_PLAN.md).

**Deliberately not in MVP:** a second model provider, auth or multi-user, a public deployment, a
SPA, schema enforcement of component data. All premature until someone other than Dante plays it.

---

## Housekeeping

Not on the MVP path, but cheap and worth doing before the repository gets shared or a play-test
database becomes precious.

**Tracked files that should not be tracked.** `git rm` them and let `.gitignore` keep them out
(the entries are already added):

```
git rm -r --cached _to_delete && rm -rf _to_delete
git rm --cached _src.tgz .webui_secret_key DantesRoleplay.MCPServer/data/dantesroleplay.db
```

`_to_delete/` is 14 tracked files of superseded code kept because this session's file bridge cannot
delete. `_src.tgz` is a build artifact. `.webui_secret_key` belongs to another tool entirely. The
committed development database means every server run dirties the working tree.

**`ProcedureRelation` is a dead table.** The entity, the enum, the `DbSet` and the model
configuration all exist; nothing reads or writes any of it, and no tool exposes it. It is the
"contract parent" idea that `procedure.contract.create` was corrected for on 2026-08-17 — a table
advertising a capability the system does not have. Removing it needs an EF migration, so it is
Dante's to run:

```
dotnet ef migrations add DropProcedureRelations --project DantesRoleplay.DataAccess
```

after deleting `DantesRoleplay/Procedures/ProcedureRelation.cs` and its `DbSet` and
`modelBuilder.Entity<ProcedureRelation>` block in `DantesRoleplayDbContext.cs`.
