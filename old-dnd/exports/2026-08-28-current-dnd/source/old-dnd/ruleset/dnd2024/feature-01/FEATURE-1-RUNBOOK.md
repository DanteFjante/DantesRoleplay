# Feature 1 — the D&D 2024 ability check

Authored over real MCP against a server running in the Cowork container, following the eight-step
loop the contracts dictate. Everything below **ran**; the outputs quoted are what came back.

The container database is not yours, so these payloads are the original transferable artifact.
Against governance contract v4, apply the taxonomy and anti-overlap adjustments recorded in the
live-replay section below; the raw JSON files preserve the earlier run verbatim.

Source throughout: *System Reference Document 5.2.1*, Wizards of the Coast, CC-BY-4.0 —
[dndbeyond.com/srd](https://www.dndbeyond.com/srd). SRD 5.2.1 was published 2025-05-01 and carries
the 2024 (5.5e) revision of the core rules.

## Live replay in the repository database — 2026-08-18

Replayed through `http://127.0.0.1:6217/mcp` after the main D&D governance contract reached v4.
That contract introduced the `ruleset.dnd2024.core` taxonomy, so the live payloads use
`ruleset.dnd2024.core.data.abilities` and
`ruleset.dnd2024.core.gameplay.ability-checks.fixed-dc`.

The clean anti-sprawl checks also required three wording/routing adjustments: the abilities
contract governs attaching/changing `dnd2024.abilities` rather than generic “writing data”; the
check contract is named “Resolve a fixed-DC ability check”; and the mechanic omits the generic
match phrases `d20 test` and `roll a dexterity check`. Ability-specific phrases remain.

Committed operation IDs:

- abilities contract: `dedcaf2b613f4ec0a9181d0e35dc04f3`
- component definition: `2772eb4861194c9590d8aca70a431dad`
- fixed-DC check contract: `b9a5e333c15846269a156f43005e10aa`
- ability-check mechanic: `3d0df86785b640f598cd0ce0450c3803`
- test creature: `d541e966c7984a9a8e66c0f0ee77c990`
- first action: `5c348dcc1cf1429cb3ebe005135c493d`
- exact replay: `dbf1600ea65b44f8893ab1bc1ec44d72`
- negative modifier: `6e6726a5b979437eb84f24855a49dda7`

The live roll was `7 +3 = 10` against DC 15 with seed `8253275941846134235`; replay matched
exactly, and Charisma 8 produced `7 -1 = 6`. History records the frozen projection and cited/read
contracts. The later [Feature 2 dependency plan](../feature-02/FEATURE-2-DEPENDENCY-PLAN.md)
resolves this runbook's source-registry sequencing conflict by making the registry its first
implementation slice. No proficiency slice may begin before that registry is verified.

---

## Run these in order

Read the governing contracts first — the audit records what you actually opened, and every write
below cites them:

```
query(kind: "procedures", id: "procedure.contract.create")
query(kind: "procedures", id: "procedure.world.model")
query(kind: "procedures", id: "procedure.mechanic.write")
query(kind: "procedures", id: "procedure.action.run")
```

| # | Call | Payload |
| --- | --- | --- |
| 1 | `commit(kind: "procedure", dryRun: true)` then without | `01-contract-abilities.json` |
| 2 | `commit(kind: "component")` — no dry run for this kind | `02-component-abilities.json` |
| 3 | `commit(kind: "procedure", dryRun: true)` then without | `03-contract-check-ability.json` |
| 4 | `commit(kind: "mechanic", dryRun: true)` then without | `04-mechanic-check-ability.json` |
| 5 | `commit(kind: "effects", dryRun: true)` then without | inline below |
| 6 | `commit(kind: "action")` — **runs it** | inline below |

**Step 5 — a creature to run it against:**

```json
{"effects":[
  {"type":"entity.create","entityId":"creature.orban","name":"Orban"},
  {"type":"component.add","entityId":"creature.orban","definitionId":"dnd2024.abilities",
   "data":"{\"str\":12,\"dex\":16,\"con\":14,\"int\":10,\"wis\":13,\"cha\":8}"}
]}
```

**Step 6 — the run:**

```json
{"intent":"Orban tries to slip past the sleeping guard - dexterity check",
 "roleEntityIds":{"subject":"creature.orban"},
 "input":"{\"ability\":\"dex\",\"dc\":15}",
 "scope":"dnd2024-srd-5.2.1"}
```

---

## What it returned here

```
RULE   : mechanic.dnd2024.check.ability v1
BEATEN : mechanic.check.threshold, mechanic.value.adjust
NARR   : Orban makes a Dexterity check: 5 +3 = 8 against DC 15 - failure.
DATA   : {"test":"ability-check","ability":"dex","dc":15,"die":"1d20","roll":5,
          "modifiers":[{"source":"dex 16","value":3}],"total":8,"succeeded":false,
          "source":"SRD 5.2.1 - Playing the Game: Ability Checks"}
EFFECTS: []
SEED   : 7876197698324674754
```

**The scope decision proved itself in the first run.** The action's own candidate list ranked
`mechanic.dnd2024.check.ability` above the shipped `mechanic.check.threshold`, which also answers
to "check". Without `scope: dnd2024-srd-5.2.1` on the rule, the generic threshold rule would have
answered a D&D question and looked plausible doing it.

Verified afterwards:

- **Replay.** Passing the recorded seed back reproduced `5 +3 = 8` exactly, twice.
- **Negative modifiers.** Charisma 8 gives −1, not −0 — `Math.floor` rounds toward negative
  infinity where `Math.trunc` would have quietly made weak creatures better at everything.
- **Missing DC** and **unknown ability** both fail with the rule's own message rather than a
  guessed value. The rule never invents a DC.
- **History explains the ruling**: mechanic id, version, seed, the frozen projection and the four
  contracts that were read. `citedWithoutReading: 0`.

The mechanic passed all eight blocking dry-run checks before it was committed.

---

## Three findings

### 1. `no-near-duplicate` counted repeated tokens (fixed in the live replay)

`ProcedureStore.Overlaps` does `Tokens(b).Count(left.Contains)` — **occurrences, not distinct
tokens** — and flags a duplicate at 2. Every write-side contract's `governs` starts with
`commit(kind:`, so a contract that governs *two* calls scores 2 against every contract that
governs *one*.

Contract 1 was flagged as a near-duplicate of `procedure.contract.create`, `procedure.action.run`,
`procedure.mechanic.write`, `procedure.world.change` and `procedure.world.model` — none of which is
about D&D ability scores. It will fire on all 26 D&D contracts, which makes the anti-sprawl guard
useless exactly where a growing ruleset needs it.

The live replay added `.Distinct(StringComparer.Ordinal)` before `.Count(...)`. It also exposed a
second false positive: parent and child contracts necessarily repeat literal
`commit(kind: "...")` fragments. Those fragments are now excluded from domain-overlap scoring
while remaining in `governs` for lookup. Regression tests cover both cases.

### 2. `MECHANIC_FAILED` always tells the caller the rule is broken (surface defect)

Omitting the required `dc` produced the rule's own correct message, wrapped in this `fix`:

> `query(kind: "mechanics", id: "…")` — **the rule is broken, not your arguments**; read it, then
> revise it with `commit(kind: "mechanic", …, dryRun: true)`.

The rule was not broken; the caller left out a required input. A GM who follows that instruction
mid-session edits a correct rule. The kernel cannot currently tell the two apart, because a thrown
`Error` is a mechanic's only channel — distinguishing them needs something like `ctx.badInput(...)`
or a returned `{problem}`, which is a kernel change, not a fix for today.

### 3. Two categories mean the same thing (pre-existing, now visible)

Procedure categories include both `mechanic` (action.run, mechanic.write) and `mechanics`
(mechanic.create/find/projection/run). Harmless as a flat list; as a tree they are two sibling
roots for one idea. Worth folding together when the kernel contracts get re-rooted under `system.*`.

**Also worth noting, positively:** the hierarchical-catalogs change earned itself in live use. The
dry run reported *"'ruleset.dnd2024.gameplay.ability-checks.fixed-dc' is NEW. Its nearest existing
branch is 'ruleset.dnd2024', whose children are: ruleset.dnd2024.data"* — which is the nudge that
keeps a 26-contract taxonomy coherent, and is what the old flat category dump could not do.

---

## Deliberately not in feature 1

Proficiency and skills (feature 2), advantage/disadvantage (3), saving throws (4), contested
checks, and the source registry. The `modifiers` list in the envelope is a list precisely so each
of those **appends an entry** rather than rewriting the arithmetic.

Skill names — `stealth`, `perception`, `athletics` — were deliberately kept out of the match
phrases. Adding them now would have this rule answer a stealth check *without* applying
proficiency, and look correct while doing it.
