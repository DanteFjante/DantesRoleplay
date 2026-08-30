# D&D 2024 legacy-source cutover completion receipt

Date: 2026-08-30  
Status: cleanup complete; repository-wide green gate has a separately owned known-issue exception  
Implementation: `ruleset/dnd2024/DND2024-LEGACY-SOURCE-CUTOVER-IMPLEMENTATION.md`

## Delivered boundary

- Moved the live React table from the nested prototype into
  `src/system/web-interface/dnd2024`, including source, public assets, the server adapter, package
  metadata, and 98 focused tests.
- Updated the solution/project, server, test, and active planning references to the canonical web
  owner.
- Moved 69 unique retired-implementation receipts to
  `ruleset/dnd2024/evidence/retired-implementation`.
- Moved 1,243 rollback/state-export files to `ruleset/dnd2024/evidence/state-exports`, including
  the Thalorien pre/post migration exports and retained database snapshots.
- Moved current plans and model/migration evidence to their ruleset or web owners.
- Moved the retained ability-check parity fixture beside its owning probe so that no executable
  test reads the retired tree.
- Removed dead adoption generators and archive-content tests whose only input was the retired
  implementation.
- Removed `prototype/` and `old-dnd/` completely.

No D&D rule, schema meaning, route, audience policy, campaign/world record, or live SQLite state was
changed by this cutover. Historical evidence may still name its original `old-dnd/...` locator;
those strings are provenance, not filesystem dependencies.

## Verification evidence

- Canonical React tests: 98 passed.
- Canonical server build: passed.
- Cleanup/catalog ownership tests: 10 passed, including canonical schema compilation, retained
  parity, extension packaging, and the complete-campaign owner ledger.
- Catalog validation: passed for 145 records with the existing 24 warnings.
- Filesystem check: both retiring roots are absent.
- Executable-source scan: no active code, project, script, or web source reads
  `prototype/dnd2024` or `old-dnd`; the sole C# `old-dnd` string is a provenance key asserted by an
  evidence test.
- Restarted the local server and received HTTP 200 from `/ui/dnd2024-play`.
- Browser verification after restart showed the server-hosted React application with world
  `Thalorien`, campaign `The Waystone at Brackenford`, the Player/DM control, and the World,
  Campaign, Party, Current View, and Rules areas.

## Repository-wide acceptance exception

The full solution run was executed after clearing the server DLL lock. Cleanup-related failures
were corrected: the schema test no longer freezes a prototype count, and the parity probe now owns
its retained fixture. The run still cannot be green because the current flat component catalog no
longer contains old components such as `dnd2024.weapon-profile`, while older D&D mechanics and their
large test harness still request them; the weapon-damage contract test fails for the same component-
convergence family. This is the current reproducible issue already documented in `KNOWN_ISSUES.md`.
Repairing those game mechanics is deliberately excluded from this repository-source cleanup.

## Recovery

Tracked deleted implementation files remain recoverable from Git history. The selected live-state
rollback artifacts remain available under `ruleset/dnd2024/evidence/state-exports`; the removed
duplicate source trees are not required for normal builds or the live page.
