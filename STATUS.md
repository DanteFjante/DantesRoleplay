# DantesRoleplay — status

Last updated 2026-08-21. Tracks against `ARCHITECTURE.md`. Update this in the same change that
moves an item.

**Legend:** ✅ done and covered by passing tests · 🟡 partly done, or done but unreachable ·
⬜ not started · ⏸ deliberately deferred

**Right now:** solution builds clean, **295/295 tests pass**, and **the MCP surface is three verbs
— `orient`, `query`, `commit`** (the migration in `VERB_MIGRATION.md`, completed 2026-08-18; the
twelve names it replaced are recorded in `VERB_HISTORY.md`). The MVP sentence is true end to end: a
rule that did not exist was written through the write path, found seconds later by what a player
would say, run in the sandbox, and its effects applied in one transaction.

The migration also added the test that had been missing all along — a protocol walk that speaks
real JSON-RPC to a running server. It immediately found that `IActionRunner` was never registered,
so **every `commit` had been failing at invocation** with no envelope and no audit row, while all
167 unit tests passed. See *The three-verb migration* below.

What is left is not machinery: content to play with, a story frame to hang it on, and a stranger
driving it without being told how. `NEXT_STEPS.md` is the list.

**Implementation:** Feature 36 Slice 1 adds closed, source-backed character XP state plus an
effect-free exact-next-level eligibility reader. Campaign C14 still owns policy/authorization and
Character CH9 remains the only level-up transaction; no campaign award or character level change
is enabled by this slice.

**Implementation:** Campaign C15 now supplies a campaign-owned active-character participation
scope and a trusted-host atomic attach operation. It preserves actors and uses no new tool/kind;
withdrawal remains blocked on Character CH13.

**Campaign C15 Slice 1:** the campaign-owned character-participation component, procedure, and
read-only active-scope verifier are verified in a disposable catalog import. The verifier writes
nothing and accepts only one valid active campaign→participation→actor graph; C15 attachment,
CH5 composition, withdrawal, and CH13 handoff remain separately confirmation-gated.

---

## What "MVP" means here

> **You can play a session through an MCP client, and the LLM can add a new game mechanic
> mid-session that survives and gets reused next time.**

That is milestone M6 in `ARCHITECTURE.md` §10, and it is the point at which the core premise is
proven rather than asserted. Everything below is scoped to that sentence — anything not needed to
make it true is listed under *Not in MVP* and should stay there.

**The sentence is now demonstrably true**, subject to a cold walk confirming a stranger can do it
without being told how. Everything in it — write a rule mid-session, have it survive, have it get
reused — was exercised over real MCP against the running server.

---

## ✅ Done

**Foundations**

- ✅ Architecture decided and recorded, with the reasoning for each overturn (`ARCHITECTURE.md`)
- ✅ Five-project solution; core has zero package references
- ✅ SQLite + EF Core, migrations covering procedures, operations and the world
- ✅ Offline build + test running in the Cowork container

**Procedure contracts — the operating manual (P1–P2)**

- ✅ Contract storage with append-only versioning; content never overwritten
- ✅ Authored as markdown, seeded into the DB, idempotent by stored fingerprint
- ✅ 8 bootstrap contracts, including `procedure.world.change` which governs every world write
- ✅ `governs` on every contract, so "which contract applies" is a lookup
- ✅ Proven live: editing a contract's markdown appended v2 rather than overwriting v1

**Audit (P3)**

- ✅ Every tool call recorded, including failures and reads
- ✅ Distinguishes procedures **cited** from procedures **demonstrably read**
- ✅ `history(subject: ...)` finds an entity inside a batch that touched several
- ✅ Reading the manual once backs every operation in the session that it governs

**World model (P5–P6)**

- ✅ Entity / component / definition / containment / relationship store
- ✅ Proven: a new stat can be added with no migration and no C# change
- ✅ **Reachable over MCP** — `query(kind: "world")`, `query(kind: "entities")`,
  `commit(kind: "component")`, `commit(kind: "effects")`

**Effects (P9)**

- ✅ Nine structural verbs, no game vocabulary, guarded by a test
- ✅ Validate the whole list, then apply the whole list — or none of it (§3.8)
- ✅ Validation simulates the batch, so an effect may depend on an earlier one in the same list
- ✅ Every fault reported at once with its position, so one round trip is enough to fix them
- ✅ Silent no-ops are faults: adding a component twice, removing what is not there, reusing a
  deleted entity's id
- ✅ Failures the validator cannot foresee — a containment cycle formed by the batch itself — roll
  the whole batch back

**Mechanic storage (P7)**

- ✅ Identity plus append-only versions, shaped exactly like the procedure store — an agent that
  learned find → get → dry run → write for contracts already knows how to author a rule
- ✅ Old source stays readable, which is what keeps a past operation explainable
- ✅ Found by the words a player would use, via the author's own match phrases
- ✅ **Scope answered the open question:** a campaign sees its own rules plus the shared ones, and
  a campaign rule outranks the shared one it replaces. That is the whole inheritance chain, and it
  cost one column and one `OrderByDescending` rather than a table
- ✅ Dry-run checks: requirements parse, named components exist, source present, near-duplicate

**JavaScript sandbox (P8) — `DantesRoleplay.RuleAccess`**

The risk `ARCHITECTURE.md` §2 calls the major one. Three things answer it, all tested:

- ✅ **No CLR access.** `AllowClr()` is never called, and there is no flag that turns it on
- ✅ **Nothing but strings crosses the boundary.** Data goes in as JSON text and comes back as JSON
  text; not one .NET object is handed to the script, so there is no object graph to walk out of.
  Even the random source is JavaScript rather than a delegate into C#
- ✅ **Every limit set on the first run**, not after something hung: statements, wall clock, memory,
  recursion, effect count, log lines
- ✅ 12 escape attempts written the way an attacker or a confused LLM would write them —
  `System.IO.File`, `importNamespace`, `clr`, reflection, `require('fs')`, `process`, `fetch`,
  `XMLHttpRequest`, and walking up `constructor.prototype` from a value the host supplied
- ✅ Runaway loop, runaway allocation and runaway recursion all stop as named limits. The recursion
  one matters most: a real stack overflow cannot be caught in .NET and would take the process
- ✅ Author error is an outcome, not an exception — a syntax error, a thrown value or a missing
  return all come back as a message the LLM can act on
- ✅ **Seeded and reproducible.** Same seed, same outcome; the seed is recorded, so "why did that
  happen?" stays answerable months later. A chance-based rule is unreviewable otherwise
- ✅ Strict mode, so `total = 5` is an error rather than a silent global
- ✅ The guard test now scans RuleAccess too — the sandbox is the most tempting place to bake in a
  rolling convention, and it offers a random source and nothing above it

**The join — the premise, demonstrated**

- ✅ A rule written in JavaScript at runtime reads a projection, proposes effects, and those effects
  land in the database in one transaction. No C# anywhere knows what the rule meant
- ✅ A rule that proposes something incoherent changes **nothing** — not even the effects that were
  fine, not even the entity created two effects earlier
- ✅ A hostile rule, stored through the ordinary authoring path, reaches nothing and leaves the
  world exactly as it was

**Projection resolver (§3.6a)**

- ✅ Compiles declared requirements plus role→entity ids into one frozen projection
- ✅ **A mechanic receives only what it declared** — not the entity's other components, not its
  relationships, not the rest of the world. Requirements that understated what a rule reads would
  make the supervision view a lie, so they are also what is enforced
- ✅ Optional roles are simply absent; a missing required one says what it is for and how to pass it
- ✅ A role the mechanic does not declare is reported, not ignored — it usually means the wrong rule
- ✅ Contents materialise only when asked for; where an entity *is* comes free
- ✅ Every fault at once, so one round trip fixes them

**Actions and rules**

- ✅ `commit(kind: "action")` — the whole chain in one call: select by intent → resolve the declared
  projection → sandbox → validate → one transaction
- 🟡 **Selection is by intent alone.** There is no way to name a rule and no caller-facing dry run.
  Both existed in an older, unreachable implementation whose next-step strings advertised them long
  after it stopped being called; that implementation is now deleted and the contracts say what the
  live path actually does. Whether to add mechanic selection back is a real question once several
  rules match the same words — see `NEXT_STEPS.md` Phase 3
- ✅ Seeded, and the seed is returned; passing it back replays a run exactly
- ✅ `query(kind: "mechanics")` is both the search and the read-in-full, by `id`
- ✅ `commit(kind: "mechanic")` with the same dry-run discipline as `commit(kind: "procedure")`
- ✅ `query(kind: "history", subject: ...)` answers both "which rules have been run" and "what has
  happened to Orban" — including checks that changed nothing, because an outcome is an event

**The game that ships (deliberately tiny)**

- ✅ Two rules, both entirely generic: test a number against a threshold, adjust a number. Neither
  knows what the numbers mean. They exist so the first session has a worked example to copy — an
  agent with no example writes something plausible and wrong
- ✅ Seeded from markdown like the contracts, idempotent by fingerprint, and ordinary revisable rows
  once loaded
- ✅ Both are tested by **running** them, not just by parsing them
- ✅ They fail usefully: a missing argument names what to pass, an unknown field lists the ones that
  exist, and an entity with no numbers says so rather than being treated as zeroes

**D&D SRD 5.2.1 ruleset track**

- ✅ Feature 1 is installed through the live MCP write path: `dnd2024.abilities`, its dedicated
  contract, a fixed-DC ability-check contract, and `mechanic.dnd2024.check.ability` v1.
- ✅ Intent search ranks the scoped ability check above the shared threshold rule. Seed
  `8253275941846134235` replayed exactly (`7 +3 = 10`), Charisma 8 produced −1, and checks applied
  zero effects as contracted.
- ✅ The procedure anti-sprawl guard now counts distinct tokens and ignores literal
  `commit(kind: "...")` fragments. Two regression tests cover both false-positive modes; the full
  suite is 213/213.
- ✅ Feature 2 has a recursive dependency-first plan at
  `ruleset/dnd2024/feature-02/FEATURE-2-DEPENDENCY-PLAN.md`. It resolves the source-registry
  sequencing conflict.
- ✅ Slice 1 is installed only in the live database: source-registry contract v1,
  `dnd2024.source`, and `source.dnd2024.srd-5.2.1`. Exact metadata assertions passed, discovery by
  component returns one entity, and duplicate creation was rejected without changing stored data.
- ✅ Slice 2 is installed only in the live database: character-level contract v1,
  `dnd2024.character-level`, and `mechanic.dnd2024.character-level.record` v1. All ten level-band
  boundaries passed through real actions; eight invalid inputs were rejected without state change;
  Orban now stores level 5 plus the fixed source reference and no derived bonus.
- ✅ Slice 3 is installed only in the live database: skill-proficiencies contract v1,
  `dnd2024.skill-proficiencies`, and `mechanic.dnd2024.skill-proficiencies.record` v1. All 18
  stable IDs and advisory defaults passed; empty and multi-skill states passed; eight malformed
  inputs were rejected with byte-stable actor state; `creature.orban` ends with only `perception`
  and `stealth` plus the fixed source reference in this component. A duplicate-name demo entity
  was caught during dependency verification and its mistaken test attachment was removed through
  a dry-run-first audited effect.
- ✅ Slice 4 is installed in the live database: ability-check contract v2 and mechanic v3 validate
  closed raw/named-skill input, derive proficiency from level, and emit no effects. Seeded actions
  prove level 4/5/16/17 bonuses of +2/+3/+5/+6, a level-5 Stealth/Acrobatics delta of +3, an
  alternate Strength (Intimidation) check, raw replay, empty proficiency behavior, rejected
  malformed/derived input, and normal natural-1/natural-20 total resolution. Test state was
  restored to level 5 with Perception and Stealth.
- ✅ A post-completion Slice 4 audit closed the only evidence gap: disposable fixtures proved that
  missing skill state and an invalid level are rejected. The rule itself needed no revision; both
  fixtures were deleted through a dry-run-first transaction.
- ✅ Feature 3 is installed only in the live database: ability-check contract v3 and the owning
  ability-check mechanic v4 implement closed `rollCircumstances`, seeded normal/Advantage/
  Disadvantage selection, non-stacking, cancellation, tie handling, and the result envelope.
  Fixed-seed actions verified normal, Advantage, Disadvantage, mixed cancellation, preserved skill
  arithmetic, natural 1/20 total behavior, malformed-input rejection, zero effects, and byte-stable
  Orban state. The catalog rejected a parallel D20 contract as an overlap, so this convention
  correctly remains with its owning ability-check rule.
- ✅ Feature 3 was independently reverified before Feature 4 planning: fresh normal, Advantage,
  Disadvantage, cancellation, tie, replay, proficient Stealth, and invalid-derived-input actions
  selected mechanic v4, applied zero effects, and left exact actor bytes unchanged. Component
  discovery confirms no Feature 3 fixture remains.
- ✅ Feature 4 Slice 1 is verified live: the existing proficiency-state contract is v2, the closed
  `dnd2024.saving-throw-proficiencies` component and scoped recorder are live, and Orban's final
  canonical state is `con,wis` plus the fixed source reference. Add/set, empty/missing semantics,
  canonicalization, replay, intent routing, and ten invalid-input state-preservation cases passed.
- ✅ Feature 4 is verified live: procedure.mechanic.dnd2024.saving-throw v1 and
  mechanic.dnd2024.saving-throw v2 resolve closed fixed-DC character saves from ability, level,
  and save-proficiency state. All ability modifiers, the exact +3 proficiency delta, level
  boundaries +2/+3/+5/+6, D20 circumstance modes/tie/cancellation, natural 1/20 total behavior,
  voluntary failure, replay, routing, malformed input, missing/corrupt state, zero effects, and
  final canonical Orban state passed. The temporary negative-state fixtures were deleted.
- ℹ️ The catalog's lexical near-duplicate warnings were addressed by revising the existing owner
  rather than creating a parallel contract; every blocking authoring check passed. Feature 4 Slice
  2 fixed-DC saving-throw resolution is the next review-gated pass.
- ✅ `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md` now defines the reusable standard for Terra
  to plan later features: live discovery, ownership analysis, recursive dependency descent,
  complete slice specifications, adversarial acceptance matrices, evidence, quality audit, and a
  planning-only stop gate.
- ✅ Further ruleset development is ordered in `ruleset/dnd2024/ROADMAP.md`, and
  `ruleset/dnd2024/TERRA-IMPLEMENTATION-HANDOFF.md` captures the exact execution and evidence bar
  for lower-cost continuation.
- ✅ Feature 5 is complete through catalog import: the encounter-order parent composes individual
  Initiative rolls, captures authorized ties, writes one encounter snapshot, and is covered by the
  file-import regression gate.
- ✅ Feature 6 is complete through catalog import: `dnd2024.armor-class` and
  `dnd2024.hit-points`, their governing contracts, and their record/correct writers own final AC
  and bounded current/maximum Hit Point state. The fresh-database import gate covers routing,
  boundaries, correction, rejection, atomicity, corrupt state, and AC preservation; catalog
  verification reports 56 matching records.
- ✅ Feature 7 is complete through catalog import: canonical Dagger, Shortbow, and Battleaxe
  profile entities plus `dnd2024.weapon-profile`; and `dnd2024.weapon-proficiencies`, its
  governing contract and closed record/correct writer. Fresh-database coverage verifies explicit
  empty, Simple, Martial, and both-category state; correction; protected state; routing; replay;
  malformed input; absent correction; and corrupt state. Catalog verification reports 65 matching
  records. Equipment ownership, range, mastery, and damage remain later owners.
- ✅ Feature 8 is complete through catalog import: `mechanic.dnd2024.weapon-attack` and its
  governing contract resolve a seeded, effect-free weapon attack against final Armor Class using
  canonical profile and category-proficiency state. Fresh-database coverage proves PB bands,
  ability/profile restrictions, AC comparison, D20 modes, replay, natural-20/1 precedence,
  rejection, and unchanged state. Catalog verification reports 67 matching records; the full
  suite passes 299/299. Feature 9 remains the separate owner of damage and Hit Point consequences.
- ✅ Feature 9 is complete through catalog import: the effect-free
  `mechanic.dnd2024.weapon-damage.roll` resolves canonical normal/critical damage, and its
  `mechanic.dnd2024.weapon-damage.apply` parent consumes exactly one child result to atomically
  replace only target current Hit Points. Fresh-import coverage verifies normal/critical/overkill
  behavior, target-only state change, maximum/source preservation, replay, and rejection.
  Catalog verification reports 71 matching records and the full suite passes 302/302.
- ✅ Feature 10 is complete at `ruleset/dnd2024/feature-10/FEATURE-10-DEPENDENCY-PLAN.md`:
  catalog-owned training encounter, hero, and target fixtures establish the clean baseline, while
  two independent fresh databases replay the same seeded Perception check, Constitution save,
  Initiative order, critical Dagger attack, and damage application with identical structured
  results and final state. Only the encounter order and target current Hit Points change. Catalog
  verification reports 74 matching records and the full suite passes 304/304.
- ✅ E1 events and subscriptions is complete: all six slices of
  `EVENTS_AND_SUBSCRIPTIONS_PLAN.md` are implemented and verified at 365/365. Event types and a
  mode-aware versioned middleware registry; pre-commit guards that veto a proposed change and roll
  the root back with structured evidence; an append-only structural event ledger whose payloads
  carry `before` and `after`, so the record says what a change replaced and not only what it set;
  reaction subscriptions whose effects commit in the same transaction as the change that triggered
  them; events a rule declares itself, validated at emission and answerable by further rules; and
  notifications, written once by a committed reaction and never editable afterwards.
  Bounded by four chain limits with four distinct codes, and reproducible: every reaction's seed is
  derived from the root, so a chain replays exactly.
  What is deliberately absent: real-time scheduling, and external delivery of any kind. A
  notification is a row that waits until somebody asks. Nothing in the system moves unless a verb
  is called.
- ✅ `procedure.system.create-feature` establishes the file-first repository-wide workflow: plan
  to verified leaves, implement one lowest unimplemented slice in catalog files, dry-run/import/
  verify it against the database, then stop for review.
- 🟡 Feature 11 has a complete planning-only dependency plan at
  `ruleset/dnd2024/feature-11/FEATURE-11-DEPENDENCY-PLAN.md`. It scopes turn/round lifecycle to
  one encounter-owned state derived from Feature 5's Initiative snapshot; Slice 1 alone creates
  the closed state and start transition. Advance and explicit end remain separately review-gated.
  No runtime game artifact was created. Current unrelated catalog/database drift must be resolved
  before its file-first import pass.
- 🟡 `CHARACTER_CREATION_PLAN.md` is the authoritative CH0–CH14 character-feature base roadmap.
  CH0's Human Soldier Fighter first path is ratified as one complete SRD 5.2.1 level-one
  non-spellcasting fixture. CH1 Slice 1 now verifies immutable Human, Soldier, and Fighter
  content provenance; profile writing and later content remain gated on their named owners;
  campaign attachment, missing grant/derivation owners, CH5 staged composition, and a campaign-owned
  advancement authorization remain blockers. CH6 presumes no new surface; CH7 limits
  correction/expansion; CH8 is stateless guidance with a separately gated, loopback website consumer;
  CH9 is one atomic non-spellcasting level 1→2 fixture, not XP/milestone policy or general leveling;
  CH10 consumes a future ruleset spellcasting-resource owner and does not duplicate spells/slots or
  spell resolution; CH11 consumes a future ruleset feat/ASI owner and a level-appropriate CH9 slice,
  not the creation-time ability writer; CH12 owns an atomic migration from singular to plural class
  membership before one non-spellcasting second-class fixture; CH13 adds only voluntary
  active→retired→archived lifecycle with campaign participation coordination; CH14 consumes future
  identity/policy enforcement for one principal-to-active-character control grant. No character runtime artifact was created.
- 🟡 `SESSION_OPERATIONS_PLAN.md` is the authoritative S0–S9 session-product roadmap. It maps
  Campaign Feature C8 to the initial lifecycle, factual recap, and checkpoint boundary without
  duplicating C8's session-record owner; later participant control, gameplay handoff, narrative
  artifacts, player-safe controls, and remote collaboration remain separate named successors.
  [S0](session/feature-00/SESSION-FEATURE-00-RATIFICATION.md) is ratified for C8's C3-only,
  trusted-host first fixture: quest/audience projections are omitted and checkpoint evidence waits
  for S4. [S1](session/feature-01/SESSION-FEATURE-01-DEPENDENCY-PLAN.md)
  then plans one campaign-scoped active-session record/start transaction without recap, checkpoint,
  participant, or gameplay state. [S2](session/feature-02/SESSION-FEATURE-02-DEPENDENCY-PLAN.md)
  then plans a zero-effect fresh-host resume projection from current approved owner state, not a
  transcript or session-local cache. [S3](session/feature-03/SESSION-FEATURE-03-DEPENDENCY-PLAN.md)
  then plans one atomic active→ended transition with an immutable source-bound factual recap, not
  free-form GM/AI prose. [S4](session/feature-04/SESSION-FEATURE-04-DEPENDENCY-PLAN.md) separates
  named checkpoint evidence and host-interruption read recovery from a separately gated, full-scope
  atomic restore. [S5](session/feature-05/SESSION-FEATURE-05-DEPENDENCY-PLAN.md) then plans an
  immutable same-campaign session-to-character roster reference without copying player, character,
  or gameplay state. [S6](session/feature-06/SESSION-FEATURE-06-DEPENDENCY-PLAN.md) plans
  pre-selection eligibility and atomic audit correlation for one opt-in existing action, without a
  second action root or session activity log. [S7](session/feature-07/SESSION-FEATURE-07-DEPENDENCY-PLAN.md)
  then plans one immutable, attributed, source-bound noncanonical trusted-host recap, not a
  transcript or a second factual record. [S8](session/feature-08/SESSION-FEATURE-08-DEPENDENCY-PLAN.md)
  then separates a fixed policy-gated participant view from one later delegated player-safe action;
  it creates no browser-owned session or game authority. [S9](session/feature-09/SESSION-FEATURE-09-DEPENDENCY-PLAN.md)
  finally plans authenticated remote participant freshness and one action-owner concurrency fixture,
  without a host lease, presence log, global lock, or second transaction system. No session runtime
  artifact was created.
- ✅ Quest Q1 is verified: the closed creation-only quest surface creates a draft quest with three
  dormant objectives, canonical entity-name titles, exact C3 scope, and scoped/visibility-checked
  references. The focused Q1 suite passes 5/5, capability/guard/protocol coverage passes 17/17,
  disposable catalog validation passes 197 records (four non-blocking overlap warnings), and the
  serialized full suite passes 472/472. See
  `QUEST_IMPLEMENTATION_PLAN.md`. Q2.1 now replaces the former provisional lifecycle sketch; Q3
  remains blocked on the complete Q2 feature.
- ✅ Quest Q2.1 is verified in `quest/feature-02/QUEST-FEATURE-02-SLICE-1-RECEIPT.md`: the closed
  `procedure.quest.modify` offer/accept union keeps creation on the existing quest commit, records
  the factual reason in its audit, applies one atomic derived batch, and activates only initially
  eligible objectives. Focused/surface/catalog checks pass and the serialized full suite passes
  491/491.
- ✅ Quest Q2.2 is verified in `quest/feature-02/QUEST-FEATURE-02-SLICE-2-RECEIPT.md`: closed
  objective completion activates newly eligible dependants in canonical order, while block and
  unblock change only the owned objective. Foreign, stale, malformed, and invalid-target requests
  write no structural event. Focused/surface/catalog checks pass and the serialized full suite
  passes 498/498. Q2.3 reconciliation and terminal correction is recorded below.
- ✅ Quest Q2 is complete in `quest/feature-02/QUEST-FEATURE-02-SLICE-3-RECEIPT.md`: explicit
  reconciliation applies required-failure precedence and required-completion outcome only when
  called; fail/reopen/archive and guarded objective reopening are one-effect corrections. Focused,
  surface, catalog, and serialized full-suite evidence passes 8/8, 30/30, valid 231-record catalog,
  and 503/503 respectively. Q3 remains the separately planned read/history boundary.
- 🟡 Quest Q3 is planned in `quest/feature-03/QUEST-FEATURE-03-DEPENDENCY-PLAN.md`: one proposed
  bounded trusted-host summary derives current Q2 state, reference metadata, and status-only
  ledger history without parsing audit prose or creating another mutable store. Q3.0 requires
  confirmation of its new procedure/query/result boundary; real player/party authorization remains C5.
- ✅ World Feature 1 Slice 1 is verified: `game.core.world.root`,
  `game.core.world.location`, their governing topology procedure, and a five-entity world fixture
  establish one world root, region, three locations, four containment edges, and two canonical
  adjacency edges. Fresh-import regression coverage, `roleplay validate catalog` (99 records; two
  pre-existing near-duplicate warnings), and the full suite (374/374) pass without a persistent
  database import. See `world/feature-01/WORLD-FEATURE-01-RECEIPT.md`. World Feature 2 movement
  remains a separate planning gate.
- ✅ World Feature 2 Slice 1 is verified: mechanics can now declare
  `includeRelationships` on a role and receive only a frozen, ordered list of relationship records
  touching that role. Unrequested relationships remain absent from the JavaScript projection and
  deleted endpoints are excluded. The focused projection suite passes 27/27 and the full suite
  passes 382/382; no persistent database import occurred. See
  `world/feature-02/WORLD-FEATURE-02-SLICE-1-RECEIPT.md`.
- ✅ World Feature 2 is complete: the active traveller fixture and governed adjacent-movement
  mechanic move only across one stored Feature 1 connection, preserve all non-containment state,
  and record the existing correlated structural event. Fresh-import movement coverage passes 4/4,
  catalog validation reports 103 records (five non-blocking warnings), and the full suite passes
  386/386. No persistent database import occurred. See
  `world/feature-02/WORLD-FEATURE-02-SLICE-2-RECEIPT.md`.
- ✅ World Feature 3 Slice 1 is verified: confirmed `game.core.world.faction` and
  `game.core.world.motive` contracts, The Lantern Compact, and two recurring actors establish
  faction membership, a market-control claim, and durable motives without changing Feature 1
  topology. Focused coverage passes 2/2, catalog validation reports 109 records (six
  non-blocking warnings), and the full suite passes 388/388. No persistent database import
  occurred. See `world/feature-03/WORLD-FEATURE-03-SLICE-1-RECEIPT.md`.
- ✅ World Feature 3 is complete: its active fixture faction advances the closed agenda exactly
  once from ready to advanced through one `component.set`; all unrelated motive, link, and topology
  state remains unchanged, and the existing structural event is correlated to the action. Focused
  coverage passes 4/4, catalog validation reports 110 records (six non-blocking warnings), and the
  full suite passes 390/390. No persistent database import occurred. See
  `world/feature-03/WORLD-FEATURE-03-SLICE-2-RECEIPT.md`.
- ✅ World Feature 4 Slice 1 is verified: closed fact, rumour, secret, and clue contracts plus
  six root-scoped fixture records establish durable trusted-GM knowledge without copying hidden
  truth or changing prior world state. Focused coverage passes 2/2, catalog validation reports
  121 records (seven non-blocking warnings), and the full suite passes 392/392. No persistent
  database import occurred. See `world/feature-04/WORLD-FEATURE-04-SLICE-1-RECEIPT.md`.
- ✅ World Feature 4 is complete: one scoped clue reveals and one scoped rumour confirms only
  through deterministic actions that preserve secrets and unrelated world state. Focused coverage
  passes 3/3, catalog validation reports 123 records (nine non-blocking warnings), and the full
  suite passes 393/393. No persistent database import occurred. See
  `world/feature-04/WORLD-FEATURE-04-SLICE-2-RECEIPT.md`.
- 🟡 Invalid action input returns the mechanic's correct explanation but still pairs it with the
  misleading generic fix “the rule is broken, not your arguments.” The runbook records this
  pre-existing surface defect.

**MCP surface (P4) — complete, three verbs**

- ✅ **3 tools**: `orient`, `query`, `commit`. Six query kinds (`capabilities`, `procedures`,
  `world`, `entities`, `mechanics`, `history`) and five commit kinds (`procedure`, `component`,
  `effects`, `mechanic`, `action`), both closed enums, both flat
- ✅ `VerbSurface.cs` is the single description of all of it — kinds, the parameters each reads,
  every payload shape and a complete copyable example. `orient` and both dispatchers read from it,
  so the manual cannot describe an operation the code does not have
- ✅ `query(kind: "capabilities")` returns that catalog, and every `INVALID_PAYLOAD` repeats the
  shape it needed inside the error rather than pointing at where the shape lives
- ✅ Uniform envelope; every error names the exact next call, as a call that can actually be made
- ✅ Dry run on `procedure`, `mechanic` and `effects`, and neither dry run spends read evidence.
  `component` and `action` return `NOT_SUPPORTED` explaining why rather than pretending
- ✅ 9 guard tests: no game vocabulary in the kernel; orient announces exactly the registered verbs;
  **each dispatcher handles exactly the kinds the catalog advertises, asserted both ways**; both
  tool descriptions name every kind they serve; no recovery call names a retired verb; every
  example payload parses; every effect type is documented; the three-tool budget
- ✅ 4 protocol-walk tests over real JSON-RPC — `tools/list`, the full orient → query → commit →
  confirm sequence including a real sandbox run, that reading a contract and citing it shows up in
  the audit, and the two ways a session guesses wrong
- ✅ Seeded contracts are held to the same checks an agent's would be. The seeder writes directly,
  so nothing validated the shipped manual until a build-time test did
- ✅ 10 bootstrap contracts, including `procedure.mechanic.write` and `procedure.action.run`
- ✅ End-to-end smoke test over real JSON-RPC: orient → describe → define → reject bad effects →
  dry run → commit → read back → history

**Cold walks**

**Runs 1–5 predate the three-verb migration** and are recorded in the tool names that existed at
the time. They remain accurate as history and were re-run against the new surface as run 6; see
`COLDWALK.md`.

- ✅ **Run one (2026-08-17, Codex).** `orient` accurate including what is not built; found and read
  the existing contract instead of duplicating it; dry run passed all six checks.
- 🟡 **Run two.** Full sequence passed on a fresh database; blocked on the existing one, and the
  final audit was wrong.
- 🟡 **Run three.** Existing-database migration, drift check, startup and all five tools passed.
  One defect: `history` flagged the dry run as an unbacked citation. Fixed.
- ✅ **Run four — the M0 gate.** *"Create a character called Orban who is carrying a lantern."*
  Called `describe_world` before inventing a definition; read `procedure.world.change` and
  `procedure.world.model`; created a **generic** `stats` definition rather than `orban_stats`;
  checked both entities did not already exist; sent the whole change as **one** four-effect list,
  dry-run first. Verification confirmed the containment and the components.
  - **It also found and read `procedure.world.naming` — the contract cold walk run three wrote.**
    A contract authored in play was retrieved and followed by a later session that had never seen
    it. That is the premise of this system working at the smallest possible scale.
  - One finding, now fixed: see *the read-evidence model* below.

**Fixed after run one:** the stale migration; whole-phrase search that failed on "create
contract" (now token matching with ranking); read-evidence leaking across sessions.

**Fixed after run two:** migration history restored (regenerating `Initial` passes every
fresh-database check and then fails on the only database that exists); dry runs no longer consume
read evidence; `orient` is also a session boundary.

**Fixed after run three:** `Operation.ConsumedReadEvidence`, so `history` judges only operations
that actually consume evidence; no migration needs a non-transactional operation.

**Fixed after run four — the read-evidence model.** Runs two, three and four each found a defect in
the same mechanism, which is the signal that the mechanism was wrong rather than incomplete.
Reading was modelled as a **currency**: a read backed the next write and was then spent. Run four
showed why that fails. The agent read three contracts, called `define_component`, then
`apply_effects` — and the definition write spent all three, so the world write those contracts
actually govern reported no reads and was accused of citing what it had never opened.

A read is now a **window**, not a currency: every operation in a session sees every procedure read
in that session, bounded by the last `orient` and by a 30-minute backstop. Reading the manual once
and following it for several steps is correct behaviour, and the old model punished it — the only
way to satisfy it was to re-read the same contract before every write, which is busywork performed
to look compliant. The cold-walk subject worked that workaround out and recommended it, which *is*
the finding: the audit was teaching the agent to game it.

The known weakness is now asserted in a test rather than left to be discovered: two runs not
separated by `orient` and falling inside 30 minutes share their reading. Nothing observable
distinguishes them, because the stateless MCP host issues no session id.

---

## Ruleset planning — Features 12–17 (2026-08-20)

- ✅ Ruleset Feature 28 Slice 1 verifies the character-origin language/tool foundation: two
  separate source-cited proficiency membership records, each with a closed SRD 5.2.1 vocabulary
  and normal recorder. Focused catalog coverage, catalog validation, and the 455-test full suite
  pass; it created no character or persistent-game state.

Planning-only pass. **No runtime game artifact was created**: no procedure, component, entity,
mechanic, event type, subscription, or fixture. Six recursive dependency plans now exist under
`ruleset/dnd2024/feature-NN/` for Features 12–17, written to `TERRA-FEATURE-PLANNING-GUIDE.md` and
each stopping before implementation. `TERRA-IMPLEMENTATION-HANDOFF.md` is unchanged: the next and
only authorized implementation candidate remains **Feature 11 Slice 1**.

## Ruleset planning — Feature 18 (2026-08-20)

- ✅ Feature 18 concentration now has a complete planning-only dependency plan at
  `ruleset/dnd2024/feature-18/FEATURE-18-DEPENDENCY-PLAN.md`. It establishes the source-cited
  one-effect, voluntary/replacement, damage-save, Incapacitated, and death boundaries; finds the
  missing Feature 32 persistent-effect protocol and event-reaction child/input-composition
  decision; and authorizes no runtime concentration artifact until those owners are confirmed.

## Ruleset planning — Feature 19 (2026-08-20)

- ✅ Feature 19 reactions in play now has a complete planning-only dependency plan at
  `ruleset/dnd2024/feature-19/FEATURE-19-DEPENDENCY-PLAN.md`. It establishes the source-cited
  opportunity-attack timing/exclusions and finds the required Feature 20 pre-departure spatial
  trigger, dynamic reaction-composition, and bounded choice/decline dependencies. No runtime
  reaction artifact is authorised until those owners are confirmed.

## Ruleset planning — Feature 20 (2026-08-20)

- ✅ Feature 20 tactical position and movement now has a complete planning-only dependency plan at
  `ruleset/dnd2024/feature-20/FEATURE-20-DEPENDENCY-PLAN.md`. It provides a 2.5-foot anchor model
  for all six Size categories, separates base Speed from per-turn movement remaining, and identifies
  the platform contract needed to pass a derived path cost into the existing budget spender. Slice 1
  (base Speed and budget migration) is the only authorised future implementation slice.

## Ruleset planning — Feature 21 (2026-08-21)

- ✅ Feature 21 cover and ranged combat now has a complete planning-only dependency plan at
  `ruleset/dnd2024/feature-21/FEATURE-21-DEPENDENCY-PLAN.md`. It separates static weapon range,
  transient cover, obstacle geometry, combat sides, and Feature 34 sight; corrects the backlog's
  “firing into melee” wording to the SRD's attacker-side close-combat rule; and authorises only the
  static ranged-profile migration as a future first slice.

## Ruleset planning — Feature 23 (2026-08-20)

Feature 23 Slice 1 is complete: [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-1-RECEIPT.md).
It adds the generic, bounded nested-containment action projection needed before inventory rules can
derive burden or admit nested transfers. It created no D&D runtime artifact. Focused projection and
mechanic-store tests (52), the 460-test full suite, and `roleplay validate catalog` pass; the latter
reports only existing near-duplicate warnings. Slice 2 remains unstarted pending its permanent-id
and durable-definition reference confirmation boundary.

Feature 23 Slice 2 is complete: [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-2-RECEIPT.md).
It adds the immutable, source-cited `dnd2024.item-definition` catalog vocabulary and ten v1
definitions (ordinary containers/gear, a Dagger bridged to Feature 7, and all coin denominations).
Definitions use exact rational measures; campaign instances, inventory mutation, and currency
transactions remain unimplemented. Focused import/schema tests and disposable catalog validation
pass. The broader catalog snapshot test is excluded from the receipt because unrelated shared
catalog drift changed its before/after snapshot during the run.

Feature 23 Slice 3 is complete: [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-3-RECEIPT.md).
It adds `dnd2024.item-instance` plus record, atomic create-and-place, move, and read mechanics.
An instance stores only its immutable definition id; containment is its only custody/location.
Focused action-runner tests and disposable catalog validation pass. Quantities, stacking, capacity,
equipment, currency transactions, and ownership remain unimplemented.

Feature 23 Slice 4 is complete: [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-4-RECEIPT.md).
It adds `dnd2024.item-quantity` and controlled fungible-stack create/record, split, merge, and
consume mechanics. Counts are positive safe integers; the current derived stack key is the exact
versioned definition id; merging uses an explicit retained target in the same direct container;
and final consumption deletes the stack rather than storing zero. Focused action-runner/schema
tests and disposable catalog validation pass. Capacity, recursive burden, and inter-container
transfer admission remain unimplemented.

Feature 23 Slice 5 is complete: [receipt](ruleset/dnd2024/feature-23/FEATURE-23-SLICE-5-RECEIPT.md).
It adds declared component-reference projection and a read-only exact recursive physical-burden
mechanic. Static mass remains on definitions and quantities on stacks; an unmeasured/malformed
contained entity fails instead of silently counting as zero. Focused Feature 23 tests pass.
Creature Size/carry derivation and transfer admission remain unimplemented.

Four things the pass established that are worth having outside those files:

- **Legacy contents carry no components.** `includeContents` without the Slice 1 opt-ins still
  materialises `ContainedProjection(Id, Name, Slot)` only. A rule may now make a bounded,
  allow-listed nested request with `contentsDepth` and `contentComponentIds`; a child
  `forEachContentsOf`/`$item` binding remains the existing per-child execution shape.
- **A child's input has exactly three sources** (`MechanicComposer.ResolveInput`): inherited, a
  static literal, or an object-valued top-level key of the parent input. It can never be templated
  from a sibling child's result or a projected component value, because all children resolve before
  the parent source runs. Three drafted designs died on this and were rewritten.
- **`component.set` is an upsert.** `EffectApplier` faults `ComponentAdd` on a present pair and
  `ComponentRemove` on an absent one; `ComponentSet` does neither. Choosing between add and set has
  a silent failure mode.
- **There is no "optional component".** `RoleRequirement.Optional` is a role-level flag; a declared
  component the entity lacks is simply absent from the projection and never fails it.

Two hidden dependencies surfaced that no roadmap row names: a minimal creature-kind marker (nothing
distinguishes a player character from a monster, and the dying rules branch on it) and a
condition-integrity guard (a subscribed mechanic may not declare children, so a reaction cannot
reuse the condition writer). Both are Feature 17 slices.

Caveat on the planning evidence: no MCP server was reachable, so plan inventories are evidenced
from `catalog/` rather than operation ids. Slice 1 itself was repository-verified: focused and
full tests passed, as did catalog validation, without touching the live database.

---

## ⬜ Left to do for MVP

**`NEXT_STEPS.md` is the list.** It is kept there rather than here so there is one of it — this
file records what is true, that one records what to do next. Eight items, of which three are
Dante's and unautomatable: a cold walk on the three-verb surface, then a played session, then a
reuse session on the same database.

**Nothing left is machinery.** The chain that makes the MVP sentence true is built and exercised
end to end. What remains is content to play with, a story frame to hang it on, and a stranger
driving it without being told how.

**Tool budget check:** 3 — `orient`, `query`, `commit`. There is no room for a fourth, by design.
Growth happens in kinds, and a kind costs ~10–20 tokens in the catalog rather than a tool
description loaded into every conversation. `procedure.mcp.add-tool` governs adding one and starts
by telling you not to.

---

## The three-verb migration (2026-08-18)

Executed per `VERB_MIGRATION.md`. Decisions D1–D12 held, with two deliberate deviations, both
recorded in that document's status section. Five defects were found and fixed on the way, none of
which any existing test could have caught:

1. **`IActionRunner` was never registered.** `commit` takes it as a parameter for every kind, so
   the entire write verb failed at invocation over MCP — no envelope, no `fix`, no audit row. All
   167 unit tests passed, because each constructed its dependencies by hand and passed `null` for
   the runner. This is the whole argument for the protocol walk.
2. **Every write-side `fix` named a call that could not be made.** A regex adapter rewrote the
   preserved handlers' recovery calls by prefix, turning `write_procedure(id: "x", ...)` into
   `commit(kind: "procedure", id: "x", ...)` — `commit` takes `payload`, not `id`. The adapter is
   gone; the handlers write the public form themselves, built from `VerbSurface`.
3. **Responses were `\u0022`-escaped.** The SDK's default JSON encoder wrote every quote in the
   envelope that way, so the `fix` a model reads was `query(kind: \u0022capabilities\u0022)`.
   Fixed at both the tool-result and stored-projection serialisers.
4. **A dead second `run_action`** lived in `MechanicTools`, unreachable since `ActionRunner`
   replaced it and never tested. Its next-step strings were the source of every "choose a mechanic
   by id" and "dry-run an action" claim in the codebase and the contracts. Deleted.
5. **The action surface was described wrongly everywhere**, including in `orient`: "called with no
   roleEntityIds it returns candidate rules without running any" is false. It selects and runs.

Two contract pairs were also found governing the same call with no way to choose between them
(`mechanic.create`/`mechanic.write`, `action.run`/`mechanic.run`). Each pair is now one
caller-facing contract and one kernel-facing one with a redirect, matching what
`mechanic.projection` already did.

**What the end-to-end run proved.** Over real JSON-RPC, in one session: `run_action` with intent
alone returned candidates without running anything; a dry run showed the working
(`rolled 11 + strength 12 = 23 vs 14`); passing the seed back reproduced it exactly; a rule that
changes something merged one number and left the others; a wrong role name came back with both
faults and the call that fixes them. Then a rule that **did not exist** was written through
`write_mechanic`, passed all eight checks, and was found seconds later by "Orban tries to squeeze
past the rubble" — reading the contents of what he was carrying, because it declared that it
would. `CitedWithoutReading: 0` throughout.

**What the spike caught.** The projection was reaching JavaScript with .NET's naming, so
`ctx.roles.subject.Name` was defined and `ctx.roles.subject.name` was not. Every mechanic an LLM
wrote would have read `undefined` and produced a confidently wrong answer rather than an error —
in every rule at once, invisibly. Fixed, and locked by a test that also checks the opposite
mistake: dictionary keys are component ids and role names chosen by an author, and recasing those
would be the same bug wearing the other hat.

**Cold walk run five** is the MVP acceptance test itself: *add a new game mechanic mid-session, in
a new session confirm it survived and gets reused.* It cannot be run before item 6.

---

## ⏸ Not in MVP — deliberately

Each of these is wanted eventually; none is needed to prove the premise. Keeping them out is what
makes the MVP reachable.

| Item | Why it waits |
| --- | --- |
| Composition — `ctx.mechanics.run` (§9.7) | Only matters once enough mechanics exist to duplicate each other. |
| Multi-step actions — `ctx.ask` | "Which slot do you want to burn?" is a refinement, not a blocker. |
| Growth controls ladder (P12) | The duplicate warning already exists; the full Reuse→Create ladder needs a mechanic population first. |
| Vector / semantic retrieval (P11, P13) | §8.3 — revisit at ~150 contracts or ~200 mechanics. |
| Full control room: rollback, approval UI, event chains (P14) | Item 8 covers the minimum. |
| Source / tool introspection | Real gap found by the cold-model test, but it blocks the agent writing docs *about the system*, not playing. |
| Schema validation of component data | `commit(kind: "component")` accepts a JSON Schema and stores it as documentation. Enforcing it is a nice-to-have that would slow every write. |
| Postgres | §8.3, with named triggers. |

---

## Open questions that block MVP items

**Answered:** *do mechanics belong to a campaign or to a shared ruleset?* — a scope column on the
mechanic, empty meaning shared. Retrieval prefers an exact scope match and always includes shared
rules. That is the whole of the inheritance the MVP needs, and unlike a real chain it can be
removed later if it turns out to be wrong.

- **What is the closed display vocabulary for view specs?** TravelRoleplay's 13 display hints are
  the starting point but were derived for a record model, not entity-component. Blocks item 8.
- **What may `procedure.system.create-feature` actually touch?** Blocks the agent modifying its
  own source, which is M2, not MVP — but worth settling before it comes up.

---

## Housekeeping

- ✅ Removed superseded `_to_delete/` code and stopped tracking local/generated artifacts
- ⬜ Pin a newer `SQLitePCLRaw.bundle_e_sqlite3` (CVE-2025-6965, warning only)
- ✅ `_packages/`, `_src.tgz`, output logs, tool secrets, and the development database are ignored
- ℹ️ **This container has no NuGet access.** Any new package has to be restored on your machine and
  copied into `_packages/`, which is where Jint and Acornima came from. Worth remembering before
  the next dependency, rather than rediscovering it as a confusing restore failure.

---

## Full backlog

The complete 21-item list lives in `ARCHITECTURE.md` §6; the milestone definitions in §10. This
file scopes them to MVP. Items 16, 19, 20 and 21 there are post-MVP by design — the backlog
deliberately puts "actual RPG rules and content" last, because the point is that the game gets
authored in play rather than built in advance.
