# Known issues

Things found and deliberately not fixed, so they do not quietly become permanent. Each says what is
wrong, where, why it matters, and what closing it involves.

Last reviewed: 2026-08-20. Add to this rather than leaving a finding in a chat log.

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

---

## Not an issue, recorded so nobody re-diagnoses it

- **The Cowork device bridge cannot delete files.** Running `git` through it leaves a
  `.git/index.lock` that blocks the next real git command. See `CATALOG_HANDOVER.md` §7 for the full
  set of environment traps.
