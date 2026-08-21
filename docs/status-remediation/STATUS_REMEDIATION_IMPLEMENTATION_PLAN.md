# Status remediation implementation plan

Status: **Implementation-ready**  
Last reviewed: 2026-08-21

Slice S0 completed on 2026-08-21. Its evidence is recorded in
`STATUS_REMEDIATION-SLICE-S0-RECEIPT.md`: Feature 7 and protocol action recovery are green, while
the full suite has one independently classified Feature 10 transcript compatibility failure caused
by the new encounter-side fixture component. Repair that expectation before claiming full-suite
acceptance in a later slice.

## Scope and authoritative disposition

This plan resolves the items currently presented as open, blocked, or stale in `STATUS.md`. It
does not treat every deferred roadmap feature as a defect.

| Status item | Current evidence | Disposition |
| --- | --- | --- |
| Feature 7 weapon-profile regression | The named focused test currently passes. | Reconcile stale status; do not alter weapon rules unless the baseline test fails again. |
| Invalid-action generic recovery text | The status note is older than the current commit error path. | Reproduce through the public action path; correct only if the generic text is still emitted. |
| CH13 retirement/archive | The capability is planned and its campaign composition seam is the next real dependency. | Implement after verifying the existing campaign-participation owner can provide the required atomic transition. |
| Feature 23 carry/transfer | Feature 23 Slices 1–11 are accepted. | Remove the obsolete “unimplemented” statement. |
| E9 identity/authorization | Intentionally deferred; no identity provider is selected. | Keep deferred, state the concrete re-entry gate, and do not create placeholder authentication. |
| Feature 20/21 and Quest Q3 status | Current implementation/receipts contradict older status prose. | Correct status and roadmap wording using verified receipts only. |

## Slice S0 — capture one truthful baseline

1. Run `roleplay validate catalog`, `git diff --check`, the Feature 7 focused test, the public
   commit/action error test, and the full test suite from the same clean-or-documented worktree.
2. Record the exact test count and any failure names in a short receipt. Do not copy an old count
   into `STATUS.md`.
3. Classify every failure as either in scope of this plan or an unrelated concurrent worktree
   change. An unrelated failure remains visible with its owner; it is never “fixed” by weakening a
   test or changing its expected value.

Exit: every status claim below has a command/test result dated from this pass.

## Slice S1 — retire or repair the Feature 7 regression claim

1. Run only `CatalogFeature7Tests.Imported_catalog_records_corrects_and_guards_canonical_weapon_profiles`.
2. If it passes, replace the `STATUS.md` “Known current issue” entry with a dated closure stating
   that the stale `5` versus `6` report no longer reproduces. Do not change weapon fixtures,
   profile schemas, or expectations.
3. If it fails, inspect only the Shortbow catalog fixture, the weapon-profile schema/writer, and
   `AssertProfileAsync` in `CatalogFeature7Tests`.
4. Preserve the SRD Shortbow damage die (`1d6` piercing) and range (`80/320`) as separate fields.
   Change the single divergent owner—fixture if content is wrong, assertion if it is wrong, or
   writer/schema if it mutates the accepted profile. Do not modify the test merely to match a
   malformed imported record.
5. Add a regression assertion that imports the canonical Shortbow and verifies both its `damage`
   and `rangeFeet` closed shapes.

Exit: the focused test passes, catalog validation passes, and `STATUS.md` does not claim an
unreproduced Feature 7 failure.

## Slice S2 — make invalid action recovery truthful

1. Add a public-surface regression test that submits malformed `commit(kind: "action")` input.
   Assert `INVALID_PAYLOAD`, a precise `why`, and a `fix` that is a literal callable action/commit
   example—not “the rule is broken, not your arguments.”
2. Exercise malformed JSON, a non-object payload, missing required action field, unknown field,
   and a validly shaped action that fails rule validation. The first four must identify payload
   repair; the last must preserve the mechanic’s actionable domain explanation.
3. If any case emits the generic text, change the shared error-construction branch that creates
   that outcome. Do not add special cases per mechanic or per MCP tool.
4. Verify direct in-process action execution and JSON-RPC protocol-walk output have byte-equivalent
   error code/why/fix semantics where their envelopes correspond.
5. Replace the status warning with the test name and result, or retain it with the exact remaining
   failing case if the evidence proves it is not yet fixed.

Exit: malformed caller input always receives a repairable payload/action recovery, while genuine
rule failures retain their owning mechanic’s recovery; no generic blame text remains on these paths.

## Slice S3 — reconcile status and roadmap claims

1. Update the opening `STATUS.md` baseline only from Slice S0 evidence. Remove frozen historical
   test counts such as `295/295`; state the command and current result instead.
2. Replace Feature 20 Slice 4’s “path movement remains blocked” text with the verified Slice 5
   receipt: difficult terrain, encounter sides, legal passage, and final-footprint refusal.
3. Replace Feature 21’s “sides remain blocked” text with the shared
   `dnd2024.encounter-sides` foundation, while leaving cover, sight, and tactical ranged attack
   blocked under their actual owners.
4. Replace Feature 23’s carry/transfer “unimplemented” statement with its accepted Slices 1–11
   status and receipt links.
5. Reconcile `STORY_FIRST_ROADMAP.md`, Quest Feature 3 plan/receipt, and dependent status prose:
   Q3.0–Q3.1 and `quest-summary` are accepted; Campaign C4 is the remaining campaign-to-quest
   continuity bridge. Do not change the `quest-summary` public contract.
6. Move Feature 7 and invalid-action items to dated closures only when S1/S2 exit gates pass.
   Preserve deliberately deferred CH13/E9 as blocked work with their exact next gate.
7. Search `STATUS.md`, `STORY_FIRST_ROADMAP.md`, Quest Feature 3 documents, and Feature 20/21/23
   plans for the superseded claims. Update each source of truth; do not paper over conflicts with a
   new summary document.

Exit: no document calls an accepted feature blocked or calls a currently passing focused test a
current failure. Every remaining blocker names its owner and next prerequisite.

## Slice S4 — implement CH13 voluntary retirement/archive lifecycle

This slice begins only after Campaign confirms that its existing character-participation owner can
emit one root-composable, reversible-on-failure transition that removes active player-character
availability without deleting the character or its campaign history.

1. Adopt the existing CH13 vocabulary exactly: `dnd2024.character.lifecycle`,
   `procedure.character.lifecycle`, and `mechanic.dnd2024.character.lifecycle`. The component is
   closed `{ status: "active" | "retired" | "archived" }`; it holds no campaign ID, principal,
   reason, timestamps, inventory, or audit data.
2. Extend the CH5-created-character path to add `active` lifecycle state atomically. For legacy
   created characters, provide one explicitly scoped/idempotent migration or fail closed until it
   is applied; never infer `active` at read time.
3. Add lifecycle-aware readers/preconditions to CH6–CH9 consumers: retired actors reject ordinary
   correction, advancement, and player-character handoff; archived actors are omitted from ordinary
   discovery but remain available through explicit historical inspection.
4. Add the closed action request
   `{ operation: "validate" | "retire" | "archive", characterId, expectedStatus }`.
   Resolve campaign scope from the existing attachment, not caller input. Allow only
   `active → retired → archived`.
5. Compose exactly one Campaign participation transition plus exactly one lifecycle component set
   in the existing root transaction. Preserve character profile, sources, abilities, class data,
   items/containment, world location, campaign history, and operation history byte-for-byte.
6. Test fresh creation, legacy migration/refusal, all valid/invalid state edges, missing/duplicate/
   cross-campaign attachments, stale requests, inactive campaign/context, event/audit/guard/reaction
   failure injection, rollback, replay, fresh-host historical inspection, and no D&D death/resource
   semantics.
7. Run focused CH13/Campaign/CH5–CH9 tests, catalog validation, the full suite, and protocol walk
   only if action/query registration changes. Record a receipt and update `STATUS.md`.

Exit: retirement/archive is monotonic, auditable, campaign-composed, and atomic; no failed request
leaves either participation or lifecycle partially changed.

## Slice S5 — retain E9 as a secure deferred gate

E9 cannot be safely implemented independently because its first prerequisite is a product/security
choice: the real identity provider and the shared authorization interception boundary. Do not use a
payload principal ID, display name, campaign attachment, local username, or a catalog component as
a substitute.

1. Update `STATUS.md` to say E9 is deliberately deferred, not an unowned defect.
2. When identity work is authorized, first create an E9-0 decision record selecting one provider,
   trusted token/session validation method, anonymous behavior, principal identifier, revocation
   behavior, audit retention, and MCP/HTTP parity boundary.
3. Only after E9-0 approval, implement the existing E9 slices in order: provider adapter and
   immutable principal result; request-context propagation; deny-by-default shared authorization
   hook; then one Campaign/CH14 consumer.
4. Each slice must prove forged/missing/expired/cross-principal context denial, no caller-controlled
   identity fields, audit privacy, and parity across all enabled transports before a consumer is
   enabled.

Exit: until E9-0 is explicitly authorized, status accurately presents it as deferred. After E9-0,
the provider-backed shared hook—not an ad hoc feature check—becomes the only implementation path.

## Final acceptance

1. Run `git diff --check`, `roleplay validate catalog` after catalog changes, focused tests for
   every changed owner, and the complete test suite.
2. Re-run the public protocol walk only when MCP registration or transport behavior changes.
3. Re-read the status/roadmap files and ensure their claims match receipts and command results.
4. Leave live database import out of ordinary development; it is permitted only at the existing
   integration/release synchronization boundary.
