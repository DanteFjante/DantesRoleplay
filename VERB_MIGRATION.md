# Migration: twelve tools → three verbs

Written 2026-08-17 by Claude Fable 5, from a design review of the MCP surface. This is the
implementation procedure for collapsing the tool surface to `orient`, `query`, `commit`. Every
decision an implementer would otherwise have to make is pinned below — follow it as written, and
raise a decision rather than improvising when something does not fit.

**Scope:** `DantesRoleplay.MCPServer`, the guard/smoke tests, and the bootstrap contracts.
**Non-goal:** no kernel change. `IProcedureStore`, `IWorldStore`, `IMechanicStore`,
`IEffectApplier`, `IActionRunner`, `IOperationLog` and the envelope are untouched.

---

## The pinned decisions

**D1 — Exactly three tools.** `orient`, `query`, `commit`. Nothing else appears in `tools/list`.
The tool-budget guard test changes from twelve to three.

**D2 — Query kinds are a closed enum, flat, no hierarchy.**
`capabilities | procedures | world | entities | mechanics | history`
There is no singular/plural pair: passing `id` to a plural kind returns the one full record
(`query(kind: "procedures", id: "...")` is today's `get_procedure`). The enum goes in the JSON
schema itself, not only in prose, so an invented kind is a protocol-level validation failure.
Do NOT add subcategory layers — bounded lists are cheaper flat (see the layering rule at the end).

**D3 — Commit kinds are a closed enum.**
`procedure | component | effects | mechanic | action`

**D4 — Commit takes one `payload` argument: a JSON object as a string.** The per-kind shapes are
exactly today's tool parameters, moved into an object (table below). The shapes are returned by
`query(kind: "capabilities")` and — critically — echoed IN FULL inside every `INVALID_PAYLOAD`
error, so a model that guessed wrong gets the correct shape in the same round trip instead of a
pointer to it.

**D5 — The old tool classes become the implementation, not the surface.** Keep
`ProcedureTools`, `WorldTools`, `MechanicTools`, `ActionTools`, `HistoryTool` exactly as they
are, attributes and all — but stop passing them to `WithTools<T>()` in `Program.cs`. An
attributed class that is never registered is not exposed; no attribute-stripping is needed. The
new `QueryTool`/`CommitTool` dispatch on kind and call the old classes' public methods directly.
No logic moves; no logic is rewritten. This keeps the diff reviewable and the behaviour
provably identical per kind.

**D6 — `dryRun` is a first-class commit parameter, default false, honoured by every kind that
can honour it.** `procedure`, `mechanic`, `effects` behave exactly as today. `component` has no
check path today: dryRun on it returns `NOT_SUPPORTED` with fix
`commit(kind: "component", payload: {...})` — do not invent a check path in this migration.
`action` likewise returns `NOT_SUPPORTED` explaining that actions dry-run their effects
internally. Neither dry run consumes read evidence (same rule as today).

**D7 — Errors.** `UNKNOWN_KIND` lists every valid kind for that verb. `INVALID_PAYLOAD` includes
the expected shape inline. Every `fix` remains a literal call. All existing error codes survive
unchanged underneath.

**D8 — `governs` format in contracts.** Literal call fragments:
`query(kind: "mechanics")`, `commit(kind: "effects")`. The read-before-write audit keys on these
strings, so the format must be exact and consistent across all contracts.

**D9 — Orient asserts the surface both ways.** Orient's capability section becomes two lists —
query kinds and commit kinds. The guard test asserts each list equals the corresponding
dispatcher's switch cases, in both directions, exactly as the old orient-vs-tools test did.

**D10 — Hard cutover, no aliases.** This is a prototype; the old names simply stop existing.
`history` rows keep old tool names in old rows — that is correct history, do not rewrite it. New
rows record `query`/`commit` as the tool and the kind in the subject where no better subject
exists.

**D11 — Read evidence is preserved.** `query(kind: "procedures", id: ...)` records the read
subject exactly as `get_procedure` does today (`ToolOutcome.OkAbout`). Commits pass
`intent`/`proceduresUsed` through unchanged and consume evidence unless dryRun.

**D12 — Irrelevant query parameters are ignored, and `capabilities` documents which parameters
each kind reads.** Erroring on a harmless extra filter would punish exploration; the
capabilities catalog is the contract for what matters.

---

## The three schemas

```
orient()                              — unchanged signature; response updated per D9

query(
  kind:        required enum  capabilities|procedures|world|entities|mechanics|history
  id:          string?        one full record instead of a list (procedures, mechanics, entities-by-single-id)
  ids:         string[]?      entities in full, batch
  version:     int?           historical revision; only with id (procedures, mechanics)
  query:       string?        search words (procedures, mechanics)
  nameQuery:   string?        entity name substring
  withDefinitionId: string?   entities carrying this component
  category:    string?        filter (procedures, mechanics)
  scope:       string?        ruleset preference (mechanics)
  includeInactive: bool?      default false
  limit:       int?           default 50
  sample:      int?           world example entities, default 10
  failuresOnly: bool?         history
  tool:        string?        history filter
  subject:     string?        history filter
)

commit(
  kind:           required enum  procedure|component|effects|mechanic|action
  payload:        required string — JSON object, shape per kind (below)
  intent:         string?   what you were trying to do, your own words
  proceduresUsed: string[]? contract ids you actually read
  dryRun:         bool?     default false; ALWAYS true first where supported
)
```

### Commit payload shapes (identical to today's parameters)

| kind | payload |
| --- | --- |
| `procedure` | `{id, category, name, description, instructions, governs?, constraints?, status?, changeNote?}` |
| `component` | `{id, name, description, schema?}` |
| `effects`   | `{effects: [{type, entityId?, definitionId?, toEntityId?, kind?, slot?, name?, data?}, ...]}` |
| `mechanic`  | `{id, category, name, description?, matches?, requirements?, source, scope?, status?, changeNote?}` |
| `action`    | `{intent, roleEntityIds?, input?, scope?, seed?}` |

### Old call → new call

| Today | After |
| --- | --- |
| `find_procedures(...)` | `query(kind: "procedures", query?, category?)` |
| `get_procedure(id, version?)` | `query(kind: "procedures", id, version?)` |
| `write_procedure(...)` | `commit(kind: "procedure", payload, dryRun)` |
| `describe_world(sample?)` | `query(kind: "world", sample?)` |
| `get_entities(ids?/nameQuery?/withDefinitionId?)` | `query(kind: "entities", ...)` same params |
| `define_component(...)` | `commit(kind: "component", payload)` |
| `apply_effects(effects, dryRun)` | `commit(kind: "effects", payload, dryRun)` |
| `find_mechanics(...)` | `query(kind: "mechanics", ...)` same params |
| `write_mechanic(...)` | `commit(kind: "mechanic", payload, dryRun)` |
| `run_action(...)` | `commit(kind: "action", payload)` |
| `history(...)` | `query(kind: "history", ...)` same params |

### `query(kind: "capabilities")`

Returns, from one static structure kept next to the dispatchers: every query kind with the
parameters it reads and what it returns; every commit kind with its full payload shape, whether
it supports dryRun, and the contract that governs it. This is the machine-readable version of
orient's capability lists — orient summarises, capabilities specifies. The D9 guard test asserts
this structure matches both dispatch switches, both directions, so it can never advertise a kind
that does not exist (the exact failure that crippled TravelRoleplay).

---

## Batches (≤5 files each, build and full suite between batches)

**B1 — the dispatchers, unregistered.** New `Tools/QueryTool.cs`, `Tools/CommitTool.cs` (plus
the static capabilities structure, in `CommitTool.cs` or its own small file). Not yet in
`Program.cs`, so the surface is unchanged and every existing test still passes. Build + suite.

**B2 — the cutover.** `Program.cs` (register only Orient/Query/Commit), `OrientTool.cs` (two
kind lists, next-steps rewritten to the new call forms), guard-test file(s) (budget=3, both-ways
assertions per D9). Build + suite. The end-to-end smoke test will fail here — expected.

**B3 — the smoke test.** Rewrite the JSON-RPC end-to-end test to the new calls: orient → query
world → commit component → commit effects (reject, dry-run, commit) → query entities → query
history. Build + suite green.

**B4–B6 — the contracts.** All 12 bootstrap files: `governs` to D8 format, and every old tool
name inside Instructions/Constraints text rewritten to the new call form. Add `system-use.md`
(sits next to this document; move it into `Bootstrap/`) in B4 so the entry contract lands first.
Batches of ≤5 files; run the server once per batch and confirm the seeder appends revisions.

**B7 — the cold walk.** Re-run COLDWALK runs 1–5 against the new surface, uncoached. This is the
acceptance test: the migration succeeds if a cold session navigates orient → query → commit
without inventing kinds or payload shapes. Update `COLDWALK.md` and `STATUS.md`.

---

## The layering rule (why capabilities is flat)

An extra MCP hop costs a response envelope (~100–300 tokens), a payload, and one more
navigation decision by the model — and for weaker models each decision is ~90–95% reliable, so
hops compound into wrong turns. A flat catalog entry costs ~10–20 tokens. Therefore: a bounded
list (the ~11 kinds) is always flat; add a layer only when it removes 1,000+ tokens the model
would otherwise always load; unbounded collections use exactly two layers (summary list → full
record by id), which the surface already has. Categories are filters on a flat list, never
levels — a filter costs nothing when unused, a level costs a round trip always.

---

## Status: complete, 2026-08-18

Implemented by Claude Opus 5 over B1–B6. **177/177 tests green.** B7, the cold walk, is Dante's and
is written up as run 6 in `COLDWALK.md`. `VERB_HISTORY.md` records the surface this replaced.

### Deviations from the pinned decisions, and why

**D5 — "the old tool classes become the implementation, exactly as they are."** Held for the
classes; not held for their strings, and one class lost a method.

- The literal recovery calls inside the preserved handlers were rewritten by hand to the public
  call form. The plan was to translate them at dispatch, and that translator was written and
  shipped — it rewrote by prefix, so `write_procedure(id: "x", ...)` became
  `commit(kind: "procedure", id: "x", ...)`, which `commit` rejects because it takes `payload`.
  Named arguments cannot be regexed into a payload object. D7 says every `fix` is a literal call;
  the only version of that which can be checked is one written out, so the translator was deleted.
  `VerbSurface.CommitCall(kind, id?, dryRun?)` builds them now, and a test takes each apart the way
  a client would.
- `MechanicTools.RunActionAsync` was deleted. It was a second `run_action`, superseded by
  `ActionTools` over `IActionRunner`, unreachable and untested since — but its next-step strings
  were the origin of every "choose a mechanic by id" and "dry-run an action" claim in the codebase
  and in four contracts. D5's purpose is that behaviour per kind stays provably identical; a method
  no kind dispatches to has no behaviour to preserve.

**D10 — history.** `ActionRunner` recorded `run_action` as its own tool name, bypassing the
dispatcher that stamps the protocol identity. Changed to `commit`; the mechanic id is still the
subject and `MechanicId` is still its own column, so nothing is lost.

### Added beyond the plan

- **`VerbSurface.cs`**, the single description of the surface. D9 asks orient and the catalog to be
  asserted against the dispatchers; making them all read one structure means three of the four
  copies cannot drift at all, and the guard test only has to check the structure against the two
  switches.
- **`ServerConfiguration.AddDantesRoleplayMcpServer`**, so the protocol walk boots the same
  registration `Program.cs` does rather than a private copy that would silently go stale.
- **A relaxed JSON encoder** on tool results and on the stored projection. The default escapes
  every quote as `\u0022`, which made every `fix` unreadable as a call.
- **Two guard tests the plan did not name**: no source string may contain a retired verb followed
  by `(`, and no seeded contract or rule may name one either.

### What B3 found that nothing else could

`IActionRunner` was never registered. `commit` takes it as a parameter for every kind, so the
entire write verb failed at invocation with "An error occurred invoking 'commit'" — no envelope, no
`fix`, no audit row — while 167 unit tests passed, because each built its dependencies by hand.
The end-to-end test was the last batch in the plan; on this evidence it should have been the first.

### The review pass, and what it found after "complete"

An independent review of the finished change (a subagent with no memory of writing it, given the
pinned decisions and told to verify claims against the implementation) found seven more defects.
Six are fixed; the two below are accepted deviations, recorded here rather than left implicit.

Fixed:

- **Read evidence was dead.** `OperationLog` derived "which contracts were demonstrably read" by
  looking for the tool name `get_procedure`, which no row carries any more. Every honest commit
  came back reporting nothing read and was flagged `CitedWithoutReading` — the exact false
  accusation the read-window model had been rewritten to stop. It keys on the public verb now, and
  narrows by the subject actually being a contract id, since one verb serves every read. **All 177
  tests passed with this broken**, because each recorded the old name by hand; there is now a
  protocol-level test that reads a contract and commits citing it.
- **`limit` was advertised for `procedures` and `mechanics` and silently ignored** — the handlers
  had no such parameter. Wired through, and the catalog now states the real per-kind defaults.
- **Two `fix` strings were advice, not calls** (`INVALID_EFFECTS`, and `INVALID_STATUS` on the
  procedure path). Both now begin with the call to make; the walk asserts that separately from the
  looser check it applies to next steps.
- **The effect vocabulary had fallen off the surface.** All nine types used to reach clients inside
  the old `apply_effects` description; unregistering that class took five of them
  (`entity.delete`, `component.remove`, `containment.move`, `relationship.create`,
  `relationship.remove`) somewhere no session could read, discoverable only by sending a wrong type
  and reading the rejection. They are in the catalog and in `procedure.world.change` now, and a
  guard test asserts the documented set equals `EffectType.All`.
- **Action failures pointed at kernel contracts** whose own first line tells an action caller to go
  read a different one. They point at the rule the caller needs instead.
- **A top-level `intent` was silently dropped for `commit(kind: "action")`.** It falls back now, and
  the parameter says which one is recorded.

Accepted, not fixed:

- **D2's schema enum is prose only.** `kind` is declared `string`, so an invented kind reaches the
  handler and comes back as `UNKNOWN_KIND` listing the valid ones, rather than failing JSON-schema
  validation at the protocol layer. The runtime error is a good one and arguably better for a weak
  model — it names the alternatives — but D2 asked for both, and this is a deviation.
- **`DispatchedKinds` checks which kinds a switch handles, not which handler each one calls.**
  Swapping two arms would leave the D9 guards green. The protocol walk exercises the routing
  behaviourally for five of the six query kinds, which is the coverage that actually matters, but
  the guard is weaker than it reads.
