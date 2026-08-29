# Application kernel Slice 11C implementation — bounded-profile catalog-schema alignment and safe contract adoption

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / application-owned component contracts](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible contract migration only**  
Source ID and locator: **not applicable** — this changes no D&D rule, formula, eligibility, or outcome.  
Outcome: Rewrite the confirmed safe subset of legacy `game.core.*` JSON Schema sidecars into the
existing bounded system profile, then prove immutable registration of every resulting compatible
`dnd2024.game.core.*` contract on a fresh disposable host.  
Exclusions: Generic schema-profile expansion; changing value semantics; removing, relaxing, or
approximating `if`/`then`, `pattern`, or `format` constraints; `stats` schema invention;
component values/backfill; state-space creation/migration; default-host registration; catalog
projection/alias adoption; game mechanics; remote MCP; vectors; and AI orchestration.  
Allowed files/areas: The safe legacy schema sidecars, focused schema/component-administration and
fresh-host protocol tests, the existing `dnd2024` source-adoption proof, this document, receipt,
and concise roadmap/dependency status updates.  
Stop point: Stop after 28 exact compatible sidecars are registered only in disposable acceptance
hosts, four semantic-constraint sidecars and `stats` remain absent, and no generic schema behavior,
application values, state space, or default host changes.

## Confirmed decisions

- On 2026-08-24 the user confirmed the recommended migration direction: rewrite legacy catalog
  schemas into the existing bounded profile; do not expand that generic profile.
- The confirmed mapping remains `game.core.<tail>` → `dnd2024.game.core.<tail>`.
- `title` and `$comment` are documentation annotations, not validation rules, and may be removed
  from the schema bytes. The nine malformed sidecars share one unambiguous extra closing brace
  before `$comment`; repair only that syntax defect.
- The four sidecars using `if`/`then`, `pattern`, or `format` retain their current bytes and remain
  unregistered. Replacing those constraints needs a later explicit semantic design.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Rules and outcomes | None is read or changed. | Catalog mechanics/data | No SRD or Foundry review applies. |
| Component identity | Confirmed prefix mapping preserves every legacy tail. | User confirmation and Slice 11B preflight | Register only the safe mapped subset in disposable evidence. |
| Schema constraints | Annotation/syntax correction only. | Catalog schema sidecars | Preserve all bounded validation constraints; defer four non-profile constraints. |

## External implementation reference

No Foundry dnd5e review applies: this slice changes neither D&D behavior nor game data values.

## Prerequisite evidence

- [Slice 11A receipt](receipts/APPLICATION-KERNEL-SLICE-11A-RECEIPT.md) proves fresh-host source
  adoption with no default application registration.
- [Slice 11B receipt](receipts/APPLICATION-KERNEL-SLICE-11B-RECEIPT.md) proves the authenticated,
  append-only component-type registration protocol and reports the exact 2/21/9/32 preflight split.

## Runtime artifacts

- Revise 26 safe `game.core.*.schema.json` sidecars by removing only `title`/`$comment` and repair
  the nine shared JSON syntax defects. The two already compatible quest schemas are unchanged.
- Add no permanent ID, public kind, profile keyword, migration, or default host registration.
- Extend the fresh-host `dnd2024` MCP walk to dry-run and commit each of the 28 profile-compatible
  mapped contracts, retaining immutable receipt/replay evidence.

## Authoritative state and closed input

Catalog sidecars remain authored development authority. SQLite type/version rows remain runtime
authority and are written only by the closed `system.component-type.register` protocol. Tests use
the protocol's five-field payload, deriving profile/hash/version server-side. No test or caller may
supply a version, profile, normalized schema, or mapping override.

## Behavior, result, and typed effects

The safe schema bytes compile under the existing profile without a profile change. A fresh
disposable host registers `dnd2024`, dry-runs then commits each mapped compatible schema, and
replays a representative registration exactly. Each contract receives version 1, the accepted
profile ID, and an immutable derived hash. The four deferred schema IDs and `dnd2024.stats` have
no type/version record.

## Failure, replay, and rollback contract

Malformed or non-profile sidecars do not produce component-type rows. Registration without its
exact dry run, stale expected hash, unauthorized transport, or audit failure retains Slice 11B
behavior. A single failure rolls back its attempted type/audit transaction; no partial schema
rewrite is hidden by registration evidence.

## Implementation sequence

1. Add the safe schema-normalization fixture assertions and exact deferred-schema inventory.
2. Rewrite only titles/comments and the nine unambiguous syntax defects; validate the catalog.
3. Extend fresh-host protocol coverage for all 28 compatible mappings and deferred absence.
4. Run focused/full/local-AI/catalog/model-drift/build/diff checks; write the receipt and stop.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Safe rewrite | Exactly 26 edited sidecars parse/compile; all preserve their non-annotation bounded keywords. |
| Compatibility | 28 mapped sidecars compile and register at version 1; exact profile/hash derive server-side. |
| Deferred safety | The four semantic-constraint IDs and `dnd2024.stats` remain absent. |
| Protocol | Every registration dry-runs before commit; representative replay is exact. |
| Boundary | No profile/migration/default-host/value/state-space/game behavior/AI change. |

## Verification commands

- Focused component-type administration, schema validation, authorization, catalog coverage, and
  fresh-host MCP protocol tests.
- `roleplay validate catalog`; full shared and local-AI tests; warning-free solution build; EF
  model-drift coverage; `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11C-RECEIPT.md`, mark this document
accepted, and update the Slice 11 status. Stop before translating the four deferred constraints or
registering/importing any component values.
