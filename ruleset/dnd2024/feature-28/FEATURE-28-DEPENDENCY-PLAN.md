# Feature 28 dependency plan — character-origin language and tool foundations

Status: **Slices 1–4 are implemented and accepted. Static Origin-feat identities are available; active feat behavior remains blocked.**
Last updated: 2026-08-21

## Execution rule

This is a repository planning artifact governed by `AGENTS.md`, `procedure.system.create-feature`,
`procedure.system.modify`, the D&D planning guide, the D&D roadmap, and the Character Creation
Plan. It creates no catalog/runtime content, fixture, or live-game state.

Feature 28 retains its later background, feat, and ability-score-improvement scope. Slice 1
established language/tool membership, and Slice 2 established source-cited background
ability-increase resolution. Neither accepted slice bypasses the existing owners for items,
classes, effects, or character creation.

## Target capability

A future character-creation transaction can record a character's complete known SRD 5.2.1 language
and tool-proficiency membership through two validated state owners, never free-text profile data or
opaque origin state.

### Included

- One closed language-proficiency component and one closed tool-proficiency component on an actor.
- The full SRD 5.2.1 language/tool vocabularies as canonical stable keys.
- One normal complete-replacement recorder per list, with fixed source attribution and unchanged
  state on rejection.
- Discovery/readback through the existing component/world surface and focused catalog tests.

### Excluded

- Character/campaign creation, attachment, profile, immutable content, grants, choices, receipts,
  public creation operations, and actor authorisation.
- Translation, language/tool checks, crafting, tool items, equipment, feat/species/class effects,
  Heroic Inspiration, HP, AC, or derived bonuses.
- Acquisition provenance, class/background/species identity, item-instance ID, proficiency bonus,
  ability mapping, free-text name, or inventory state.

## Official source basis

| Source | Exact locator | Rule used here |
| --- | --- | --- |
| `source.dnd2024.srd-5.2.1` | *Character Creation > Step 2: Character Origin > Choose Languages*, PDF page 20 | A player character knows Common plus two selected standard languages; a language allows communication, reading, and writing. |
| `source.dnd2024.srd-5.2.1` | *Equipment > Tools > Tool Proficiency*, PDF pages 93–94 | Tool proficiency is membership in one named tool. A later tool check adds Proficiency Bonus and may gain Advantage from an applicable skill; that resolution is excluded here. |
| `source.dnd2024.srd-5.2.1` | *Equipment > Tools*, PDF pages 93–94 | Defines the initial closed tool vocabulary, including gaming-set and instrument variants. |
| [CH0 draft](../../../character/feature-00/CHARACTER-FEATURE-00-DEPENDENCY-PLAN.md) | Human Soldier Fighter owner map | All legal player characters require language state; Soldier additionally needs dice gaming-set proficiency. |

The source registry remains the attribution/license owner. These records retain only its fixed
reference and locator, never copied SRD prose, check rules, or item statistics.

## Verified dependencies and ownership search

| Dependency or candidate | Evidence and conclusion |
| --- | --- |
| `dnd2024.skill-proficiencies` and `dnd2024.saving-throw-proficiencies` | Existing independent closed membership records, fixed source refs, explicit-empty semantics, and normal recorders. They do not own languages or tools. |
| `procedure.mechanic.dnd2024.skill-proficiencies` | It owns only skills/saves and explicitly forbids tools. It establishes recorder conventions but is not a language/tool owner. |
| D&D source registry | Existing catalog entity fixes SRD 5.2.1, attribution, and heading-plus-page locator format. |
| Items / Ruleset Feature 23 | Own physical tool definitions, instances, containment, and equipment. Proficiency is a separate actor capability. |
| Character Feature 3 | Its owner search found no language/tool owner and forbids opaque origin storage. |
| Ruleset Features 25–28 | Own later mastery, species, class, background, and feat effects. Membership alone cannot imply any of them. |

Repository searches for `language`, `language proficiency`, `tool proficiency`, `gaming set`,
`tool check`, `translation`, and `craft` found no compatible component/procedure/mechanic. Repeat
that search immediately before implementation.

## Recursive dependency analysis

```text
Character-origin language and tool foundation
├─ SRD 5.2.1 source identity/attribution                    [implemented: source registry]
├─ component/effect/event/audit runtime                      [implemented]
├─ closed language state plus normal recorder                [accepted: Slice 1]
├─ closed tool state plus normal recorder                    [accepted: Slice 1]
├─ universal Common-plus-two origin-language resolution      [accepted: Slice 3]
├─ immutable Origin-feat identity/catalog                     [accepted: Slice 4]
├─ Alert and Savage Attacker active behavior                  [blocked: initiative/damage composition]
├─ source/grant resolution                                   [blocked parent: CH1/CH3/CH4]
├─ physical tool/item ownership                              [blocked parent: Items / Feature 23]
├─ language/tool checks and crafting                         [excluded: future D&D/item owners]
└─ complete character transaction                            [blocked parent: CH5]
```

The paired records are one coherent lowest slice: both are source-cited, closed proficiency
membership with the same no-derived-data boundary. They are not a feature-effect resolver.

## Ownership decisions

1. Languages and tools are separate actor components; their vocabularies, future consumers, and
   grant rules differ, so they never share one generic proficiency array.
2. Each list is complete known state. Missing means unknown/not recorded; explicit `[]` means
   known-none. A later player-character grant must enforce Common-plus-two; this generic recorder
   must not.
3. Values are stable keys in canonical order. Display labels, case variants, duplicates, nulls,
   and free text reject. `primordial` is one key; its SRD dialect note does not create four keys.
4. The recorder fixes `sourceRef`; callers cannot supply a source, provenance, background/class,
   item, ability, proficiency bonus, check outcome, Advantage, or effects.
5. A new procedure is warranted because the existing skill/save procedure's complete state and
   vocabulary do not overlap. The two new normal recorders remain independent.
6. Possession is not proficiency. Items owns tool/item possession; later mechanics own using a
   language/tool and any resulting check/crafting effect.

## Proposed permanent vocabulary — confirmation required

No catalog artifact is authorised until this boundary and all five IDs are confirmed after a fresh
owner search.

| Role | Proposed permanent ID | Boundary |
| --- | --- | --- |
| Language component | `dnd2024.language-proficiencies` | Complete known language keys and fixed source reference only. |
| Tool component | `dnd2024.tool-proficiencies` | Complete known tool-proficiency keys and fixed source reference only. |
| Procedure | `procedure.mechanic.dnd2024.languages-and-tools` | Governs these two lists and recorders; no grants, checks, items, or effects. |
| Language recorder | `mechanic.dnd2024.language-proficiencies.record` | Validates/canonicalizes `languages`; returns one add/set component effect. |
| Tool recorder | `mechanic.dnd2024.tool-proficiencies.record` | Validates/canonicalizes `tools`; returns one add/set component effect. |

### Canonical vocabularies

Languages: `abyssal`, `celestial`, `common`, `common-sign-language`, `deep-speech`, `draconic`,
`druidic`, `dwarvish`, `elvish`, `giant`, `gnomish`, `goblin`, `halfling`, `infernal`, `orc`,
`primordial`, `sylvan`, `thieves-cant`, `undercommon`.

Tools: `alchemists-supplies`, `bagpipes`, `brewers-supplies`, `calligraphers-supplies`,
`carpenters-tools`, `cartographers-tools`, `cobblers-tools`, `cooks-utensils`, `dice-set`,
`disguise-kit`, `dragonchess-set`, `drum`, `dulcimer`, `flute`, `forgery-kit`,
`glassblowers-tools`, `herbalism-kit`, `horn`, `jewelers-tools`, `leatherworkers-tools`, `lute`,
`masons-tools`, `navigators-tools`, `painters-supplies`, `pan-flute`, `playing-cards`,
`poisoners-kit`, `potters-tools`, `shawm`, `smiths-tools`, `thieves-tools`,
`three-dragon-ante`, `tinkers-tools`, `viol`, `weavers-tools`, `woodcarvers-tools`.

Each schema has exactly its list plus `sourceRef`, forbids additional properties, and fixes
`sourceId` to `source.dnd2024.srd-5.2.1`. Locators are respectively
`Character Creation > Step 2: Character Origin > Choose Languages` and
`Equipment > Tools > Tool Proficiency`.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Language and tool membership records | This plan and all five IDs confirmed; owner search repeated | Both lists record/read back safely; every invalid write is unchanged. |
| 2 | Background ability-increase resolution | CH1, CH2, C15, and CH5 staged-world composition present | A source-cited background produces one valid ability merge fragment or a named no-effect failure. |
| 3 | Universal origin-language resolution | Slice 1, C15, CH5 staged-world composition, and the proposed permanent contracts below confirmed | A player-character origin produces one source-cited initial language-list fragment or a named no-effect failure. |
| 4 | Static Origin-feat identity catalog | CH1 feature-content convention and proposed permanent contracts confirmed | Source-cited Alert and Savage Attacker definitions can be referenced with no actor state or mechanics. |
| 5 | Background/species/class grant composition | CH1–CH4, Feature 27, and item/feature owners verified | Deferred to CH3/CH4/CH5; not authorised here. |

## Slice 1 — record language and tool proficiencies

### Runtime artifacts and governing contracts

Create the two confirmed schemas/components, their shared procedure, and two mechanics scoped to
`dnd2024-srd-5.2.1`. Add no entity fixture, migration, C# game vocabulary, event, subscription,
notification, commit/query kind, or public tool.

Immediately before writing, re-read `procedure.system.create-feature`, `procedure.system.modify`,
`procedure.world.model`, `procedure.world.change`, `procedure.mechanic.write`, the existing
skill/save proficiency procedure, the source registry, and the official pages above.

### Data/input contract and required state

- Language input is exactly `{ languages: string[] }`; tool input is exactly `{ tools: string[] }`.
- Omitted/null/non-array/non-string/unknown/wrong-case/display/duplicate/extra input rejects,
  including caller `sourceRef`, provenance, source definition, item, ability, bonus, result,
  Advantage, and effects. Empty arrays are valid.
- The named subject must exist. A present component must fully match its closed schema/fixed source;
  corrupt stored data rejects before effects. Absence produces `component.add`; presence produces
  complete-replacement `component.set`.

### Recording behavior and result

1. Validate the exact one-field input and every member before proposing effects.
2. Reject duplicates, then sort according to the canonical vocabulary.
3. Read/validate prior component state and construct only `{ list, sourceRef }`.
4. Return exactly one add/set effect plus canonical list, prior list (`null` when absent), and fixed
   source reference. Use no randomness, merge, grant resolution, or derived check result.

The enclosing normal world-change/action path supplies ordinary transaction, event, and audit
evidence. This slice neither records a source grant nor treats the list as authorisation.

### Invariants and failure behaviour

- A valid repeated complete request is replacement-stable and never duplicates a member/source.
- Language writes cannot change tools, skills, saves, abilities, items, containment, campaign, or
  profile state; tool writes have the symmetric rule.
- Any malformed/corrupt/stale/guard/reaction/event/audit/cancellation failure returns zero durable
  state/event/notification/success-audit change for the enclosing root transaction.
- Existing skill/save artifacts are unchanged.

### Implementation sequence

1. Repeat source/owner reads and stop for the permanent-ID/schema confirmation.
2. Author components, procedure, mechanics, and focused tests together.
3. Test absent creation, replacement, explicit empty, full-vocabulary order, and replay.
4. Test malformed, duplicate, unknown, wrong-case, extra-field, corrupt-state, cancellation, and
   injected effect/event/audit failure with byte/revision comparisons.
5. Query artifacts/actor back; run `roleplay validate catalog`, focused tests, full suite, and
   `git diff --check`; write a receipt and stop.

### Acceptance matrix

| Case | Required evidence |
| --- | --- |
| Creation | Each absent component creates exactly one sorted list with fixed source reference and one component effect. |
| Replacement | A new complete list replaces, never merges with, the prior list. |
| Empty/missing | `[]` is known-empty and remains distinct from an absent component. |
| Vocabulary | `common-sign-language`, `primordial`, a gaming-set variant, and a musical-instrument variant round-trip in canonical order. |
| Closed input | Missing/null/type/extra/duplicate/wrong-case/display/unknown/source/derived input rejects with no effects. |
| Corrupt state | Bad list/source/order/duplicate stored data fails before effects. |
| Isolation | The non-target list and all unrelated actor/world state are byte-for-byte unchanged. |
| Atomicity | Guard/reaction/event/audit/cancellation failures leave no durable success evidence or partial state. |
| Repository | Focused tests, catalog validation, full suite, and diff check pass. |

### Slice 1 exit gate

Slice 1 is verified only when both source-cited lists have one unambiguous owner, safe normal
recorders, objective positive/negative/atomicity coverage, catalog validation, and repository
evidence. It unblocks CH0's owner map but does not ratify or create a character.

## Slice 2 — background ability-increase resolution

### Boundary

This is the smallest source-complete leaf needed for the ratified Soldier choice. It records the
ability-score-increase choices an immutable background permits and resolves one submitted choice
against an actor's already-recorded CH2 base scores. It returns one `component.merge` fragment for
the existing `dnd2024.abilities` owner; CH5 alone appends and applies it in its staged root bundle.

It does not select a background, create a character, write a receipt, accept final caller-supplied
scores, grant a feat/skill/tool/language/item, calculate a modifier, or apply a feature effect.
CH3 later proves why a background is legal and records its receipt; this slice validates only the
closed ability-increase form.

### Source and ownership

The initial declaration is CH0's Soldier background at `Character Origins > Character Backgrounds
> Soldier, PDF page 83`. Its eligible abilities are `str`, `con`, and `wis`; the ratified choice is
`+2 str, +1 con`. The reusable declaration also represents the source alternative `+1` to all three
eligible abilities, but does not make an unreviewed background selectable.

| Concern | Owner and Slice 2 rule |
| --- | --- |
| Immutable background identity/version/source | CH1 content definition. The new profile agrees exactly; it never duplicates title or rules prose. |
| Base allocation | CH2 validates and records it. Slice 2 requires that component and never accepts a replacement score object. |
| Ability state | Existing `dnd2024.abilities`. The resolver returns only a closed merge delta after calculating raw values. |
| Campaign scope | C15 verifier, including CH5's staged-world overlay. No campaign ID is stored or accepted. |
| Background selection/receipt | CH3. This slice accepts only a trusted pre-bound background definition and creates no actor-side background state. |
| Atomic root | CH5. The resolver opens no transaction and applies no effect. |

### Proposed permanent vocabulary — confirmed

| Role | Proposed ID and closed meaning |
| --- | --- |
| Static profile | `dnd2024.background.ability-increase-options`, attached only to a CH1 `background` content entity. It declares canonical eligible abilities and which standard patterns are permitted. |
| Governing procedure | `procedure.mechanic.dnd2024.background-ability-increases`, governing the static declaration and internal resolver only. |
| Draft declaration | `mechanic.dnd2024.background-ability-increases.resolve`, documenting the C# resolver contract for CH5 composition; it is not public. |
| Typed resolver | `IBackgroundAbilityScoreIncreaseResolver`, accepting actor ID, pre-bound background definition ID, and a closed selection JSON object; it returns zero or one ordinary effect fragment. |

Confirmed 2026-08-21. The static profile repeats the CH1 content key/version/source only to require exact agreement, as
Feature 26 does. It otherwise contains only canonical `eligibleAbilities` and ordered
`allowedPatterns`. The selection is either two eligible distinct keys with values `2` and `1`, or
all three eligible keys with value `1`. Omitted, null, noninteger, zero, negative, extra,
ineligible, wrong-pattern, archived, wrong-kind/version, missing-base-score, corrupt-state, and
over-20 results fail before effects.

### Slice 2 implementation sequence

1. Repeat the owner search and confirm the four proposed entries above.
2. Add the closed static schema/component, procedure, draft declaration, and Soldier profile. No
   actor selection or receipt is authored.
3. Implement the C15-scoped staged-world-compatible resolver. It reads the exact background
   identity/profile and base ability component, validates the selection, and returns one merge
   effect containing only changed raw scores.
4. Test both legal patterns, the ratified fixture, canonical output, absent/corrupt/over-cap state,
   malformed/ineligible selections, mismatched content, C15/staged scope, and zero persistence on
   rejection.
5. Run catalog validation, focused tests, and diff check; write a receipt and stop for acceptance.

### Slice 2 exit gate

A source-cited background declares its legal ability-increase choices, and a valid CH2 base-state
choice yields precisely one merge fragment or a named no-effect failure. No caller can substitute
final scores, and no character/world state changes outside CH5's eventual root. **Implemented; see
`FEATURE-28-SLICE-2-RECEIPT.md`.**

## Slice 3 — universal origin-language resolution

### Boundary and source rule

This is the source rule at *Character Creation > Step 2: Character Origin > Choose Languages*,
PDF page 20. It is universal to player characters, so it is neither a species grant nor a
background grant. The extracted SRD table makes the closed rule exact: initial state is `common`
plus exactly two distinct selections from `common-sign-language`, `draconic`, `dwarvish`,
`elvish`, `giant`, `gnomish`, `goblin`, `halfling`, and `orc`.

The slice resolves the chosen form only. It introduces no random roll, die event, grant receipt,
species/background selection, later language grant, translation/check behavior, direct write,
transaction, event, audit, or public operation. A future random-character-generation owner may
derive a selected pair before calling this same closed resolver.

### Composition and required state

CH5 supplies a staged actor and has already appended the C15 participation fragments. The resolver
requires that active scope, requires the actor to have no existing `dnd2024.language-proficiencies`
component, validates exactly `{ "languages": string[2] }`, and returns exactly one
`component.add` fragment. Its data is the canonical complete list—`common` plus the two selected
standard keys in the established alphabetical vocabulary order—with the fixed Slice 1 source
reference. It never invokes the generic recorder or accepts a caller-provided source reference.

Missing/null/non-array/non-string/duplicate/unknown/rare/`common`/wrong-case/display/extra input,
missing actor, inactive or absent C15 scope, corrupt existing language state, or an already-recorded
language component returns a named invalid plan with no effects. The resolver accepts only the
chosen pair; it cannot overwrite a later language grant or silently infer selections.

### Proposed permanent vocabulary — confirmation required

| Role | Proposed permanent ID and closed meaning |
| --- | --- |
| Governing procedure | `procedure.mechanic.dnd2024.origin-languages`, governing the source-cited universal initial-language resolver only; no language checks or later grants. |
| Draft declaration | `mechanic.dnd2024.origin-languages.resolve`, documenting the internal staged composition contract; it is not public. |
| Typed resolver | `ICharacterOriginLanguageResolver`, accepting actor ID and the closed two-language selection JSON, and returning zero or one ordinary `dnd2024.language-proficiencies` add fragment. |

The stable source reference is the existing Slice 1 language locator. The resolver introduces no
new component schema, content entity, caller-selectable mechanic, or actor-side receipt; the
existing language component remains the sole durable language-state owner.

### Implementation and acceptance gate

1. Repeat the source/owner search and obtain confirmation for the three permanent entries above.
2. Add the governing procedure and non-public draft declaration, typed request/problem/plan
   records, C15- and staged-world-compatible resolver, and dependency injection registration.
3. Test the ratified `dwarvish`/`giant` pair, the other eight standard choices, canonical order,
   absence requirement, C15/staged scope, and source-reference exactness.
4. Test malformed, duplicate, `common`, rare, unknown, wrong-case, extra-field, existing/corrupt
   language state, actor/scope failures, and zero persistence on each rejection.
5. Run focused tests, catalog validation, and diff check; write a receipt and stop for acceptance.

Slice 3 exits only when the universal rule has one unambiguous owner, the resolver emits precisely
one ordinary initial-language fragment for a legal pair, every failure has no fragment or durable
change, and later language grants remain unimplemented. **Implemented and accepted; see
`FEATURE-28-SLICE-3-RECEIPT.md`.**

## Slice 4 — immutable Origin-feat identity catalog

### Boundary and source basis

This static leaf records only the two CH0-referenced Origin feats from *Feats > Origin Feats*, PDF
page 87: Alert and Savage Attacker. Each receives a CH1 versioned `feature` content definition and
one immutable profile declaring that it is an `origin` feat and is not repeatable. The profile holds
no benefits/effect keys, prerequisites, selected actor state, choice values, source prose, dice,
initiative values, or executable payload.

Alert's initiative proficiency/swap behavior and Savage Attacker's once-per-turn weapon-damage
reroll remain explicitly unimplemented. The static profile is not permission to use, select, or
simulate either benefit; later mechanics must prove their initiative/ally-condition or
attack/damage/turn-boundary dependencies separately.

### Proposed permanent vocabulary — confirmation required

| Role | Proposed permanent ID and closed meaning |
| --- | --- |
| Static profile component | `dnd2024.feat-profile`, attached only to an active CH1 `feature` content entity. It agrees exactly with the content key/version/source and declares only `category: "origin"` and `repeatable: false`. |
| Governing procedure | `procedure.mechanic.dnd2024.feat-profile`, governing immutable feat-profile catalog authoring only; it grants no feat or benefit. |
| Alert identity | `content.dnd2024.feature.alert.v1`, an immutable CH1 `feature` definition with the page-87 Alert source locator. |
| Savage Attacker identity | `content.dnd2024.feature.savage-attacker.v1`, an immutable CH1 `feature` definition with the page-87 Savage Attacker source locator. |

### Implementation and exit gate

1. Re-read the CH1 content-definition and source registry contracts, then confirm the four entries
   above.
2. Add the closed profile/schema, governing procedure, and exactly the two versioned definitions.
   Add no actor component, mechanic, resolver, event, action, or public operation.
3. Test profile/content agreement, exact source locators, category/repeatability shape, duplicate
   key/version rejection, wrong kind/source/version/extra-field rejection, and read-only access.
4. Run focused tests, catalog validation, and diff check; write a receipt and stop for acceptance.

Slice 4 exits only when the two source-cited definitions have one immutable catalog owner and no
actor-side feat state, initiative bonus/swap, damage reroll, feature effect, or character-creation
grant can be produced from them. **Implemented and accepted; see
`FEATURE-28-SLICE-4-RECEIPT.md`.**

## Plan-quality audit

The target is singular, sources/locators and existing owners are concrete, every missing runtime
dependency is a Slice 1 leaf, inputs/effects/empty semantics/failure behaviour are closed, and the
acceptance matrix covers positive, boundary, malformed, corrupt-state, isolation, and rollback
cases. No runtime artifact was created in this planning pass.

## Plan-change rule

Revise before implementation if a compatible owner appears, the SRD vocabulary/locator changes, or
a requested behaviour needs a mechanical trait, item, grant, check, crafting effect, or background
resolution. Those belong to the appropriate Feature 23, 25, 26, 27, later Feature 28 slice, or
CH3/CH4/CH5 owner.
