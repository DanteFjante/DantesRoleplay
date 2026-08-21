# Character Feature 12 dependency plan — governed multiclassing

Status: **Planned; implementation awaits the ruleset multiclass owner, accepted CH10 spellcasting foundation, a higher-level CH9 transition, and a confirmed singular-to-plural membership migration.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH4–CH11, and D&D 2024 ruleset roadmap features 27, 31, and 36. It writes no runtime artifact.

CH12 owns the data-model migration and one atomic acquisition of a second class. It is not a second-class checkbox on CH4. It preserves one authoritative representation of class memberships, leaves total character level authoritative, applies actual multiclass prerequisites/grants through ruleset owners, and consumes the same campaign advancement authorization as an ordinary level-up.

## Target capability

A played, active, single-class character at one source-approved total level can select one legal second non-spellcasting class from a closed multiclass choice set and advance atomically: total level rises by one, the new class begins at class level 1, immutable grants resolve once, and all class membership/progression state has one canonical representation. The system migrates existing CH4 single-membership actors in the same root transaction, with no period in which the old and new membership models both claim authority.

The first fixture is an approved **two-class, non-spellcasting** combination: a character at one ratified prerequisite/total level gains one named second class at level 1. It proves a reusable ordered plural membership model, not arbitrary class combinations, repeated class choices, subclass handling, spell-slot aggregation, every class-level grant, or respec.

### Included

- One authoritative plural class-membership representation, a compatibility migration from CH4's singular record, and revision of future CH4/CH5 creation to write the plural form only.
- One closed second-class selection keyed to immutable source definitions, source-cited multiclass prerequisites, and a ruleset Feature 27 eligibility resolver.
- One CH9-root multiclass transition from supported total level `N` to `N+1`, with new class level `0→1`, campaign authorization, HP/grants/items/features only through their named owners, and the shared progression receipt.
- Reuse of CH3 grant/choice receipts and the CH9 `dnd2024.character.advancement-receipts` transition record; no parallel multiclass receipt or copied class grant state.
- Explicit integration with the accepted CH10 spellcasting foundation even though the initial fixture is non-spellcasting, so the model is not later replaced to cope with caster interactions.

### Excluded

- Free-text or arbitrary class selection, caller-supplied prerequisite evidence, target total/class levels, HP/AC/proficiency/ability calculations, class grant effects, raw component payloads/effects, or direct membership edits.
- Adding a third class, a second level in either class after the first multiclass transition, class removal/replacement/reorder, downgrade, retraining/respec, or an unsourced migration.
- Spellcasting class selection, multiclass spell-slot aggregation, pact/alternative casting models, spell preparation/known lists, subclass choice, feats/ASIs, item-granted class behavior, optional/homebrew rules, or public UI/tool work.
- Maintaining CH4's singular membership beside a plural list, treating list order as rules text, copying total level into each membership, a second campaign authorization, or a nested action transaction.

## Ownership and migration result

| Concern | Authoritative owner and CH12 rule |
| --- | --- |
| Total character level | Existing `dnd2024.character-level`; one guarded CH9 transition raises it. It remains the total of all class levels and is never copied into membership records. |
| Per-class identity/level | CH12 replaces the CH4 singular membership with the plural `dnd2024.character.class-memberships` model after semantic confirmation. Once migration succeeds, the singular component is absent and invalid for that actor. |
| Migration | CH12 owns migration of every in-scope active CH4 actor before or during its next class-affecting transaction, plus revision of CH4/CH5 new creation. It is an atomic structural migration, not a read-time fallback. |
| Multiclass eligibility, prerequisites, class-grant/HP semantics | Ruleset Feature 27. CH12 provides immutable candidates and current state; it neither implements rules text nor accepts a host declaration that prerequisites pass. |
| Campaign advancement decision | Campaign authorization/atomic consume contract used by CH9. It authorizes exact total-level `N→N+1`, not a special second-class bypass. |
| Class source definitions, choices, dedupe | CH1/CH3/CH4 immutable content definitions, generic choice/grant declarations and receipts. The second class is selected by a source-bound stable choice key—not an unconstrained entity ID. |
| Progression provenance | CH9 advancement receipt records the selected class's `0→1` transition. Its unique `(classDefinitionId, toClassLevel)` key prevents a second acquisition; it is not replaced by a multiclass receipt. |
| Spellcasting identity and resource aggregation | CH10 and ruleset Features 31–32. Their accepted state model is a prerequisite; CH12's first non-caster fixture does not calculate or store a future caster aggregation. |
| Feat/ASI and subclass entitlements | CH11/Feature 28 and Feature 27. They remain absent unless the exact class-level transition has a separately accepted owner. |

The plural model is required even if the first fixture has only two entries. A generic `classIds` array or copied class-level map is not sufficient: it would not preserve versioned definitions, a class's own level, deterministic acquisition order, or a migration invariant. Conversely, a permissive plural schema does not make a third class or unsupported class source legal; the resolver and source fixtures remain closed.

## Canonical membership migration

The proposed replacement actor component is `dnd2024.character.class-memberships`:

~~~text
{
  memberships: [
    {
      classDefinitionId: canonical immutable class-definition ID,
      classLevel: integer 1–20,
      acquiredOrder: positive integer
    }
  ]
}
~~~

`memberships` is stored in ascending `acquiredOrder`, which is contiguous from 1 and unique. `classDefinitionId` is unique across the array. Each record's `classLevel` is 1–20; the sum equals the existing total-character-level value. Initial creation has exactly one entry at order 1 / level 1; the first CH12 fixture appends exactly one new definition at the next order / level 1. Acquisition order is an evidence/order key only; class rules come from the immutable referenced definition, not its position.

The exact component ID/schema and transition mechanism remain confirmation-required because they change a permanent actor representation. No migrated actor may retain `dnd2024.character.class-membership`; effects must add/set the complete plural component and remove the singular component in the same confirmed root transaction. New character creation is revised in that same release to create only plural membership. Read projections reject missing, duplicate, empty, noncontiguous, noncanonical, wrong-kind, class-level/total-level mismatch, or both/neither representation as corrupt state. There is no silent on-read conversion.

If release sequencing requires a one-time bulk migration, it must use the same resolver/effect bundle in batches with preflight, rollback/recovery, readback, idempotence, and an explicit campaign scope—not raw database mutation. If that cannot be proven atomically per actor, do not enable multiclass actions or revise CH4 creation.

## Source and fixture boundary

Before implementation, ratify the exact SRD 5.2.1 locators for multiclass prerequisites, the first and second class entries, total-level/class-level grant rules, hit-point rule, all starting new-class grants, and every class feature/equipment consequence. Record source edition, locator, immutable class-definition/version IDs, exact total-level transition, candidate-choice set, and each owner mapping in the implementation receipt.

The reusable capability is a versioned ordered set of class memberships and a source-bound second-class selection. The only accepted source fixture is one two-class non-spellcasting combination at one total-level transition. A different prerequisite attribute, selection form, initial class grant, class pair, spellcasting model, subclass, or later class-level grant requires ratified sources, owners, and CH7 expansion evidence; it is not enabled by the new schema alone.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Plural actor state | `dnd2024.character.class-memberships`, replacing—not augmenting—the CH4 singular membership component. |
| Migration/record procedure | Revision of `procedure.mechanic.dnd2024.class`, with a confirmed migration/record mechanism; exact mechanic ID remains undecided until the existing class procedure and effect-composition owner are re-read. |
| Multiclass resolver | `mechanic.dnd2024.class.multiclass.resolve`, a zero-effect Feature-27 child resolver that validates closed candidate/prerequisite/grant/HP results. |
| Character integration contract | `procedure.character.multiclass`, governing source-bound class selection and the CH9 advancement extension; it owns no rules formula or independent write action. |
| Class-choice declaration | `dnd2024.character.multiclass-choice-declaration`, attached to immutable class/content only if Feature 27 does not already own the candidate declaration. It contains closed source-bound candidate keys, not actor state or prerequisites. |

Confirm IDs, parent scopes, migration release strategy, canonical ordering, CH4/CH5 revision, Feature 27 eligibility/HP/grant contract, CH9 request extension, and interaction with Feature 31 under `procedure.system.modify`. If Ruleset Feature 27 owns class membership or candidate declarations, use its vocabulary and amend CH4/CH9 rather than authoring parallel CH12 components. A new public action or transport still needs CH6/protocol and `procedure.mcp.add-tool` confirmation.

## Closed request/result boundary

CH12 extends the existing CH9 `procedure.character.advance` only after the migration and eligibility contract are confirmed; it does not add a separate `multiclass` write route. Its request keeps `operation`, `characterId`, and the stale expected total/class state guard from CH9. For this branch it adds only an immutable, source-bound `multiclassChoiceKey` from the approved candidate declaration. It accepts no class entity ID, arbitrary class name, target level, prerequisite proof, campaign ID, spell-slot data, final derived state, raw effect, or migration switch.

If a required candidate choice is absent, return `incomplete`. Unknown, duplicate, malformed, cross-class, unavailable, prerequisite-failing, stale, or already-held choice returns a stable correction. `validate` performs migration-readiness, eligibility, all grants, and owner dry runs but emits zero effects and consumes no campaign authorization. `advance` repeats resolution under the root transaction; no cached preview, client selection, or precomputed migration is trusted.

The canonical success result adds only `multiclassed: true`, sorted immutable source IDs, ordered class definition IDs with their authoritative class levels, applied sorted grant keys, existing CH9 receipt presence, and actual next action. It returns no raw prerequisite calculation, campaign decision, spell calculation, source prose, effect bundle, final HP/AC, or audit/event ID. Readback serves the plural projection alone after migration.

## Resolution and transaction rules

1. Resolve one existing active actor through its campaign attachment, all current class state, total level, CH9 receipt history, and source definitions. Require either one valid singular CH4 membership eligible for migration or one valid plural representation; reject both/neither/corrupt state.
2. Resolve one current campaign authorization for exact total level `N→N+1`. Resolve the immutable candidate declaration and supplied closed choice key. Ruleset Feature 27 evaluates actual multiclass prerequisites and the full existing class set; it returns no success based on caller-provided ability data.
3. Resolve the new class's level-one source declaration, all choices/grants/HP effects, and every dependent owner in dry-run mode. Reject duplicate class, an unsupported spellcasting profile, source/archive/version mismatch, feature/item owner absence, ambiguous grant overlap, prior `0→1` receipt, or membership/total-level inconsistency.
4. For `validate`, return the named preview/correction with no migration, authorization consumption, class state, grants, receipt, event, or audit success. For `advance`, repeat all checks inside one ActionRunner root.
5. Apply the confirmed canonical bundle: campaign authorization consume; singular-to-plural migration if needed; append/transition the selected class to level 1; guarded total-level transition; HP and named feature/item owner effects; CH3 grant receipts; CH9 `0→1` advancement receipt last. All membership effects are a coherent replacement—not a merge that risks stale entries.
6. Root audit/events follow CH9. A failure at eligibility, migration, level/HP/grant/item, receipt, event, audit, cancellation, or timeout rolls back authorization and every character change. Repeating a completed choice cannot add the class, grants, or receipt twice.

The exact placement of migration relative to child resolver calls is a confirmed composition concern: resolvers may inspect a normalized virtual plural projection, but durable singular removal occurs only with successful root effects. If the platform cannot provide that projected context safely, extend the shared staging mechanism; do not write an early migration transaction.

## Dependency graph and slices

~~~text
Played CH9/CH10 evidence + source-approved total-level N→N+1 fixture
├─ CH9 slice capable of that total-level transition                    [missing character leaf]
├─ ruleset Feature 27 multiclass prerequisites/grants/HP owner         [missing primary ruleset leaf]
├─ accepted CH10 / Features 31–32 multiclass-readiness                 [spellcasting compatibility gate]
├─ campaign authorization + CH9 atomic composition                     [shared root gate]
├─ singular-to-plural component migration + CH4/CH5 creation revision  [migration leaf]
└─ all selected second-class feature/item owners                       [conditional leaves]
   └─ Slice 1: plural representation and migration proof
      └─ Slice 2: zero-effect second-class validation
         └─ Slice 3: one atomic non-spellcasting multiclass fixture
            └─ Later class pairs, caster integration, third class, and progression expansion
~~~

### Slice 1 — canonical plural membership and migration

**Prerequisites:** Feature 27 agrees the representation is compatible; permanent schema/migration IDs and release scope are confirmed; CH4/CH5 owners accept the creation revision.

1. Add the confirmed plural component/reader/transition and revise CH4/CH5 to create only one plural entry for new characters.
2. Implement the guarded per-actor migration bundle: exact singular input becomes one canonical plural entry and singular removal in one transaction.
3. Test valid migration, already-migrated state, both/neither state, malformed/duplicate/out-of-order entries, sum mismatch, wrong-kind/archived source, rollback at add/remove boundary, idempotence, readback, and restoration. If bulk migration is accepted, test bounded scope, dry-run, resume, and each actor's atomicity.
4. Run focused tests and `roleplay validate catalog` after catalog changes.

**Exit:** every supported actor has exactly one authoritative membership representation, and all new characters are born with it; no multiclass grant/action is enabled yet.

### Slice 2 — pure multiclass validation

**Prerequisites:** Slice 1 accepted; the exact CH9 `N→N+1` slice, Feature 27 eligibility resolver, campaign authorization projection, source locators, candidate choice, and every grant owner are accepted.

1. Add confirmed source candidate declaration only if Feature 27 does not own it, and add the zero-effect multiclass resolver/CH9 choice extension.
2. Dry-run migration-normalized current state, prerequisites, source grants, HP, items, and all child owners without durable effects.
3. Test missing/unknown candidate, duplicate class, failed prerequisite, wrong total/class state, unavailable/stale authorization, archived source, unsupported caster profile, duplicate grant/receipt, absent owner, corrupt representation, and zero-effect validation.
4. Run focused tests and catalog validation where applicable.

**Exit:** one eligible actor can obtain an exact, source-bound second-class preview only when every existing owner and progression condition is satisfied.

### Slice 3 — atomic two-class fixture

**Prerequisites:** Slice 2 accepted; CH9 root can compose campaign consume, class/total-level/HP/grants/items/receipts atomically with failure injection.

1. Execute the one approved non-spellcasting multiclass transition and query its plural membership, total level, source/grant receipts, HP/equipment projections, and first supported action.
2. Inject failures before and after authorization consumption, migration, class entry, total-level, HP, every grant/item/feature owner, receipt, event, audit, cancellation, and timeout.
3. Prove replay/stale intent/concurrent requests cannot double-consume authorization, add a third/duplicate class, or create a second `0→1` receipt. Prove source revision preserves the acquired class definition/version.
4. Run focused tests, `roleplay validate catalog` when applicable, full suite at acceptance, and a protocol walk only if action routing/dependency registration changes.

**Exit:** the first actor becomes exactly a supported two-class character with correct total/class levels and no partial/mirrored state; all invalid/replayed attempts are unchanged.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| One membership truth | An actor carries either valid singular state before migration or valid plural state after it—never both/neither. New creation uses plural only after CH12 acceptance. |
| Canonical plural state | Every membership has one immutable definition, class level 1–20, unique contiguous acquisition order, and a unique class definition; their sum equals authoritative total level. |
| Eligibility | Feature 27 evaluates true multiclass prerequisites and grants from the exact current source versions. Caller choices/evidence cannot bypass it. |
| Closed selection | Only a source-bound candidate key from the approved set is selectable. A duplicate/unknown/unavailable/caster/invalid candidate fails before state changes. |
| Progression consistency | One approved total `N→N+1` transition adds a new class `0→1` and one CH9 receipt. Second/third class addition, old-class downgrade, or mismatch fails unchanged. |
| Atomicity | Campaign consume, migration, memberships, total level, HP/grants/items, receipts, events, and audit commit or roll back as one root. |
| Spellcasting boundary | Initial fixture has no spellcasting class. CH10/Feature 31–32 compatibility is required before accepting a caster combination; CH12 never calculates slot aggregation. |
| History/provenance | Original class versions, CH3 grant receipts, and CH9 transition receipts remain immutable. New source corrections require explicit version/migration policy and never rewrite acquired history. |

## Evidence and change control

The implementation receipt records confirmed migration/component IDs, CH4/CH5 creation revision, Feature 27/31 owner map, campaign consume composition proof, exact SRD locators, source candidate fixture, migration/readback evidence, canonical input/results, valid post-multiclass action, failure/replay/concurrency/rollback/restore results, catalog validation, and full-suite result. It does not copy rule prose, prerequisite formulas, spell calculations, raw effects, campaign policy, or audit IDs.

Amend CH12 before adding a different class pair, another class, a later level in either class, a spellcasting combination/slot aggregation, subclass, feat/ASI choice, class replacement/respec, bulk migration scope, public UI/transport, or optional/homebrew multiclass rule. These boundaries belong to a later CH12 source slice, CH9, CH10/Feature 31–32, Feature 27, CH11, a dedicated respec/migration plan, CH6/CH8 with public-surface confirmation, or a separately ratified ruleset option.
