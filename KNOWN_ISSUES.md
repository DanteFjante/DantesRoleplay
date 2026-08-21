# Known issues

Things found and deliberately not fixed, so they do not quietly become permanent. Each says what is
wrong, where, why it matters, and what closing it involves.

Last reviewed: 2026-08-21. Add to this rather than leaving a finding in a chat log.

Seven entries closed on 2026-08-20 and are recorded here so nobody re-files them. **`orient()` no
longer denies a capability that works** — its composition line said a mechanic cannot call another
mechanic, which was false for a whole feature; it now states precisely what is missing, which is
the imperative form, not composition itself. **The pinned regression baseline is gone** — it said
213 and then 304 while the suite was neither, and a number that must be hand-maintained will always
drift. "The suite passes" is the claim worth making. **`procedure_relation` is gone** — the unused
table, model type, and EF mapping were removed together with a forward migration; contract
relations can return only as a catalog-backed capability.

**Catalog provenance now round-trips** — `CreatedBy` and `ChangeNote` travel through mechanics,
procedures, event types, and subscriptions without affecting their content fingerprints. Legacy
catalogs continue to receive the old import defaults when those fields are absent.

**Guard and reaction randomness now shares one derivation** — guards predict the ledger sequence
of their proposal and use the same root-seed, U+001F-separated formula as reactions. Their previous
draws change intentionally; both the first and continuation sequence are regression-tested.

**The transitive security advisories are resolved** — EF Core is updated to 10.0.11, with explicit
patched pins for `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12 and
`System.Security.Cryptography.Xml` 10.0.11. `dotnet list package --vulnerable --include-transitive`
now reports no vulnerable packages.

**The event-code readability regression is corrected** — `GuardRouter` and `EventTypeStore` were
rewritten into named, independently readable steps, and the remaining dense control flow in
`EffectApplier` was expanded while preserving its transaction behavior. `EventLedger` already met
that standard; future changes retain it by treating explanatory comments as part of the code.

**The encounter-space role-contract mismatch is corrected** — the Feature 20 encounter-space
fixture and selected mechanic now agree on their roles. The focused Feature 8/12/20 compatibility
set passes, including `CatalogFeature20Tests`, so this no longer blocks tactical acceptance.

---

## Open issues

- **Catalog validation is blocked by an incomplete untracked Feature 20 encounter-space procedure.**
  `catalog/procedures/ruleset/dnd2024/core/tactical/space/procedure.mechanic.dnd2024.encounter-space.md`
  has active front matter and descriptive text but lacks the required `## Instructions` section.
  `CatalogReader.ReadAsync` therefore rejects the entire catalog before Feature 28 Slice 4's new
  immutable feat definitions can be imported or validated. The file is a shared-worktree change,
  so it was deliberately left untouched by the Feature 28 work. The Feature 20 owner must complete
  the procedure contract and its focused checks; then rerun catalog validation and Feature 28
  Slice 4's focused catalog tests.

- **The shared data-access build is blocked by an incomplete System Feedback implementation.**
  `DantesRoleplay.DataAccess/SystemFeedbackService.cs` references the missing
  `DantesRoleplayDbContext.SystemFeedbackReports` set, an unavailable `RequestToken` member, and
  captures an `out` parameter from a lambda (`CS1061` and `CS1628`). This prevents the repository
  suite from compiling and currently blocks acceptance of Feature 20 Slice 2A, despite its focused
  tests and catalog validation passing. Closing it requires the System Feedback owner to complete
  its model/context wiring and validation flow, run its focused tests, then rerun the blocked full
  suite.

- **The shared data-access build is currently blocked by a syntax error in
  `KnowledgeTimelineCoordinator.Interval`.** The expression at
  `DantesRoleplay.DataAccess/KnowledgeTimelineCoordinator.cs:212` uses `from` in a relational
  pattern where the compiler requires a valid expression, causing `CS1001`, `CS1003`, `CS1525`, and
  `CS0742`. This blocks all builds/tests that compile `DantesRoleplay.DataAccess`, including S4
  Slice 2 checkpoint-capture verification. It is unrelated to S4. Closing it requires the owner to
  correct the interval predicate, run its focused knowledge-timeline tests, then rerun the blocked
  S4 focused suite.

- **The shared build can observe the vector-index interface and implementation out of sync during concurrent edits.**
  `dotnet build DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore` intermittently reports
  `SqliteVecKnowledgeVectorIndex` missing `IKnowledgeVectorIndex.ReplaceWorldAsync` and
  `MarkOtherGenerationsStaleAsync`, even though both members are being changed in the retrieval
  workstream. This blocks compiling the new Feature 17 Slice 4 acceptance test; it is not a dying
  or damage-reaction failure. Finish and stabilize the retrieval interface/implementation edit,
  then rebuild Feature 17 and run its focused tests before accepting Slice 4.

- **Tactical movement does not yet apply difficult terrain or creature-space pass-through rules.**
  Feature 20 Slice 4 charges five feet for every accepted cardinal or diagonal step and rejects
  every other occupied footprint. It therefore cannot yet represent the SRD's doubled
  difficult-terrain cost or its limited cases for moving through another creature's space. This
  is intentionally deferred to Feature 20 Slice 5; closing it requires authoritative terrain-cost
  derivation and Size/ally/Incapacitated pass-through admission while preserving the existing
  atomic budget-and-position transaction. See
  `ruleset/dnd2024/feature-20/FEATURE-20-DEPENDENCY-PLAN.md`.

---

## Not an issue, recorded so nobody re-diagnoses it

- **The Cowork device bridge cannot delete files.** Running `git` through it leaves a
  `.git/index.lock` that blocks the next real git command. See `CATALOG_HANDOVER.md` §7 for the full
  set of environment traps.
