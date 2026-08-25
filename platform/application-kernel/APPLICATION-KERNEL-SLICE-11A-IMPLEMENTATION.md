# Application kernel Slice 11A implementation — exact `dnd2024` legacy-source adoption proof

Status: **accepted 2026-08-24**; [receipt](receipts/APPLICATION-KERNEL-SLICE-11A-RECEIPT.md)  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / register current catalog sources](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible metadata/ownership only**  
Source ID and locator: **not applicable** — this slice interprets no D&D rule or content; it only
uses the accepted legacy ownership ratification and existing authored file paths.  
Outcome: Prove through the existing authenticated `system.*` protocol and a fresh disposable
SQLite database that `dnd2024` can be registered, assigned only its ratified legacy authored
sources, previewed, activated, replayed, and queried without claiming generic system documents or
mutating legacy world state.  
Exclusions: Live-database registration, startup auto-registration, component-definition import,
catalog moves/copies, identifier rewrites, aliases, state-space creation/backfill/upgrade, fixture
or world-state import, projection adoption, application mechanic execution, SRD behavior, local AI,
vectors, new public kinds, migrations, and generic-host dependence on `dnd2024`.  
Allowed files/areas: one focused MCP/application-kernel adoption test and its fixture helpers;
application-kernel plan/receipt/status documentation; an existing generic defect only when this
proof exposes it and the repair adds no `dnd2024` branch to system code.  
Stop point: Stop when the fresh protocol proof activates an exact non-empty manifest containing
every ratified legacy component/mechanic/procedure/event/subscription source class, excludes known
system procedures and structural events plus authored world fixtures, survives exact replay/query,
and leaves legacy entity/component tables empty.

## Confirmed decisions

- The accepted ownership ratification assigns the initial application ID `dnd2024` and every
  previously unresolved gameplay record to it while leaving existing legacy IDs unchanged.
- Slice 10 already confirms the exact public registration, source, preview, activation, replay,
  authorization, and query contracts used here. No new serialized contract is required.
- The user's 2026-08-24 “Continue” after Slice 10 completion authorizes beginning Slice 11. This
  first coherent part proves registration/activation in disposable state; it does not authorize a
  live-state backfill or compatibility alias.
- `catalog/` remains the single authored catalog. Sources reference existing files in place; this
  slice creates no application-owned copies.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Rules/content | No rule text, calculation, eligibility, or outcome is read or changed. | Existing catalog records remain authoritative. | No SRD locator or mechanic edit is needed. |
| Ownership | Ratified legacy gameplay records belong to `dnd2024`. | `LEGACY-OWNERSHIP-RATIFICATION.md` | Source selection includes only the ratified record classes and preserves legacy IDs. |
| Runtime state | Authored fixtures are not application activation authority or live state. | Catalog files / SQLite live state | World entities and relationships are deliberately excluded from this activation proof. |

## External implementation reference

No Foundry dnd5e review applies because this slice implements no rule behavior, data model, or game
content and adopts no external edge case or code.

## Prerequisite evidence

- [Legacy ownership ratification](LEGACY-OWNERSHIP-RATIFICATION.md) owns the exact initial
  application and legacy record-class assignment.
- [Slice 10H receipt](receipts/APPLICATION-KERNEL-SLICE-10H-RECEIPT.md) proves the complete
  authenticated three-verb application protocol and closes Slice 10.
- Current catalog files and catalog validation are implementation evidence for the paths/counts
  exercised by the proof; the stale Slice 1 inventory is used only for the ratified class boundary.

## Runtime artifacts

- Add no production runtime artifact, table, public kind, schema, permanent source ID, or bootstrap
  registration.
- Add one repository integration test that starts the real MCP host with an explicit workspace
  allowed root and a fresh SQLite database, then uses the existing public commits/queries.
- Test-only source IDs and request tokens are disposable protocol inputs, not authored catalog IDs.

## Authoritative state and closed input

The current authored `catalog/` tree owns source bytes. The accepted ownership ratification owns
which legacy record classes may be selected. A fresh SQLite database owns test registrations,
preview/activation history, and operation evidence. The configured allowed-root resolver owns the
repository path and protocol callers receive only its opaque ID.

The test registers exact safe relative paths/globs for legacy game component definitions and schema
sidecars, game mechanics and scripts, game/campaign/quest/play procedures, the game event type, and
game subscriptions. It never registers the catalog root, system/information/event infrastructure
procedures, structural event types, manifest, world fixtures, relationships, or any absolute path.

## Behavior, result, and typed effects

Use `commit` dry-run then commit for the immutable application and each source registration, query
the exact application/source discovery results, query the current preview, dry-run and commit the
exact preview fingerprint, then replay activation and query the active application.

The preview and activation must contain identical deterministic winner paths and non-zero document
counts. Every winner must be inside the closed legacy-game path set. Representative game-owned
component, mechanic, procedure, event, and subscription files must be present; representative
system procedure, structural event, catalog manifest, entity fixture, and relationship files must
be absent. The activation writes only registry/activation/audit evidence.

## Failure, replay, and rollback contract

The proof must fail if a selected source is unavailable, produces a scan problem, selects no
documents, claims a forbidden system/fixture path, produces a stale preview, or cannot be replayed
exactly. Existing adapters retain their typed authorization, malformed payload, stale expectation,
token conflict, and transaction rollback contracts. No failed proof may modify the repository or a
normal host database.

## Implementation sequence

1. Add the disposable real-host adoption test and closed source-selection helper.
2. Assert exact source discovery, selection coverage/exclusions, activation/replay/query evidence,
   audit evidence, and empty legacy/runtime state.
3. Run focused live-protocol and guard tests, catalog validation, full shared/local-AI suites,
   warning-free solution build, model-drift check, and `git diff --check`.
4. Record the receipt, mark 11A accepted, and leave component/schema/state migration to 11B+.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Fresh host registers, previews, and activates non-empty `dnd2024` sources. |
| Ownership | Only ratified game source classes are winners; representative system and fixture paths are absent. |
| Surface | Existing three verbs and existing `system.*` kinds are used without additions. |
| Determinism | Preview winners equal activated winners and exact activation replay is byte-equivalent. |
| Authorization/audit | Existing private-operator flow and operation evidence cover every mutation. |
| Isolation | No live database, authored file, entity/component, state space, or legacy world row changes. |
| Host independence | Production composition still contains no `dnd2024` literal or auto-registration. |
| Boundary | No rule interpretation, aliases, component import, projection, or migration. |

## Verification commands

- Focused Slice 11A real-host adoption, protocol, authorization, guard, and catalog-coverage tests.
- `roleplay validate catalog` against a fresh disposable database.
- Full `DantesRoleplay.Tests` and local-AI suites.
- Warning-free solution build, EF model-drift check, and `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11A-RECEIPT.md`, mark this document accepted,
and update only the Slice 11 adoption leaf/status. Stop before importing/versioning component
contracts, moving catalog files, binding a state space, migrating fixtures/live state, aliases,
application execution, or AI consumption.
