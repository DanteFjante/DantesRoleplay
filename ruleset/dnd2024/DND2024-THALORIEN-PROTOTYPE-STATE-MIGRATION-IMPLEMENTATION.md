# D&D 2024 Thalorien prototype-state migration implementation

Status: complete
Owner/roadmap: `ruleset/dnd2024/ROADMAP.md`
Dependency tree: `DND2024-THALORIEN-PROTOTYPE-STATE-MIGRATION-DEPENDENCY-TREE.md`
Ruleset alignment: dnd2024-compatible
Source ID and locator: `2026-08-29-thalorien-live-pre-migration-export`; live SQLite export, not a D&D rule source
Outcome: create one new prototype-compatible runtime state space containing the confirmed Thalorien transfer scope.
Exclusions: new D&D rule behavior, deletion or overwrite of classic state, unrelated worlds,
browser/API redesign, and world-secret disclosure.
Allowed files/areas: a generic scoped migration seam and tests, source activation/component registration support, migration evidence, this plan, and the owning roadmap/tree.
Stop point: completed after a dry-run, one atomic state-space adoption, exact read-back, and retained rollback export; classic state was not replaced.

## Confirmed decisions

- The user requested migration of current Thalorien and campaign records on 2026-08-29.
- A full live export with operation history exists before any mutation.
- The user confirmed the full 199-entity Thalorien graph on 2026-08-29.
- The user authorised missing Orban facts to be invented on 2026-08-29, provided a review ledger
  distinguishes each invention from preserved source facts.
- Orban's ocarina retains its narrative description only. Its special effects, item statistics,
  rarity, attunement, and activation rules are unresolved and must not be materialized as a magic item.

## Delivered implementation sequence

1. Added a generic, exact, entity-scope constraint to legacy-state adoption; its payload has no
   ruleset-specific IDs and preserves the existing all-graph behavior when scope is absent.
2. Added focused regression coverage for exact scope closure, source mutation after preview, and
   rejection of boundary-crossing edges.
3. Prepared Orban's provisional sheet and review ledger from the exported narrative facts. This is
   D&D data, not C# behavior.
4. Registered and activated the current prototype contracts, added direct D&D campaign/world
   component contracts, ran the mandatory dry-run, committed once, and read the resulting state
   space back.

## Completion receipt and exit gate

See `ruleset/dnd2024/evidence/DND2024-THALORIEN-PROTOTYPE-STATE-MIGRATION-RECEIPT.md` for the
completion evidence.
