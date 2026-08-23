# Character Feature 10 dependency plan — spellcasting foundation integration

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Planned; no spellcasting character work begins until the separate D&D spellcasting-resource owner (ruleset feature 31), spell-definition provenance, and played CH9 evidence are accepted.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact. It follows AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH3–CH9, and the D&D 2024 ruleset roadmap features 27, 31, and 32. It writes no runtime artifact.

CH10 makes one source-cited spellcasting class path usable through existing character creation and advancement coordination. It does not make a generic character component authoritative for spells, slots, casting ability, spell DC/attack bonus, preparation, or casting. Those are ruleset spellcasting-resource state and calculations; spell resolution remains ruleset feature 32.

## Target capability

After a spellcasting resource foundation exists, a host can create or advance one supported spellcasting character using immutable class and spell definitions, closed legal spell-selection/preparation choices, and one atomic character operation. The ruleset owner derives all casting state from the actor's actual class level and source declarations. The character operation records source/grant provenance and invokes the owner; it never accepts or writes a caller-calculated spell slot, spell DC, spell attack bonus, casting ability, spell list, or cast result.

The first fixture is one ratified SRD 5.2.1 spellcasting class at one explicitly approved class-level transition and one spell-selection convention. It may be a known-spell or prepared-spell model, but not an improvised hybrid. That fixture proves the reusable integration boundary; it does not declare every caster, every spell, multiclass slot aggregation, or every spell effect supported.

### Included

- One immutable class spellcasting declaration and spell-definition references, source-cited and versioned with the CH1/CH4 content model.
- One closed known/prepared spell choice convention only after the ruleset resource owner confirms its actor-state and mutation contract.
- Character creation/CH9 advancement composition that sends source-bound class-level entitlement and selected legal spells to that owner in the existing atomic root transaction.
- Reuse of CH3 choice declarations and grant receipts where a class grants an initial or level-specific spell choice; source-level provenance remains immutable.
- Read-only discovery/inspection through CH6 conventions, plus validate-before-write, stale-content, duplicate, rollback, replay, and source-revision tests.

### Excluded

- A character-owned `spells`, `preparedSpells`, `slots`, `spellcastingAbility`, `spellSaveDc`, `spellAttackBonus`, concentration, duration, target, area, casting, or effect component.
- A D&D spell rules engine, spell attack, target/save resolution, damage/healing, concentration checks, ritual/casting-time execution, reactions, or turn economy (ruleset feature 32 and its named successors).
- Multiclass spell-slot aggregation, pact/alternative casting models, spell points, non-SRD spells, feats/item-granted spellcasting, subclass-only casting, optional rules, and all unratified caster content.
- Free-text spell names, arbitrary spell definition IDs, arbitrary preparation changes, caller-provided derived values, raw component payloads/effects, browser-specific casting logic, a new MCP kind, or a second transaction.

## Ownership and integration result

| Concern | Authoritative owner and CH10 rule |
| --- | --- |
| Spell definitions, spell lists, slots/resources, known/prepared state, casting ability, save DC, attack bonus | Ruleset D&D 2024 feature 31. Its accepted component/procedure/mechanic contracts are the only source of truth. CH10 may pass validated declaration references and choices to it. |
| Spell execution and effect resolution | Ruleset D&D 2024 feature 32. CH10 does not claim that a selected or prepared spell is castable in play until feature 32 exposes a real action. |
| Class identity/level | CH4 membership and CH9 guarded transition. The spellcasting resource owner reads them; CH10 does not copy class level or add a caster-only level field. |
| Character source declarations and grant provenance | CH1/CH3/CH4 content-definition and generic grant/choice receipts. CH10 adds the spellcasting declaration only after confirming its parent/owner with ruleset feature 31. |
| Initial/level-up atomic composition | CH5 creation root and CH9 advancement root. The spellcasting resource call is a child planned effect; it may not commit separately or reserve a spell choice before root success. |
| Item-granted casting and focus/component equipment | Items and the ruleset item/spell owner. They are absent from the first fixture unless their actual transaction/projection owner is accepted. |
| Spell-selection changes after creation | Ruleset feature 31's confirmed preparation/known-spell mutation contract. CH10 does not invent an unrestricted `character.choose` update path. |
| Public transport, guide, website, identity | CH6, CH8, and CH14. Existing governed action/query surface remains presumed; no new route/tool is authorized here. |

The source-declaration/actor-state split is mandatory. A class content definition may describe which ruleset spellcasting profile applies; only the ruleset owner records what this actor currently knows, has prepared, or can spend. If Feature 31 cannot receive the chosen sources as a typed child operation in CH5/CH9's root transaction, stop for the shared composition design. Do not serialize an opaque spell list on the character as a workaround.

## Source and fixture boundary

Before implementation, ratify one complete SRD 5.2.1 spellcasting vertical path: exact class section and level locator, spellcasting feature text locator, legal spell-list source, each initial/advanced selection rule, casting ability, slot/resource rule, and every initially selectable spell definition/version. The implementation receipt records those locators and a feature-owner map; actor components retain only immutable definition references and the ruleset owner's state.

The reusable declaration must represent a named spellcasting profile reference, a source-versioned spell-list reference, and closed grant/choice references per class level. The initial fixture deliberately supports exactly one convention—either `known` or `prepared`, as defined by Feature 31—and one source-approved level state. Any difference in list, replacement timing, ritual/always-prepared behavior, focus requirement, or selection cardinality needs a named source declaration, owner readiness, and CH7 expansion evidence; generic JSON must not silently claim it.

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and boundary |
| --- | --- |
| Character integration contract | `procedure.character.spellcasting`, governing source-bound selection/inspection and its calls to the ruleset owner. It has no independent casting/resource state. |
| Class declaration extension | `dnd2024.character.class-spellcasting-declaration`, attached only to immutable CH4 class content. It contains references to confirmed ruleset spellcasting profile/list declarations and level-keyed CH3 choice/grant keys, never actor state, source prose, formula, or a spell result. |
| Integration resolver | `mechanic.dnd2024.character.spellcasting.resolve`, a zero-effect resolver under the confirmed parent procedure; it validates immutable source versions and closed choices, then returns a typed request for Feature 31. |
| Ruleset resource state/procedure | **Intentionally not named here.** Ruleset Feature 31 must choose and own its component, procedure, recorder/resolver, and preparation mutation contract before CH10 is implemented. |

Confirm every permanent ID, the class declaration's parent procedure, source-list schema, choice vocabulary, Ruleset 31 state owner, and CH5/CH9 composition semantics under `procedure.system.modify`. If Feature 31 chooses a generic declaration owned outside character content, use that owner rather than creating a duplicate CH10 component. A public action/kind requires the separate CH6/protocol and `procedure.mcp.add-tool` decision.

## Closed request/result boundary

Until Feature 31 supplies its exact legal selector, CH10's public request shape is deliberately not frozen. The final schema must contain an explicit `operation` (`validate` or the accepted create/advance integration operation), an existing or root-bound character identity as appropriate, and only stable keys for the exact required closed choice sets. It must omit class ID, campaign ID, target level, spell list ID, spellcasting ability, slot count, prepared/known count, spell definitions outside the pre-bound declaration, raw effects, and derived values.

Missing a required spell selection is `incomplete`; an invalid present selection returns a stable named correction. `validate` uses the same resolver and has zero durable spell/character/campaign effects. A create/advance operation re-resolves in its root transaction; it never trusts a cached preview or browser state. Canonical output may expose character ID, sorted immutable source-definition IDs, the spellcasting profile reference, stable pending/accepted choice keys, and actual currently usable actions only. It must not expose formulas, raw effect bundles, hidden campaign data, source prose, slots as caller-editable state, or an audit ID.

For first creation, this integration is a CH5 child and the new actor may require CH5's confirmed staged virtual-actor composition. For later level-up, it is a CH9 child against an existing actor. The two roots may share the same resolver but must not create separate spellcasting transactions or divergent selection rules.

## Resolution and transaction rules

1. Resolve the character/root build, campaign scope, single class membership, exact immutable class version, and allowed level state through CH4/CH5 or CH9. Reject a spellcasting declaration on a nonmatching class, wrong/archived source, unratified level, multiclass state, or unsupported profile.
2. Resolve the class's exact Feature-31 profile and source spell list; validate each submitted stable choice against the source version, cardinality, uniqueness, prerequisites, and replacement/timing rules supplied by that owner. No spell name, raw ID, or list inferred from prose is accepted.
3. Ask Feature 31 to dry-run its typed resource-state transition. It derives casting ability, slots, known/prepared status, spell DC, and spell attack bonus from its authoritative inputs. If it cannot produce a typed planned result, fail before any character/campaign state changes.
4. In CH5/CH9's single root transaction, apply the agreed class/membership/level effects, ruleset spellcasting resource effect, any named spell grant receipt entries, and the existing CH5/CH9 receipt last. The resource owner never writes an independent success receipt/audit outside this root.
5. After commit, ordinary audit/event history records the root operation. Inspect reads the authoritative ruleset projection and immutable character source references. A failure, guard/reaction failure, cancellation, timeout, or audit failure rolls back every child effect and no spell/preparation/slot state is consumed.

The exact ordering is confirmed with Feature 31: creation needs class-level inputs available to the resource resolver, and advancement must not spend a campaign authorization unless the resource transition can commit. CH10 cannot resolve an ordering conflict by directly setting a component.

## Dependency graph and slices

~~~text
Played CH9 evidence and a supported class/source expansion decision
├─ ruleset Feature 27 class-level owner                                  [class prerequisite]
├─ ruleset Feature 31 spell definitions/resources/preparation/DC owner   [missing primary leaf]
├─ exact SRD caster class + spell-list/source locators                   [source gate]
├─ CH3 closed choices + CH4/CH9 class/level integration                 [character prerequisite]
├─ CH5/CH9 atomic child-effect composition                               [transaction gate]
└─ ruleset Feature 32 spell resolution                                   [separate future play leaf]
   └─ Slice 1: declaration and zero-effect spellcasting validation
      └─ Slice 2: one creation or 1-level advancement resource integration
         └─ Slice 3: consume the later Feature 32 cast action; CH11/CH12 expansions
~~~

### Slice 1 — source declaration and validation

**Prerequisites:** Feature 31 accepts its authoritative data/state/mutation contract; CH7 permits the caster source expansion; exact SRD locators and one selection convention are ratified; all proposed IDs are confirmed.

1. Add the confirmed immutable class spellcasting declaration and one spell-list/profile source fixture; create no actor spellcasting state in CH10.
2. Add the zero-effect resolver and closed validation path reusing Feature 31's selector/canonical result.
3. Test empty/incomplete choice, invalid/duplicate/out-of-list spell, wrong class/level, stale/archived source, profile mismatch, unsupported replacement/timing, missing resource owner, and no-effects validation.
4. Run focused tests and `roleplay validate catalog` after catalog changes.

**Exit:** the system can prove exactly what one source-cited caster profile would grant to a specific build/actor, without creating a spell/resource record or reimplementing spell rules.

### Slice 2 — one atomic caster integration

**Prerequisites:** Slice 1 accepted; CH5 virtual-actor or CH9 existing-actor composition is accepted for the chosen fixture; Feature 31 exposes an owner-bound planned transition; all affected item/feature dependencies are available.

1. Integrate the Feature 31 planned resource transition into exactly one CH5 creation or CH9 advancement fixture, as ratified before implementation.
2. Record CH3 grant receipts for closed spell grants and the pre-existing CH5/CH9 root receipt only after all child owners validate; create no parallel spellcasting receipt.
3. Inspect the actual ruleset resource projection and demonstrate one non-casting action remains usable. Do not claim spell execution until Feature 32 is accepted.
4. Inject failures at source resolution, choice validation, resource transition, receipt, containment/item, event, audit, cancellation, and timeout boundaries. Verify no partial character/resource/slot/preparation state or consumed campaign authorization. Run focused tests, catalog validation where applicable, full suite at acceptance, and protocol walk only if the governed surface/dependency registration changes.

**Exit:** the chosen spellcasting character is created or advanced with exactly its source-authorized ruleset spellcasting state and provenance, and any failed attempt leaves all roots unchanged.

### Slice 3 — later spell action consumption

**Prerequisites:** Slice 2 accepted and ruleset Feature 32 exposes a source-cited, governed spell action.

1. Register or discover the Feature 32 action through CH6's existing surface conventions; CH10 adds no character-specific transport.
2. Prove a spellcasting resource projection can be consumed only by the ruleset action, with ordinary target/effect/duration/rollback semantics owned there.
3. Add end-to-end character readback/play fixtures without copying spell execution behavior into CH10.

**Exit:** a supported caster can use the actual ruleset spell action; this is not an authorization to add more spell content or casting models.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Ownership | Only Feature 31 records/derives spellcasting resource state. CH10 stores source declarations and calls the owner; it never adds a parallel actor spell list, slots, DC, or ability. |
| Source/version integrity | Class profile, spell list, choices, and selected spell definitions resolve to exact immutable versions. Wrong-kind, stale, archived, cross-profile, or later-revised content cannot reinterpret an existing character. |
| Closed choices | Known/prepared selection follows one confirmed source convention and Feature 31 cardinality/timing/prerequisites. Missing is incomplete; free text, duplicate, arbitrary, excess, or unsupported replacement fails. |
| Derived state | Casting ability, slots, save DC, attack bonus, and availability are derived by the ruleset owner; caller or character-plan values are rejected. |
| Transactionality | Creation/advancement, resource transition, grant receipts, and root receipt/audit commit together or roll back together. No pre-reserved spell choice, independently persisted spell state, or consumed authorization remains on failure. |
| Scope boundary | The initial single-class fixture excludes multiclass aggregation, feat/item/subclass casting, optional systems, and unratified spells. Each requires its named owner/feature plan. |
| Play boundary | A legal resource projection alone is not a spell cast. Only Feature 32 can supply attacks, saves, targets, effects, durations, or resource spending in play. |
| Readback | CH6 inspection returns immutable sources and safe owner projections/capabilities without source prose, raw effects, formula details, or an independently editable list. |

## Evidence and change control

The implementation receipt records confirmed Feature 31/32 contracts, approved IDs, exact SRD locators, source fixtures, one selection convention, owner-composition proof, canonical validation/result fixtures, rollback/replay/readback evidence, catalog validation, full-suite result, and—only after Feature 32—a real spell-action proof. It does not duplicate spells, formulae, resource state, campaign policy, source prose, raw effects, or audit IDs.

Amend CH10 before adding another caster class, spell list, selection convention, replacement rule, spell category, subclass/feat/item-granted casting, multiclass aggregation, spell points, non-SRD content, public surface, web flow, or spell resolution. Those belong to CH7 expansion plus Feature 31, CH11, CH12, Items, CH6/CH8 with public-surface confirmation, or Feature 32 respectively.
