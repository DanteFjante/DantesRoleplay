# Character Feature 3 dependency plan — origins and closed choices

Status: **Reconciled; CH0–CH2 and language/tool leaves are accepted; Feature 26 supplies immutable profiles and the selected-species seam. Origin runtime work remains blocked on complete grant ownership.**
Last updated: 2026-08-21

## Execution rule

This is a planning-only repository artifact governed by AGENTS.md, `procedure.system.create-feature`, `procedure.system.modify`, `procedure.world.change`, the [Character Creation Plan](../../CHARACTER_CREATION_PLAN.md), CH0–CH2 plans, the source registry, and the Item and Inventory Plan. It writes no runtime artifact.

CH3 owns source-cited background grant declarations, closed choice declarations, and the
generic origin-grant resolution/receipt convention. Feature 26 owns the selected-species state
seam because it must validate the species profile's Size and trait/choice inventory. CH3 consumes
that validated species definition when resolving an origin grant; it does not create a competing
species-selection component. CH3 does not turn a source reference into working rules, create item
instances, or make arbitrary direct component writes. CH5 remains the only root transaction owner.

## Target capability

A complete proposed character build can name exactly one approved background and one approved species, resolve only the fixed and choose-N origin grants declared by their immutable versions, preserve a frozen record of selections, and pass the resulting values only to their existing or separately planned owners.

The first ratified origin pair is fixture content. Its names, choices, and grants must not be embedded in a component schema or resolver; a second approved definition of the same declared form must work through the same contract.

### Included

- Separate immutable content entities of CH1 kind `background` and `species`, each with its own source locator and version.
- One generic origin-grant declaration attached to each origin definition, plus closed choice-set definition entities.
- A background selection record and frozen grant/choice receipts that CH4 may reuse. Species
  selection is Feature 26 Slice 2 state and remains absent until its owner is accepted.
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
| Tools and languages | Feature 28 owns their separate membership recorders. CH3 may resolve only the source-cited closed values routed to those owners; it never stores an origin copy or makes them free text. |
| Equipment options | Items owns definitions, instances, possession, and containment. CH3 can preserve a selected approved definition key; Items Slice 6/CH5 creates/contains the instance. |
| Species/background feature effects | A content/receipt reference is not mechanical support. Each active trait effect needs its own existing or future ruleset owner; otherwise CH0 must reject that path as unsupported. |
| Campaign scope and actor identity | Campaign/CH1 own them. CH3 verifies the actor's valid character scope through their contract but never stores a campaign ID or profile copy. |
| Selected species | Feature 26 Slice 2 owns the one-species record and validates it against `dnd2024.species-profile`. CH3 may consume its immutable source-definition identity for grant resolution, but must not introduce a second species reference. |
| Soldier ability increases | Accepted Feature 28 Slice 2 supplies the source-cited zero-write composition resolver. CH2 still validates the base allocation only; it must not be bypassed with a caller-supplied final array. |
| Universal origin languages | Accepted Feature 28 Slice 3 supplies the source-cited `Common` plus two standard-language composition resolver. It is sourced from character creation rather than a species or background definition, so CH3 must not misattribute it to either origin. |

## Proposed permanent vocabulary — confirmation required

| Role | Proposed ID and scope |
| --- | --- |
| Reusable grant declaration | `dnd2024.character.grant-declarations`, attached to a CH1 background, species, or class definition entity. CH3 first uses it only for origins; CH4 reuses it for class grants. |
| Closed choice-set declaration | `dnd2024.character.choice-set-definition`, attached only to a CH1 `choice-set` content entity. |
| Background actor selection | `dnd2024.character.background-selection`. It contains only a validated immutable background-definition entity ID. |
| Shared frozen outcomes | `dnd2024.character.grant-receipts`, one ordered unique receipt list on the actor reusable by CH4; it never replays a past grant. |
| Governing contracts/recorders | `procedure.mechanic.dnd2024.background` / `mechanic.dnd2024.background.record`; `procedure.mechanic.dnd2024.character-grant-receipts` / `mechanic.dnd2024.character-grant-receipts.record`; `procedure.mechanic.dnd2024.character-origin-resolution` / `mechanic.dnd2024.character-origin.resolve`. |

All IDs need `procedure.system.modify` confirmation and an owner search immediately before authoring. The generic receipt list is deliberately shared: CH4 must extend the same proven receipt convention, not invent a class-only duplicate. If a compatible established grant/choice/receipt owner is found, stop and reconcile the schemas before adding this vocabulary.

## Closed declaration shapes

A `grant-declarations` component contains a unique ordered list of grant declarations. Each declaration has a canonical `grantKey`, a closed `target` (`skill-proficiencies`, `saving-throw-proficiencies`, `language-proficiencies`, `tool-proficiencies`, `item-definition`, or a separately confirmed owner key), and exactly one form:

| Form | Declaration | Resolution |
| --- | --- | --- |
| `fixed-membership` | One or more unique canonical values. | Every listed value is included once; none comes from caller choice. |
| `choose-n` | A reference to one immutable choice-set entity and a positive selection count. | Submitted values are unique, belong to that exact set, and count exactly N. |

A choice-set declaration has a unique stable key, one option domain, an ordered duplicate-free option list, and its allowed selection count. The initial domains are only owners proven at implementation time: `skill`, `saving-throw-ability`, `item-definition`, `tool`, `language`, or `feature-definition`. A domain does not make its owner exist; an unsupported domain has no active fixture and is rejected before any actor creation.

The background selection record contains only its versioned definition ID. Feature 26's future
selection record supplies the corresponding species definition ID. A receipt contains the source
definition ID, grant key, resolved owner key, exact selected or fixed canonical values, and
choice-set ID when applicable. It is ordered and unique by `(sourceDefinitionId, grantKey)`; it
stores neither mechanics, source prose, derived values, item instances, campaign identity, or an
invented effect. This generic source vocabulary is deliberately reusable by CH4 class grants; a
later class-level advancement receipt needs its own CH9 transaction/progression meaning rather
than a second grant-deduplication store. The resolver accepts the pre-bound CH0 origin definition
versions and submitted choices, never arbitrary definition IDs or raw effects.

## Dependency graph and slices

~~~text
CH0 ratified Human Soldier Fighter choices and source locators             [accepted]
├─ CH1 immutable background/species/choice-set definitions                 [accepted base]
├─ CH2 base ability allocation                                              [accepted; no origin increase owner]
├─ skill/save/language/tool membership recorders                            [accepted]
├─ Feature 26 immutable species profiles                                    [accepted]
│  └─ Feature 26 Slice 2 selected-species seam                             [accepted]
├─ Feature 28 background ASI and universal language accepted; feat blocked [external leaf]
├─ Feature 23 Items Slice 6 for the approved equipment package             [blocked external leaf]
└─ confirmed CH3 vocabulary and a source-complete origin owner map
   ├─ Slice 1: origin/choice content declarations
   ├─ Slice 2: background selection and zero-effect grant resolver
   └─ CH5: atomic selection, owner calls, item creation, and receipts
~~~

### Slice 1 — immutable origin and choice content

**Prerequisites:** CH0's exact background/species, origin choices, source locators, and owner map are ratified; CH1 content definitions are accepted; Feature 26 has accepted the selected-species seam; every selected grant target has a confirmed owner; permanent IDs are confirmed.

1. Add the two origin contracts and closed declaration schemas.
2. Record only the one CH0-approved versioned background and necessary choice-set entities with generic grants/option domains. Extend no species entity here: Feature 26 remains its static-profile and selected-state owner.
3. Verify duplicate grant keys, kind mismatch, unknown choice domain/value, blank locator, unowned target, and source-version mismatch fail unchanged.
4. Run `roleplay validate catalog`.

**Exit:** a reviewer can traverse background and the Feature-26-selected species definition → declared grants → choice sets → existing owner without copied source text, a hidden default, a partial-source declaration, or a grant with no legal target.

### Slice 2 — selection resolution and immutable receipts

**Prerequisites:** Slice 1 accepted; CH1 campaign-scoped profile contract and CH2 assignment path are accepted; origin selection/receipt IDs and resolver input are confirmed.

1. Add the background recorder that accepts only a validated, pre-bound background definition and requires its selection component to be absent. Consume the one species definition already validated by Feature 26; do not write species state.
2. Add a zero-effect resolver that checks all required choices and returns canonical owner-targeted values plus the frozen receipt data. It never writes the target owner itself.
3. Have CH5 later call the resolver, origin recorders, existing proficiency recorders, Item Slice 6, and receipt writer inside one transaction; no standalone origin writer may leave a partial actor or loose item.
4. Test valid fixed/choose-N paths, missing/extra/duplicate/unavailable/cross-origin choices, duplicate selection, wrong kind/version, unsupported owner, and corrupt receipt prerequisites.

**Exit:** each origin is selected once, each declared grant resolves once, and a rejected origin path produces no actor state, item instance, receipt, or success audit. A repeated request is detected from immutable receipt identity rather than guessed from current rules.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Separate origins | Exactly one approved background selection and one Feature-26-approved species selection exist; a species in the background field, archived definition, or wrong version fails unchanged. |
| Closed choices | Selection cardinality, domain, and allowed options come from the selected immutable choice set. Missing, excess, duplicate, free-text, or cross-origin options fail. |
| Grant integrity | Each `grantKey` is unique and resolves once for its source definition. A receipt duplicates no `(sourceDefinitionId, grantKey)` pair and is never reinterpreted against revised content. |
| Existing-owner composition | Skills/saves call their existing recorders; item selections remain definition references until Items/CH5 creates instances. No duplicate lists or item arrays are written. |
| Unsupported capability | An ability increase, universal language rule, tool, trait effect, feat, or item choice lacking its named owner blocks the fixture with a named dependency; it is not stored as opaque text. |
| Atomic boundary | The resolver returns no effects. CH5 performs all persistent selection, owner calls, containment, and receipt writes atomically. |
| Breadth boundary | A second source-cited origin using the same fixed/choose-N structures is representable; optional rules, arbitrary conditions, and unplanned option domains remain rejected. |

## Evidence and change control

The later receipt cites confirmed IDs, CH0 definition/choice locators, owner-map evidence, fixture IDs, positive/negative resolution tests, and catalog validation. Do not duplicate source rules or test receipts into the roadmap.

Amend CH3 before adding an origin grant form, option domain, language/tool owner, mechanical trait effect, arbitrary prerequisite, second choice resolver, source version migration, correction flow, or public command. The owning successor is CH3 amendment, a D&D data/ruleset feature, Items, CH7, or CH5/CH6 according to the boundary above.
