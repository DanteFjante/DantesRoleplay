# DantesRoleplay — Architecture Decisions & Requirements

**Status:** design record, pre-implementation. Written 2026-08-16, revised 2026-08-16.
**Supersedes:** the `TravelRoleplay` prototype (kept for reference, not for reuse).

**Revision note.** Two decisions were taken after the original review and overturn parts of it:

- **§4 — stack, overturned twice.** The review recommended all-TypeScript. First the backend
  became **C#** (§4.1; §9.1 records the technical argument of mine that was wrong). Then, on
  2026-08-17, the SvelteKit frontend was dropped too: with a C# backend its rationale had already
  evaporated and had been kept out of inertia. Now **server-rendered HTML with screens as
  declarative view specs** (§4.2).
- **§0.2 / §3.11 — the kernel is minimal and contains no game.** Everything playable is
  JavaScript written at runtime. This shrinks the effect vocabulary (§P9) and sets a size budget
  on the engine.
- **§8.3 — database.** SQLite with FTS5. No Postgres, no pgvector, no embeddings, with named
  triggers for revisiting.
- **§3.6 — a mechanic declares a PROJECTION, not a fetch list.** Names roles, component types and
  fields; the engine compiles it to one query. Anything needing a predicate goes through a named
  engine query. Considered and rejected: per-mechanic SQL views (§3.6a).

This document records the decisions taken in the architecture review, the requirements they
imply, and — marked as such — additional implementation proposals that were not part of that
review. It is meant to be the thing you hand a coding agent instead of a giant prompt.

Provenance markers used throughout:

- **[R]** — decided in the architecture review.
- **[L]** — lesson carried over from TravelRoleplay; each one cost real debugging time.
- **[P]** — proposal added here, not yet ratified. Argue with these.

---

## 0. The premise, in two sentences

> **1.** A persistent RPG simulation kernel where the LLM acts as a dynamic GM and can extend the
> game's mechanics without owning the underlying state machine.

> **2.** The kernel is a *system for the system*. It is deliberately small and readable, and it
> contains **no game**. Every feature of actually playing — dice conventions, checks, combat,
> conditions, classes, spells, rests, death — is JavaScript, written and edited at runtime.

Everything below is downstream of those two sentences. When a decision is unclear, the tiebreakers
are: *does this keep authority over state in the engine, and authority over intent in the model?*
and *could this live in JavaScript instead?* If the answer to the second is yes, it does.

The second sentence is the more demanding of the two, because the pressure is always toward
putting "just this one game thing" in C# where it is easier to write. See §3.11.

---

## 1. The central inversion

**[R] The project is not organised by feature category. It is organised by dependency order.**

The first goal is not "make an RPG engine." It is **"make an AI-operable system that can safely
grow into one."**

This produces two parallel tracks, and the first one is built first:

| Track | Contents | Purpose |
| --- | --- | --- |
| **AI development infrastructure** | Procedure contracts, contract discovery, operation logging, MCP semantics, admin inspection | Teaches the coding agent how to work on this system |
| **RPG runtime** | Entities/components, dynamic definitions, mechanics, effects, events, retrieval, world simulation | The actual game |

The first track is what builds the second. The payoff is that by the time you start adding RPG
features, the agent already has the infrastructure needed to add them *correctly, itself*.

**[R] The first thing coded is therefore not `attack.js` and not `Entity`.** It is
`ProcedureContract`, `ProcedureContractVersion`, `search_procedures`, `get_procedure`, and a
minimal set of development contracts.

**[P] Why this matters more than it sounds.** TravelRoleplay's single most damaging failure was
not a bug — it was that `AUTHORING.md`, the document served to every LLM as the operating
manual, drifted out of date. It listed eight hooks when the kernel had thirteen, and never
mentioned a whole registration verb that had already shipped. The observable symptom was *"the
LLM reports a bug it could have fixed"* and *"the LLM doesn't know it can write features."* The
capability existed; the surface the model read did not say so.

Procedure contracts are the structural fix for that class of failure: the operating manual lives
in the database, next to the thing it governs, is versioned, and is **retrieved rather than
remembered**. That is the whole reason it is Priority 1.

---

## 2. Verdict on the previous system

**[R]** The TravelRoleplay concept was assessed as fundamentally sound. The pipeline —

```
Player intent → LLM interprets intent → LLM selects mechanic → load state from SQLite
→ execute JavaScript rule → structured mechanical result → LLM interprets consequences
→ update persistent world state → narrate result
```

— is preserved. What changes is the **boundaries**, which is where the review found the risk.

**This table is the opening assessment, not the verdict.** Every row marked *Add* or *Major risk*
is answered later in the same review — the right-hand column names where. Read it as a problem
list with its solutions already attached, not as a list of unresolved objections.

| Decision | Initial assessment | Where it is answered |
| --- | --- | --- |
| SQLite persistence | Very good | — |
| JS/TS rules | Very good | — |
| LLM selects mechanics | Good | P11 discovery (metadata + FTS + vectors), replacing filename guessing |
| JS receives current world context | Good, *depending on implementation* | §3.6 — mechanics are pure functions; state arrives as arguments, never baked into generated source |
| JS produces result for LLM | Very good | P8 — standard result envelope |
| Mechanics editable during gameplay | Very powerful | P7 — every edit creates a version |
| LLM creates mechanics dynamically | Good *with constraints* | P12 — Reuse → Parameterize → Compose → Extend → Create |
| **Arbitrary AI-written JS execution** | **Major risk** | **Six mechanisms together — see below** |
| Conversation as narrative layer | Good | — |
| Database as authoritative state | Should be mandatory | §3.1 invariant |
| Versioned mechanics | Add | P7 |
| Event ledger | Add | P10 |
| Standard mechanic result schema | Add | P8 |
| Transactional action execution | Add | P9 |

### 2.1 How the "major risk" row gets closed

This is the one row worth spelling out, because it is the only *red* entry in the table and the
entire rest of the architecture is the answer to it. Six mechanisms, each independently useful,
which together mean AI-written JavaScript is no longer arbitrary:

1. **It cannot reach anything.** The mechanic sees only the injected `ctx` (§P8). No filesystem,
   no network, no database handle, no host types.
2. **It cannot mutate state.** It *returns effects*; the engine validates and applies them (§3.3).
   A malicious or broken mechanic can propose nonsense, and the engine rejects it.
3. **It cannot be non-deterministic.** No `Math.random()`; only `ctx.dice.roll()`, which is
   recorded (§3.5). Every execution is replayable.
4. **It cannot partially apply.** Everything runs in a transaction (§P9).
5. **It cannot silently become the truth.** Every version is stored and every event records the
   version that produced it (§P7), so any behaviour is attributable and reversible.
6. **It cannot run unbounded.** Execution limits — wall-clock, memory, statement count, recursion
   depth — are set by the host (§4.3).

Plus the human layer: new mechanics default to `draft` as the MVP's experimental state (§P12), and the control room can
`disable` / `rollback` / `approve` / `deprecate` any of them (§P14).

**The concept is not redesigned. The interfaces around it are hardened.**

---

## 3. Governing invariants

These are the rules that do not bend. Each should be enforced in code, not merely documented —
that distinction is itself one of the decisions.

1. **[R] SQLite is authoritative for facts.** The LLM's conversation context is *never*
   authoritative state. Where they disagree, SQLite wins. Conversation history is narrative
   context, not game state. This is the direct answer to gradual state drift, which is the
   biggest problem in LLM-run campaigns.
2. **[R] The model decides what is attempted; the rules engine decides what mechanically
   happens.** Kept strict. The rule system answers mechanical questions, never storytelling
   questions.
3. **[R] Generated mechanics propose changes. The engine applies changes.** A mechanic never
   mutates state directly — it returns effects, and the engine validates and applies them.
   *Enforced in code, not documented.*
4. **[R] Mechanics never execute SQL.** All data access goes through the injected context API.
   This buys reproducibility and an audit trail.
5. **[R] No non-determinism inside mechanics.** `Math.random()` is not available; `ctx.dice.roll("1d20")`
   is. Every roll is therefore recorded and replayable.
6. **[R] JavaScript is static logic receiving dynamic data.** Never generate JS that contains
   the current state baked into it. State arrives as arguments.
   **[P] Strengthened: a mechanic is a pure function.** It declares up front what data it needs;
   the engine materialises exactly that, hands it in, and the mechanic returns effects. It never
   reads from the store mid-execution. This is not just tidiness — it is what makes the host
   language irrelevant (see §4.3) and what makes replay from the event ledger exact.
   **[P] And it declares a PROJECTION, not a fetch list** — roles, component types, and optionally
   the fields within them. See §3.6a; this is what stops "hand it the whole world" from being the
   only option.
7. **[R] Actions are transactional.** `BEGIN → execute mechanic → validate effects → apply
   effects → persist result → COMMIT`. A failure leaves nothing half-applied.
8. **[L] A guarantee you state but do not enforce is worse than no guarantee.** TravelRoleplay's
   `Transact(Action work)` was literally `work();` with a TODO, while every document claimed
   rollback. Do not ship the claim before the mechanism.
9. **[P] Every capability is discoverable from inside the system.** If a thing can be done but
   no contract, tool description, or `describe` output says so, it does not exist. Enforced by
   test (see §7.9).
11. **The kernel contains no game vocabulary.** The C# knows how to store things, run JavaScript
    safely, apply effects atomically, route events, and record what happened. It does not know
    what a hit point is. Its entire irreducible job is six items:

    | # | The kernel's job | Not the kernel's job |
    | --- | --- | --- |
    | 1 | Persist and query | Knowing what is worth persisting |
    | 2 | Run JavaScript in a sandbox | What the JavaScript says |
    | 3 | Validate and apply effects in a transaction | Which effects a situation calls for |
    | 4 | Route events to subscribers, with loop guards | What events mean |
    | 5 | Expose the MCP and HTTP surface | Game-specific tools |
    | 6 | Record every operation | Judging operations |

    **The test, and it is a real one:** grep the engine for `attack`, `damage`, `initiative`,
    `spell`, `hitpoints`, `skill`, `level`, `condition`. If any appears outside a comment,
    something has leaked into the wrong layer. Write it as an actual failing test in
    `DantesRoleplay.Tests`, alongside the §7.9 drift test.

    **Size budget [P]:** if `DantesRoleplay.Engine` passes roughly 3,000 lines, treat that as a
    signal rather than a limit — the next feature almost certainly belongs in JavaScript. A kernel
    you can read in one sitting is the whole point; it is what makes supervising AI-written
    mechanics tractable.

12. **[P] The maintainer must be able to read every line of the engine.** This is a real
    architectural constraint, not a preference. The entire premise of the project is that you
    *macro-manage* AI-written code — reviewing operations, approving mechanics, rejecting bad
    ones. If you cannot fluently read the host language, the supervision layer is decorative and
    the premise collapses. This invariant outranks language uniformity, and it is why §4 chooses
    C# for the engine.

---

### 3.6a How a mechanic asks for data

**Decided 2026-08-17**, closing the open question "what does a `requirements` declaration look
like". The problem it solves: handing every mechanic a large generic context is wasteful and
vague, and each mechanic wants data shaped to itself.

A requirement names **roles, component types, and optionally fields**:

```
actor:    { stats: [str, dex], vitals: [hp] }
target:   { vitals: [hp], conditions: * }
location: { of: actor, components: [terrain] }
```

The engine compiles the whole thing to one query and materialises exactly that. Per-mechanic
tailored data, no over-fetch, and no new database objects.

**Keep the projection dumb: no predicates.** Roles, components, fields, and `of:` to walk one
relation — nothing more. The moment it wants a `WHERE`, that is the signal to use a named engine
query instead. A projection DSL that grows predicates becomes a bad query language.

**The escape hatch is named engine queries** — a small curated set the kernel provides, for
aggregates and "everything within 10 metres". [L] This is not a new invention: TravelRoleplay
reached the same conclusion, recording raw SQL in rules as rejected and named broker queries as
the accepted alternative.

**Considered and rejected: a SQL view per mechanic.** Appealing — precise, expressive, and it
puts the shape next to the mechanic. Three reasons it loses:

1. **SQLite views cannot be parameterised.** `CREATE VIEW` is a fixed SELECT; a mechanic needs
   *this* actor and *that* target. You end up either with a view over everything, which defeats
   the precision, or with stored parameterised SQL, which is not a view.
2. **It couples every mechanic to physical storage.** The table schema is fixed, but
   `component.data` is JSON, so any useful view needs `json_extract(...)` — SQLite dialect plus
   component internals. §8.3 deliberately keeps Postgres open, and the entity-component model
   makes that move *more* likely, since JSONB indexes far better. Every authored view breaks that
   day.
3. **It reverses §3.4.** Read-only softens "mechanics never execute SQL" but does not remove it:
   the AI would be authoring storage-layer code, and the failure mode is a mechanic that works
   until someone changes how a component is stored.

The honest cost of choosing the projection: SQL is more expressive than any DSL designed here.
That is accepted deliberately, and rule one above is the guard against re-inventing SQL badly.

---

## 4. Stack

**Decided 2026-08-16.** The review recommended an all-TypeScript stack; that recommendation was
revisited and **partially overturned**. What follows is the decision actually taken, with the
reasoning for the change recorded so it is not relitigated.

| Layer | Choice | Source |
| --- | --- | --- |
| Engine / backend | **C# (.NET 10), ASP.NET Core** | Decided 2026-08-16, against the review |
| MCP server | `ModelContextProtocol.AspNetCore` | Already scaffolded |
| Dynamic mechanics | **sandboxed JavaScript via Jint** | [R] language, [P] host |
| Persistence | SQLite | [R] |
| Frontend | **ASP.NET server-rendered HTML.** No SPA framework, no build step | Decided 2026-08-17, against the review |
| Screens | **declarative view/page specs stored as data**, rendered by the engine | [L] carried from TravelRoleplay |
| Transport | HTTP; SSE later for live events | [R] |

### 4.1 Why the review's recommendation was overturned — twice

The review argued for Node/TypeScript on one principle: one language means one type system, one
build, and shared DTOs instead of hand-synced ones. That argument is sound in general. It loses
here for two reasons.

**Reason 1 — supervisability is the top-priority requirement (§3.12).** The project's whole
premise is that AI writes most of the code while you manage the architecture. That only works if
you can read what it produced fluently enough to reject bad work. You are a C# developer. An
engine you must translate in your head before you can review it defeats the point of building a
control room in the first place. **A stack you can supervise beats a stack that is theoretically
cleaner.**

**Reason 2 — the technical argument I originally made for Node was wrong.** I claimed the
synchronous-store constraint that dominated TravelRoleplay was inherent to embedding JS in C#,
and that Node would dissolve it. Half of that is right; the conclusion isn't. See §9.1 — the
constraint is dissolved by **§3.6, the pure-function rule**, not by the host language. Once a
mechanic receives its data up front and returns effects, it never calls the store mid-execution,
so there is nothing left to be synchronous *about*. The constraint was a symptom of lazy store
access inside rules, which the new architecture forbids anyway.

### 4.2 The second overturn: no SPA, no build step, no seam

**Decided 2026-08-17.** The review chose SvelteKit when the backend was going to be TypeScript,
for ecosystem alignment. **When the backend became C# that argument evaporated, and the frontend
recommendation was kept anyway — out of inertia, not reasoning.** This section replaces the
cost/mitigation table that used to justify the split, because there is no longer a split to
justify.

What the SvelteKit plan was paying for, to render tables and forms: a Node toolchain, a second
package ecosystem, an OpenAPI-to-TypeScript generator, a generated client that goes stale, and a
CI check to catch it going stale. All of that is now deleted. ASP.NET returns complete HTML pages
with inline `<style>` and `<script>`; the browser gets a document, not an application.

**And a screen is data, not code.** This is the part that matters, and it is not speculative —
TravelRoleplay built it and it worked (§9.8):

- `view(kind, spec)` — how ONE record is drawn, from a **closed vocabulary** of display hints.
- `page(id, spec)` — a whole screen: sections pairing a data source with a view. Optional HTML
  template carrying `[data-section]` slots for layout only.
- The engine resolves a page into one payload and renders it.

**Declarative specs rather than AI-authored raw HTML**, for three reasons:

1. A closed vocabulary cannot be got wrong. Free-form HTML can.
2. `describe` can return the same spec the renderer consumes, so the LLM learns record shapes
   from the thing that draws them — §7.10, which was written from this exact lesson.
3. AI-authored HTML rendered in a browser is an XSS surface. Same trust model as AI-authored
   JavaScript, so not a new category of risk — but a bounded vocabulary shrinks it, and
   **loopback-only stays a hard rule** either way.

The payoff is that the control room becomes extensible by the same mechanism as everything else:
adding a screen is authoring a spec, exactly as adding a mechanic is authoring JavaScript. In
TravelRoleplay a campaign-authored page design overrode the registered one, and deleting the
record restored the original — an LLM redesigning a screen mid-session with no code change.

**What this gives up:** component reuse, reactivity, and the React/Svelte ecosystem for things
like Monaco or graph views. If the control room ever becomes a genuine visual editor, revisit —
and note that a rich editor can be dropped into one page as a script tag without adopting a
framework for the whole site.

### 4.3 The sandbox — and why C# is arguably *better* here

This is where the overturn is not merely acceptable but an improvement, and it bears directly on
the "Arbitrary AI-written JS execution — **Major risk**" row in §2.

**Node's own sandbox is not a sandbox.** The Node documentation states it plainly:

> "The `node:vm` module is not a security mechanism. Do not use it to run untrusted code."

Running AI-generated JavaScript is *precisely* the untrusted-code case. The credible Node answer
is `isolated-vm`, a native module — extra build complexity and a real maintenance dependency.

**Jint has no ambient authority to escape from.** It is a JavaScript interpreter written in C#.
A script sees only what you hand it; there are no host objects in scope by default, and CLR
access is opt-in (`AllowClr()` — leave it off, permanently). It also ships the execution limits
this architecture wants, as first-class options:

| Concern | Jint option |
| --- | --- |
| Runaway loop | `MaxStatements(...)` |
| Long execution | `TimeoutInterval(...)` |
| Memory exhaustion | `LimitMemory(...)` |
| External abort | `CancellationToken(...)` |
| Deep recursion | call-stack / recursion limits |
| Custom rules | derive from `Constraint` |
| **Host access** | **`AllowClr()` — never enable** |

Jint is at v4.x and tracks ECMAScript through 2025, so the mechanic-authoring experience is
modern JavaScript, which is what matters for the LLM writing it.

**[P] Recorded for later:** if Jint's interpreter performance ever becomes the bottleneck,
**ClearScript** (V8 hosted in .NET) is the escalation path. Do not start there — Jint's weaker
performance is irrelevant at prototype scale, and its "nothing is reachable unless you pass it
in" model is the safer default.

**[L]** This does mean TravelRoleplay's host was not the mistake. The mistake was letting rules
call the store lazily from inside the sandbox. Keep Jint; change the calling convention.

### 4.4 The web app is still only a window

**[R] Authority never moves to the presentation layer.** This survived the change of technology
unaltered: state, rules, validation and transactions live in the engine. The difference is that
the window is now rendered by the same process rather than fetched by a separate one.

This also keeps the frontend decision **cheap to reverse**. Because screens are specs and the
engine still exposes `/api`, putting a SPA in front later is an additive change, not a rewrite.

### 4.7 ✅ Resolved: the repo scaffold is correct

The existing `DantesRoleplay.MCPServer` (.NET 10, `ModelContextProtocol.AspNetCore`) is the right
starting point and is kept. `DantesRoleplay` becomes the engine class library. Delete only the
stock `RandomNumberTools` sample.

Three conditions attach to this decision. If any is dropped, revisit it:

1. **Mechanics are pure functions** (§3.6). No lazy store access from inside Jint. This is the
   condition that makes the host language a non-issue; without it, the sync constraint returns
   exactly as it did in TravelRoleplay.
2. **Cross-boundary types are generated, never hand-written** (§4.2).
3. **`AllowClr()` is never enabled**, and the Jint execution limits in §4.3 are set from the
   first mechanic that runs, not added later.

---

## 5. Subsystems in dependency order

This is the build order. Each entry states what it is, why it is at this position, and what
"done" means.

### P1 — Procedure-contract system

**[R]** First, because every later subsystem describes its own usage through it.

Three concepts only:

```
ProcedureContract
ProcedureContractVersion
```

A contract contains at least: `Id`, `Name`, `Category`, `Description`, `Status`, `Version`,
`Instructions`, `Constraints`.

Worked example:

```
procedure.system.modify

Purpose:
  How the AI should modify the application.

Steps:
  1. Inspect the relevant subsystem.
  2. Retrieve its governing contracts.
  3. Prefer extending existing abstractions over adding parallel ones.
  4. Preserve backward compatibility unless intentionally changing it.
  5. Add or update tests.
  6. Record what changed.

Constraints:
  - Never bypass persistence APIs with arbitrary SQL.
  - Never modify core invariants without an explicit architecture decision.
```

MCP surface:

```
search_procedures(query)
get_procedure(id)
list_procedure_children(id)
```

**[R] No embeddings initially.** Categories/tags + full-text search. Vector retrieval is P13,
and only once there is enough content for retrieval to mean anything.

The core system instruction can then be one sentence:

> Before performing a system operation, retrieve and follow the relevant procedure contracts.

**Done when:** the LLM can discover the correct procedure for a requested operation. That is the
first test, and it is a behavioural test, not a unit test.

### P2 — Contract hierarchy and bootstrap contracts

**[R]** Define the contracts governing *development itself* before any game mechanics.

```
system                    database                mcp
├── system.inspect        ├── database.read       ├── mcp.create-tool
├── system.modify         ├── database.schema-change   └── mcp.modify-tool
├── system.create-feature └── database.migration
├── system.fix-bug
└── system.refactor       contracts
                          ├── contracts.create
                          ├── contracts.modify
                          └── contracts.deprecate
```

This is the AI-development handbook. The self-referential part is the point: **contracts explain
how contracts are created.**

```
procedure.contract.create

Before creating a contract:
  - search for overlapping contracts;
  - determine its parent;
  - keep it narrow;
  - separate enforceable invariants from prose guidance;
  - add examples when interpretation is ambiguous.
```

Note *"separate enforceable invariants from prose guidance"* — that separation is what later lets
you promote an invariant from advice into a check.

### P3 — Audit trail for AI operations

**[R] Do this very early.** Not chain-of-thought — **observable decisions.**

```
Operation
---------
Id
Timestamp
RequestedIntent
ProcedureContractsUsed
ToolsUsed
Result
Success
```

Example record:

```
Intent:   "Add support for dynamic stats."
Contracts used:
  procedure.system.create-feature v2
  procedure.component.modify v1
Operations:
  inspect schema
  create migration
  update component service
  add test
```

**Why early:** this is what makes macro-management possible. You should be able to inspect what
the AI changed without reading every generated line.

**[L]** TravelRoleplay produced execution traces and then threw them away — they came back inline
from `execute_action` and were never persisted. "Why did that happen?" worked only for the action
you had just run, never for the one from last session. Persist from day one.

### P4 — Stable MCP capability model

**[R]** Once contracts exist, define a small, stable set of MCP operations.

Initial set:

```
procedure.search / procedure.get
system.describe
database.describe_schema
file/source inspection tools
operation.history
```

**[R] Generic escape hatches must not be the main interface:**

```
execute_sql          ✗
execute_shell        ✗
write_arbitrary_file ✗
```

They may exist for development, but normal operation goes through **semantic tools**:

```
create_component_definition
create_mechanic
add_subscription
```

Procedure contracts describe how each is used. See **§7** for the low-context design rules that
govern this surface — that is the part this project is actually optimising for.

### P5 — Dynamic entity/component persistence

**[R]** The world-data foundation, built only after the AI knows how to operate the system.

```
Entity   ComponentDefinition   Component   Containment   Relationship
```

**Entity stays tiny** — `Id`, `Name`, `CreatedAt`, `DeletedAt`. Everything game-specific is a
component:

```
Entity: Orban
Components: Character, Stats, Skills, Position, Inventory, Bard
```

Contracts gained here:

```
procedure.entity.create      procedure.component.create
procedure.entity.inspect     procedure.component.modify
                             procedure.component.attach
```

**[L] One place, and only one place, may know the shape of a character sheet.** In TravelRoleplay
that was `dnd5e/derives/actor.js`, and it worked precisely because it was singular. The component
model generalises this — but the rule survives: a component's shape is defined by its
`ComponentDefinition` and nothing else may assume it.

### P6 — Dynamic definitions inside components

**[R]** Definition types:

```
StatDefinition   ResourceDefinition   ConditionDefinition   SkillDefinition
```

This is what lets you add `Luck`, `Sanity`, `Resonance`, `Corruption` **without touching the
database schema.**

```
procedure.stat.create

1. Search existing stat definitions.
2. Determine whether this is really a stat rather than resource/condition.
3. Define identifier, meaning, range and default.
4. Do not modify existing entities unnecessarily.
```

Step 2 is the valuable one — it stops the model from inventing a stat when it needed a condition.

**[L]** TravelRoleplay hardcoded the skill list in a derive while the catalog *already* seeded 18
skill definitions with governing abilities. Two sources of truth, and adding a custom skill
required a code edit — which contradicted the entire premise of the project. Definitions are data
or the premise is false.

### P7 — Mechanic storage and versioning

**[R]** Executable mechanics live **in SQLite**, not on disk.

```
Mechanic   MechanicVersion
```

Every edit creates a new version. MVP lifecycle:

```
draft → active → deprecated → archived
```

The planned `experimental` approval state is represented by `draft` in the MVP because the
surface deliberately does not yet include separate activation or approval operations. New
mechanics therefore default to `draft`; an author must explicitly request `active` before
`commit(kind: "action")` can execute one. A later lifecycle slice may add `experimental` without changing
the append-only storage model.

Contracts:

```
procedure.mechanic.find      procedure.mechanic.activate
procedure.mechanic.create    procedure.mechanic.deprecate
procedure.mechanic.modify
```

**[R] Why versioning matters:** if the AI changes `combat.grapple` in session 20, you must still
be able to know that an event in session 4 ran `grapple v2`. Event history records the version.
Seems unnecessary until the campaign is 100 hours old.

**[L] Storing mechanics in the DB rather than on disk is a genuine change from TravelRoleplay,
and it resolves a real inconsistency there:** base packs were read straight off disk on every
load (so editing a file deployed instantly), while campaign-learned rules went through a
`save_rule` tool. Two deployment models for the same kind of artifact, and the difference was
invisible from inside the game. One store, one path, versioned.

### P8 — Controlled JavaScript runtime

**[R]** Immediately after mechanic storage — dynamic mechanics must never get unrestricted
host/SQLite access. Concretely, given §4.3: a Jint engine with `AllowClr()` off, the limits from
§4.3 applied, and nothing in scope but `ctx`.

Injected context, and nothing else:

```
ctx.dice   ctx.entities   ctx.components   ctx.effects
ctx.events ctx.mechanics  ctx.time
```

Mechanics return a structured result:

```json
{
  "outcome": "success",
  "rolls": [],
  "effects": [],
  "events": []
}
```

**[R] Core invariant, enforced in code:** *generated mechanics propose changes; the engine
applies changes.*

**[R] A common result envelope pays for itself repeatedly:** the UI can show dice rolls, the LLM
can narrate them, the database can persist the effects, debugging can reconstruct what happened,
and a future frontend does not need to understand every custom mechanic.

**[L] Build one sandbox per action and reuse it across resolve and apply.** TravelRoleplay built
two separate engines and reloaded the whole rule chain for each, which re-seeded the RNG — so a
roll inside an effect handler replayed the resolve stream instead of continuing it. Subtle, and
it silently corrupts exactly the thing you were trying to make reproducible.

**[P] Add `ctx.ask` for multi-step actions from the start.** TravelRoleplay shipped a `needsInput`
field in its result shape that nothing ever populated, and documented a `resumeToken` that was
never issued — a documented field that never populates is a trap. The version that eventually
worked was **stateless**: the mechanic calls `ctx.ask(...)`, the engine returns the question, the
caller re-invokes the same action with an `answers` object, and the mechanic re-runs from the top
reading its answers. No resume tokens, no server-side continuation state. Design it that way on
day one or leave the field out entirely.

### P9 — Effects and transactions

**[R]** Before event subscriptions, make mutation robust.

An **effect** is one entry on the kernel's list of permitted changes. A mechanic never writes
anything; it returns a list of effects, and the kernel validates the list and applies it (§3.3).
So "what is on that list" is the exact boundary between kernel and game.

**[P] The vocabulary is purely structural.** The review's draft list included `resource.modify`,
`condition.add` and `condition.remove` — but those are *game* concepts, and §3.11 forbids them in
C#. A condition is just a component; a resource is just a field in one. Removing them makes the
kernel smaller and, more importantly, lets the LLM redefine what a "condition" even is without a
C# change. The list:

```
entity.create           component.add         containment.move
entity.delete           component.set         relationship.create
                        component.merge       relationship.remove
                        component.remove      event.emit
```

That is the whole vocabulary, and it should stay that size. Everything game-flavoured is a
JavaScript helper built on top:

```
"poison the goblin"   →  component.add(goblin, "condition:poisoned", {...})
"spend 3 hit points"  →  component.merge(actor, "vitals", { hp: current - 3 })
```

**The one thing this gives up:** atomic clamping. `resource.modify` in the kernel could guarantee
"never below zero"; as a JS helper, a mechanic could write `hp: -12`. That is an acceptable trade
at prototype scale — and the better fix lives in JavaScript anyway, where a `ComponentDefinition`
can declare a range that a shared helper enforces. Revisit only if effects start bypassing the
helpers in practice.

Transactional processing:

```
BEGIN
  execute mechanic
  validate effects
  apply effects
  persist result
COMMIT
```

The worked failure case: a rule deducts 20 gp, attempts to add a sword, and crashes. Without a
transaction, Orban has lost 20 gp and received nothing.

**[L] Two effect-semantics traps, both of which caused real bugs:**
- Decide explicitly whether `component.set` **replaces or merges**, then name it accordingly.
  TravelRoleplay had an entry-patch operation that replaced `data` wholesale while reading like a
  merge, so partial patches silently wiped adjacent keys.
- An upsert that always creates is not an upsert. If a component is a singleton, the effect
  vocabulary must make that impossible to get wrong, rather than relying on callers to
  list-then-patch.

### P10 — Event system

**[R]** Only once effects work properly.

```
Event                EventSubscription
-----
EventId, Type, CorrelationId, CausationId, ActorId, TargetId, Payload, Timestamp
```

`CorrelationId` and `CausationId` are what make a chain reconstructable rather than a pile of
rows.

Processing:

```
Effect applied → Event generated → Subscribers found → Reactive mechanic executed
→ More effects → More events
```

**[R] Loop protection is added immediately, not later:** max depth, max events per action, max
same-subscriber executions per action.

Contracts: `procedure.event.define`, `procedure.subscription.create`,
`procedure.subscription.modify`.

**[L] Event types must be a registry, not ad-hoc strings.** TravelRoleplay emitted events as bare
strings, so a subscription with a typo in its trigger was simply never invoked — silently, with
no error, forever. A registry makes a bad subscription **fail loudly at creation time**. This is
cheap and it is the single highest-value guardrail in the event system.

**[P] The event ledger is also the memory system.** Besides current state, the ledger stores what
happened — and that is what lets the model answer *"what does this NPC remember about us?"*
without rereading forty sessions of chat. Design the ledger's query surface with that use case in
mind, not just debugging.

### P11 — Mechanic discovery

**[R] Only now** does vector retrieval earn its place; before this there is not enough content
for it to be meaningful.

Index: `name`, `description`, `intents`, `tags`, `requirements`, `outputs`, `examples`.

Retrieval combines: **metadata filters + full-text + vector similarity.** Return top candidates.

**[R] Vector distance must never directly decide rule creation.** It returns candidates; the LLM
decides.

```
"hold him to the ground" → combat.grapple
```

**[L] The prerequisite nobody expects: fix tokenisation before adding semantics.**
TravelRoleplay's intent matcher had *no stopword list*. Phrases containing bare function words
collided — "talk to X" tangled with movement intents because `to` was a scored token. A test
enforcing zero intent collisions across shipped rulesets was worth more than any amount of
ranking cleverness. Ship that test with P11.

**[L]** Semantic search for *scripts* was explicitly rejected at ~31 actions as not being a
semantic-search problem. Revisit past roughly 200. Do not build P11 early because it is
interesting.

### P12 — Mechanic-growth controls

**[R]** This is where rule explosion gets handled, and it was called out as **one of the most
important contracts in the entire system.**

Track per mechanic: `times considered`, `times executed`, `last executed`, `creator`, `status`.
Store previous successful intent mappings so a phrase that worked once resolves the same way again.

New mechanics default to `experimental`.

The governing hierarchy the contract enforces:

```
Reuse → Parameterize → Compose → Extend → Create
```

Create is the **last** resort, not the first.

**[L] Make the guard structural, not advisory.** TravelRoleplay's anti-sprawl design rested
entirely on the LLM remembering to call `match_intent` before authoring — a soft instruction in a
tool description. Creation should itself run the new mechanic's intents against the registry and
**return a warning (or refuse) on collision.** An instruction the model can skip is not a control.

### P13 — Dynamic procedure retrieval

**[R]** Semantic retrieval over the *contracts*, once there are enough of them. The LLM can then
express a need conceptually —

```
"I need to add a new reactive curse mechanic."
```

— and retrieve:

```
procedure.mechanic.create
procedure.subscription.create
procedure.component.create
```

At this point the application is substantially self-documenting.

### P14 — Admin / control-room UI

**[R] Not postponed to the end** — but the useful version depends on the subsystems existing.

> The most useful UI is not a polished RPG interface. It is a **control room.**

Dashboards for: recent AI operations, procedure contracts, entities/components, mechanics,
mechanic versions, events, event chains, subscriptions, failed operations, experimental mechanics.

For each AI operation, show: what was requested, which procedures were selected, which mechanics
were selected, what changed, which events fired, whether errors occurred.

Actions available: `disable`, `rollback`, `edit`, `approve`, `deprecate`.

**[R] The first screen built is the Procedure Contracts browser** — it matches the development
order. The AI operation inspector comes immediately after. Those two screens are what let you
supervise everything else the AI generates.

**[R] The initial UI should be intentionally ugly.** Sidebar only: Procedures, Operations,
Entities, Mechanics, Events.

**[L] Built as view specs, not as pages of code** (§4.2, §9.8). Adding a screen is authoring a
spec — the same operation as adding a mechanic, which is the whole thesis applied to the UI.

Eventual shape:

```
Campaign
├── Play
├── World      → Entities, Components, Locations, Relationships
├── Rules      → Mechanics, Versions, Subscriptions, Definitions
├── Agent      → Procedure Contracts, Operations, Retrieval
└── Debug      → Events, Event chains, Effects, Logs
```

---

## 6. Recommended implementation backlog

**[R]** In order:

1. Procedure contract persistence + MCP retrieval
2. Contract hierarchy / versioning
3. Operation / audit log
4. Core development contracts
5. Basic admin contract viewer
6. Entity/component persistence
7. Containment / relationships
8. Dynamic definitions such as stats
9. Mechanic + mechanic-version persistence
10. JS execution sandbox / context
11. Structured effects
12. Transactional action processor
13. Events + subscriptions
14. Event-chain safety
15. Mechanic metadata / search
16. Vector retrieval
17. Reuse / composition / create decision workflow
18. Usage tracking and mechanic lifecycle
19. Semantic procedure retrieval
20. Full control-room UI
21. **Actual RPG rules and content**

The ordering deliberately postpones the fun part. That is the trade: when you finally start
adding RPG features, the AI already has the infrastructure to add them correctly itself.

---

## 7. MCP surface design for low-context operation  **[P]**

This section is the project's stated focus and was not covered in the review. **The requirement
is that an LLM with almost no context can use this system correctly on its first call.**

**7.1 — Hard tool budget: 12 tools, permanently.** New capability is a new *procedure* or a new
*semantic operation behind an existing tool*, never a new tool. Tool descriptions are loaded into
every conversation whether used or not; procedures are loaded on demand. This is the single
highest-leverage rule here.

> **Superseded 2026-08-18 — the budget is 3.** The twelve were collapsed into `orient`, `query`
> and `commit`, each taking a closed `kind` enum; `VERB_MIGRATION.md` is the implementation record
> and `VERB_HISTORY.md` maps the old names. The rule above was right and this is the same rule
> applied harder: twelve descriptions were still twelve descriptions loaded every time, and the
> distinctions between them (`find_procedures` vs `get_procedure`, `find_mechanics` vs
> `run_action`) were navigation decisions a weak model had to get right before it could do
> anything. What the twelve names carried is now data — a flat, bounded list of kinds a session
> reads once from `query(kind: "capabilities")` — instead of prompt. The corollary is new: growth
> happens in kinds, and a kind costs ~10–20 tokens in a catalog rather than a description in every
> conversation. Sections 7.2–7.10 are unchanged and still hold.

**7.2 — One obvious entry point.** A single `orient()` (or `start_here()`) that is cheap,
idempotent, and returns: the one-sentence premise, the current campaign digest, and the three to
five calls that make sense right now. A model that has lost the thread should always have exactly
one right move.

**7.3 — Every result carries `nextSteps`.** Literal, callable suggestions — not prose. The
envelope is uniform everywhere:

```
{ ok, data, effects, events, nextSteps, trace, operationId }
```

Uniformity means the model learns the shape once.

**7.4 — Errors are instructions.** Every failure returns `code`, `why`, and `fix` — where `fix`
names the exact next call. **[L]** TravelRoleplay did this well and it should be carried over
verbatim; it was the difference between a model recovering by itself and a model giving up.

**7.5 — Progressive disclosure over enumeration.** One `describe(topic)` rather than a dozen
`list_*` tools. The topic list is discoverable from `orient()`.

**7.6 — Descriptions are budget-managed.** Total tool-description tokens stay under a fixed
budget (suggest 2,000). Add a test that fails the build when it is exceeded. This forces 7.1 to
be obeyed rather than admired.

**7.7 — No tool requires knowing another tool's output shape** beyond opaque IDs. Tools compose
by ID, never by structure.

**7.8 — Every write tool accepts `dryRun`.** Combined with the effects model, this gives the LLM
a safe way to check its understanding before committing — and gives you a safe way to review.

**7.9 — The drift test.** An automated test asserts that **every registered capability appears in
the operating surface**: every semantic operation, every event type, every effect type, every
`ctx.*` member is either present in a contract or explicitly marked internal. **[L]** This is the
structural fix for the TravelRoleplay failure described in §1. Without this test, the rule
"update the docs in the same change" is a promise, and promises rot.

**7.10 — `describe` returns the same shape the renderer consumes.** **[L]** A late TravelRoleplay
insight worth carrying: when the self-description endpoint returns the *same* record spec the UI
draws from, the LLM learns record shapes from the thing that renders them, and the two cannot
drift. Design view specs and description output as one artifact.

---

## 8. Solution layout

**Five projects.** Revised 2026-08-16 after the first slice was built — the original three-project
plan was overruled in favour of an explicit layering. The dependency direction is the load-bearing
part: **arrows point inwards, and the core has no package references at all.**

```
DantesRoleplay.MCPServer  ─┐
DantesRoleplay.Tests      ─┼──▶ DantesRoleplay.DataAccess ──▶ DantesRoleplay
DantesRoleplay.RuleAccess ─┘                                        ▲
                                                                    │
                                        (core: domain types + interfaces, no packages)
```

### 8.1 The projects

**`DantesRoleplay`** — the core. Domain types and the interfaces everything else talks to.

```
Procedures/    ProcedureContract, ...Version, ...Relation, models, IProcedureStore
Operations/    Operation, IOperationLog
World/         Entity, ComponentDefinition, Component, Containment, Relationship,
               models, IWorldStore                                          P5–P6
../catalog/procedures/ non-ruleset *.md — the seeded operating manual, embedded resources
```

**[P] The core has zero PackageReferences, and that is a rule rather than a coincidence.** If one
is ever needed here, something belongs in DataAccess or RuleAccess instead. It is the cheapest
possible enforcement of "the kernel does not depend on its plumbing".

**`DantesRoleplay.DataAccess`** — the only project that knows a database exists (§3.4).

```
DantesRoleplayDbContext.cs      every table, every index, every FK
ProcedureStore.cs               P1
OperationLog.cs                 P3
WorldStore.cs                   P5–P6
Bootstrap/ProcedureFile.cs      markdown front-matter parser
Bootstrap/ProcedureSeeder.cs    files → database, idempotent by content hash
DataAccessServiceCollectionExtensions.cs   registration + provider switch
DesignTimeDbContextFactory.cs   so `dotnet ef` runs without the host
```

**`DantesRoleplay.RuleAccess`** — the Jint sandbox (P8). Empty until the runtime lands. This is
the one boundary here with a security argument behind it: an assembly whose entire job is running
untrusted AI-written JavaScript.

**`DantesRoleplay.MCPServer`** — the ASP.NET Core host. `/mcp` for the LLM, `/api` for the control
room, one process. **[P] Holds no logic**: parse arguments, call the engine, shape the envelope.

**`DantesRoleplay.Tests`** — xunit, in-memory SQLite per test.

### 8.2 Procedure contracts: authored as files, stored in the database

**Decided 2026-08-16; consolidated 2026-08-20.** Contracts are written as markdown with flat front
matter under `catalog/procedures/`. The non-ruleset contracts are embedded into the core assembly
under the bootstrap resource name and seeded into the database at startup. Seeding is idempotent
by content hash, so restarts write nothing until a file changes. There is no mirrored authored
copy under `DantesRoleplay/Bootstrap/`.

Why both rather than one:

| Files alone | Database alone | Hybrid |
| --- | --- | --- |
| Editable, diffable in git | The LLM can revise at runtime | Both |
| No version pinning | No editor authoring | Version history *and* an editor |
| No operation → revision link | — | Every operation names the exact revision in force |

**[L]** Files alone would also recreate TravelRoleplay's split, where base rules lived on disk and
learned rules lived in a store — two deployment models for one kind of artifact, invisible from
inside the game. Here the database is the single runtime source during play; repository files are
the development source and the reviewed input to installation.

### 8.3 Database: SQLite, and **not** Postgres yet

**Decision: SQLite. Do not install Postgres/pgvector for this project.** The reasoning, because
this looks like the kind of choice that is cheaper to make early:

1. **The vector database solves a problem that does not exist yet.** The review was explicit that
   P1 needs *no embeddings* — categories, tags and full-text search are enough. With twenty or
   forty procedure contracts, an LLM does not need semantic retrieval; it needs a **list**.
   `list_procedures()` returning forty names and one-line descriptions is roughly 600 tokens,
   fully deterministic, and has no recall failure mode. Embedding search over forty documents is
   strictly worse — it can miss, and you cannot tell when it did.
2. **Vectors are not free infrastructure.** They need an embedding model, which means either an
   API dependency (network, key, cost, latency, and it must be reachable from wherever the engine
   runs) or a local ONNX model to ship and load. FTS5 needs none of that and is already in SQLite.
3. **A server contradicts §3.11.** SQLite is one file: copy it to snapshot a campaign, delete it
   to reset, open it in any viewer to see exactly what the AI did. For a prototype whose defining
   requirement is "small and easy to get through", that matters more than it sounds.
4. **You would be embedding the wrong thing.** At P13 you would learn what retrieval actually has
   to match; committing to an embedding strategy before there is content to retrieve is how you
   end up indexing the wrong field.

**When to revisit** — any one of these is a genuine trigger:

- Procedure contracts pass roughly 150, and `list_procedures()` stops fitting comfortably in
  context.
- Mechanics pass roughly 200 (the number the review itself named for revisiting semantic search).
- The project stops being single-user or single-machine.

**The migration path is short, and only because the architecture already forces it.** §3.4 says
mechanics never execute SQL, so every query in the system is inside `Engine/Database/`. Changing
engines is a change to one folder. Two options at that point: `sqlite-vec`, which keeps the
one-file property, or Postgres + pgvector if you have outgrown single-machine anyway. Decide then,
with real data.

**[P] Two rules keep the door open**, and both are already implemented:

1. No SQLite-specific SQL outside `DantesRoleplay.DataAccess`, and no SQLite types in any
   signature the rest of the solution sees.
2. The provider is an explicit parameter, not an assumption:
   `AddDantesRoleplayDataAccess(connectionString, DatabaseProvider.Sqlite | .Postgres)`. The
   Postgres branch currently throws with the package name to add, rather than silently falling
   back — switching should fail loudly and early, not work by accident.

Note that the entity-component model raises the value of Postgres specifically: all game data is
JSON in `component.data`, and JSONB indexes that far better than SQLite's json1. That is the most
likely reason this decision gets revisited.

---

## 9. Constraints and lessons carried from TravelRoleplay  **[L]**

Beyond the ones inlined above.

**9.1 — The synchronous-store constraint: correctly diagnosed, wrongly blamed.** TravelRoleplay's
store interface had to be synchronous because rules ran inside a JS sandbox embedded in a C#
request pipeline, which had no `await`. That one constraint cascaded: no network service was
reachable from a rule, any index had to be in-process, and every "call an API from a rule"
proposal had to be routed through the host instead.

The obvious conclusion — *"therefore do not embed JS in C#"* — **is wrong, and this document
originally drew it.** The constraint did not come from the host language. It came from
**mechanics reading the store lazily, mid-execution.** That is the thing that needs an `await`
in the middle of a script.

Remove the lazy read and the constraint evaporates:

```
declare requirements  →  engine materialises data (async, in C#, freely)
                      →  sandbox runs a pure function over plain data (sync, trivially)
                      →  mechanic returns effects
                      →  engine validates + applies (async, in C#, freely)
```

The sandbox is only ever entered with everything it needs already in hand, so there is nothing
left inside it that *could* be asynchronous. All the async work happens on the C# side of the
boundary, where async is native.

**This is why §3.6 is an invariant and not a style note.** It is load-bearing for the §4 stack
decision. Note that the "requirements" field is *already* in the mechanic metadata specified in
P11 — it was there for search. It does double duty as the data-dependency declaration.

**9.2 — Registration is data.** The thing that most clearly worked. Because everything registered
through a small set of verbs, self-description fell out for free and could not drift from reality
— there was no second inventory to maintain. **Keep this property.** In the new design,
"registration is data" becomes "definitions, mechanics and contracts are rows," which is strictly
stronger.

**9.3 — Failure must be safe by construction, not by convention.** Resolve *physically could not*
write, because the guard lived in the kernel. An unregistered effect type, a bad input or a
thrown rule aborted before anything applied. Reproduce this: validation before application, in
the engine, not in the mechanics.

**9.4 — Build a reference host that runs the real mechanics against a mock database.** In
TravelRoleplay this ran the identical rule files outside the app and caught three real bugs
during development, including a wrap-API flaw that would have made every override silently do
nothing. It is worth more than it looks: it is how you debug a mechanic without booting the
world.

**9.5 — Name collisions with reserved concepts bite.** A `recordType: 'clock'` was already taken
by a time-of-day singleton, so a later feature had to use `clock-front`. Reserve and validate
identifier namespaces in `ComponentDefinition` from the start.

**9.6 — Two sources of truth for the same quantity will diverge.** Authored travel distance
versus geometric distance from coordinates was resolved by a precedence rule: *authored always
wins; geometry only when nothing is authored.* Any derived quantity needs that decision made
explicitly and written down, or a future editor silently changes gameplay.

**9.7 — Composition beats duplication, and it needs engine support.** A "move the whole party"
action had to duplicate reachability checks and distance maths, because calling the single-actor
move per actor triggered the travel-time wrapper once per actor — the exact bug it existed to
fix. The fix is an engine primitive for *running another mechanic and receiving its effects
unapplied*, so the caller chooses what to keep. **[P] Put this in the `ctx.mechanics` API from
the start** (with a depth cap and cycle check), and make de-duplicating a case like this the
acceptance test for it.

**9.8 — The screen can be data, and it was proven.** TravelRoleplay drove its entire site from
the ruleset: `view(kind, spec)` for how one record draws from a closed set of 13 display hints,
`page(id, spec)` for a screen as sections pairing a data source with a view, an optional HTML
template with `[data-section]` slots for layout, and a `page.design` action that saved a campaign
layout **overriding** the registered page — deleting the record restored the original. 220 tests;
all seven pages rendered in headless Chromium with zero console errors, including a nested
inn → strongroom → chest → items chain.

Two details worth carrying over. **An inventory was not a new kind of thing** — it was a container
item, so nesting came free; the same reasoning applies to containment here. And the container view
**deliberately showed one level**: a nested chest is a link to another screen, not a recursive wall
of cards. Depth limits in a renderer are a feature.

---

## 10. Milestones

**[P] M0 — infrastructure only.** Added because the first real question is not "what can the game
do" but *"can the coding agent reach the system at all."* Until an LLM can connect, read, and
write, every later milestone is theoretical.

The thinnest slice that is genuinely useful:

| Piece | Scope |
| --- | --- |
| SQLite + migrations | `Engine/Database/` — one file, created on first run |
| Schema | `ProcedureContract`, `ProcedureContractVersion`, `Operation` |
| Retrieval | `list_procedures()`, `search_procedures(query)` over **FTS5** — no embeddings (§8.3) |
| Writes | create / update a contract, producing a new version |
| Audit | every call writes an `Operation` row (§P3) |
| MCP host | `/mcp` reachable from a local client, five or six tools, uniform envelope (§7.3) |
| Seed | the embedded non-ruleset contracts from `catalog/procedures/` |

**No entities, no components, no mechanics, no sandbox, no events, no web app.**

**M0 is done when:** from a fresh MCP session with no prior context, an LLM can call one tool,
learn what the system is, find the right procedure, write a new contract, and you can see the
operation recorded afterwards. That is also the first real test of §7 — if a fresh model flounders
here, the surface is wrong, and it is far cheaper to discover that now than after the RPG runtime
exists.

**[P] The compounding reason to do this first:** once M0 works, the system is usable *for building
itself*. Contracts written during M0 are how the next milestone gets specified — you stop
describing file edits and start writing procedures the agent retrieves. Every hour after M0 is
leveraged.

---

**[R]** Milestone 1 is deliberately small, and it is *not* a game.

> **M1.** An LLM can use MCP to inspect the system, retrieve the correct development procedure,
> perform a controlled modification, and leave an auditable record of what it did.

```
User: "Add a simple notes subsystem."

LLM → search_procedures("add feature")
    → procedure.system.create-feature
    → inspect system
    → perform modifications
    → record operation
    → return summary
```

Once that works you have something useful before the RPG runtime exists at all.

**M2.** Ask the AI: *"Add the dynamic entity-component subsystem according to our architecture."*
Instead of a giant prompt describing every file edit, give it the architecture and let it retrieve
`procedure.system.create-feature`, `procedure.database.schema-change`, `procedure.mcp.create-tool`.
**The project starts exercising its own operational architecture during development.**

**M3.** The AI builds `Entity`, `ComponentDefinition`, `Component`, `Containment`. Then test:
*"Create a character named Orban with Strength and Luck."* Then: *"Add a new stat called Resonance
without changing the database schema."* Clean pass = the dynamic data model is proven.

**M4.** Mechanics. *"Add a generic ability-check mechanic."* Then *"Add a Luck check without
changing the engine's C# code."* Success here — Luck definition + generic mechanic + dynamic entity
state — proves the core architectural premise.

**M5.** Events. *"When an entity takes damage while poisoned, trigger poison damage."* Verify the
full chain `attack → damage → event → poison subscriber → secondary damage` **with a complete
audit trail.**

**M6.** Self-extension. Player says: *"I try to tie the goblin's hands."*

```
retrieve action procedure → search mechanics → evaluate candidates
→ determine missing reusable behavior → retrieve mechanic-creation procedure
→ create experimental mechanic → validate → execute → persist
```

Then: *"Tie another goblin's hands."* It must **find and reuse** the mechanic it just made.

**[R] M6 passing is the definition of prototype success.**

---

## 11. Open questions

1. ~~C# or TypeScript~~ — **resolved 2026-08-16**, see §4. C# engine. The SvelteKit half was
   later dropped too (§4.2, 2026-08-17): server-rendered HTML, no seam to generate.
2. ~~What does a mechanic's `requirements` declaration look like?~~ — **resolved 2026-08-17**,
   see §3.6a. A projection over roles / components / fields, with named engine queries as the
   escape hatch. Per-mechanic SQL views were considered and rejected, with reasons recorded.
3. **Does the campaign own its mechanics, or does a mechanic belong to a ruleset shared across
   campaigns?** TravelRoleplay had base packs plus a per-campaign learned layer. The new design
   puts everything in SQLite — decide whether that is one table with a scope column or a genuine
   inheritance chain, because retrieval and promotion both depend on the answer.
4. **What exactly is `procedure.system.create-feature` allowed to touch?** M2 depends on the
   agent being able to write source files. That is the one place a broad escape hatch is genuinely
   needed, and it deserves its own constraint list.
5. **Does the player-facing play UI exist in the prototype at all**, or is MCP the only client
   until the control room is done? §P14 says the control room comes first; worth stating that the
   play surface is explicitly out of scope for M1–M4.
6. ~~Vector store choice~~ — **resolved 2026-08-16**, see §8.3. SQLite + FTS5, no vector store, no
   Postgres. Named revisit triggers rather than a date.
7. ~~Which OpenAPI-to-TypeScript generator~~ — **moot as of 2026-08-17**. There is no generated
   client, because there is no separate frontend (§4.2).
8. **What is the closed display vocabulary for view specs?** TravelRoleplay settled on 13 display
   hints; that is the obvious starting point but has not been re-derived for this project's
   entity-component model. Needed before P14.

---

## Appendix — the two sentences worth remembering

> Build the infrastructure that teaches the coding agent how to work first; then use that
> infrastructure to progressively build the RPG engine itself.

> The first thing to code is not `attack.js` and not `Entity`. It is `ProcedureContract`,
> `ProcedureContractVersion`, `search_procedures`, `get_procedure`, and a minimal set of
> development contracts — the mechanism through which you stop micromanaging the implementation
> and start managing the architectural rules the coding agent operates under.
