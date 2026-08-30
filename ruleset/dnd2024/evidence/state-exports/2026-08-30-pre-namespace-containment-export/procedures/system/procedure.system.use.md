---
id: procedure.system.use
category: system
name: Use this system
governs: orient(), query(kind: "capabilities"), any session operating this system
status: active
createdBy: "seed"
changeNote: "Re-seeded: the bootstrap file changed."
---

## Description
How to operate this system through its three verbs. `orient` tells you where you are, `query`
reads anything, `commit` changes anything. Nothing else exists.

## Instructions
1. Call `orient()` first. It states what this system is, what exists in it right now, what is not
   built, and which call to make next. Call it again whenever you lose track — it is cheap.
2. Read with `query(kind: ...)`. The kinds are `capabilities`, `procedures`, `categories`, `world`, `entities`,
   `graph`, `journey-plan`, `itinerary-plan`, `campaign-resume`, `session-recap`, `quest-summary`, `knowledge-answer`, `information-answer`, `information-actions`, `story-plan`, `system.audience-context`, `mechanics`, `event-types`, `events`, `subscriptions`, `notifications`, `feedback`, `system.applications`, `system.sources`, `system.application-preview`, `system.dependencies`, `system.catalogs`, `system.catalog.browse`, `system.catalog.search`, `system.catalog.record`, `system.feature-search`, `system.interaction-plan`, `system.interaction-receipt`, `system.interaction-recipes`, `system.trigger-scheduling`, and `history`. No `id` returns a list or search; `id` returns one record in full; fixed planning/summary kinds state their required ID and reject unrelated filters in their own contract.
   `version` with `id` returns an older revision. Read the full record before revising anything —
   a summary is not the thing itself.
   Use `query(kind: "categories", catalog: "procedures")` or
   `query(kind: "categories", catalog: "mechanics")` to browse one category level at a time.
   Its `category` is a branch — the selected path and its descendants — and the record lists
   accept the same branch filter.
   Private-host administrators can inspect immutable registrations with
   `query(kind: "system.applications")`, then inspect one application's relative source stack and
   latest scan evidence with `query(kind: "system.sources", applicationId: "...")`. These calls
   authenticate from the MCP transport; never place a principal, role, or credential in tool input.
   Before activation work, use
   `query(kind: "system.application-preview", applicationId: "...")` to scan the registered
   allowed-root-relative paths/globs and inspect the candidate fingerprint, winners, shadows, and
   closed problems. Canonical root paths come only from host configuration and never from this call.
   Use `query(kind: "system.dependencies", applicationId: "...")` to inventory declared exact
   component-field and projection dependencies. Supply a returned canonical node `id` to traverse
   its direct or transitive dependents. The response names consumer kinds not yet indexed; the
   system never guesses dependencies by parsing JavaScript or filenames.
   In the private web workspace, ordinary **Ask** remains read-only. **Plan task** may resolve a
   bounded `system.*` administration intent through read-only discovery rounds, or an authorized
   outer assistant may submit an ordered semantic agenda. Treat the returned plan as inert: review
   every exact capability, owner, step, and plan fingerprint before choosing **Confirm and run**.
   Confirmation expires after five minutes, current authority and owner preconditions are checked
   again before each write, and every step receives a durable receipt. If a later step fails,
   earlier successful owner transactions remain committed; never claim a cross-step rollback.
   For an application catalog, start with
   `query(kind: "system.catalogs", applicationId: "...")`, browse one described logical node with
   `system.catalog.browse`, search without vectors using `system.catalog.search`, then inspect the
   exact effective contract using `system.catalog.record`. Continue pages only with the returned
   cursor; restart at the root if the cursor is stale.
   For intent-oriented discovery, use `system.feature-search` within one application. Resolve an
   inert proposal with `system.interaction-plan` operation `resolve`, or submit a caller-built
   proposal through the same verifier with operation `submit`. Inspect its durable evidence with
   `system.interaction-receipt`. Private operators may inspect learned routes with
   `system.interaction-recipes`; candidate routes are inert until explicitly verified. The only
   automatic verification path is the deterministic host policy for an explicitly opted-in,
   value-free action route whose durable receipts prove one eligible inner non-resolution followed
   by one completely successful correlated outer fallback. Query steps, result bindings, non-empty
   inputs, old entity values, and model review decisions are never eligible. A verified route may
   guide later trusted search and inspection but remains non-executable until a new current proposal
   passes the common verifier. Do not execute until an operator explicitly confirms the exact
   receipt ID, proposal fingerprint, full proposal, application, and state-space scope.
   Private operators inspect scheduling with `system.trigger-scheduling`. Omit `applicationId` for
   bounded application summaries; otherwise select one closed `resource`: `overview`, `structures`,
   `sources`, `devices`, `one-time`, `recurring`, `conditional`, `observation-triggers`,
   `observations`, `fires`, or `phone-principal`. These projections never recover a phone
   credential or expose stored verifiers, raw observation JSON, transport headers, or leases.
   A local player chat begins with `query(kind: "system.audience-context")`. It accepts no
   caller-selected identity and returns only the current host-authorized application, state-space,
   campaign, and actor binding when one is available.
3. When you do not know a payload shape or which parameters a kind reads, call
   `query(kind: "capabilities")`. It is the exact catalog, and it is generated from the same
   structure the two dispatchers switch on, so it cannot describe a kind that does not work.
   Never guess a kind or a shape.
4. Before any `commit`, find and read the contract governing it:
   `query(kind: "procedures")` lists the manual, and each entry states what it governs — match
   that against the commit you are about to make. Then cite what you read in `proceduresUsed` and
   say what you are doing in `intent`, in your own words. The audit records both, and records
   separately which contracts you actually opened.
5. Change with `commit(kind: ..., payload: ...)`. The generic-host kinds are `component`,
   `effects`, `mechanic`, `action`, `system.application.register`, `system.component-type.register`, `system.source.register`, and
   `system.application.activate`, `system.state-space.create`, `system.state-space.upgrade`,
   `system.state-space.adopt-legacy`, `system.world-state.sync`, `system.interaction-execute`,
   `system.interaction-recipe-review`, `system.trigger-scheduling`, and
   `system.knowledge-state.sync`. Every `system.*` kind
   authenticates from the transport. Registry, activation, component-type, and state-space
   administration commits require a 32-character lowercase hexadecimal `requestToken`.
   `system.interaction-execute` instead requires a distinct bounded idempotency key and the exact
   prior resolution evidence; equal retries replay and conflicting reuse fails. Its `learn` option
   is false by default and requires the exact original `learningIntent` when true. Review other
   candidates only with `system.interaction-recipe-review`; manual and deterministic verification
   and retirement are append-only and request-token replay protected. Application and source registration require
   `expectedFingerprint`: use `null` only when the target must be absent, otherwise use the exact
   current fingerprint from the corresponding `system.applications` or `system.sources` query.
   `system.component-type.register` accepts only an already registered application, an owner-qualified
   type ID, its raw JSON schema, and `expectedSchemaHash`: use `null` only when that type is absent,
   otherwise use the latest exact schema hash returned by the registration receipt. It derives the
   profile, normalized schema, version, and hash itself; old schemas cannot roll a type backward.
   Register sources only beneath a configured
   `allowedRootId` using a relative path or glob; registration never creates or scans a directory.
   Activate only the exact valid `previewFingerprint` returned by `system.application-preview`.
   Supply `expectedActiveFingerprint: null` only when no overlay is active; otherwise copy the
   exact current activation fingerprint from `system.applications`. Activation retains redacted
   source/winner hashes but neither imports nor makes source files executable, and its dependency
   coverage remains explicitly incomplete.
   Create a state space only after activation, using the exact `activeFingerprint` returned by
   `system.applications`, a new globally unique `stateSpaceId`, and `expectedFingerprint: null`.
   Creation binds one empty isolated runtime instance to that immutable application evidence; it
   does not create entities/components, upgrade an existing space, or migrate legacy state.
   Upgrade only an empty state space using the exact current `activeFingerprint` and the space's
   current `bindingFingerprint` from `system.applications`. The system records zero entity and
   component counts as compatibility evidence. Any non-empty space returns `MIGRATION_REQUIRED`;
   there is no caller-supplied migration or compatibility override.
   Adopt legacy state only into a new state space, after registering every exact destination
   component contract and activating the application. Supply a complete explicit mapping for
   every used legacy component definition and relationship kind. Dry run fingerprints the entire
   source graph; commit copies it atomically and leaves all legacy rows unchanged. Never infer a
   mapping or retry after `DRY_RUN_STALE` without a new dry run.
   Synchronize reviewed application World state only with `system.world-state.sync`. It accepts one
   exact root-scoped additive/update-only manifest, resolves current application component types and
   revisions, and delegates one atomic typed-effects transaction. New entities must terminate below
   the selected existing root; existing entities and relationship endpoints must already be in that
   root. It cannot delete, remove, rename, register schemas, or accept raw effects. Dry-run the exact
   manifest first, then commit the identical payload and read back every affected record.
   Trigger scheduling accepts exactly `{requestToken, operation, applicationId, value}`. The closed
   operations are `structure.register`, `source.register`, `one-time.register`,
   `recurring.register`, `conditional.register`, `observation-trigger.register`, `phone.register`,
   and `phone.revoke`. The value cannot contain effects, events, actions, code, destinations,
   authorization, current pointers, receipts, or observations. A structure command synchronizes
   an already reviewed catalog-authored schema into live SQLite; it never edits catalog files.
   Preview the identical command first. A phone credential appears only in the first successful
   registration response, so copy it then; replay and every query intentionally omit it.
   Reviewed knowledge-state synchronization accepts exactly
   `{requestToken, campaignId, entries:[{knowledgeId,state}]}`. The private host resolves the actor
   from ambient policy, validates exact campaign participation and canonical campaign-world
   membership, and atomically applies only the reviewed entries through the application ECS effect
   owner. It never accepts an actor, role, world, visibility, sensitivity, or baseline override,
   and it never infers knowledge from record visibility or presence. Dry-run the identical manifest
   first and confirm its reviewed/change counts before committing it.
   Dry-run the identical administrative payload first, then confirm it through the matching query
   where a query exists; component-type registration returns its immutable receipt directly.
   Retained application adapters may additionally expose `procedure`,
   `effects`, `mechanic`, `event-type`, `subscription`, `action`, `itinerary-advance`, `campaign`, `quest`, `notification`, `feedback`, `information-source`, `information-record`, `information-action-contract`, `information-action`, `story-plan`. `campaign` validates or creates a closed existing-world campaign blueprint; `quest` creates one closed campaign-scoped draft quest. Neither accepts caller-supplied effects. `event-type` registers a schema only; a `subscription` registers middleware. `information-source` and `information-record` store neutral scoped material; `information-action-contract` declares a schema-validated host executor, and `information-action` runs that declared contract. Registered guards run before a world change commits and can veto it, an accepted change records structural events readable with `query(kind: "events")`, and registered reactions run on those events with their effects committing in the same transaction, and may declare an event or raise a notification. `notification` moves one notice between `unread`, `read` and `archived` and cannot change what it says. `feedback` records append-only testing feedback about the host system and never changes game state. `story-plan` starts/cancels one development-GM bounded semantic plan; its backend executes context, knowledge, and action steps serially. `payload` is a JSON object encoded as a string — the whole
   object in one argument, not loose named arguments. Where `dryRun` is supported, ALWAYS call
   with `dryRun: true` first and read every named check or problem that comes back; then commit
   the identical payload. A dry run you did not read is worse than none.
6. Treat every failure as an instruction: the `fix` field names the literal next call, and a
   rejected payload comes back with the shape you needed inside the error. Make that call rather
   than retrying blind or giving up.
7. After a commit, confirm: query back what you wrote, and quote the returned `operationId` when
   reporting what you did.

## Constraints
- `query` never changes state. `commit` is the only write path. Raw generic world changes use
  `commit(kind: "effects")` or `commit(kind: "action")`; the closed reviewed knowledge-state sync
  delegates its validated edge batch to the same generic application ECS transaction owner.
- Never invent a kind, a parameter or a payload field. If it is not in
  `query(kind: "capabilities")`, it does not exist.
- Never commit a payload that differs from the one that passed its dry run.
- Never report an outcome you did not confirm with a query.
- If `orient()` says a capability is not built, believe it over anything a contract or your prior
  experience suggests.

