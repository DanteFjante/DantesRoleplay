# Character Feature 0 dependency plan — ratify the first supported character path

Status: **Ratified; the Human Soldier Fighter path is the only supported first-path fixture. CH1 Slice 1 remains gated on permanent-vocabulary confirmation.**
Last updated: 2026-08-21

## Execution rule

This is a repository planning artifact. It follows AGENTS.md, procedure.system.create-feature, the
Character Creation Plan, and the D&D feature-planning guide. Repository files are authoritative
during development. CH0 writes no catalog record, database state, procedure, component, entity,
mechanic, public operation, event, notification, audit, fixture, or source content.

A later implementation pass must confirm the current catalog and runtime state immediately before
writing. CH0 is the semantic boundary that gives CH1–CH6 a complete supported-build decision; it
does not authorize any of them.

## Target capability

A host can approve one complete, source-cited, level-one, non-spellcasting SRD 5.2.1 character
path, including every required choice and the exact resulting state, so a later creation command
does not guess rules, grant choices, or scope.

### Included

- Exactly one SRD 5.2.1 species, background, and non-spellcasting class at level 1.
- One source-cited ability-score generation method and complete score assignment.
- Every level-one origin, class, proficiency, language, feature, and starting-equipment choice
  required by the selected path.
- One declared campaign-scope policy: the first create operation requires an existing active
  campaign root and creates a campaign-owned actor only within that campaign.
- A component-by-component expected-state inventory, including the owner of each future write and
  every value that remains derived.
- Source, licensing, visibility, and no-runtime-write review.

### Excluded

- Creating a character, content definition, item, campaign record, actor, component, relationship,
  procedure, mechanic, event, notification, audit row, public surface, migration, or fixture.
- Choosing more than one class/species/background path, spellcasting, feats, multiclassing,
  level-up, optional rules, AI-generated build choices, a browser wizard, partial server-side
  drafts, or campaign membership beyond the required existing campaign scope.
- Treating an existing generic rules component as proof that a source option is supported.

## Official source basis

| Source | Locator to confirm in the ratification | CH0 decision supplied |
| --- | --- | --- |
| source.dnd2024.srd-5.2.1 | D&D System Reference Document v5.2.1, CC-BY-4.0; Character Creation; Character Origins; Classes; Equipment | The exact official version, license, and headings for the selected species/background/class/equipment path. |
| Existing source registry | catalog/world/entities/source.dnd2024.srd-5.2.1.json | Canonical source ID, attribution, and locator format: section heading plus stable PDF pages when available. |
| Character Feature Base Plan | Goal, ownership, grant/choice resolution, and CH0–CH7 | One complete path before any character runtime vocabulary. |
| Items and Inventory Plan | Slices 0–6 | Starting equipment stays definition/instance/containment-owned; CH0 cannot make loose items. |
| Campaign Creation Plan | C2 campaign root boundary | First character ownership is campaign-scoped, but CH0 neither creates nor validates a campaign. |

The official SRD page identifies SRD v5.2.1 as the canonical downloadable document and places it
under CC-BY-4.0. CH0 records section headings and PDF page references from that exact version for
the selected content; no rules text is copied into this plan.

## Verified repository foundation and ownership search

| Existing artifact | Evidence and restriction |
| --- | --- |
| dnd2024.abilities | Six raw scores are authoritative; modifiers remain derived. |
| dnd2024.character-level | Existing total level is 1–20 and derives proficiency bonus; it does not represent class identity or class grants. |
| dnd2024.skill-proficiencies and dnd2024.saving-throw-proficiencies | Each stores only canonical membership; neither stores acquisition provenance, class, background, or derived bonus. |
| dnd2024.hit-points and dnd2024.armor-class | Existing final records have their own writers. CH0 must state the approved source inputs but cannot duplicate those records or formulas. |
| dnd2024.weapon-proficiencies and weapon-profile | Existing category proficiency and immutable weapon facts remain separate from class grants and item possession. |
| Items Slices 0–4 | Definition, instance, possession, and equipped-state work is a prerequisite for an actually equipped character. |
| Character/campaign ownership search | No character-creation runtime owner exists. The `character/` directory contains planning artifacts only; existing ruleset recorders are administrative data writers, not a legal-character builder. |

Before CH1 implementation, re-run the ownership search for character, actor, species, background,
class, origin, grant, choice, provenance, creation, starting equipment, and the chosen source
names. If a current catalog artifact owns any proposed responsibility, CH1 revises that owner.

## Ownership decisions

1. CH0 is human-approved editorial input, not persistent game truth. CH1 owns immutable content
   definitions and character provenance; CH5 owns the atomic create operation.
2. A character references immutable source content/version. It never copies rules prose, source
   text, ability modifiers, proficiency bonus, a second AC value/formula, or a serialized inventory.
3. The campaign owns campaign scope; the future character actor owns its identity and chosen state;
   items own item definitions, instances, possession, and equipped state.
4. The chosen ability method and source-grant choices are authoritative input for the first path.
   Modifiers, proficiency bonus, and later projections are calculated by their existing owners.
5. One supported build is a vertical product path, not a schema enum that makes every SRD option
   valid. Adding another source option requires a later source-cited expansion review.

## Closed ratification record

FirstCharacterSupportReview is documentation-only. It is neither a component schema nor a future
create request.

~~~text
FirstCharacterSupportReview
{
  status: "draft" | "ratified",
  rulesSource: {
    sourceId: exactly source.dnd2024.srd-5.2.1,
    documentVersion: exactly 5.2.1,
    attribution: exact registry attribution,
    selectedLocators: ordered 4–12 entries, each section heading plus PDF page when stable
  },
  campaignScopePolicy: exactly "requires-existing-active-campaign",
  characterLevel: exactly 1,
  species: { name, sourceLocator, immutableContentKey },
  background: { name, sourceLocator, immutableContentKey },
  class: { name, sourceLocator, immutableContentKey, spellcasting: exactly false },
  abilityGeneration: {
    methodName, sourceLocator,
    completeAllowedInputRule,
    exactSixScoreAssignment: ordered str, dex, con, int, wis, cha values
  },
  requiredChoices: ordered complete list of {
    source: "species" | "background" | "class" | "equipment",
    label, sourceLocator, cardinality, allowedOptionKeys, selectedOptionKeys
  },
  expectedState: ordered list of {
    futureOwner, statePurpose, sourceKeyOrLocator, selectedResult, derived: boolean
  },
  startingEquipment: ordered selected package/options with immutable definition keys,
  partyFacingIdentityPolicy: one stated name/pronoun policy without a created actor ID,
  ratifiedBy: host display name,
  ratifiedOn: ISO-8601 calendar date
}
~~~

All text is trimmed and nonempty. Immutable content keys are stable source-definition identifiers
to be ratified in CH1, not caller-chosen entity IDs. Every required choice has a selected option
count exactly matching its cardinality. No list can contain duplicates. A ratified review may not
contain unknown fields, unresolved source locator, a spellcasting class, deferred choice, placeholder
item, caller-authoritative derived value, proposed runtime ID, raw component data, raw effects, or an absent
ratifiedBy/ratifiedOn.

## Ratified candidate — Human Soldier Fighter

This is a source-checked ratification record, not runtime content. It is deliberately recorded
before CH1 so the first character path exposes every real owner it needs. The path uses
one level-one, non-spellcasting path with no spell selection, optional rule, feat substitution, or
unresolved player choice:

| Choice | Draft selection | SRD 5.2.1 locator |
| --- | --- | --- |
| Class | Fighter, level 1 | *Classes > Fighter*, PDF pages 47–48 |
| Background | Soldier; ability increases `+2 Strength`, `+1 Constitution`; Savage Attacker; Athletics and Intimidation; dice gaming set; equipment package A | *Character Origins > Character Backgrounds > Soldier*, PDF page 83 |
| Species | Human, Medium; Resourceful; Skillful chooses Insight; Versatile chooses Alert | *Character Origins > Character Species > Human*, PDF page 86; *Feats > Origin Feats > Alert*, PDF page 87 |
| Languages | Common, Dwarvish, Giant | *Character Creation > Step 2: Character Origin > Choose Languages*, PDF page 20 |
| Ability method | Standard Array, assigned `Str 15, Dex 14, Con 13, Int 8, Wis 10, Cha 12`; Soldier increases yield final raw scores `Str 17, Dex 14, Con 14, Int 8, Wis 10, Cha 12` | *Character Creation > Step 3: Ability Scores*, PDF page 21 |
| Fighter skills | Perception and Survival | *Classes > Fighter > Core Fighter Traits*, PDF page 47 |
| Fighter choices | Defense Fighting Style; Greatsword, Flail, and Javelin Weapon Mastery choices | *Classes > Fighter*, PDF pages 47–48; *Feats > Fighting Style Feats > Defense*, PDF page 88 |
| Fighter equipment | Package A: Chain Mail, Greatsword, Flail, 8 Javelins, Dungeoneer’s Pack, 4 GP | *Classes > Fighter > Core Fighter Traits*, PDF page 47 |
| Party-facing identity policy | The later caller supplies one trimmed display name and may supply pronouns, appearance, and biography; no actor ID, campaign ID, secret, or player-control assertion is part of CH0. | Character Creation Plan; CH1 profile boundary |

The ratified path deliberately chooses options that avoid spellcasting and additional open choice sets. It
does **not** ratify an alignment yet: the SRD creation sequence asks the player to choose one, but
the proposed CH1 profile has no alignment field. CH0 must decide whether that is retained as
non-authoritative player narration or whether a later character-owned descriptive field is needed;
it must not silently become a second mechanical state.

### Owner map and blockers

| Selected result | Intended owner | Current status |
| --- | --- | --- |
| Display name and non-secret descriptive profile | CH1 profile after a campaign-owned attachment | Blocked: campaign attachment contract is absent. |
| Immutable selected source identity/version | CH1 content-definition convention | Planned. |
| Six raw ability scores and total level 1 | CH2 plus existing ability and character-level recorders | CH2 planned; component owners exist. |
| Skill, saving-throw, and weapon-category proficiency membership | Existing D&D proficiency recorders, composed by CH3/CH4 | Existing state owners; grant-resolution work remains planned. |
| Common, Dwarvish, and Giant | Ruleset Feature 28 `dnd2024.language-proficiencies` | **Verified.** Source-cited membership recorder exists; CH3 later owns source-grant resolution. |
| Dice gaming-set proficiency | Ruleset Feature 28 `dnd2024.tool-proficiencies` | **Verified.** Source-cited membership recorder exists; CH3 later owns source-grant resolution. |
| Human traits and Alert | Species-trait and feat owners | Blocked by Ruleset Features 26 and 28; Heroic Inspiration has no current owner. |
| Soldier Savage Attacker | Background/feat owner | Blocked by Ruleset Feature 28 and weapon-damage composition. |
| Fighter membership, Fighting Style, Second Wind | Class/feature owners | Blocked by Ruleset Feature 27. |
| Weapon Mastery | Weapon-property/mastery owner | Blocked by Ruleset Feature 25. |
| Chain Mail, weapons, pack, gear, currency, and containment | Item definitions/instances and equipment owner | Blocked by Ruleset Features 23–24 and Items slices. |
| Level-one HP and final AC | Class/HP and equipment/AC derivation owners | Blocked by Ruleset Features 24 and 27. |

Language and tool membership owners are verified. Their later source-grant composition remains
CH3 work; CH0 records them as selected source content and never stores either as free text.

## Host ratification — 2026-08-21

`FirstCharacterSupportReview` is ratified with the following closed content and choices:

- Rules source: `source.dnd2024.srd-5.2.1`, *System Reference Document* v5.2.1, CC-BY-4.0,
  using the registered attribution and locator format.
- Campaign policy: `requires-existing-active-campaign`.
- Species: Human (`human`), *Character Origins > Character Species > Human*, PDF page 86.
- Background: Soldier (`soldier`), *Character Origins > Character Backgrounds > Soldier*, PDF
  page 83.
- Class: Fighter (`fighter`), level 1, non-spellcasting, *Classes > Fighter*, PDF pages 47–48.
- Ability generation: Standard Array, assigned in `str,dex,con,int,wis,cha` order as
  `15,14,13,8,10,12`; the Soldier increases produce final raw scores `17,14,14,8,10,12`.
- Closed selected choices: Soldier `+2 Strength/+1 Constitution`, Athletics, Intimidation, dice
  gaming set, package A; Human Insight and Alert; Common, Dwarvish, and Giant; Fighter Perception
  and Survival, Defense Fighting Style, and Greatsword/Flail/Javelin Weapon Mastery choices.
- Starting equipment: Fighter package A — Chain Mail, Greatsword, Flail, eight Javelins,
  Dungeoneer's Pack, and four GP — represented later by immutable item-definition keys, never by
  item instances or an inventory array in this review.
- Identity policy: a later caller supplies a trimmed display name and may supply pronouns,
  appearance, and biography; the review has no actor, campaign, player-control, or item-instance
  identifier.

The expected-state owner map remains the table above. Unavailable future owners (notably class,
feat, mastery, AC/armor, and some item content) are recorded as blockers, not waived requirements.
No catalog or runtime artifact is authorized by this ratification.

Ratified by: **Dante**  
Ratified on: **2026-08-21**

## Ratification algorithm

1. Resolve the SRD source identity and confirm all selected section locators are from version 5.2.1.
2. Select exactly one species, one background, and one non-spellcasting class. Reject a source
   option that needs spell selection, an optional rule, a feat, multiclassing, or an unplanned
   mechanical subsystem.
3. Choose one complete ability-generation rule and show its exact legal score assignment. Reject
   an unvalidated total, duplicate use of a limited array value, or an input that supplies a
   modifier/proficiency bonus/derived AC.
4. List every choice each selected source offers at level one, including equipment alternatives.
   Resolve every choice now; no “choose later” placeholder is permitted.
5. Map every result to exactly one future owner: immutable definition, character actor, existing
   component recorder, item grant capability, or derived projection. Reject a duplicated owner or
   unowned result.
6. Confirm every future resulting item is an immutable definition reference that Items can create
   as an instance. Do not create or reserve it.
7. Confirm the path can produce the existing minimum playable state: ability checks, saving throws,
   hit points, armor class, weapon proficiency, a weapon profile reference, and level 1.
8. Record party-facing identity policy and campaign-scope requirement. A campaign ID, actor ID,
   source entity ID, and inventory instance ID remain absent.
9. The host signs and dates the record. Until then its status remains draft and no downstream
   character slice may be assigned.

## Dependency analysis

~~~text
CH0: ratify first supported character path
├─ official SRD 5.2.1 source identity and attribution                  [implemented: source registry]
├─ existing abilities/level/proficiencies/HP/AC/weapon contracts       [implemented repository artifacts]
├─ item definition/instance/equipment boundary                         [blocked parent: Items Slices 0–4]
├─ campaign-owned actor scope                                           [blocked parent: C2 campaign root]
├─ host-selected complete non-spellcasting build                       [missing leaf: CH0 ratification]
│  └─ CH1 actor shell and source provenance                            [blocked parent]
└─ spells, feats, multiclassing, level advancement, browser workflow  [excluded future]
~~~

CH0 is the lowest missing character-creation leaf. Its exit evidence is an approved record, not a
runtime test fixture. Items and campaign work do not block ratifying the path; they block later
creation of a playable actor from it.

## Acceptance matrix

| Case | Exact expected result |
| --- | --- |
| Complete path | One source-cited species/background/non-spellcasting class, method, six scores, choices, equipment, expected-state owners, identity policy, and signature form a ratified record. |
| Source fidelity | Every selected locator resolves to SRD v5.2.1; a different version, missing heading, or unsourced option leaves the record draft. |
| Choice completeness | Every level-one source/equipment choice is selected once at its declared cardinality; a missing, duplicate, unavailable, or “later” selection rejects ratification. |
| Derived-state boundary | Submitted choices contain no ability modifier, proficiency bonus, caller-supplied AC, roll, effect, actor ID, item-instance ID, or campaign ID. Expected-state review may label an output as derived but never turns it into authoritative input. |
| Ownership boundary | Every expected result has exactly one future owner; copied rules text, inventory arrays, class data in total level, or item statistics on the actor rejects. |
| Scope boundary | The record requires an existing active campaign for later creation but creates no campaign membership or character state. |
| Non-goal boundary | Spellcasting, feats, multiclassing, optional rules, level advancement, partial wizard state, and additional source options are absent. |
| No-write proof | Git status/diff shows only planning/roadmap/status documents; no catalog/runtime code, test fixture, database, or live-game state is authored. |

## Exit gate and next assignment

CH0 is complete only when the host has ratified one complete review record and a reviewer can map
every selected result to an official locator and exactly one future owner without inferring a rule.

Then—and only then—the next planning/implementation assignment is CH1: define the actor shell,
immutable source-definition convention, and provenance records for this one approved path. It must
first re-read the current creation, campaign, item, and existing D&D component contracts and confirm
the required campaign/item prerequisites.

## Plan-change rule

Revise CH0 before ratification if the chosen path needs a spell, feat, optional rule, unsupported
item behavior, a second class/origin, a new campaign ownership policy, or a source rule whose
choice/grant cannot be closed. Do not solve such a gap with free-form text, a hidden default,
caller-supplied derived values, copied source data, or an unplanned generic component.
