# Known issues

Things found and deliberately not fixed, so they do not quietly become permanent. Each says what is
wrong, where, why it matters, and what closing it involves.

Last reviewed: 2026-08-19. Add to this rather than leaving a finding in a chat log.

---

## 0. Events and subscriptions stopped mid-slice — read this first

Written 2026-08-19, at the point the session ran out of room. This is not a defect report like the
rest of the file; it is where the work was put down, so the next session does not have to
reconstruct it. Delete this section when Slice 6 closes.

The design record is `EVENTS_AND_SUBSCRIPTIONS_PLAN.md` — read its "Progress" section under Slice 5
before anything here. `CATALOG_HANDOVER.md` §7 has the environment traps, and they will cost you an
hour each if you skip them.

### Nothing since the 322/322 run has been compiled

There is no .NET SDK reachable from the authoring environment. Slices 5a and 5b, and the contract
revisions that landed with them, are **written and unbuilt**. Start here:

```powershell
dotnet build > out-build.txt 2>&1
dotnet test  > out-test.txt  2>&1
```

Redirected output is UTF-16; decode it. Then run the app once so the revised bootstrap contracts
reseed, then `roleplay export catalog` — the catalog and its manifest are stale until you do, and
`roleplay verify catalog` will say so. Commit the catalog, the manifest and the database together.

Do not trust the description above as evidence. It is a list of things to check.

### What is genuinely missing from Slice 5

Its exit gate is NOT met. Three pieces, in the order they should be taken:

1. **Derived events (the pass this plan calls 5c).** `MechanicOutput.Events`, validated against the
   exact active event type version at emission, storing that version, with a guard veto on a
   derived event rolling back the complete root. `EventExecution.EventCount` is hard-coded to `0`
   until this lands, and both `procedure.event.react` and `orient()` say so in as many words —
   narrow both the day it changes.
2. **Event and execution summaries in result envelopes.** The plan says action and direct-effect
   results carry them as ADDITIVE fields, changing no existing field. They do not yet, so a caller
   cannot tell a reaction fired without querying the ledger separately.
3. **The acceptance matrix gaps.** 27 event tests exist and cover dispatch, the three declarative
   filters, depth, per-subscription limits, seed derivation, the envelope, the declared-components
   projection, and a guard vetoing a reaction's effect. NOT covered: a three-link A to B to C
   chain; the order-then-id subscriber tiebreak; replay in two fresh databases producing identical
   parsed output, event order and final state; a rich condition returning empty (the "evaluated and
   had nothing to do" execution record); and most of the failure matrix — timeout, cancellation,
   bad event type or payload, invalid child effect, and root audit failure.

### One decision blocks 5c

Item 5 below. The plan's mapping table wants `before` and `after` in every payload; the nine shipped
schemas have neither. 5c adds a SECOND producer of events, so this is the last cheap moment to
settle which of the two is wrong. Deciding after 5c means changing two producers instead of one.

### Then Slice 6, and that is the feature

Notifications and the final surface: two tables, `MechanicOutput.Notifications`,
`query(kind: "notifications")`, `commit(kind: "notification")`, `procedure.notification.inspect`,
and the capabilities / orient / protocol-walk / catalog-coverage sweep. Its acceptance matrix is
already written out in the plan.

### Two habits this feature keeps proving

- **`procedure.system.create-feature` step 6 is the one that gets skipped.** Slice 5's central
  capability shipped with no contract at all, and three shipped contracts still told sessions it
  did not work. A capability nobody can discover does not exist. Write the contract in the same
  change, and narrow `orient()`'s not-built list the same day — a session is instructed to believe
  that list over anything else it reads.
- **Simulating in Python against a copy of the live database catches real defects, and cannot catch
  everything.** It found the missing hash separator and the schema violations. It provably could
  not find the relationships-import bug. See `CATALOG_HANDOVER.md` §8.

---

## 1. `orient()` denies a capability that works

**Where:** `DantesRoleplay.MCPServer/Tools/OrientTool.cs`, the `NotYetBuilt` list.

> "Composing one rule from another — a mechanic cannot call another mechanic."

A mechanic *can*: `ctx.children` composition is implemented and its `ActionRunnerTests` pass.

**Why it matters more than a stale comment.** `procedure.system.use` tells every session: *"If
`orient()` says a capability is not built, believe it over anything a contract or your prior
experience suggests."* An over-broad denial there does not merely misinform — it actively talks
sessions out of a working feature, and they will write a worse rule instead.

The neighbouring events entry had the same fault and was narrowed when Slice 4 landed; this one was
left because whether composition is *finished* is Feature 5's call, not this feature's.

**To close:** confirm what composition supports, then narrow the sentence to what is genuinely
missing. Deleting it outright is also fine if nothing is.

---

## 2. `ROADMAP.md` states a regression baseline that is years out of date

> "Repository regression baseline: 213/213 tests."

The suite is now **322**. Anyone comparing a run against that number reaches the wrong conclusion in
either direction.

**To close:** delete the number. A baseline that must be hand-maintained will always drift; "the
suite passes" is the claim worth making, and CI is where it belongs.

---

## 3. `procedure_relation` is declared, unused, and outside the catalog

**Where:** the table exists in the model and migrations; `CatalogCoverageTests` marks it `GAP:`.

A real table — `FromContractId`, `ToContractId`, `Kind` (parent / related / supersedes) — with zero
rows, because **nothing in the solution reads or writes it**: no store method, no MCP verb, no
seeder. Dead schema, exactly as `SourceHash` was before the fingerprint work.

**Why it matters:** it is not a problem while nothing uses it. The moment anything does, export
silently drops it and nothing says so.

**To close:** either give contract relations an API *and* catalog coverage in the same change, or
drop the table. The coverage guard fails if the entry is removed without one of those.

---

## 4. Catalog round-trip loses `ChangeNote` and `CreatedBy`

**Where:** `mechanic_version` and `procedure_contract_version`; marked `GAP:` in
`CatalogCoverageTests`.

Authored text, not derived. **10 of 10 mechanics and 26 of 27 contracts** carry a non-empty change
note on their current version — real sentences like *"Feature 3 Slice 2: execute the v3
ability-check contract's validated Advantage…"*. Export → wipe → import replaces every one with
"Imported from the catalog."

**To close:** carry both as front matter, **outside** the content fingerprint — they describe an
edit, not the edited thing, and putting them inside would make an edited note read as an edited rule.

---

## 5. Event payloads cannot say what a change was *from*

**Where:** `EVENTS_AND_SUBSCRIPTIONS_PLAN.md` §"Exact structural event mapping" versus the schemas in
`catalog/event-types/`.

The plan's mapping table says every payload should carry an effect index plus `before` and `after`
snapshots. The schemas Slice 1 actually shipped have none of that — `world.component.replaced`
declares only `entityId`, `definitionId`, `data`. **The plan and the shipped contract disagree**, and
the producer conforms to the shipped contract.

**Why it matters:** a ledger that records what a change set, but not what it changed from, cannot
answer "what did this rule actually do?" — which is most of why an audit ledger exists. Slice 5's
reactive chains will be built on whatever this settles into.

**To close:** decide which is wrong. Widening means a v2 of all nine event types plus the receipt
pipeline the plan describes — its own slice. Narrowing means editing the plan's table. Do it before
Slice 5.

---

## 6. The two routers derive their seeds differently

**Where:** `GuardRouter.Seed` versus `EventRouter.DeriveSeed`.

A reaction's seed is `SHA256(rootSeed \u001f sequence \u001f subscriptionId \u001f mode \u001f ordinal)`.
A guard's is `SHA256(subscriptionId | version | type | ordinal | payloadJson)`. Two differences, both
real:

- **The separator.** `|` is a legal character in a payload and in nothing else the reaction side
  joins. Two different guard positions can therefore encode to identical bytes — the exact collision
  the `\u001f` separator exists to prevent, and the same defect `MechanicFile.ContentHash` had
  before Slice 0.
- **The root seed is absent.** A guard's draw is not reproducible from the chain's root seed, so a
  chain does not replay a guard's randomness. A guard cannot change the world, so nothing committed
  depends on it — but a guard that denies on a die roll would deny unreproducibly, and the audit
  would not be able to say why.

`EventRouter`'s comments claimed the two matched. That claim is now corrected in place; the
divergence itself is not.

**To close:** move both onto one derivation. It changes existing guard seeds, so it is a slice of
its own with its own test, not a fix to fold into something else.

---

## 7. Two transitive packages carry known high-severity advisories

`NU1903`, on every build:

- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 — GHSA-2m69-gcr7-jv3q
- `System.Security.Cryptography.Xml` 9.0.0 — nine advisories

Both arrive transitively, neither is referenced directly, and they predate all recent work.

**To close:** check whether a newer EF Core 10 patch pulls fixed versions; otherwise pin them with an
explicit `PackageReference`. Worth doing before this is a released tool rather than a local one.

---

## 8. `QueryTool.QueryAsync` is positionally fragile

Its store arguments are injected by DI at runtime but passed **positionally** in tests. Inserting a
parameter anywhere but the end silently breaks those call sites until compile — this happened when
`IEventLedger` was added ahead of `IOperationLog`.

**To close:** append new stores rather than inserting them, or switch the test call sites to named
arguments. Slice 5 will add at least a reaction router and probably a notification store.

---

## 9. Code style has drifted in the newer event code

The Slice 3–4 additions (`GuardRouter`, the original `EventLedger`, parts of `EffectApplier`) are
written as dense single-line statements with no comments. The rest of this codebase explains *why* at
every non-obvious decision, and that commentary is load-bearing — it is how the next session learns
the constraints instead of rediscovering them.

This is the same decay diagnosed in the D&D mechanics, where a rule went from 87 commented lines to
24 averaging 233 characters. There it had a cause: authoring through JSON string escaping. Here there
is no such excuse.

**Why it matters:** the dense version of `EffectApplier`'s commit path concealed a real bug for three
slices — the unguarded path committed before writing its events, so a failure between the two would
leave a committed world change with no record of it. It was one line among three near-identical
lines, and nothing about it looked wrong.

**To close:** treat it as you would a failing test — fix it when touching the file, not as a sweep.

---

## Not an issue, recorded so nobody re-diagnoses it

- **The Cowork device bridge cannot delete files.** Running `git` through it leaves a
  `.git/index.lock` that blocks the next real git command. See `CATALOG_HANDOVER.md` §7 for the full
  set of environment traps.
