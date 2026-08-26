# Application-aware workspace Slice A implementation — live reviewed application onboarding

Status: **accepted 2026-08-25**  
Owner/roadmap: [Web interface roadmap](WEB-INTERFACE-ROADMAP.md), Feature 4  
Dependency tree/leaf: [Application-aware workspace Slice A](WEB-APPLICATION-AWARE-WORKSPACE-DEPENDENCY-PLAN.md#dependency-tree-and-ordered-subslices)  
Ruleset alignment: **ruleset-neutral installation of already accepted packages**  
Source ID and locator: **not applicable** — no game rule or authored content is interpreted or changed  
Outcome: Export the current normal-host registration evidence, install the reviewed `dnd2024` and
`trail-survival` application packages through the existing private `system.*` MCP contracts, bind
their initial state spaces, and read back exact application/source/activation/state-space and
operation evidence so the current homepage can discover applications. Correct the discovered
generic legacy-adoption defect that treated soft-deleted entities and their retained rows as active
state.  
Exclusions: New schema, migration, public route/kind, startup auto-registration, app-page
association, shared navigation, AI-context change, mechanics/content changes, Trail simulation
state, application UI, direct SQL mutation, or application action execution.  
Allowed files/areas: this implementation document; immutable before/after evidence and recoverable
database backup under `web/exports/`; one Slice A receipt; the parent plan and concise web roadmap
status; `src/system/legacy-state-adoption/persistence/LegacyStateAdoptionService.cs`; and its focused
test file. The normal database may change only through the existing private MCP commits named below.  
Stop point: Stop after both selected application registrations, exact reviewed sources and component
types, activations, and initial bindings are read back or after a typed MCP failure is recorded. Do
not begin Slice B.

Current progress and typed stop evidence are retained in
[the 2026-08-25 progress record](exports/WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-PROGRESS-20260825.md).
Slice A remains active and unaccepted; no later slice may start from this partial state.

## Confirmed decisions

- The user's 2026-08-25 request to implement every parent-plan slice one at a time confirms the
  previously recommended two-application Slice A boundary.
- The accepted application IDs are `dnd2024` and `trail-survival`; `system` remains reserved.
- The reviewed Trail onboarding state-space ID `trail-survival-onboarding` is used unchanged.
- The D&D legacy adoption state-space ID is `dnd2024-main`. It receives a complete atomic copy of
  current active legacy state; soft-deleted entities and rows attached to deleted endpoints remain
  legacy tombstone evidence and are not runtime state. Legacy rows remain unchanged.
- Every administrative payload is dry-run first and then committed byte-for-byte unchanged. Exact
  replays return the original operation; changed work uses a new request token.
- An immutable pre-mutation database backup and bounded read evidence are retained. Recovery is
  forward-only through the existing revisioned owners; the backup is for explicit operator recovery,
  never automatically restored.

## D&D 5e 2024 alignment

| Concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Rule behavior | No D&D rule, formula, eligibility, or outcome changes. | Existing catalog mechanics and accepted ownership ratification | Slice A scans and registers accepted files without interpreting them. |
| Component ownership | The accepted 33 legacy component contracts are owned by `dnd2024`. | Application-kernel Slice 11 adoption evidence | Register exact current schemas as qualified immutable types before adoption. |
| Runtime state | SQLite legacy state remains authoritative until explicit adoption copies it. | Legacy-state adoption owner | Complete mapping and atomic copy are mandatory; partial/inferred adoption is forbidden. |

## External implementation reference

No Foundry dnd5e review applies because this operational slice implements no rule behavior, data
model, or edge case and adopts no external code.

## Prerequisite evidence

- Application-kernel Slice 11A proves the exact `dnd2024` application, component, and eleven-source
  registration/activation boundary through the current three verbs.
- Application-kernel Slice 11J proves complete dry-run-gated legacy adoption, mapping completeness,
  atomicity, replay, and unchanged legacy rows.
- Trail TG1/TG2 receipts prove the `trail-survival` source remains valid after expansion to eleven
  component contracts. The old onboarding statement that the glob has exactly one winner is stale;
  current preview truth must be valid, nonempty, additive, and confined to the accepted directory.
- `procedure.system.use` governs every operation in this slice.

## Runtime artifacts

No new protocol or authored catalog artifact is created. The generic adoption reader is corrected
to use the same active-entity boundary as the legacy world reader. Live runtime records are limited to:

- application revisions for `dnd2024` and `trail-survival`;
- exact source registrations and scan receipts;
- immutable component type registrations already accepted by the two application owners;
- exact activation revisions;
- state-space bindings `dnd2024-main` and `trail-survival-onboarding`;
- a complete adopted D&D ECS graph only in `dnd2024-main`; and
- operation/audit receipts created by the existing owners.

## Authoritative state and closed input

The normal SQLite database owns current registrations, legacy state, operations, and state spaces.
The repository's current accepted authored files own source bytes and schemas. Host configuration
maps opaque allowed-root ID `repository` to this checkout; MCP inputs never contain an absolute path.

Server queries derive current fingerprints, preview winners, activation fingerprints, registered
schema hashes/versions, used legacy component definitions, relationship kinds, and adoption source
fingerprint. The operator supplies only exact reviewed IDs, relative globs, schemas read from the
accepted files, complete explicit mappings, and fresh request tokens. The operator cannot supply
scan truth, hashes, revisions, activation success, copied ECS values, or operation outcomes.

## Behavior and transaction ownership

1. Verify no host process owns the normal database. Retain a recoverable copy of the database and
   WAL/SHM companions when present.
2. Start the existing private MCP host against the exact normal database with only the opaque
   `repository` source-root configuration. Do not build or apply an unrelated pending migration.
3. Call `orient`, inspect `capabilities`, read `procedure.system.use`, and query current applications,
   sources, state spaces, and bounded history. Save the redacted JSON evidence before mutation.
4. For Trail Survival, dry-run/commit application and source registration, preview the current
   accepted directory, require a valid nonempty confined winner set, dry-run/commit exact activation,
   register the eleven accepted component schemas if not already current, and create the reviewed
   empty state-space binding.
5. For D&D, dry-run/commit application registration, register the accepted 33 exact component
   schemas, register the eleven ratified legacy source globs, require a valid confined preview,
   dry-run/commit exact activation, derive complete active-legacy component/relationship mappings,
   then dry-run/commit adoption into `dnd2024-main`. Adoption excludes soft-deleted entities plus
   components and edges whose entity endpoints are deleted, while retaining rejection of truly
   unknown references.
6. Query both exact applications/sources/catalogs and state-space summaries plus bounded operation
   history. Retain after evidence and exact operation IDs.
7. Verify the homepage's existing application discovery returns both IDs after the normal host is
   next run. Slice A does not alter the page or start Slice B.

Each registry, component-type, activation, state-space, and adoption owner owns its own existing
transaction. The sequence is intentionally forward-recoverable rather than falsely atomic across
all commits. A later failure retains earlier confirmed immutable records and is reported exactly.

## Failure, replay, and recovery contract

| Failure | Required behavior |
| --- | --- |
| Database is owned or backup cannot be verified | Stop before the first mutation. |
| Host would apply an unrelated pending migration | Use a compatible already-built host or stop; do not broaden Slice A. |
| Existing target differs | Query exact fingerprints and stop for conflict; never overwrite or invent an expected value. |
| Unknown source root/path escape/scan problem | No source activation or state-space mutation. |
| Preview invalid, empty, stale, or outside accepted boundary | Do not activate. |
| Component schema invalid or used mapping incomplete | Do not adopt D&D state. |
| Dry-run/commit payload drift | Do not commit; regenerate one exact pair. |
| Request-token conflict | Preserve existing operation; use a new token only for genuinely different work. |
| Partial sequence failure | Retain prior immutable committed records, save the typed error and read-back evidence, and stop. |
| Exact retry | Return the prior operation/result and create no second semantic change. |

## Acceptance matrix

| Area | Required evidence |
| --- | --- |
| Before state | Recoverable backup plus bounded MCP application/source/state/history evidence. |
| Trail package | Exact application/source, valid confined preview, activation, eleven current type contracts, and one empty binding. |
| D&D package | Exact application/eleven sources/current types, valid confined activation, complete 212/388/29/357 active-state adoption, deleted tombstones excluded, and unchanged legacy rows. |
| Protocol | Only `orient`, `query`, and `commit` with existing `system.*` kinds; every write is dry-run gated. |
| Replay/audit | Exact operation IDs and equal replay evidence; no duplicate semantic mutation. |
| Isolation | Apps have no base link, Trail state is empty, D&D state is exact-scope, and no cross-app source/state appears. |
| Repository | No authored source, rule, schema meaning, code, migration, or public surface changed. |
| Homepage readiness | Normal registry discovery contains both application IDs and their state spaces. |

## Verification commands and evidence

- Read-only EF applied/pending migration inspection before host startup.
- MCP `orient`, `capabilities`, `procedures`, `system.applications`, `system.sources`,
  `system.application-preview`, `system.catalogs`, and bounded `history` queries.
- Dry-run/commit/replay/read-back for each existing administrative kind.
- Focused existing Trail onboarding and application-kernel adoption tests when current build state
  permits without touching the live database.
- `git diff --check` for Slice A documentation.

No full suite or protocol walk is required because Slice A changes no code, catalog, dependency
registration, migration, or public surface. Those run at Slice H acceptance.

## Completion receipt and exit gate

Write `web/WEB-APPLICATION-AWARE-WORKSPACE-SLICE-A-RECEIPT.md` with before/after evidence locations,
exact operation IDs, application/activation/binding fingerprints, counts, failures/recovery notes,
and deliberate exclusions. Mark Slice A accepted only after the user confirms its completed live
installation. Stop before Slice B.
