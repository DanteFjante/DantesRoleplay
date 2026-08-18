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

```
dotnet run --project DantesRoleplay.MCPServer
```

Endpoint: **`http://localhost:6217/mcp`**

First run creates the SQLite file and seeds the bootstrap contracts. Check the console for
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
