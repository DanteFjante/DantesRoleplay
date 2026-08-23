# Application-kernel implementation guide for coding agents

Status: **required guide for this plan; not implementation authorization**  
Master plan: [Generic application kernel dependency plan](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)

## Mandatory start procedure

For every slice:

1. Read repository `AGENTS.md` and `docs/IMPLEMENTATION_DOCUMENT_READING.md` completely.
2. Read this guide, the one active application-kernel slice, its root-to-leaf dependency path, only
   the named owner contracts/code, and prerequisite receipts used as evidence.
3. Inspect the dirty worktree and preserve unrelated/user-owned edits and moves.
4. Restate ruleset alignment, owners, allowed files/tables, forbidden work, confirmation gates,
   migration behavior, verification commands, and exact stop point.
5. If the slice is not `active` or a semantic/public/migration gate is open, change planning only or
   stop with the smallest blocking decision.
6. Search code and catalog before adding an interface, ID, schema, table, kind, alias, or source
   format. Never assume the master plan itself confirms a proposed name.
7. Do not parallelize editing unless the user or active instructions explicitly request it.

## Non-negotiable boundaries

### System/application boundary

- `system` is reserved and cannot be registered as an application.
- The kernel validates opaque application IDs and registry relationships; it never branches on the
  literal `dnd2024` or embeds game/application vocabulary.
- Every non-system component type and executable/searchable catalog record has exactly one
  application owner and a qualified key.
- Application files may declare data/contracts but may not provide arbitrary CLR implementations,
  database access, shell/network access, effects, or validation truth.
- A zero-application host must remain buildable and runnable throughout migration.

### ECS and datatype boundary

- Do not create `IComponent<TGameType>`, application DTOs, switch statements for application type
  IDs, or per-component database columns in the generic kernel.
- Represent a component value as a bounded generic JSON value plus exact type ID/version/hash.
- Preserve all JSON kinds and numeric fidelity; do not coerce values for convenience.
- `add`/`set` may store any schema-approved JSON value. `merge` accepts objects only. Null is a
  value; remove is a distinct operation.
- Resolve the effective current/pinned component contract and run generic schema validation before
  mutation. Never accept a caller/model claim that validation succeeded.
- Treat binary/large content as an external resource reference, not an ECS serialization escape.

### Version and migration boundary

- A changed application/component schema appends an immutable version and content hash.
- Do not update an existing definition's meaning in place or make old state silently inherit a new
  schema.
- State spaces bind to an exact application revision/effective-manifest fingerprint.
- A state-space upgrade is explicit and separate from application activation. Incompatible legacy
  values are reported and preserved until a confirmed migration handles them.
- Use dual reads/aliases only as temporary compatibility adapters with tests and a removal gate.

### Source and overlay boundary

- Register existing canonical allowed roots/path specifications. Do not expose arbitrary remote
  directory creation or unrestricted filesystem browsing.
- Store source application, trust, precedence, scan generation, logical identity, hash, and winner
  evidence through the approved registry owner.
- Resolve one effective winner before catalog import, lexical indexing, vector indexing, or AI
  prompting. Equal-precedence competitors fail.
- Shadowed files remain diagnostic evidence but cannot become ordinary search results or execution
  authority.
- An untrusted source cannot override a trusted source, and no application source overrides
  `system.*`.
- A rescan/reorder/removal produces a candidate application revision; it never silently mutates the
  active application.

### Transaction, protocol, and AI boundary

- Preserve existing typed effects, complete-batch validation, root transaction, guards/events,
  notifications, deterministic seed/replay, and audit ownership.
- Keep exactly `orient`, `query`, and `commit`. Proposed `system.*` kinds require confirmation and
  must be defined once with capability discovery, dispatch, examples, authorization, and protocol
  tests in agreement.
- Registration, preview, activation, and upgrades require trusted administrative authorization,
  idempotency, fingerprints, and no-change failure evidence.
- Local/remote models may propose or inspect bounded values only. They cannot register sources,
  activate applications, validate schemas, migrate state, or execute effects by assertion.
- Local AI receives opaque application/source/logical keys and generic documents only.

## Slice implementation algorithm

1. Copy one ready leaf into an implementation document using
   `docs/FEATURE_IMPLEMENTATION_AUTHORING.md`.
2. Freeze its conceptual contracts in tests before persistence or protocol work.
3. For migrations, write inventory/backfill/compatibility and rollback/no-change evidence before
   enforcing new non-null/unique/foreign-key constraints.
4. Implement pure identifier, dependency, overlay, schema, and fingerprint validation first.
5. Implement component-owned repositories behind ports second; do not reach into another
   component's concrete persistence.
6. Integrate ECS/effects transactions third, preserving existing behavior through parity tests.
7. Add protocol adapters last, after internal contracts and authorization are stable.
8. Run focused tests while iterating. Run fresh catalog validation after catalog changes.
9. At acceptance run the solution build/full suite; run the protocol walk when kinds, dispatch,
   examples, descriptions, or registrations change.
10. Inspect artifacts, run `git diff --check`, write one completion receipt, update the master leaf
    and roadmap once, and stop.

## Required failure and boundary tests

Every applicable slice proves:

- reserved/malformed/duplicate application ID, unknown/cyclic base, stale application revision,
  namespace mismatch, and cross-application access;
- source outside allowed root, traversal/reparse escape, malformed glob, inaccessible/oversized or
  changed-during-read file, duplicate precedence, ambiguous identity, untrusted override, disabled
  source, stale scan generation, and path redaction;
- invalid/oversized/recursive schema, prohibited remote `$ref`, resource exhaustion bound, every
  invalid JSON datatype case, numeric fidelity, null-versus-remove, and merge on non-object;
- unknown/inactive/stale component type, wrong type version/hash, invalid value, duplicate add,
  missing remove, stale component revision, and cross-state-space entity/type use;
- preview failure, activation fingerprint mismatch, repeated activation/idempotency key, concurrent
  registry change, state-space upgrade incompatibility, and migration rollback/no-change;
- shadowed document exclusion from import/search/vector/AI, removal revealing the lower definition
  only after explicit activation, and recipe/proposal invalidation after winner changes;
- unauthorized administrative query/commit and absence of raw host paths/hidden application data in
  remote receipts;
- system-only host resolution, non-game fixture application registration, and no application
  assembly/ID/vocabulary dependency in system or local AI.

Every rejection asserts no unauthorized state/active-manifest mutation and exact receipt/audit
evidence.

## Model selection and switch protocol

- Use **Terra High** for a confirmed, bounded slice: contracts, validators, repositories, reviewed
  migrations, deterministic overlays, schema evaluator integration, ECS ports, parity tests, and
  mechanical protocol wiring.
- Use **Sol High/Extra High** for Slice 0 semantics, ambiguous legacy ownership, migration/schema
  security review, administrative authorization/public kinds, and final independence acceptance.

Switch from Terra to Sol before continuing if:

1. application versus system ownership is ambiguous;
2. a `game.core.*` or other legacy record has no confirmed application owner;
3. two sources or stores could both be authoritative;
4. schema versioning would reinterpret existing state without an explicit migration;
5. arbitrary code/type loading or uncontrolled `$ref` resolution appears necessary;
6. application activation and state-space upgrade cannot remain separate;
7. a public kind/migration/authorization meaning exceeds the active slice;
8. transaction/replay parity conflicts with the proposed ECS seam; or
9. any semantic choice would have to be invented rather than read from confirmation/tests.

The handoff includes only the active slice/stop point, confirmed/open decisions, changed/unrelated
files, focused evidence, owner/version assumptions, and the smallest question for Sol.

## Reusable Terra kickoff prompt

```text
Implement exactly the one active application-kernel slice.

Read AGENTS.md, docs/IMPLEMENTATION_DOCUMENT_READING.md,
platform/application-kernel/APPLICATION-KERNEL-AGENT-GUIDE.md, the active slice, its root-to-leaf
master-plan path, and only named owners/receipts. Preserve the dirty worktree.

Restate the ruleset-neutral boundary, allowed files/tables, forbidden work, confirmations,
migration/no-change behavior, tests, and stop point. Reserve system.* for generic platform
administration. Treat application IDs as opaque and never hard-code dnd2024 in system/local AI.
Applications own component schemas and mechanics; the system owns generic registries, bounded JSON
Schema validation, ECS persistence, effects, transactions, audit, and protocol transport.

Support generic schema-approved JSON values without arbitrary CLR type loading. Keep merge
object-only, null distinct from remove, schemas/version hashes immutable, state spaces pinned, and
upgrades explicit. Register existing allowed path/glob sources—do not expose arbitrary directory
creation. Resolve one trust-aware overlay winner before import/search/AI; ties fail and shadowed
records never execute.

Write focused failure/no-change tests first, use apply_patch, run the active acceptance commands,
write one receipt/update, and stop. Do not implement sibling slices or invent a missing ID,
migration, public kind, ownership, or authorization rule.
```

## Sol review prompt

```text
Review the active application-kernel slice at its named Sol gate. Verify system/application/state-
space ownership, any-JSON safety, immutable schema/version semantics, legacy mapping, source trust
and deterministic overlay authority, activation versus state migration, transaction/replay parity,
administrative authorization/redaction, public compatibility, zero-app independence, and the local-
AI boundary against the master plan and current code/tests. Return concrete blocking findings and
the smallest required decision or patch. Accept only with all named evidence and the slice stop gate.
```
