# Feature 26 dependency plan — SRD species traits and mechanical grants

Status: **Slices 1–2 are implemented and accepted. Immutable profiles and selected-species references are available; full origin assembly remains Feature 30 work.**
Last updated: 2026-08-21

## Execution rule

Slice 1 was implemented as reviewed catalog data only. It created the closed
`dnd2024.species-profile` definition, its static governing procedure, and nine versioned source
entities; it created no actor selection, mechanic, event, subscription, migration, fixture, or
campaign state. Its evidence is recorded in `FEATURE-26-SLICE-1-RECEIPT.md`. All later slices
remain prospective and require their stated confirmation boundaries.

## Target capability

The ruleset can identify each SRD 5.2.1 playable species and, in later composed slices, apply only
those species traits whose authoritative rule owners are available.

### Included

- The SRD Dragonborn, Dwarf, Elf, Gnome, Goliath, Halfling, Human, Orc, and Tiefling species.
- Immutable source-cited identity, Humanoid type, allowed Size, base Speed, and trait/choice
  inventory data on versioned content definitions.
- A future selected-species reference, choice validation, and grants for permanent, activated,
  triggered, rest-limited, damage, sensory, spell, and movement traits.
- Explicit integrations with existing Size, Speed, proficiency, damage, HP, condition, and
  character-origin boundaries.

### Excluded

- Non-SRD species, custom lineages, mixed species, homebrew ancestry, lore prose, portraits,
  appearance, age, alignment, and player-facing builder UX.
- Any feature granted by a species: attacks, resistance, Darkvision, temporary Hit Points,
  teleportation, flight, spells, Heroic Inspiration, Feats, rest recovery, or a D20 reroll.
- Class/background/feat grants, languages or tool membership, item ownership, natural weapons,
  monster stat blocks, and a generic creature-type system.

## Official source basis

The source is `source.dnd2024.srd-5.2.1`, *System Reference Document 5.2.1* (Wizards of the Coast
LLC, 2025-05-01, CC-BY-4.0), [Character Origins > Character Species, PDF pp. 82–86](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf).

- Species establishes creature type, Size, Speed, and special traits. Each included player
  species is Humanoid and has the exact Size/Speed/trait choices stated in its species entry.
- The source’s traits span incompatible kinds of rule: passive facts, saving-throw advantage,
  resistance, bounded rest resources, temporary effects, movement exceptions, spell grants, and
  choices. A static profile may declare the source facts, but cannot execute them itself.

## Planning inventory and ownership result

| Inquiry | Evidence and decision |
| --- | --- |
| Immutable species identity | Character Feature 1 already supplies `dnd2024.character.content-definition` and `content.dnd2024.species.human.v1`. It is versioned identity only and expressly does not copy grants. Feature 26 extends it rather than inventing a second identity namespace. |
| Size and Speed | Features 23 and 20 respectively own `dnd2024.creature-size` and `dnd2024.speed`, each with a normal recorder. A profile may state source values; no species effect writes a competing Size/Speed component. |
| Proficiency membership | Feature 2 owns skill membership; Feature 28 Slice 1 owns language/tool membership. Species selection/grants must compose with those records rather than storing skill/language/tool arrays in species state. |
| Damage and HP | Feature 15 plans the sole mitigation owner; Features 6 and 16 own HP and temporary HP. Resistance, Dwarven Toughness, and Adrenaline Rush wait for those confirmed mutation paths. |
| Conditions/saves | Features 3–4 and 13 own D20 circumstances, saves, and condition state. Fey Ancestry, Gnomish Cunning, Brave, and poison defenses cannot become unscoped species modifiers. |
| Senses, spells, rest, and time | No Feature 34 vision/senses owner, Feature 31–32 spell owner, or Feature 33 rest/expiry owner is verified. Darkvision, Tremorsense, spell lineages, flight duration, and use recovery are blocked parents. |
| Character creation | Feature 30 will assemble a legal sheet. It must atomically coordinate selected species, source choices, Size, Speed, proficiencies, and grants; this feature does not create a competing creation transaction. |
| Creature kind | Feature 17 identified a minimal PC/monster death-rule marker as a separate shared gap. Species’ Humanoid type does not decide that branch and must not silently become the missing marker. |

## Recursive dependency analysis

```text
Feature 26: SRD species traits and mechanical grants
├─ SRD species source and content-definition convention       [implemented]
├─ immutable source-cited species profile catalog              [missing leaf: Slice 1]
├─ selected species reference                                  [accepted: Slice 2]
├─ species-specific choice record                              [blocked: later source-choice owner]
├─ base Size/Speed application                                 [blocked: selected species + Features 20/23]
├─ static proficiency grants                                   [blocked: selected species + Features 2/28]
├─ damage/HP/temporary-HP species effects                      [blocked: Features 15–16]
├─ save/check circumstance traits                              [blocked: Features 3–4 and 13]
├─ senses, hiding, movement, and spatial traits                [blocked: Features 20, 34, and 22]
├─ rest-limited resources and timed transformations             [blocked: Feature 33 + duration owner]
├─ spell/Cantrip lineage traits                                 [blocked: Features 31–32]
├─ Heroic Inspiration, Origin Feat, and level-gated grants     [blocked: Features 28, 30, and 36]
└─ complete selectable species behaviour                       [blocked parent]
```

Slice 1 is independent because it records source facts only. It has no actor selection, state
change, player intent, roll, resource, or trait consequence.

## Dependency and ownership decisions

1. A versioned content entity remains the canonical species identity. A new immutable
   `dnd2024.species-profile` component belongs on that entity, never on a creature. It holds only
   source facts needed to validate a later choice: species key, Humanoid type, allowed Size values,
   base movement profile, declared trait keys, and declared choice families.
2. A profile is not selected character state. Absence of a future selection means unknown/not
   selected; it never means Human, Medium, 30 feet, or no traits. Feature 30 will own the atomic
   legal-sheet transaction and call the normal state owners once confirmed.
3. Size and base Speed stay authoritative only in their existing actor components. The profile is
   a referenced immutable definition; it never updates a turn budget, placement, carrying total,
   or effective Speed.
4. Trait keys are a closed, source-facing catalog vocabulary, not executable scripts, condition
   names, or a generic “feature payload.” A later trait family receives a profile/selection
   reference and proves the exact trait before it can grant a consequence.
5. Choice families describe only source-required selections, such as Draconic Ancestry, Elf
   Lineage, Gnome Lineage, Giant Ancestry, and Fiendish Legacy. They do not choose spells, skills,
   damage, Feats, or sizes on behalf of a character.
6. A generic actor `creature-kind` is deliberately not part of this slice. Humanoid is species
   source data; the PC/monster policy that Feature 17 needs has different semantics and a shared
   owner to be confirmed.

## Confirmation boundary

| Decision | Required confirmation before implementation |
| --- | --- |
| Profile schema/ID | Exact component id, profile fields, source-reference shape, canonical list ordering, and immutable-revision policy. |
| Content attachment | The existing content-definition procedure’s entity requirements and the catalog authoring path for an additional static component. |
| Trait vocabulary | Exact source-to-key table, choice-family vocabulary, and which values are facts versus later selected values. |
| Selection state | Owner, component/reader ids, one-species invariant, source-version reference, missing/replace semantics, and Feature 30 transaction boundary. |
| Base facts | Confirmed composition from profile/selection to the Feature 20 Speed and Feature 23 Size normal writers without a second state owner. |
| Trait effects | The precise effect owner and duration/resource lifecycle for every trait family before any trait becomes executable. |

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Static species-profile catalog | **Verified 2026-08-21.** | Nine source-cited immutable profiles validate and expose no behaviour. |
| 2 | Selected-species reference | Slice 1, C15, and CH5 Slice 0's accepted staged-world composition seam; permanent contracts confirmed. | One actor can safely reference one active immutable profile and grants nothing. |
| 3 | Creation assembly of Size, Speed, skills, languages, and tools | Slice 2; Features 2, 20, 23, 28; transaction composition confirmed. | A legal origin applies each source fact through its sole normal owner atomically. |
| 4 | Passive permanent trait grants | Slices 2–3 plus Feature 15/16/34/17 owners. | Each supported passive trait has one owner and no duplicated permanent state. |
| 5 | Activated and triggered traits | Slice 2 plus action economy, target, D20/save, HP, and tactical owners. | A declared trait validates uses/targets/costs and resolves only its stated consequence. |
| 6 | Rest-limited, timed, sensory, movement, and spell traits | Slices 2–5 plus Features 20, 31–34 and duration lifecycle. | Every expiry/recovery/replacement rule is source-backed and durable. |
| 7 | Full character-creation integration | Slices 1–6 plus Feature 30. | The builder produces a legal selected species with all supported choices/grants and no parallel data. |

## Slice 1 — immutable SRD species-profile catalog

### Runtime artifacts

- New confirmed `dnd2024.species-profile` component/schema and its governing static-definition
  procedure or a reviewed revision of the existing character-content-definition procedure.
- Nine versioned `content.dnd2024.species.*.v1` entities. The already-existing Human content
  identity is extended rather than replaced; all entity revisions follow catalog policy.
- Focused validation fixtures/tests. No actor component, selection writer, character sheet,
  feature action, event, subscription, migration, or public player intent.

### Data contract and required state

Each profile is closed and immutable. It records its stable content key, fixed source reference,
`humanoid` creature type, canonical allowed Size list, base five-mode Speed facts, a canonical
ordered declared-trait-key list, and canonical declared choice-family list. It contains no
selected choice, free text, granted language/skill/tool, spell id, HP number, D20 modifier,
resource count, duration, target, condition, item, action cost, or executable payload.

The initial entities are Dragonborn, Dwarf, Elf, Gnome, Goliath, Halfling, Human, Orc, and
Tiefling. Each source locator must identify the corresponding PDF entry. Profile identity/version
must agree exactly with `dnd2024.character.content-definition`; missing or mismatched identity,
unknown key, invalid Size/Speed, duplicate/out-of-order vocabulary, improper source, or extra
field rejects unchanged.

### Recording behaviour, result, and effects

Catalog authoring validates the complete profile before creating/revising its immutable definition
entity. The administrative result returns the profile id/key/version and canonical static facts.
It has no creature role, dice, effects on a creature, player-facing routing phrase, or runtime
action. A valid profile can be read deterministically; no profile implies a character has selected
it.

### Invariants, failure behaviour, and non-goals

- All nine profiles cite the registered source; their static Size/Speed facts match the source.
- A trait/choice exists in a profile only as a declaration. It cannot cause resistance, vision,
  a spell, an attack, a resource spend/recovery, a condition, movement, or an effect.
- The existing Human content identity remains the unique Human v1 definition. No duplicate species
  key/version or silent mutable revision is accepted.
- Rejected validation/authoring leaves catalog entity bytes and all actor/world state unchanged.
- This slice cannot select a species, record a creature type, alter Size/Speed, or initiate
  character creation.

### Slice 1 implementation sequence

1. Re-read the source registry, the current content-definition contract, representative Human
   entity, catalog authoring conventions, and owners listed above; repeat species/trait/ancestry
   overlap searches.
2. Stop for the confirmation boundary. Confirm permanent IDs and the profile/definition attachment
   rather than duplicating the existing immutable content convention.
3. Author the schema/procedure/catalog definitions and focused tests together. Validate the full
   nine-species table from the source without copying rules prose into catalog descriptions.
4. Test valid readback, canonical ordering, each Size option, and every base-Speed boundary.
   Test malformed, unknown, duplicate, wrong-version, wrong-source, extra-field, and attempted
   mutable-revision rejection with before/after comparisons.
5. Query all artifacts back; run `roleplay validate catalog`, focused tests, the full suite, and
   `git diff --check`; write a receipt and stop. Do not begin Slice 2.

### Slice 1 acceptance matrix

| Case | Exact assertion |
| --- | --- |
| Source inventory | Exactly the nine named SRD species have one active v1 identity/profile and the appropriate page locator. |
| Static facts | Every profile is Humanoid; its allowed Size and base Speed match the source, including Human’s Small-or-Medium choice and Goliath’s 35-foot base Speed. |
| Declaration only | Dragonborn ancestry, Elf/Gnome lineage, Giant Ancestry, and Fiendish Legacy appear as declared choice families, with no selected value or actor effect. |
| Trait isolation | Reading every profile produces zero effects and cannot change an actor’s Size, Speed, HP, proficiencies, conditions, inventory, or campaign state. |
| Closed shape | Missing/null/wrong type/unknown/duplicate/out-of-order/extra profile fields, wrong content key/version, and wrong source reject unchanged. |
| Immutability | A second entity for an existing key/version and an attempted rewrite of Human v1 reject; a future content revision requires a distinct reviewed version. |
| Readback/replay | Repeated reads return byte-identical canonical data and source reference. |
| Repository | Catalog validation, focused tests, full suite, diff check, and source query-backs pass. |

### Slice 1 exit gate

Slice 1 is verified only when the nine source-cited immutable profiles have a single catalog
owner, exact closed data/readback/immutability evidence, no actor behaviour, catalog validation,
repository checks, and a receipt. Stop before selected-species state or any trait grant.

## Slice 2 — selected-species reference

### Refined boundary

CH5 Slice 0 now supplies a read-only staged-world overlay, and C15 supplies an effect-free active
campaign-participation planner. Those lower seams are sufficient for one narrow child fragment:
record the immutable selected species definition for a staged actor. They are **not** a substitute
for Feature 30's future public validation/create transaction, source-choice collection, Size/Speed
assembly, trait resolution, or receipts.

The first runtime form deliberately records no choice values. It accepts a trusted CH5 binding to
one active CH1 species content definition with a valid matching `dnd2024.species-profile`, and
returns exactly one add-only actor component fragment. The component contains only the versioned
definition ID. It has no species key/title/source reference/type, trait, size, speed, language,
skill, feat, ancestry/lineage value, provenance receipt, or executable data.

The actor must be present in the staged overlay, have valid active C15 scope, and have no existing
selection. The bound definition must have exactly one active CH1 species identity and exactly one
valid matching immutable species profile. Absent/archived/wrong-kind/mismatched/corrupt content,
duplicate actor state, invalid scope, malformed IDs, and all direct-write attempts return a named
no-effect failure. CH5 alone appends and applies a valid fragment.

This selection record does not claim that every source-required species choice or trait has been
resolved. Choice-family values, Human's skill/Origin Feat decisions, Size, Speed, Heroic
Inspiration, and all trait consequences remain with their named owners. A future source-choice
slice must compose with this selection rather than adding a second species reference.

### Proposed permanent vocabulary — confirmation required

| Role | Proposed permanent ID and closed meaning |
| --- | --- |
| Actor selection component | `dnd2024.selected-species`, present at most once on an actor and containing only `speciesDefinitionId`, a canonical `content.*` reference to one immutable active species definition. |
| Governing procedure | `procedure.mechanic.dnd2024.species-selection`, governing the selected-species reference and its internal staged resolver only; it grants no species fact or effect. |
| Draft declaration | `mechanic.dnd2024.species-selection.resolve`, documenting the non-public CH5 composition contract. |
| Typed resolver | `ICharacterSpeciesSelectionResolver`, accepting actor ID and trusted species-definition ID and returning zero or one ordinary `dnd2024.selected-species` add fragment. |

### Implementation and exit gate

1. Re-read the CH1 content-definition, species-profile, C15, and CH5 staged-world contracts and
   confirm the four entries above.
2. Add the closed component/schema, procedure, draft declaration, typed request/problem/plan
   records, staged-world-compatible resolver, and dependency-injection registration.
3. Test Human's source-cited v1 definition, all other valid profile identities, canonical data,
   C15/staged scope, component absence, and zero base-world persistence.
4. Test malformed/unknown/archived/wrong-kind/mismatched/corrupt definition state; malformed
   existing selection; duplicate selection; missing actor/scope; and no effects on every failure.
5. Run focused tests, catalog validation, and diff check; write a receipt and stop for acceptance.

Slice 2 exits only when selected-species state has one durable owner, references exactly one valid
immutable profile, creates no trait or base-fact effect, and cannot be applied outside CH5's future
atomic root. **Implemented and accepted; see `FEATURE-26-SLICE-2-RECEIPT.md`.**

## Trait-family dependency map

```text
static profile declaration
├─ Size / base Speed ──────────────> Feature 30 assembly -> Features 23 / 20 state owners
├─ skill/language/tool choices ────> Features 2 / 28 membership owners
├─ damage resistance ──────────────> Feature 15 mitigation owner
├─ maximum/temporary HP ───────────> Features 6 / 16 HP owners
├─ save/check Advantage or reroll ─> Features 3 / 4 + trusted circumstance/choice protocol
├─ Darkvision / Tremorsense / Hide ─> Feature 34 senses and hiding owner
├─ reach, teleport, flight, pass-through, carry ─> Features 20 / 22 / 23 tactical owners
├─ Breath Weapon / damage / saves ─> Features 4 / 9 / 12 / 15 + targeting/area owner
├─ spells and Cantrips ────────────> Features 31 / 32 spell-resource/effect owner
└─ rest uses, durations, level gates ─> Features 27 / 33 / 36 + duration lifecycle
```

Examples: Dragonborn Breath Weapon and resistance need area/save/damage/mitigation; Dwarf poison
defences and Toughness need condition/save/HP; Elf, Gnome, and Tiefling lineage spells need the
spell system; Goliath’s Large Form needs Size/Speed/duration; Halfling Nimbleness and Naturally
Stealthy need tactical/hiding state; Human’s Heroic Inspiration and Origin Feat need their own
grant/resource owners; Orc’s zero-HP response needs Feature 17. None is authorised merely because
the profile declares it.

## Plan-quality audit

- One capability, concrete source/locators, explicit non-goals, ownership search, and recursive
  dependency graph: yes.
- Static identity is separated from actor selection, authoritative actor state, and transient
  resolution; all downstream trait consequences name their owners: yes.
- Slice 1 is an independently valid catalog leaf with closed data, readback, rejection,
  immutability, isolation, and repository acceptance criteria: yes.
- This planning pass created no runtime game artifact: yes.

## Plan-change rule

Revise before implementation if a compatible static species-profile owner already exists, the
source catalog/version changes, Feature 30 establishes a different immutable-reference protocol,
or any profile field would duplicate a verified state owner. Do not create an opaque trait script,
put selected choices on immutable content, treat a missing selection as a default species, or
implement a trait by writing Size, Speed, HP, proficiency, condition, spell, or duration state
outside its confirmed owner.
