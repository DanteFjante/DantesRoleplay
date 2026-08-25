# Application kernel Slice 11B implementation — component-type registration protocol and legacy-schema preflight

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / application-owned component contracts](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible contract migration only**  
Source ID and locator: **not applicable** — no D&D rule, formula, eligibility, or outcome is
implemented; existing catalog schema bytes are compatibility inputs only.  
Outcome: Expose the confirmed authenticated `system.component-type.register` commit for generic,
immutable application component-type schemas; prove exact dry-run, replay, audit, and rollback;
then preflight ratified legacy `dnd2024.game.core.*` mappings without registering a schema the
accepted bounded profile rejects.  
Exclusions: Schema-profile expansion, schema translation/normalization beyond the existing profile,
legacy component definition/value import or backfill, `stats` schema invention, state-space creation
or migration, activation binding changes, projection/catalog/alias adoption, game mechanics,
application-specific C#, remote MCP, vectors, and AI orchestration.  
Allowed files/areas: a new generic `component-type-administration` system component (contracts,
persistence, hosting, tests, component metadata), the ECS registry's ambient-transaction seam,
data-access composition, MCP commit dispatcher/capabilities/authorization tests/live protocol
walk, system-use procedure/component metadata, a focused legacy schema preflight test, and this
document/receipt/status updates.  
Stop point: Stop when the closed command creates/replays a fixture type atomically, rejects stale,
malformed, unauthorized, and profile-incompatible input without rows, and reports the current
legacy `game.core.*` profile incompatibility without creating any `dnd2024` type/version row.

## Confirmed decisions

- On 2026-08-24 the user confirmed the public kind `system.component-type.register` and the
  permanent mapping `game.core.<tail>` → `dnd2024.game.core.<tail>`, with `stats` reserved as
  `dnd2024.stats`.
- The closed payload is `{requestToken, applicationId, qualifiedTypeId, schemaJson,
  expectedSchemaHash}`. The caller supplies no type version, profile ID, derived normalized schema,
  schema hash, authorization decision, timestamp, or migration assertion.
- `expectedSchemaHash` is `null` only for an absent type; otherwise it must equal the latest exact
  stored type schema hash. A changed valid schema appends a version; an old previously registered
  schema cannot silently roll a type backward.
- `dnd2024.stats` remains unregistered in this slice because its legacy definition has no schema.
  Inferring one from fixtures would be a schema-meaning change and is not authorized.
- The accepted Slice 5 schema profile remains unchanged. Thirty legacy sidecars currently reject:
  21 for unsupported keywords/annotations and 9 for invalid JSON; two are profile-compatible.
  This slice records that compatibility split without registering any legacy schema, weakening the
  validator, or silently removing constraints.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Rules and outcomes | None is read or changed. | Existing catalog JavaScript/data | No SRD or Foundry rule review applies. |
| Component identity | User-confirmed prefix mapping preserves the legacy tail. | Legacy ownership ratification + user confirmation | Preflight names `dnd2024.game.core.*` but writes none when profile validation rejects. |
| `stats` | Legacy descriptor has no schema. | `catalog/components/stats.json` | Reserve mapping only; do not infer a contract. |

## External implementation reference

No Foundry dnd5e review applies because this slice changes no D&D behavior, source content, or
game data model. It uses no external code.

## Prerequisite evidence

- [Slice 5 receipt](receipts/APPLICATION-KERNEL-SLICE-5-RECEIPT.md) proves the append-only type
  registry and closed bounded schema profile.
- [Slice 10H receipt](receipts/APPLICATION-KERNEL-SLICE-10H-RECEIPT.md) proves authenticated
  three-verb administrative dry-run/replay/audit conventions.
- [Slice 11A receipt](receipts/APPLICATION-KERNEL-SLICE-11A-RECEIPT.md) proves exact fresh-host
  `dnd2024` source ownership without default host registration.

## Runtime artifacts

- Add the confirmed `commit(kind: "system.component-type.register")` to the existing `commit`
  verb, capability catalog, and procedure guidance; add no tool or query kind.
- Add ruleset-neutral component-type administration contracts/service. It owns a single ambient
  transaction combining exact type-version registration and successful operation audit.
- Add `GetLatest` to the existing generic component-type registry port so administration can derive
  closed concurrency/version evidence without reading registry tables directly.
- Adapt the existing registry to participate in an ambient transaction; it keeps standalone
  transaction behavior for existing callers.
- Add no migration: existing component type tables already own the required immutable records.

## Authoritative state and closed input

SQLite application/type/version rows are authoritative. The existing bounded validator owns profile
acceptance, normalized schema, and hash. The operation log owns exact request-token preview/commit
and audit evidence. The registry owns append-only version assignment.

The adapter parses only the five confirmed payload fields after private-operator `Modify`
authorization. It derives the current type, normalized schema, profile ID, hash, next version, and
outcome. It cannot accept a caller-authored version/hash/profile/migration decision.

## Behavior, result, and typed effects

Dry run validates application/type ownership, expected latest hash, and bounded schema acceptance;
it returns the derived type version/hash/profile and records preview evidence without mutation.
Commit requires that exact preview, revalidates current evidence, registers an immutable type
version, and writes one success audit record in the same transaction. Exact token replay returns
the original receipt. Identical current schema is `unchanged`; a valid changed schema is
`registered` at the next contiguous version.

Legacy preflight uses actual schema sidecars and the user-confirmed prefix mapping. It reports both
bounded-profile rejections and currently compatible schemas as no-write compatibility findings. It
must not register a partial `dnd2024` type set, rewrite catalog schema bytes, or manufacture
`dnd2024.stats`.

## Failure, replay, and rollback contract

Closed failures include authorization before parse, malformed/extra fields, invalid IDs/hashes,
unknown app, cross-owner qualified ID, stale expected hash, rejected schema profile, missing/stale
dry run, token conflict, unavailable service, and injected registry/audit failure. Each causes no
type/version mutation; an audit failure rolls back a previously staged type row. Resubmitting an
older schema after a newer version is rejected rather than reverting the latest type.

## Implementation sequence

1. Add generic type administration/ambient registry transaction tests.
2. Add authorization-first MCP adapter, dispatcher/capability/procedure metadata, and live
   protocol coverage for a neutral fixture type.
3. Add current-catalog legacy preflight proving exact mappings are rejected without `dnd2024`
   component-type writes; record the unsupported-profile boundary.
4. Run focused tests, catalog validation, full shared/local-AI suites, warning-free build,
   model-drift check, and `git diff --check`; record the receipt.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Positive | Valid fixture schema dry-runs, commits, query-independent receipt confirms, and replays exactly. |
| Versioning | Valid changed schema appends; stale hash and attempted old-schema rollback reject. |
| Atomicity | Registry/audit injected failure leaves type/version/audit rows unchanged. |
| Authorization | Remote/missing principal denies before malformed payload parsing or service access. |
| Surface | Three verbs, capabilities, dispatcher, examples, and procedure guidance agree. |
| Legacy safety | All actual legacy sidecars are classified against the profile, with no `dnd2024` type/version row; `stats` stays absent. |
| Boundary | No profile expansion, catalog rewrite/copy, state migration, projection, game behavior, or AI. |

## Verification commands

- Focused component-type administration, ECS registry, schema-validation, authorization, protocol,
  guard, catalog-coverage, and live MCP tests.
- `roleplay validate catalog`; full shared and local-AI tests; warning-free solution build; EF
  model-drift check; `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11B-RECEIPT.md`, mark this document
accepted, and update the Slice 11 status. Stop before profile/migration confirmation. The next slice
must decide whether each legacy schema is rewritten as reviewed application content or the bounded
profile is deliberately extended with a security review.
