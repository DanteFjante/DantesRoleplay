# Character Feature 3 dependency plan — origins and closed choices

Status: **Planned; content work awaits CH0/CH1/CH2, and any selected language, tool, feature, or item grant also awaits its actual state owner.**  
Last updated: 2026-08-20

## Execution rule

This is a planning-only repository artifact governed by AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.world.change`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH0–CH2 plans, the source registry, and the Item and Inventory Plan. It writes no runtime artifact.

CH3 owns source-cited background/species selection and the closed declaration/resolution of their grants. It does not turn a source reference into working rules, create item instances, or make arbitrary direct component writes. CH5 remains the only root transaction owner.

## Target capability

A complete proposed character build can name exactly one approved background and one approved species, resolve only the fixed and choose-N origin grants declared by their immutable versions, preserve a frozen record of selections, and pass the resulting values only to their existing or separately planned owners.

The first ratified origin pair is fixture content. Its names, choices, and grants must not be embedded in a component schema or resolver; a second approved definition of the same declared form must work through the same contract.

### Included

- Separate immutable content entities of CH1 kind `background` and `species`, each with its own source locator and version.
- One generic origin-grant declaration attached to each origin definition, plus closed choice-set definition entities.
- Separate background and species selection records on a character actor, and frozen grant/choice receipts that CH4 may reuse.
- Fixed membership and choose-N grants to a named existing owner, with deterministic duplicate/eligibility checks.
- Read-only discovery of approved origin definitions, choice sets, and selected/receipt projections.

### Excluded

- Class grants, class membership, hit dice, level advancement, feats, spellcasting, and multiclassing (CH4 and later).
- Ability assignment or total level (CH2); actor/profile/campaign attachment (CH1); atomic actor/item creation and public command (CH5/CH6).
- A tool-proficiency, language, feature-effect, or item-instance owner. These must be supplied by their own ruleset/item plan before a fixture requiring them may be released.
- Free text selections, arbitrary predicates/scripts, a choice whose options are computed from caller data, hidden defaults, partial drafts, or copied SRD rules prose.

## Ownership and dependency findings

| Concern | Owner and CH3 boundary |
| --- | --- |
| Source identity, title, kind, status, version | CH1 `dnd2024.character.content-definition`. A CH3 grant or choice component never repeats sourceRef, origin kind, display title, or version. |
| Skills and saving throws | Existing proficiency recorders own membership. CH3 resolves a permitted closed list but does not store a second list, modifier, bonus, or source text. |
| Tools and languages | No catalog owner was found in the current repository search. A CH0 path containing either is blocked until a source-cited D&D data feature supplies a single owner and recorder. |
| Equipment options | Items owns definitions, instances, possession, and containment. CH3 can preserve a selected approved definition key; Items Slice 6/CH5 creates/contains the instance. |
| Species/background feature effects | A content/receipt reference is not mechanical support. Each active trait effect needs its own existing or future ruleset owner; otherwise CH0 must reject that path as unsupported. |
| Campaign scope and actor identity | Campaign/CH1 own them. CH3 verifies the actor's valid character scope through their contract but never stores a campaign ID or profile copy. |

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and scope |
| --- | --- |
| Reusable grant declaration | `dnd2024.character.grant-declarations`, attached to a CH1 background, species, or class definition entity. CH3 first uses it only for origins; CH4 reuses it for class grants. |
| Closed choice-set declaration | `dnd2024.character.choice-set-definition`, attached only to a CH1 `choice-set` content entity. |
| Separate actor selections | `dnd2024.character.background-selection`; `dnd2024.character.species-selection`. Each contains only a validated immutable origin-definition entity ID. |
| Shared frozen outcomes | `dnd2024.character.grant-receipts`, one ordered unique receipt list on the actor reusable by CH4; it never replays a past grant. |
| Governing contracts/recorders | `procedure.mechanic.dnd2024.background` / `mechanic.dnd2024.background.record`; `procedure.mechanic.dnd2024.species` / `mechanic.dnd2024.species.record`; `procedure.mechanic.dnd2024.character-grant-receipts` / `mechanic.dnd2024.character-grant-receipts.record`; `procedure.character.choose` / `mechanic.dnd2024.character-origin.resolve`. |

All IDs need `procedure.system.modify` confirmation and an owner search immediately before authoring. The generic receipt list is deliberately shared: CH4 must extend the same proven receipt convention, not invent a class-only duplicate. If a compatible established grant/choice/receipt owner is found, stop and reconcile the schemas before adding this vocabulary.

## Closed declaration shapes

A `grant-declarations` component contains a unique ordered list of grant declarations. Each declaration has a canonical `grantKey`, a closed `target` (`skill-proficiencies`, `saving-throw-proficiencies`, `item-definition`, or a separately confirmed owner key), and exactly one form:

| Form | Declaration | Resolution |
| --- | --- | --- |
| `fixed-membership` | One or more unique canonical values. | Every listed value is included once; none comes from caller choice. |
| `choose-n` | A reference to one immutable choice-set entity and a positive selection count. | Submitted values are unique, belong to that exact set, and count exactly N. |

A choice-set declaration has a unique stable key, one option domain, an ordered duplicate-free option list, and its allowed selection count. The initial domains are only owners proven at implementation time: `skill`, `saving-throw-ability`, `item-definition`, `tool`, `language`, or `feature-definition`. A domain does not make its owner exist; an unsupported domain has no active fixture and is rejected before any actor creation.

Actor selection records contain only their versioned definition ID. A receipt contains the source definition ID, grant key, resolved owner key, exact selected or fixed canonical values, and choice-set ID when applicable. It is ordered and unique by `(sourceDefinitionId, grantKey)`; it stores neither mechanics, source prose, derived values, item instances, campaign identity, or an invented effect. This generic source vocabulary is deliberately reusable by CH4 class grants; a later class-level advancement receipt needs its own CH9 transaction/progression meaning rather than a second grant-deduplication store. The resolver accepts the pre-bound CH0 origin definition versions and submitted choices, never arbitrary definition IDs or raw effects.

## Dependency graph and slices

~~~text
CH0 complete origin choices, locators, and expected-owner map             [missing]
├─ CH1 immutable background/species/choice-set definitions                [blocked parent]
├─ CH2 valid abilities/level composition                                  [blocked parent]
├─ skill/save recorders                                                    [implemented]
├─ tool/language/trait-effect owner when CH0 needs one                    [missing external leaf]
└─ Items Slices 1–6 for any equipment option                              [blocked external leaf]
   └─ confirmed CH3 vocabulary
      ├─ Slice 1: origin/choice content declarations
      ├─ Slice 2: selection and zero-effect grant resolver
      └─ CH5: atomic selection, owner calls, item creation, and receipts
~~~

### Slice 1 — immutable origin and choice content

**Prerequisites:** CH0 ratifies exact background/species, all origin choices, source locators, and owner map; CH1 content definitions are accepted; every selected grant target has a confirmed owner; permanent IDs are confirmed.

1. Add the two origin contracts and closed declaration schemas.
2. Record only the one CH0-approved versioned background, species, and necessary choice-set entities with generic grants/option domains.
3. Verify duplicate grant keys, kind mismatch, unknown choice domain/value, blank locator, unowned target, and source-version mismatch fail unchanged.
4. Run `roleplay validate catalog`.

**Exit:** a reviewer can traverse background/species → declared grants → choice sets → existing owner without copied source text, a hidden default, or a grant with no legal target.

### Slice 2 — selection resolution and immutable receipts

**Prerequisites:** Slice 1 accepted; CH1 campaign-scoped profile contract and CH2 assignment path are accepted; origin selection/receipt IDs and resolver input are confirmed.

1. Add separate background/species recorders that accept only a validated, pre-bound definition and require each selection component to be absent.
2. Add a zero-effect resolver that checks all required choices and returns canonical owner-targeted values plus the frozen receipt data. It never writes the target owner itself.
3. Have CH5 later call the resolver, origin recorders, existing proficiency recorders, Item Slice 6, and receipt writer inside one transaction; no standalone origin writer may leave a partial actor or loose item.
4. Test valid fixed/choose-N paths, missing/extra/duplicate/unavailable/cross-origin choices, duplicate selection, wrong kind/version, unsupported owner, and corrupt receipt prerequisites.

**Exit:** each origin is selected once, each declared grant resolves once, and a rejected origin path produces no actor state, item instance, receipt, or success audit. A repeated request is detected from immutable receipt identity rather than guessed from current rules.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Separate origins | Exactly one approved background and one approved species selection exist; a species in the background field, archived definition, or wrong version fails unchanged. |
| Closed choices | Selection cardinality, domain, and allowed options come from the selected immutable choice set. Missing, excess, duplicate, free-text, or cross-origin options fail. |
| Grant integrity | Each `grantKey` is unique and resolves once for its source definition. A receipt duplicates no `(sourceDefinitionId, grantKey)` pair and is never reinterpreted against revised content. |
| Existing-owner composition | Skills/saves call their existing recorders; item selections remain definition references until Items/CH5 creates instances. No duplicate lists or item arrays are written. |
| Unsupported capability | A tool, language, trait effect, or item choice lacking its named owner blocks the fixture with a named dependency; it is not stored as opaque text. |
| Atomic boundary | The resolver returns no effects. CH5 performs all persistent selection, owner calls, containment, and receipt writes atomically. |
| Breadth boundary | A second source-cited origin using the same fixed/choose-N structures is representable; optional rules, arbitrary conditions, and unplanned option domains remain rejected. |

## Evidence and change control

The later receipt cites confirmed IDs, CH0 definition/choice locators, owner-map evidence, fixture IDs, positive/negative resolution tests, and catalog validation. Do not duplicate source rules or test receipts into the roadmap.

Amend CH3 before adding an origin grant form, option domain, language/tool owner, mechanical trait effect, arbitrary prerequisite, second choice resolver, source version migration, correction flow, or public command. The owning successor is CH3 amendment, a D&D data/ruleset feature, Items, CH7, or CH5/CH6 according to the boundary above.
