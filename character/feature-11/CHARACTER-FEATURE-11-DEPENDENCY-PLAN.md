# Character Feature 11 dependency plan — feat and ability-score-improvement integration

Status: **Planned; implementation awaits a level-appropriate CH9 advancement slice and the D&D ruleset Feature 28 feat/ASI owner.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH2–CH10, and ruleset D&D 2024 roadmap features 27, 28, and 31. It writes no runtime artifact.

CH11 is a source-bound integration feature. It makes one already-earned class/background feat-or-ability-score-improvement entitlement selectable and atomic. It does not turn the initial ability-assignment policy into an editable score sheet, nor does it make character content the rule engine for feat prerequisites or effects.

## Target capability

When a played single-class character reaches one ratified level that grants an eligible feat or ability-score-improvement choice, the CH9 advancement root can validate and apply exactly one legal, source-cited option through the ruleset owner. The actor's authoritative feat state and ability changes come from that owner; the existing CH9 progression receipt and CH3 grant receipts preserve immutable source provenance and prevent replay. A failed, stale, duplicate, ineligible, or partial attempt leaves both the campaign authorization and the character unchanged.

The first fixture is one **non-spellcasting** feat-or-ASI entitlement at one exact source-cited class level. It proves a reusable closed entitlement/choice integration, not all feats, every ASI distribution, background-origin feats, spell-granting feats, subclass grants, or arbitrary post-creation retraining.

### Included

- One immutable class-level (or separately confirmed background-level) entitlement reference that identifies a ruleset Feature 28 choice family and exact source version.
- One closed mutually exclusive choice form: the exact ratified feat option **or** ability-score-improvement option, with all selections validated by the Feature 28 owner.
- A ruleset-owned feat selection/effect transition and ruleset-owned ability-score-improvement transition composed into the CH9 atomic advancement transaction.
- Reuse of CH3 choice declarations/grant receipts for closed source grants, and CH9 advancement receipt for the completed progression; no duplicate character feat or ASI receipt.
- Inspection, replay, stale-source, prerequisite, duplicate-benefit, rollback, restoration, and post-advance-play evidence using CH6/CH7 conventions.

### Excluded

- Editing initial CH2 ability-assignment policy, free allocation of scores, direct ability component writes, or caller-supplied resulting scores/modifiers/proficiency/DC/HP/AC.
- A generic feat list, feat rules text, opaque feature payload, arbitrary prerequisite expression, feat replacement/retraining, duplicate feat selection, background/species/class feature migration, or automatic optimization.
- Any spell, spell slot, cantrip, preparation, casting ability, spell DC/attack bonus, or spell effect granted by a feat until CH10 and its Feature 31/32 owners are accepted.
- Multiclass prerequisite/entitlement calculation, subclass grants, item-granted feats, epic boons, optional/homebrew feats, XP/milestone policy, a new MCP kind, or a separate browser workflow.

## Ownership and overlap result

| Concern | Authoritative owner and CH11 rule |
| --- | --- |
| Feat definitions, prerequisites, selections, actor-side feat state, feature effects, and ASI legal distribution | Ruleset D&D 2024 Feature 28. CH11 calls its confirmed resolver/transition and stores no copied feat/ASI state. |
| Class-level entitlement and total/class transition | Ruleset Feature 27 plus CH4 membership/CH9 guarded advance. A new CH9 slice must first support the exact level which grants the choice; CH11 cannot pretend the current 1→2 fixture reaches it. |
| Campaign eligibility/consumption | Campaign advancement authorization contract used by CH9. CH11 never creates or consumes a second authorization. |
| Ability-score truth and derived modifiers | Existing ability component and its derived consumers. Feature 28 is the only future authority to make a legal ASI transition; CH2's generation policy/recorder remains creation-only. |
| Source declaration and selection provenance | CH1/CH3/CH4 immutable definitions and generic grant/choice receipts. CH9 receipt records the level transition; Feature 28 owns any selection state it requires. |
| Feat effects touching skills, saves, HP, AC, equipment, or actions | Their named existing/future ruleset or Items owners. A first feat whose effect lacks every real owner is blocked, not stored as descriptive text. |
| Spell-related feat behavior | CH10 / ruleset Features 31–32; excluded from the first non-spellcasting fixture. |
| Public discovery/transport/UI | CH6/CH8. CH11 extends a confirmed `procedure.character.advance` choice schema only after confirmation; it does not create a tool, kind, or web route. |

The first choice must be genuinely exclusive at the source level: Feature 28 decides the legal forms and their relationship. CH11 must not encode a product assumption such as “every level offers ASI or feat,” nor merge separately granted choices into a synthetic pick-one menu. If a source grants more than one independent choice, a later amendment names and orders them explicitly.

## Source and fixture boundary

Before implementation, ratify exact SRD 5.2.1 locators for the selected class/background entitlement, its required class level, the feat/ASI rule, one non-spellcasting feat definition if chosen, every prerequisite, and every mechanical effect. Also record the Feature 28 owner map for each effect and the resulting CH9 class-level source declaration. The implementation receipt records the exact locators/version; actor state contains only owner-approved IDs/projections.

The reusable form represents a level-keyed entitlement reference plus a closed Feature-28 choice-family key. The initial content fixture is one legal option family at one reached class level. An ASI split shape, a different feat category, an origin feat, spell/item/subclass interaction, feat replacement, and extra choices are new source/capability slices—not values in an unbounded `options` array.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Character integration contract | `procedure.character.advancement-choice`, governing character-side source binding for a Feature 28 entitlement. It does not own feat or ability state. |
| Immutable entitlement reference | `dnd2024.character.advancement-choice-declaration`, attached to the approved CH4 class definition or confirmed origin definition. It contains level/source entitlement keys and references to the Feature 28 choice family only; it holds no feat text, prerequisite code, selected feat, or ability values. |
| Zero-effect integration resolver | `mechanic.dnd2024.character.advancement-choice.resolve`, under the confirmed parent procedure, validates immutable source/level binding and turns closed choice keys into a typed Feature-28 request. |
| Ruleset feat/ASI state and transition | **Intentionally not named here.** Feature 28 must own its actor components, procedures, prerequisite evaluator, ability-change semantics, and effect dispatch before CH11 writes anything. |

Confirm the IDs, source attachment, class/background applicability, choice cardinality, Feature 28 owner, and extension of `procedure.character.advance` under `procedure.system.modify`. If Feature 28 owns the declaration alongside feat content, CH11 uses that instead of adding a character-owned duplicate. A schema change to a permanent CH9 request is a public contract decision and must be confirmed with its owner before implementation.

## Closed input/result boundary

CH11 does not freeze field names before the Feature 28 choice contract exists. The later CH9 `advance` request may carry only a stable, source-bound key and the exact closed selection structure endorsed by Feature 28 for the current entitlement. It still contains `operation`, `characterId`, and its stale expected-level guard from CH9; it never accepts an arbitrary feat ID, a target ability score, an increment, a final six-score array, prerequisite evidence, raw effect, or a campaign authorization.

Omitted mandatory selection returns `incomplete`; a malformed/non-object/null/duplicate/unknown field or present but illegal option returns a stable named correction. `validate` makes no durable choice, ability, feat, grant, receipt, or authorization change. `advance` repeats full source, prerequisite, and owner resolution in the same root transaction, so a preview cannot be replayed after the character/source/authorization changes.

The canonical result adds only sorted entitlement/selected stable keys and actual newly usable capabilities to CH9's existing result. It does not expose rule prose, prerequisite evaluation internals, final ability scores, derived modifiers, raw effects, a campaign policy, or audit/event IDs. Inspection reads authoritative owner projections and immutable source IDs; it must never reconstruct a selectable menu from an actor's mutable state.

## Resolution and transaction rules

1. CH9 resolves the existing actor, active campaign attachment, exactly one class membership, consistent current total/class level, and one valid campaign authorization for the approved next transition. The required next class level must be the source-cited entitlement level; otherwise CH11 has no work.
2. Resolve the immutable class/origin definition and its exact Feature-28 entitlement declaration. Reject absent, duplicate, wrong-kind, archived, cross-class, wrong-level, stale, or unsupported family references before selection.
3. Send only the owner-bound choice keys to Feature 28 in dry-run mode. It validates cardinality, mutual exclusivity, all prerequisites, duplicate acquisition, legal ASI distribution/caps, and each named feat effect owner. CH11 cannot calculate an ability increase or waive an unavailable feat dependency.
4. On `validate`, return the canonical correction/preview without consuming campaign authorization or writing any state. On CH9 `advance`, repeat all steps in its root transaction.
5. Apply confirmed child effects in the common CH9 order: campaign authorization consumption, class/total-level transitions, Feature 28 feat-or-ASI transition and its typed child owner effects, CH3 generic grant receipt where relevant, and CH9 advancement receipt last. Feature 28 must not independently commit an actor change, audit success, or durable choice receipt.
6. Audit/event behavior is CH9's root behavior. Any owner/guard/reaction/receipt/audit/cancellation/timeout failure rolls back authorization, levels, feat state, ability scores, and all effects together. A stale/replayed request obtains no second benefit.

An ASI might affect downstream HP/AC/skills/spell metrics depending on the actual rule and already-owned projection semantics. Feature 28 must name those dependents and generate their typed transitions; CH11 must not recalculate, patch, or cache them. If any required dependent has no owner, the selected entitlement is blocked.

## Dependency graph and slices

~~~text
Played CH9 evidence + source-approved class level that grants a choice
├─ CH9 higher-level transition for that exact source level              [missing character leaf]
├─ ruleset Feature 27 class-level declaration/entitlement owner         [ruleset prerequisite]
├─ ruleset Feature 28 feat + ASI selection/effect owner                 [missing primary leaf]
├─ all owners for the selected non-spellcasting feat effects             [conditional leaves]
├─ campaign authorization and atomic CH9 composition                    [shared transaction gate]
└─ Feature 31/32 only for a later spell-related feat                    [explicit successor]
   └─ Slice 1: one source entitlement and zero-effect validation
      └─ Slice 2: one atomic feat-or-ASI advancement fixture
         └─ CH11 family expansions and CH12 multiclass interaction
~~~

### Slice 1 — entitlement and pure validation

**Prerequisites:** The exact CH9 level slice and Feature 28 selection contract are accepted; all source locators and first non-spellcasting fixture owner map are ratified; permanent vocabulary is confirmed.

1. Add the confirmed immutable entitlement reference and zero-effect resolver, reusing Feature 28's choice/precondition semantics.
2. Extend the CH9 validation request/result only with the confirmed closed choice field(s).
3. Test no entitlement at level, missing choice, invalid/duplicate/ineligible feat, invalid ASI form/cap, stale or archived source, wrong class, duplicate declaration, unavailable downstream effect owner, and zero-effects validation.
4. Run focused tests and `roleplay validate catalog` after catalog changes.

**Exit:** one eligible next-level actor receives an exact legal feat-or-ASI preview from immutable sources and Feature 28, with no actor/campaign mutation.

### Slice 2 — one atomic choice fixture

**Prerequisites:** Slice 1 accepted; Feature 28 produces one owner-bound planned transition; every selected effect owner and CH9 transaction child boundary are verified.

1. Compose Feature 28's selected transition into the exact CH9 level-up root; do not create a second feat/ASI action or receipt.
2. Demonstrate the supported transition, inspect all owner projections, and perform a genuinely affected supported action where an available owner exposes one.
3. Inject failure at source/precondition resolution, ability/feat transition, each child effect, grant receipt, advancement receipt, event, audit, cancellation, and timeout boundaries. Test replay, stale expected level, duplicate acquisition, source revision, authorization double-consume, corrupt durable receipt/state, rollback, and restore/readback.
4. Run focused tests, `roleplay validate catalog` where applicable, full suite at acceptance, and a protocol walk only if the action surface or dependency registration changes.

**Exit:** one authorized actor gains exactly one legal source entitlement in the same successful level-up transaction; no invalid, duplicate, partial, or replayed choice changes character or campaign state.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Entitlement truth | A feat/ASI choice exists only at the exact immutable source level/family that grants it. CH11 does not add one because a character has advanced. |
| Choice legality | Feature 28 enforces family/cardinality/mutual-exclusivity, prerequisites, duplicate prevention, and legal ASI distribution. Missing is incomplete; arbitrary/free-text/invalid is rejected. |
| Ability boundary | CH2 remains creation assignment only. The approved Feature 28 transition alone changes ability truth; no caller or CH11 formula supplies final scores/modifiers or downstream values. |
| Feat boundary | The ruleset owner records feat acquisition/effects. Character content holds only source entitlement references and CH3/CH9 preserve provenance; no parallel feat list or opaque feature text exists. |
| Atomicity | Campaign authorization, CH9 levels, Feature 28 state/effects, grant receipts, and advancement receipt all commit or roll back together. |
| Source preservation | Existing acquisition/receipts retain original immutable sources; a revised feat/entitlement is a new content version and never silently reinterprets or migrates an actor. |
| Narrow first fixture | One non-spellcasting entitlement proves the integration. Spell, subclass, item, origin, multiclass, new family, and replacement support require their named dependencies and expansion evidence. |
| Readback/play | Inspection is source/projection based and a real affected supported action remains valid; returned data contains no source prose, raw rules calculation, or editable derived state. |

## Evidence and change control

The implementation receipt records confirmed Feature 28/CH9 IDs, exact SRD locators, one entitlement fixture, owner/precondition map, canonical request/result fixtures, source-version proof, valid post-advance action, failure/replay/rollback/restore evidence, catalog validation, and full-suite result. It does not copy feat rules, prerequisite formulas, selected raw effects, score formulas, campaign policy, or audit IDs.

Amend CH11 before adding a second choice family, ASI shape, feat, origin/background grant, spell/item/subclass feat effect, feat replacement, optional/homebrew content, a public transport/UI, or multiclass interaction. Those belong to CH7 expansion plus Feature 28, CH10/Feature 31–32, Items, CH6/CH8 with public-surface confirmation, or CH12 respectively.
