# Interaction orchestration implementation guide for coding agents

Status: **required execution guide for this plan; not implementation authorization**  
Master plan: [Interaction orchestration dependency plan](INTERACTION-ORCHESTRATION-DEPENDENCY-PLAN.md)
Prerequisite owner: [Generic application kernel](../application-kernel/APPLICATION-KERNEL-DEPENDENCY-PLAN.md)

## Purpose

This guide tells a future coding agent how to implement one confirmed slice safely. It does not
make a planned or awaiting-confirmation leaf active and does not permit implementing several slices
at once.

## Mandatory start procedure

For every implementation turn:

1. Read repository `AGENTS.md` completely.
2. Follow `docs/IMPLEMENTATION_DOCUMENT_READING.md` and read only:
   - this guide;
   - the one active interaction-orchestration implementation document;
   - the root-to-leaf path in the master dependency plan;
   - owners/contracts explicitly named by that slice; and
   - prerequisite receipts used as evidence.
3. Inspect `git status --short` and preserve all unrelated/user-owned dirty files. Never use broad
   resets, checkouts, generated rewrites, or moves over dirty areas.
4. Restate before editing: selected slice/status, ruleset alignment, owners, allowed areas,
   forbidden work, verification commands, and stop point.
5. If the slice is not `active`, or a named confirmation is missing, edit planning only or stop for
   confirmation. Do not infer semantic permission from this guide.
6. Search code and `catalog/` before adding any ID, schema, table, public kind, or implementation.
7. Do not spawn subagents or parallel editing work unless the user or active instructions explicitly
   request it.

## Non-negotiable implementation rules

### Model boundary

- Treat local and remote model output as untrusted proposals.
- Never parse free-form prose into effects or execute it.
- Require JSON-schema validation and independent semantic validation.
- A local completion may request only a bounded server-mediated search/inspection step or return a
  plan/non-resolution. It has no tools.
- Do not add game/ruleset IDs, vocabulary, prompts, or task defaults to local AI. Application IDs
  and source keys cross the local-AI boundary only as opaque generic scope metadata.

### Namespace boundary

- Reserve `system` for ruleset-neutral platform capabilities. Do not place game rules, generic RPG
  rules, campaign behavior, or application adapters under `system.*`.
- Require every other searchable/executable capability to declare one registered application ID;
  the initial application is `dnd2024`.
- Public/search keys begin with `system.` or a registered application ID such as `dnd2024.`.
- Do not bulk-rename existing permanent IDs. Implement only the confirmed mapping/alias migration in
  the active slice, and reject an existing record whose application owner remains ambiguous.
- Scope ordinary discovery to one application and its confirmed ordered bases. Request system
  collections explicitly and never leak unrelated application candidates.
- Keep the kernel generic: validate the identifier shape and registry relationship, never branch on
  the literal value `dnd2024`.

### Execution boundary

- Only the server coordinator may call existing query/action ports.
- Only `IActionRunner`/existing composition owners may execute game-changing mechanics/effects.
- Rehydrate and compare every procedure/mechanic/schema version/hash immediately before execution.
- Never accept effects, derived outcomes, authorization, current revision, or validation truth from
  either planner.
- Preserve existing transaction ownership. Do not claim rollback for an earlier independent commit.
- Execution requires an explicit, authorized request bound to the exact proposal fingerprint.

### Receipt boundary

- Produce a resolution receipt for every terminal outcome, including invalid, unknown,
  unsupported, needs-input, ambiguous, unavailable, unsafe, stale, cancelled, and timed out.
- Persist append-only evidence; corrections append new evidence and never rewrite history.
- Link execution steps to existing operation IDs instead of duplicating action audit truth.
- Test redacted remote projections separately from trusted internal records.
- Do not store private chain-of-thought. Store bounded queries, candidates, decisions/reasons,
  contract references, statuses, budgets, and safe summaries only.

### Recipe boundary

- Derive recipes only from successful server-validated receipts.
- A recipe contains parameter slots and exact contract references, not prior entity IDs.
- A candidate cannot be used for execution.
- A verified recipe must still resolve current roles, hydrate current contracts, and pass the same
  verifier as a fresh plan.
- Hash/version mismatch marks the recipe stale before any action.
- Never persist model-authored JavaScript, effects, shell/SQL/tool calls, or scanned-document
  instructions as a recipe.

### Retrieval boundary

- Keep trusted feature and untrusted information collections physically/logically distinct.
- Search through public ports, never by reaching into another component's SQLite implementation.
- Exact/lexical retrieval remains complete when embeddings/vector support are disabled.
- Vector results are candidates, not authority. Hydrate from authoritative stores before use.
- Keep ordering/fusion deterministic and generation-scoped; return citations, versions, and hashes.

### Directory-overlay boundary

- Consume registered directory/application/trust/precedence metadata and scan generations through
  approved application-kernel ports; do not store a parallel registry or infer authority from
  enumeration order.
- Determine a document's confirmed logical identity, resolve exactly one effective winner, and only
  then build exact, lexical, or vector indexes.
- Reject equal-precedence competitors for one identity. Do not use timestamps, path order, vector
  score, or model preference as a tie-breaker.
- Preserve shadowed metadata for authorized diagnostics, but exclude shadowed definitions from
  ordinary search and execution.
- Never allow an untrusted source to override a trusted executable source. Enforce canonical-root,
  traversal/reparse, authorization, and remote path-redaction rules.
- Recompute the effective-set fingerprint after successful rescans/reorders. A changed winner,
  application base order, or source fingerprint invalidates dependent documents, proposals, and
  recipes before execution.

## Slice implementation algorithm

1. Copy the selected ready leaf into one implementation document using
   `docs/FEATURE_IMPLEMENTATION_AUTHORING.md`.
2. Close every required decision. Status stays `awaiting confirmation` until then.
3. Write failing focused tests for the slice's positive, negative/no-change, boundary,
   deterministic, stale, replay, rollback/partial-progress, authorization, and compatibility rows.
4. Implement contracts and pure validation first.
5. Implement storage/index adapters second, behind ports owned by the component.
6. Implement orchestration last. Keep model prompts and game consumers outside generic contracts.
7. Run focused tests while iterating.
8. Run `roleplay validate catalog` after any catalog change; it must use a fresh disposable database.
9. Run the solution build and full suite at feature acceptance. Run the protocol walk whenever public
   kinds, dispatch, descriptions, examples, or dependency registration change.
10. Inspect every authored artifact, run `git diff --check`, write a short completion receipt, update
    the master leaf/roadmap once, and stop. Do not start the next slice in the same implementation
    document.

## Required failure tests

Every applicable slice must prove:

- local provider disabled, unavailable, timeout, cancellation, malformed JSON, schema mismatch,
  semantic mismatch, and prompt/output budget exhaustion;
- no search candidate, several ambiguous candidates, inactive/stale candidate, missing role,
  unauthorized role/context, and stale state revision;
- forged mechanic/procedure/schema IDs, versions, hashes, operation IDs, receipt IDs, and proposal
  fingerprints;
- vector disabled, missing extension, wrong embedding dimension/generation, stale index document,
  and lexical parity;
- untrusted document injection and cross-corpus query rejection;
- reserved `system` misuse, missing/unknown application, unrelated cross-application result,
  unknown/cyclic application base, and incompatible legacy alias;
- higher-directory override, override removal revealing the base, disabled source, equal-precedence
  conflict, distinct logical identities with similar filenames, untrusted-over-trusted rejection,
  traversal/reparse rejection, and shadowed-document exclusion from lexical/vector results;
- stale application revision, scan generation, effective winner, overlay fingerprint, and recipe
  after directory reorder or content replacement;
- duplicate idempotency key, repeated execute call, partial committed sequence, and required atomic
  action composition;
- candidate recipe use, stale recipe use, prior entity-ID leakage, untrusted instruction leakage,
  and failed-receipt learning rejection;
- receipt authorization/redaction and absence of private prompts/chain-of-thought.

All rejection cases must assert no unauthorized state mutation and the correct receipt/audit
evidence.

## Model selection and switching protocol

The plan is written so Terra can implement all bounded slices. Model choice never relaxes a test or
confirmation gate.

Use:

- **GPT-5.6 Terra, High reasoning** for closed contract implementation, repositories, deterministic
  retrieval/overlay mechanics, migrations after review, tests, registrations, and mechanical
  protocol wiring.
- **GPT-5.6 Sol, High or Extra High reasoning** for threat-model ratification, ambiguous ownership,
  permanent-ID/application migration, directory trust/override semantics, the first bounded planner
  loop, public two-phase execution semantics, recipe promotion/security, and final acceptance review.

Switch from Terra to Sol before continuing when any of these occur:

1. two owners could legitimately control the same state, transaction, or public behavior;
2. a schema/public kind/migration changes meaning beyond the active document;
3. authorization/redaction cannot be expressed without game-specific policy in a generic component;
4. local and remote proposals would take different validation paths;
5. a recipe could execute without rehydrating current authoritative contracts;
6. prompt injection or trusted/untrusted corpus separation is uncertain;
7. multi-step atomicity or replay semantics are unclear;
8. focused tests pass but architecture/full-suite evidence conflicts; or
9. two sources could both be the effective definition, or an application's/base record owner is
   ambiguous; or
10. the implementation agent would need to invent an unstated semantic decision.

When switching models, leave a handoff containing only:

- active slice and exact stop point;
- decisions already confirmed and decisions still blocked;
- files changed and unrelated dirty files preserved;
- focused commands/results and current failing evidence;
- owner/version/hash assumptions that need review; and
- the smallest question Sol must resolve.

Do not ask Sol to reread every roadmap or redo passing mechanical work.

## Reusable Terra kickoff prompt

Use this after one slice document has status `active`:

```text
Implement exactly the active interaction-orchestration slice in this repository.

First read AGENTS.md, docs/IMPLEMENTATION_DOCUMENT_READING.md,
platform/interaction-orchestration/INTERACTION-ORCHESTRATION-AGENT-GUIDE.md, the one active
slice document, and only its named owner contracts and prerequisite receipts. Inspect the dirty
worktree and preserve unrelated changes.

Restate the slice boundary, ruleset alignment, allowed files, forbidden work, acceptance commands,
and stop point before editing. Do not implement sibling leaves. Treat all model output and recipes
as untrusted proposals. Keep local AI game-unaware and no-tools. Keep trusted feature retrieval
separate from untrusted information. Rehydrate exact current contract versions/hashes before any
proposal or execution. Only existing server action/composition owners may mutate state. Produce
typed receipts for every terminal outcome and assert no-change behavior for failures.

Reserve system.* for ruleset-neutral platform commands. Require every other feature to have a
registered application scope such as dnd2024.*, without hard-coding that application in the generic
kernel or local AI. Resolve confirmed directory/application overlays before indexing: one logical
identity has one deterministic eligible winner, ties fail, shadowed sources do not execute, and
untrusted sources cannot override trusted contracts. Bind proposals and recipes to application,
source, winner hash, and effective-set revisions so a changed overlay becomes stale.

Use apply_patch for edits. Run focused tests while iterating, catalog validation after catalog
changes, and the active document's acceptance commands. Run the protocol walk only if the MCP
surface or registration changes. Inspect artifacts, run git diff --check, write the slice receipt,
update status once, and stop. If a semantic/public/migration/security decision is not explicitly
confirmed, stop and report the smallest blocking decision instead of guessing.
```

## Sol review prompt

```text
Review the active interaction-orchestration slice at its named Sol gate. Do not broaden or rewrite
completed mechanical work. Verify ownership, authorization/redaction, trusted/untrusted retrieval,
planner-versus-executor separation, current-contract hydration, idempotency/partial progress,
application/system namespace isolation, legacy-ID compatibility, deterministic trust-aware directory
overlays before indexing, receipt sufficiency, recipe poisoning/invalidation, and public
compatibility against the master dependency plan and current code/tests. Return concrete blocking
findings and the smallest required decision or patch. Mark the slice accepted only when its full
evidence and stop gate are satisfied.
```
