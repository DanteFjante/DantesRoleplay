# D&D 2024 G7N receipt — application namespace containment

Status: **implemented; feature acceptance pending unrelated D&D suite repair**
Date: **2026-08-30**
Implementation: `DND2024-NAMESPACE-CONTAINMENT-IMPLEMENTATION.md`
Dependency leaf: `DND2024-COMPLETE-CAMPAIGN-DEPENDENCY-GRAPH.md`, G7N
Ruleset alignment: **dnd2024-compatible**; no D&D rule calculation changed

## Delivered boundary

- Preserved the live database before the identity cutover in
  `evidence/state-exports/2026-08-30-pre-namespace-containment-export` with its manifest, 283 JSON
  records, 99 Markdown records, 30 JavaScript sidecars, and operation history.
- Moved all 69 current mechanic IDs and Markdown/JavaScript basenames from
  `mechanic.dnd2024.*` to `dnd2024.mechanic.*`.
- Moved all 57 current procedure IDs and basenames from the inverted
  `procedure.*.dnd2024.*` forms to `dnd2024.procedure.*`.
- Rewrote current application categories, source/content/currency/item identities, child-mechanic
  references, schema constants, tests, and D&D interface consumers to the `dnd2024.*` namespace.
  This includes 2,431 SRD source references now using `dnd2024.source.srd-5.2.1`.
- Qualified every D&D application-local generic world/campaign dependency as
  `dnd2024.game.core.*`. Reusable `game.core.*` contracts outside the application remain generic.
- Renamed the optional legacy-equipment entity to
  `dnd2024.item.hempen-rope-50-foot.v1`; its archived source locator intentionally retains the old
  archive identity as provenance, not as a current alias.
- Added a deterministic namespace guard for JSON IDs, Markdown IDs/categories, filenames,
  sidecars, inverted prefixes, unqualified D&D `game.core.*`, duplicated prefixes, D&D interface
  references, and extension entity IDs.
- Kept the G1 owner ledger immutable as pre-cutover evidence while verifying that each historical
  mechanic identity maps to its current `dnd2024.mechanic.*` identity.

## Verification

- Namespace and extension tests: **7 passed**.
- Namespace and preserved owner-ledger tests: **7 passed**.
- Affected category/store/audience tests: **82 passed**.
- D&D interface tests: **114 passed**.
- Affected moved/contract/weapon mechanic tests: **19 passed, 1 unrelated failure**. The failing
  weapon-damage contract fixture omits the activity role now required by the existing mechanic.
- `roleplay validate catalog`: **passed**, 145 records, with the existing 24 near-duplicate
  warnings; no live data was touched.
- Static containment audit: **0** inverted prefixes, **0** unqualified application `game.core.*`
  references, **0** duplicated `dnd2024.dnd2024.*` prefixes, 69 matching mechanic sidecar pairs,
  and 57 current procedures.
- Full .NET suite: attempted in an isolated repository-local output. It was stopped after repeated
  failures from the existing missing `catalog/applications/dnd2024/components/dnd2024.weapon-profile.json`
  harness dependency; the independently reproduced stale weapon-activity fixture failure is also
  recorded above. Both belong to the current D&D component/mechanic redesign already described in
  `KNOWN_ISSUES.md`, not to namespace resolution.

## Deliberate exclusions

- No live SQLite record, activation, source profile, state space, world, campaign, or operation
  history was rewritten.
- No compatibility alias or fallback from an old identity was added.
- Existing source-profile labels such as `dnd2024-core` remain deployment profile names and already
  begin with the application identifier; changing live source registrations requires a separate
  preview/activation migration.
- Historical receipts, adoption evidence, and the pre-cutover export retain the identities true at
  capture time.

The authored application is ready for a separate reviewed preview/activation cutover after the
existing D&D suite blockers and the migration/backup gates are closed.
