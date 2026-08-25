# Application kernel Slice 11J implementation — complete legacy state adoption and parity

Status: **accepted 2026-08-24**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaves: [Application-kernel D, E, I and Slice 8B/8C seams](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible; generic runtime implementation is ruleset-neutral**  
Source ID and locator: **not applicable to runtime behavior** — existing D&D values are copied
byte-for-byte and no rule, formula, eligibility, or outcome is introduced or reinterpreted. The
retained source identity is `source.dnd2024.srd-5.2.1`; individual existing schemas retain their
authored locators.  
Outcome: Finish Slice 11 as one consolidated acceptance slice: add application-scoped generic
containment and directed relationships, atomically adopt a complete legacy world into one exact
active application state space, and prove state/projection/action-output/replay/catalog parity
without making the generic kernel understand game vocabulary.  
Exclusions: New D&D calculations or content; importing the quarantined `old-dnd` catalog; rewriting
legacy values; deleting or dual-writing legacy tables; vector/AI orchestration; projection caches;
non-empty application-version upgrades; and automatic mutation of the normal live database.  
Allowed files/areas: New `src/system/state-space-edges/`,
`src/system/legacy-state-adoption/`, and read-only `src/system/application-execution/` components;
application ECS/effect contracts where generic
edge seams are required; DataAccess mappings/migration/registration; the existing three-verb
state-space administration protocol and focused tests; this plan, receipt, and concise owner
status links. Catalog and quarantined game content remain read-only.  
Stop point: Stop when a disposable database can register and activate an application, register
exact component contracts, dry-run then atomically adopt every legacy entity/component/
containment/relationship, replay idempotently, read the complete adopted graph through generic
ports, prove byte-identical legacy/application mechanic projection and deterministic JavaScript
output for the ratified action set, preserve the legacy rows unchanged, and pass final Slice 11
acceptance.

## Confirmed decisions

- On 2026-08-24 the user authorized the recommended complete migration design after reviewing the
  live-state finding: the kernel owns opaque application-scoped containment/edge identity,
  isolation, revisions, transactions, and JSON validation; applications own every relationship
  kind, slot meaning, payload, component schema, and mechanic behavior.
- The existing public commit identity is extended with `system.state-space.adopt-legacy`. This is
  an explicit operator migration boundary, requires an exact successful dry run, and is never
  invoked automatically at startup.
- A request supplies an exact mapping from every used legacy component definition to a registered
  immutable application component type, and from every used legacy relationship kind to an
  application-qualified kind. The kernel never guesses ownership from a prefix.
- Adoption copies state; it does not move, delete, reinterpret, or begin dual-writing legacy rows.
  Equal request-token replay returns retained evidence. A different request using the token fails.
- One root transaction owns state-space binding, entities, components, containments,
  relationships, binding history, and operation audit.
- Slice 11 acceptance proves execution compatibility through byte-identical projections and
  deterministic sandbox outputs. Publishing a dynamic `<application>.*` write protocol remains
  owned by interaction orchestration rather than inventing a generic public game command here.

## D&D 5e 2024 alignment

| Rule concern | Existing meaning | Owner | Consequence |
| --- | --- | --- | --- |
| D&D component values | Existing SQLite JSON and exact registered schema. | `dnd2024` application | Copy bytes only; schema mismatch blocks. |
| Relationship/slot vocabulary | Opaque authored strings and JSON. | `dnd2024` application | Kernel validates qualification/bounds only. |
| Mechanic result | Existing catalog JavaScript. | Ratified mechanics | Same seed/input/projection must produce byte-identical output. |
| Rules source | Existing component source references. | `source.dnd2024.srd-5.2.1` | No new locator or rule interpretation. |

## External implementation reference

No Foundry dnd5e rule implementation is applicable. This slice adds ruleset-neutral graph storage,
copy migration, and parity comparison; it adopts no Foundry behavior or source. The useful design
constraint is repository-local: the existing world model already distinguishes exclusive
containment from non-exclusive directed relationships, and that distinction is preserved within an
application state space.

## Prerequisite evidence

- [Slice 6](receipts/APPLICATION-KERNEL-SLICE-6-RECEIPT.md) proves exact schema-valid
  application-scoped entities/components.
- [Slice 8A](receipts/APPLICATION-KERNEL-SLICE-8A-RECEIPT.md) proves atomic ECS effects and audit.
- [Slices 10F–10H](receipts/APPLICATION-KERNEL-SLICE-10H-RECEIPT.md) prove exact activation binding,
  state-space creation, history, dry-run evidence, replay, and empty upgrade behavior.
- [Slices 11A–11I](receipts/APPLICATION-KERNEL-SLICE-11I-RECEIPT.md) prove exact `dnd2024`
  activation, 33 accepted contracts, 14 mechanics, navigation, and intentionally empty structural
  projection adoption.
- Read-only live evidence on 2026-08-24 found 233 legacy entity rows, 412 component rows, 29 active
  containments, and 357 relationships while the application ECS was empty. It also found used
  definitions outside the already adopted 33; exact mapping is therefore mandatory and partial
  adoption is forbidden.

## Runtime artifacts

- `state-space-edges`: generic contracts and SQLite persistence for one exclusive containment per
  contained entity and unique directed `(from, to, qualified kind)` relationships, all scoped by
  state-space ID with optimistic revisions.
- `legacy-state-adoption`: bounded request, dry-run preview, immutable receipt, exact mapping and
  full-coverage validation, transactional copier, replay evidence, and legacy/application parity
  report.
- SQLite migration for application-scoped containment, relationship, and retained adoption
  evidence. Existing legacy tables remain unchanged.
- `commit(kind: "system.state-space.adopt-legacy")` added to the existing authenticated private
  operator surface; no new MCP verb.
- Generic ECS effects gain containment move and relationship set/remove variants backed by the
  edge store so adopted mechanics have a complete typed-effect target. No game kind is compiled.

## Authoritative state and closed input

SQLite legacy rows are source authority during the one-time copy. The exact registered
application revision, exact active fingerprint, exact component type version/hash mapping, and
complete relationship-kind mapping close destination meaning. Caller supplies no counts,
revisions, timestamps, success result, schema validity, application ownership inference, or
derived value. The service derives and fingerprints the complete source inventory and refuses a
commit if it changed after dry run.

Input is bounded to one application/state-space, one request token, at most 256 component mappings,
at most 256 relationship mappings, intent/procedure/audit evidence, and the exact active
fingerprint. Every used definition/kind must appear exactly once; unused mappings are rejected.

## Behavior, result, and typed effects

1. Validate the closed request and exact active application.
2. Snapshot all legacy entities, components, containments, and relationships in deterministic
   order; reject dangling edges, duplicate mappings, invalid JSON, unknown contracts, wrong-owner
   types, schema-invalid values, or any omitted/extra mapping.
3. Compute an immutable source/evidence fingerprint. Dry run records non-consuming evidence and
   returns counts plus findings without destination rows.
4. Commit requires matching dry-run evidence, opens one transaction, creates the state-space
   binding/history, copies all rows while retaining legacy IDs/data/timestamps/revisions where the
   destination contract supports them, records adoption evidence and audit, then commits.
5. Replay returns the retained receipt. Partial destination state or source drift fails without
   mutation.
6. Generic edge effects apply in authored batch order and participate in the ECS effect transaction.
7. Parity reads compare ordered entity/component/containment/relationship values and use the same
   mechanic requirements, role bindings, input, seed, and JavaScript sandbox to compare execution
   outputs byte-for-byte. No effects are dual-written during parity.

## Failure, replay, and rollback contract

Malformed/bounded input, inactive/stale application, missing/extra/duplicate mapping, wrong-owner
or stale type, schema-invalid legacy value, dangling/duplicate edge, source drift, existing state
space, request-token conflict, injected persistence/audit failure, cancellation, or parity mismatch
commits no destination state. Dry run never creates application state. Legacy rows are read-only in
all paths. Replay requires the same request and retained source/evidence fingerprint.

## Implementation sequence

1. Add generic state-space edge contracts, persistence, migration, component metadata, and focused
   isolation/revision tests.
2. Extend generic ECS effects with edge variants and atomic rollback tests.
3. Add legacy adoption domain/service, retained evidence, exact preflight, atomic copy, replay, and
   full-state parity tests.
4. Add the authenticated `system.state-space.adopt-legacy` dispatch/specification and extend the
   live disposable protocol walk without touching the normal database.
5. Add projection/sandbox output parity across the ratified mechanic inventory.
6. Run catalog validation, focused tests, protocol/guard checks, full shared/local-AI suites,
   isolated warning-free build, migration/model consistency, and `git diff --check`; receipt and
   mark Slice 11 complete.

## Implementation progress — 2026-08-24

- Complete through steps 1–6: application-scoped containment/relationships, edge effects,
  exact full-graph legacy adoption with retained replay evidence, and the authenticated
  `system.state-space.adopt-legacy` command are implemented.
- State parity now covers every entity/component/containment/relationship value in a disposable
  graph, generic-port readability, unchanged legacy rows, dry-run drift, replay, and audit-failure
  rollback. The migration applies from an empty database and the EF model has no pending change.
- A read-only `application-execution` component now resolves exact active catalog mechanics,
  materializes their declared projection from application-scoped ECS/edge state through explicit
  component and relationship mappings, and invokes the existing bounded JavaScript sandbox without
  applying effects. Rich graph projection is byte-identical to the legacy resolver, and all 14
  ratified mechanics select and invoke the same source/fingerprint with deterministic output parity.
- [The completion receipt](receipts/APPLICATION-KERNEL-SLICE-11J-RECEIPT.md) records the final test,
  catalog, build, migration, and no-live-mutation evidence. Dynamic application write protocol
  remains deliberately owned by interaction orchestration.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Any JSON components | Every legacy value validates against its exact mapped registered contract; bytes are preserved. |
| Graph completeness | Entity, component, containment, and relationship counts/values match exactly. |
| Isolation | Edges cannot cross state spaces; relationship kinds are owner-qualified and opaque. |
| Atomicity | Late copy/effect/audit failure leaves no destination rows or binding. |
| Dry run/replay | Dry run changes no state; exact commit consumes evidence; exact replay is stable; token/source drift fails. |
| No inference | Missing or unused mappings fail; the kernel never prefixes a legacy ID itself. |
| Legacy safety | All source tables remain byte/count equivalent after commit. |
| Execution parity | Same requirements, roles, input, and seed yield byte-identical projection and sandbox output. |
| Protocol | One authenticated system commit kind, three verbs unchanged, remote MCP policy unchanged. |
| Independence | Generic components contain no D&D/application literals; zero-app hosts remain valid. |

## Verification commands

- Focused state-space-edge, ECS-effect, legacy-adoption, activation/catalog, projection, sandbox,
  protocol, authorization, and game-vocabulary guard tests.
- Fresh EF migration application/model consistency test.
- `roleplay validate catalog`; full shared/local-AI suites; isolated solution build with zero
  warnings/errors; `git diff --check`.

## Completion receipt and exit gate

Write `receipts/APPLICATION-KERNEL-SLICE-11J-RECEIPT.md`, mark this document accepted, update the
Slice 11 row/leaf to complete, and stop before dynamic application write protocol, local/remote AI
planning, vector retrieval, recipe learning, legacy-table deletion, or non-empty upgrade work.
