# The cold walk

The acceptance test for M0, and the only test of `ARCHITECTURE.md` §7 that cannot be automated.
Re-run it after any change to the MCP surface.

**What it tests:** whether an agent that has never seen this system can find its way around and do
something useful, using nothing but the tool descriptions and what the tools return.

---

## The two rules that make it valid

**1. The subject must have no prior context.** A fresh session, with no access to this repository
and no explanation from you. If the agent has read `ARCHITECTURE.md`, it already knows what the
system is and the run proves nothing. This is the rule that is easiest to break by accident —
running it in the Codex window you have been coding in invalidates it completely.

**2. Do not coach.** No hints, no "try query(kind: \"procedures\")", no rephrasing when it stalls. When you
feel the urge to help, **write down what you wanted to say** — that sentence is the finding. The
whole point is to discover what the surface fails to tell it.

Codex is a good subject precisely because it is a different model family. If the surface only
works for Claude, that is worth knowing.

---

## Setup

**1. Start the server**

**Use a new, isolated database for every official run.** The development database accumulates
test fixtures and permanent ids. Deleting a fixture does not restore the baseline because its id
remains reserved; it also cannot make a previously written procedure become missing again. The
following creates a timestamped evidence database without touching the development one:

```powershell
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$env:ConnectionStrings__Kernel = Join-Path $PWD "artifacts\coldwalk\run-$runId.db"
dotnet run --project DantesRoleplay.MCPServer
```

Keep that server and its `ConnectionStrings__Kernel` value for all five prompts, including both
sessions of run 5. The resulting SQLite file is the audit evidence; preserve it with the notes
rather than reusing it for the next cold walk.

For an ordinary local session where isolation does not matter, the default remains:

```
dotnet run --project DantesRoleplay.MCPServer
```

Endpoint: **`http://localhost:6217/mcp`**

The isolated run creates the SQLite file and seeds the bootstrap contracts. Check the console for
migration or seeding errors before going further — a failed seed means an empty manual, and the
agent will correctly report the system as broken.

**2. Point a client at it**

`CONNECTING.md` covers every client and the traps in each. The short version:

```toml
# Codex — %USERPROFILE%\.codex\config.toml. Streamable HTTP directly, no bridge.
[mcp_servers.dantesroleplay]
url = "http://127.0.0.1:6217/mcp"
```

```bash
# Claude Code CLI — also native.
claude mcp add --transport http dantesroleplay http://127.0.0.1:6217/mcp
```

```powershell
# Claude Desktop — needs the mcp-remote stdio bridge; the script sets it up.
powershell -ExecutionPolicy Bypass -File .\connect-claude-desktop.ps1
```

Then start the client **in an empty directory**, not in the repo — for Claude Desktop, a chat with
no project attached. That is the cheapest way to guarantee rule 1.

---

## The five runs

Run them in order, each in a **new session**. Between runs, `history()` shows you what actually
happened.

### Run 1 — orientation, no task

> Prompt: *"You have a tool server connected. Tell me what this system is, what you are able to
> do with it, and what you would do first."*

**Looking for:** does it call `orient` unprompted? Does it then read the manual rather than
guessing? Does it correctly report what is **not** built — the world store being unreachable — or
does it claim capabilities it does not have?

### Run 2 — a task the manual already answers

> Prompt: *"Add a procedure describing how to add a new MCP tool to this system."*

There is already a `procedure.mcp.add-tool` contract. **The right answer is to find it and say so**,
not to write a second one.

**Looking for:** does it search before writing? If it does write, does the dry run's
`no-near-duplicate` check fire, and does it *act* on the warning rather than committing anyway?
This is the anti-sprawl guard (§P12) being tested against a real agent instead of a unit test.

### Run 3 — a task that needs a new contract

> Prompt: *"This system has no written convention for naming entities and component definitions.
> Add one."*

**Looking for:** does it read `procedure.contract.create` first? Does it use `dryRun`? Does it fill
in `governs`? Does it pick a sane id, knowing ids are permanent? Does it fill in `intent`?

Afterwards, run `query(kind: "history")` yourself and compare **Cited** against **Read**. A
citation with no matching read means it claimed a procedure it never opened.

### Run 4 — change the world

> Prompt: *"Create a character called Orban who is carrying a lantern, and give him some
> attributes of your choosing."*

This is the first run against a writable world, and it is deliberately vague. There is no
"character" and no "carrying" in this system — both have to be translated into entities,
component definitions and containment by reading the manual.

**Looking for:** does it call `query(kind: "world")` before inventing a component definition? Does
it find `procedure.world.change` and `procedure.world.model`? Does it dry-run
`commit(kind: "effects")` before committing, and does it send the whole change as **one** list
rather than several calls? Does it name definitions generically (`stats`) or bind them to this one
character (`orban_stats`)?

**The interesting failure:** if it splits the change across several `commit(kind: "effects")`
calls, the atomicity guarantee is real but unused, and the description failed to convey the point.

### Run 5 — the MVP acceptance test

This one is the milestone, so it is **two sessions**, and the gap between them is the whole point.

**Session A:**

> Prompt: *"Orban is trying to pick a lock. Resolve it. If this system has no rule for that yet,
> write one, then use it."*

**Session B — a NEW session, in a new empty directory:**

> Prompt: *"Orban is trying to pick another lock. Resolve it."*

**Looking for, in session A:** does it search with `query(kind: "mechanics", query: ...)` and read
what it finds, rather than inventing an outcome? When nothing fits, does it find
`procedure.mechanic.write` and follow it? Does it write a rule that reads a component, or one with
"lock" hard-coded so it can never be reused? Does it dry-run the write — and then, knowing there
is no dry run for an action, does it still run the rule rather than declaring it done?

**New since the three-verb migration:** an action selects the best-ranked matching rule by intent
and runs it. Watch for an agent that tries to name a mechanic id, or asks for an action dry run —
that is a sign the surface still reads as though it had either.

**Looking for, in session B:** does the rule written minutes earlier by a different session get
found and reused — without being told it exists?

**Session B is the milestone.** If it writes a second lock-picking rule instead of finding the
first, the near-duplicate check is not doing its job and retrieval is not either.

**The failure that matters most:** at any point, does it narrate an outcome the system did not
produce? An agent that says "Orban picks the lock with a deft twist" without a rule having run has
made the audit log and the story disagree, and only one of them will still be there next session.
`history()` afterwards shows you which happened.

---

## Run 6 — the three-verb surface

The migration in `VERB_MIGRATION.md` replaced twelve tools with `orient`, `query` and `commit`, so
runs 1–5 tested a surface that no longer exists. Re-run all five, unchanged, against the new one.

**This is the migration's acceptance test.** It succeeds if a cold session navigates
orient → query → commit without inventing a kind or a payload shape, and without needing
`query(kind: "capabilities")` explained to it. Record where it guessed, and what it guessed —
a guess that happened to be right is still a finding about the surface.

---

## What to record

For each run, four things:

1. **The exact call sequence**, in order, with arguments.
2. **Every point you wanted to intervene**, and what you would have said.
3. **Anything it believed that was wrong** — a capability it thought existed, a parameter it
   invented, a value it guessed at.
4. **Whether the outcome was correct**, separately from whether the navigation was smooth. These
   come apart: the last cold walk scored 9/10 on navigation and 4/10 on producing correct content.

Paste all of it back here, including the parts where it did well.

---

## What good looks like

Run 1 ends with the agent describing the system accurately, including its gaps, without you saying
anything. Run 2 ends with it finding the existing contract and declining to duplicate it. Run 3
ends with a committed contract that has a sensible id, a filled-in `governs`, and a dry run that
was read rather than skipped. Run 4 ends with Orban, a lantern inside him, and a component
definition you would happily reuse for the next character. Run 5 ends with a rule that one session
wrote and a different session found and used, which is the entire thesis of this project in two
prompts.

**What failure looks like is more useful.** If it invents a tool that does not exist, guesses a
parameter value, writes a near-duplicate over a warning, or produces a confident procedure whose
content is made up — that is the surface's fault, not the model's, and it is exactly what this
exercise is for.

---

## Known gap, already recorded

The agent cannot inspect this application's own source or tool registration. If run 3 drifts into
describing *how the code works*, it has no way to check itself and will either invent something or
stop. `orient` admits this in `notYetBuilt`. If it invents rather than stopping, that raises the
priority of a source-introspection tool — see `STATUS.md`.

---

## Assisted rehearsal — 2026-08-20 (not an acceptance run)

**Status: useful evidence, but not COLDWALK run 6.** The operator already had repository context
and used one continuing client session rather than five genuinely fresh ones. The database also
contained later D&D test content and `procedure.world.naming` before the rehearsal began. Treat
the observations below as a surface and workflow rehearsal only.

### Run 1 — orientation

**Calls:**

1. `orient()` — `dd1d1afafa85488faa653ac0c131f6e8`
2. `query(kind: "procedures", id: "procedure.system.use")` —
   `5f2e1420bbe94eb3abee97e869dca5d6`

**Result:** good. `orient` correctly described the persistent RPG kernel, the three verbs, the
writable world, 17 runnable rules, and the gaps. It directed the next call to
`procedure.system.use`; the manual then supplied exact kinds and the no-guessing rule. No invented
capability or payload shape.

### Run 2 — existing MCP-tool procedure

**Calls:**

1. `query(kind: "procedures", query: "how to add a new MCP tool to this system")` —
   `3b2a5d361fd44711bd4f2e78e3b39143`
2. `query(kind: "procedures", id: "procedure.mcp.add-tool")` —
   `673bfedae2f942b087edf2d1ad57d8b0`

**Result:** good. The search put `procedure.mcp.add-tool` first; it was read and no duplicate was
written. The contract's description explicitly says that the usual answer is not to add a tool.

### Run 3 — supposedly missing naming convention

**Calls:**

1. `query(kind: "procedures", query: "convention for naming entities and component definitions")`
   — `ee359536519945de9978b2aae9d8f3ae`
2. `query(kind: "procedures", id: "procedure.world.naming")` —
   `47042780dd5d429dbaf31e6e1a1972a3`

**Result:** blocked by the database baseline, not the surface. The already-existing convention was
found immediately (`procedure.world.naming`, v1: stable dotted lowercase ids, concise display names,
never reuse an id). Therefore this rehearsal could not test contract creation, `governs`, or the
procedure dry run. A real run needs a seed before that contract exists, or a different genuinely
missing convention.

### Run 4 — create Orban with a lantern

**Calls:**

1. `query(kind: "world", sample: 20)`, `query(kind: "capabilities")`, and
   `query(kind: "procedures", id: "procedure.world.model")` —
   `611f4c31b38f455d9e866c00e3a7fcc7`, `8429e33de71a4831a170a4539240acb7`,
   `c092eb2323d94b16aba40bc01579ef83`
2. `commit(kind: "effects", dryRun: true, ...)` —
   `8fe33f74eb5141569513dc6a5f5cc5e4`
3. Identical `commit(kind: "effects", ...)` —
   `db666390dbe747cbb0061ed74e4e2188`
4. `query(kind: "entities", ids: ["coldwalk.orban", "coldwalk.lantern"])` —
   `6143559458b7470d9c0c74209b0e61e9`

**Result:** good content and atomic navigation. It reused the generic `stats` definition and
committed all four effects in one list: create Orban, attach `{strength:12, agility:10,
role:"character"}`, create Lantern, and move it to Orban's `carried` slot. The dry run passed and
the read-back confirmed the containment.

**Finding:** the original fixture ids had been deleted before this rehearsal, but entity ids remain
reserved after deletion. The test therefore had to use `coldwalk.orban` and `coldwalk.lantern`.
Deleting fixtures does **not** recreate the original baseline; use a fresh database or snapshot for
the official test.

### Run 5 — write and reuse a lock-picking rule

**Session A calls:**

1. `query(kind: "mechanics", query: "pick a lock")` —
   `168224d3fc9e437c9bc9703e99f671e1`; no match.
2. Read `procedure.mechanic.write`, `procedure.action.run`, `procedure.world.model`, the exact
   capability catalog, and the existing generic threshold rule —
   `07679dc7f0c9488da2a1526647c515c5`, `16cb5d5db19346339864645a5af4e963`,
   `008b958a591949cf942e7e2b3db0b718`, `91da41b10dbc454b9d2993ff137a507a`,
   `6b270dd1f31345e2a664365c64a8de91`.
3. Created a reusable `lock` component definition — `7775c9393d0d4c64af727e9a51af022b`.
4. Dry-ran then committed `coldwalk.practice-lock` with `{difficulty:12}` —
   `023cd08ab51b4e2f804de54a8238a65b`, `df08729d9865468cb6c99b3167435aa4`.
5. Dry-ran then committed active `mechanic.lock.pick` —
   `75e1d0db38cb4fe1b3f3ae82c8ce73bf`, `73282addb2d5492b8aa9fb7ac74605a6`.
   All 11 checks passed, including `no-near-duplicate`.
6. Read the stored mechanic, then ran an action with roles `subject: coldwalk.orban` and
   `lock: coldwalk.practice-lock` — `25de61e322dd4b46bfb694e2952c19b3`,
   `aab5fd821ec64829a71a8243772108fb`.

**Session A result:** good. The rule is generic: it reads the subject's `stats.agility` and the
target's `lock.difficulty`, uses seeded randomness, has no hard-coded entity, and returns a
recorded narration. It ran successfully: **“Orban picks the lock on Practice Lock (30 against
12).”** No effects were needed; the outcome was narration only.

**Simulated Session B calls:**

1. `orient()` — `46307ae457af45f788e0f0641dc2bd24`
2. `query(kind: "mechanics", query: "Orban is trying to pick another lock. Resolve it.")` —
   `44605c60d00c4d27ae726a1775163bb2`
3. Read `mechanic.lock.pick`; search entities named Orban and lock —
   `60270fe8166d428b9ea8d6067b5e1374`, `178b15d7eeda442a803ff276294e0b2f`,
   `3a8fd8e1b16e46c5870446d0bd05171e`.
4. `commit(kind: "action", ...)` with the retrieved roles —
   `bcf7877808c845d1add36a70c72e988f`.

**Session B result:** retrieval ranked `mechanic.lock.pick` first and the stored mechanic was
reused rather than duplicated. It produced: **“Orban picks the lock on Practice Lock (24 against
12).”**

### Audit and findings

`query(kind: "history", limit: 50)` (`9ca72a011b844e879834c837abb0c296`) found no failed
rehearsal write or action. It did report **four operations that cited a procedure without a
matching recent read**. Most importantly, the simulated Session B action cited
`procedure.action.run` but did not re-read it after its fresh orientation. This is a navigation
failure in the rehearsal; a real cold subject should be expected to follow `orient` into the
operating manual and then read the action contract before acting.

Other findings:

- The official script's baseline is stale relative to this database: run 3's missing convention
  already exists, and later D&D data introduces a second entity named Orban.
- Mechanic retrieval worked, but the natural Session B query returned 11 results. The right rule
  ranked first, but the list is noisy.
- A new action needs explicit role-to-entity mapping. The fresh-session-style pass had to search
  for both Orban and a lock, then choose the entity whose component requirements matched. That is
  discoverable, but it is a real friction point.
- The surface did successfully convey: orient first, inspect before changing, consult the exact
  capability catalog, use a single dry-run-then-identical-commit for effects and mechanics, and
  never ask for an action dry run.
