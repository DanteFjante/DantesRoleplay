# Character feature base plan and roadmap

Status: **Authoritative base roadmap — planning only; individual features still require dependency plans and semantic confirmation**
Last updated: 2026-08-20

## Execution rule

Use [GAME_SYSTEM_MASTER_PLAN.md](GAME_SYSTEM_MASTER_PLAN.md) for cross-subsystem ownership,
[TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md) for plan quality,
and a populated [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for the active assignment. Implement one reviewed delivery
slice, meet its exit gate, record evidence, and stop.

This file is the single planning authority for character-feature scope, ownership, ordering, and
feature IDs CH0–CH14. Individual dependency plans refine one row from this roadmap; they must link
back here and must not restate or silently change its cross-subsystem ownership. If implementation
evidence contradicts this roadmap, update this file and the affected feature plan together before
continuing.

## Goal

Let a player create a source-cited, playable D&D SRD 5.2.1 character through one governed,
auditable character-creation operation. The result is campaign-owned actor state that references
immutable ruleset definitions; it is not a collection of hand-edited JSON components.

The first release is an MCP-guided creation flow. A website character builder is a later consumer
of the same contracts and command; it must not introduce a second set of creation rules.

## Scope and first playable target

The initial target is deliberately narrow:

- one level-1 SRD character;
- one initially supported species/background/class combination as fixture content under reusable
  definition/grant contracts, expanded by reviewed vertical slices;
- a player-facing identity/profile sufficient to distinguish and describe the character;
- six ability scores under one explicitly chosen generation method;
- level, hit-point inputs, proficiencies, starting equipment, and sourced granted features;
- enough final state to make ability checks, saving throws, and the existing basic combat features
  meaningful.

Spellcasting, multiclassing, feats, optional rules, broad compendium import, level-up UX, and every
class/species/background are deferred. A feature is not supported merely because a generic
component could hold its data.

## Existing foundations

The current ruleset already supplies useful pieces: ability scores/checks, skill and saving-throw
proficiency recording, character level, hit points, armor class, weapon profiles/proficiencies, and
basic attack/damage work. Character creation must compose these validated components rather than
create parallel versions.

The following gaps remain before a real creation flow:

- actor identity/profile and character-specific provenance;
- immutable source-cited content definitions for supported origins/classes/features plus stable
  references to item definitions owned by Items;
- class membership, class-level inputs, and starting grants;
- choice/grant records and eligibility validation;
- one semantic, transactional character-creation command;
- a sample supported creation path and deterministic fixtures.

## Ownership model

| Layer | Owns | Does not own |
| --- | --- | --- |
| Character content definition | Stable, versioned SRD record for a species, background, class, feature, or character choice set | A particular character's selections, item definitions, or mutable resources |
| Character actor | Identity, chosen definition references, ability scores, current resources, and acquired-feature references | Rules text, mutable global content, or a copied inventory list |
| Grant/choice record | Which source offered a choice, eligible options, selected option, and resolved result | Re-running an old grant against changed content |
| Creation command | Validation, source resolution, typed effects, transaction, and audit | Narrating the character or bypassing existing components |
| Item/inventory owner | Item definitions, instances, possession containment, and equipped state | Character identity, class/origin rules, or copied statistics on the actor |
| Derived projection | Ability modifiers, proficiency bonus, DCs, and other computed values | Independently editable truth |

Every source reference records immutable content identity and version. Content corrections must never
silently rewrite an existing character; migration is explicit and separately governed.

### Cross-subsystem boundaries

| Concern | Authoritative owner | Character features may do |
| --- | --- | --- |
| Campaign membership, scope, chapters, quests, and party context | Campaign plans | Validate/reference the exact active campaign relationship; never copy campaign state. |
| Item definitions, instances, possession, equipment, currency, and consumption | Items and Inventory Plan plus ruleset item features | Request closed starting grants inside the CH5 root and derive inventory through containment. |
| Ability, proficiency, HP, AC, attack, damage, spell, feat, class-rule, death, and rest semantics | Existing/future D&D ruleset procedures | Resolve source declarations and call the owning recorder/mechanic; never reproduce formulas or transitions. |
| Character identity/profile, selected content references, creation/grant receipts, correction, retirement, and player-control relationship | This character roadmap | Add only the character-owned state named by the relevant CH feature plan. |
| Hidden backstory facts, secrets, clues, faction relationships, and world location | Campaign/world knowledge and containment owners | Link or project authorised context; never store an unprotected duplicate on the profile. |
| Authentication, audience filtering, and permission enforcement | Identity/authorization capability | CH14 consumes verified identities and policies; visibility labels alone grant no authority. |
| Portraits, visual sheets, and builder presentation | Website/visual consumer | CH8 consumes the same read/validate/create contracts; media and browser state are not character truth. |

## Proposed actor state

Use existing entities/components/containment/relationships. Names below are proposed D&D component
responsibilities, not permission to add them in one large change.

- character identity/profile: player-facing name, pronouns, short appearance and biography, and an
  actor/character marker. Campaign scope is an explicit campaign-owned relationship or containment
  decision, not a copied identity field; hidden backstory facts remain campaign knowledge rather
  than an unprotected profile field.
- abilities: the existing six-score component; raw values only.
- character level: the existing total level component and source reference.
- class membership: exact versioned class definition and level in that class. The hit-die input is
  read from the definition by its ruleset owner; grant receipts carry feature references rather
  than copying the definition's rules.
- origins: a CH3 background reference/grant receipt plus the Feature-26-owned selected-species
  reference and their selected grants.
- proficiencies: existing skill, save, and weapon-proficiency components as their own owners.
- vital statistics: existing hit-point and final armor-class components remain owned by their
  validated writers; class/origin grant receipts identify why creation-time values are legal.
- acquired features: source definition/version and any declared selections, never copied free-form
  rule text.
- inventory: read from item instances and containment; equipped state is separate from possession
  and no inventory array is stored on the character.
- creation receipt: creation protocol version and immutable source set; introduced by CH5 with the
  atomic creation transaction, not by the profile shell. Its existence means complete; the root
  operation ID remains audit/event history rather than a copied actor field.

Do not store ability modifiers, proficiency bonus, a second armor-class value/formula, inventory
arrays, or copied rule prose as authoritative actor state.

## Character creation command

Add one semantic character-creation operation with an explicit operation field:

- validate: performs all checks and returns named failures without state change;
- create: resolves one complete submitted character build, validates it, applies all allowed
  structural effects in one transaction, and records one root operation;
- inspect or revise are later operations, only after creation has proven the data model.

The request is a complete, schema-bound build. Version 1 does not hold a partially complete wizard
on the server. A player or host collects choices outside the command, then submits them together.
The future stateless question/answer protocol and executable workflow feature may improve the
experience without changing the final validation and write boundary.

The operation does not accept raw effects, arbitrary component data, arbitrary definition IDs, or
derived values. It resolves known content definitions and invokes the existing component owners.
This roadmap does not authorise a new MCP tool or commit kind. CH6 must inspect the then-current
three-verb surface and either fit the operation behind an existing governed kind or obtain the
separate public-surface confirmation required by procedure.mcp.add-tool.

## Grant and choice resolution

Creation should be a sequence of declared grants and choices, not branching application code hidden
inside every class or background.

A supported content definition declares only the grants it owns, for example:

- a background offers skill proficiencies, equipment choices, and an origin feature;
- a species offers traits and any supported choice set;
- a class offers hit-die input, saving throws, starting proficiencies, equipment choices, and
  level-one features.

The creation command validates choices against the exact definition version, resolves all grants to
a frozen grant receipt, then applies the result atomically. Any invalid choice leaves no character
entity, components, inventory, event, or success audit record behind.

Start with simple closed choice forms: choose N items from a supplied set, choose one skill from a
supplied set, or choose one declared feature option. Do not introduce arbitrary predicates,
expressions, or scripts into content definitions.

## Capability breadth and fixture rule

The first approved species, background, class, and equipment package are acceptance fixtures, not
the only values the data model may ever represent. Every foundational feature plan must distinguish:

- the reusable capability contract, such as a versioned species definition or choose-N grant;
- the currently supported source records, initially the single CH0 path; and
- a genuinely deferred capability, such as spellcasting or multiclassing, with its owning roadmap
  feature named.

A component schema or mechanic may not hard-code the initial species, background, class, weapon,
or character name merely because only one fixture ships first. Conversely, a generic field does
not make unreviewed SRD content supported: each additional option still needs source-cited content,
choice/grant validation, and a compact fixture through CH7's expansion gate.

If a first slice deliberately supports a narrower capability—not merely fewer content records—its
plan must name the restriction in the target, exclusions, failure behavior, and an explicit
successor feature. “Future work” without an owner is not an acceptable boundary.

## Foundational feature contracts

### CH0 — ratify the supported first character

Choose exactly one source-cited vertical slice, such as one background, species, and non-spellcasting
class, plus its legal level-one choices. Decide the ability generation method and what counts as a
creation-complete character. Record the source references and licensing/provenance format.

**Acceptance:** one written example can show every submitted choice and every resulting component,
item, and feature without an unstated rule.

### CH1 — actor shell and provenance

Define the character identity/profile component, source-reference convention, and
character-content-definition base for the one supported species/background/class/feature set.
CH5 introduces the creation receipt only when it can be committed atomically with the character.
Reference, but do not redefine, the approved item definitions. Add source registration/catalog
records and read-only discovery.

**Acceptance:** the system distinguishes an immutable source definition from a campaign character
that references its exact version; a new character has no copied rules text.

### CH2 — abilities and existing state integration

Define the chosen ability-score generation input and validate ranges, total/array rules, duplicate
assignment, and allowed score placement. Integrate existing level, proficiency, HP, AC, and weapon
components without duplicating their validation.

**Acceptance:** valid ability choices produce a character whose existing ability checks and saves
resolve correctly; malformed or derived inputs make no state change.

### CH3 — origins and closed choices

Implement background grant declarations, choice declarations, a background selection, and the
shared origin-grant receipt/resolution convention. Feature 26 remains the static-profile and
selected-state owner for species; CH3 consumes its validated species definition rather than
creating a parallel species record. The closed skill/tool/language/equipment selection forms
required by the first supported path start only once their source-specific grant owner is ready.

**Acceptance:** an origin grants only its declared choices; an unavailable, duplicate, or
out-of-scope selection is rejected atomically with a named correction.

### CH4 — class and level-one grants

Implement class membership, per-class level, hit-die inputs, level-one feature references, class
proficiencies, and starting-equipment grants. Keep spellcasting classes out until the spellcasting
base exists.

**Acceptance:** the sample class produces all and only its level-one state, and no source rule can
be claimed twice through overlapping grants.

### CH5 — atomic character creation runner

Build the validated character request, grant resolver, root transaction, effect application, event
integration, and audit result. It must compose existing component-record mechanics/internal
services rather than call MCP transport handlers or issue raw database writes.

**Acceptance:** successful creation creates one coherent actor with linked equipment and a complete
root receipt; an injected failure at every grant point leaves no partial character or event trail.

### CH6 — MCP contract and play handoff

Add query discovery for supported creation options and expose the closed character operation
through the confirmed current commit surface.
Publish the governing create/choice/inspection contracts through the confirmed current surface.
Correction and advancement contracts arrive only with CH7 and CH9. The success result gives the
host the entity id, chosen source references, relevant playable capabilities, and the first safe
next action.

**Acceptance:** a fresh MCP session can discover one supported build, validate it, create it, query
it back, and use it for an ability check without manual component editing.

### CH7 — regression, correction, and expansion gate

Add deterministic fixtures for valid and invalid builds, source-version preservation, duplicate
grants, rollback, replay, and catalog import/export. Add a correction path only for explicitly
owned creation fields. Expand to one additional source choice only after the first slice has a
played-session result.

**Acceptance:** every shipped creation option has a compact source-cited fixture, and a ruleset
revision cannot silently mutate a created character.

### CH8 — guided creation and builder parity

After the command is stable, add stateless follow-up questions and a human-facing builder page.
The guide derives questions from the same contracts and retains no server draft. The UI may present
choices and previews, but it submits the same complete build and displays the same validation/audit
result as MCP. A separately accepted executable-workflow capability may later assist navigation;
it must not wrap CH5 in a duplicate creation or advancement transaction.

**Acceptance:** MCP and website creation paths create byte-equivalent actor state from the same
input; abandoning a browser wizard creates no persistent partial state.

## Character feature roadmap and dependency-plan index

This table is the single character-feature sequence. “Planned” means the capability has a bounded
roadmap row; only a linked dependency plan may author its exact IDs, contracts, schemas, tests, and
first implementation slice.

| Feature | Authoritative capability section | Direct prerequisites | Dependency plan / current gate |
| --- | --- | --- | --- |
| CH0 | [Ratify first path](#ch0--ratify-the-supported-first-character) | Source registry and existing D&D state owners. | **Ratified** — [CH0 plan](character/feature-00/CHARACTER-FEATURE-00-DEPENDENCY-PLAN.md); Human Soldier Fighter is the only first-path fixture. |
| CH1 | [Actor shell and provenance](#ch1--actor-shell-and-provenance) | Ratified CH0; verified C15 campaign participation and profile-visibility decision. | **Implemented and accepted** — [CH1 plan](character/feature-01/CHARACTER-FEATURE-01-DEPENDENCY-PLAN.md); its profile fragment remains an internal CH5 composition dependency. |
| CH2 | [Abilities and existing state](#ch2--abilities-and-existing-state-integration) | Verified CH1; ratified CH0 assignment method; existing D&D recorders. | **Implemented and accepted** — [CH2 plan](character/feature-02/CHARACTER-FEATURE-02-DEPENDENCY-PLAN.md); its fragments remain internal CH5 composition dependencies. |
| CH3 | [Origins and closed choices](#ch3--origins-and-closed-choices) | Accepted CH1–CH2 and Feature-26 selected-species seam; approved CH0 origin path; each selected grant target has an owner. | [CH3 plan](character/feature-03/CHARACTER-FEATURE-03-DEPENDENCY-PLAN.md); Feature 28's ASI/language/feat composition, Feature 23 item assembly, and Feature 26's selected-species seam still block a source-complete fixture. |
| CH4 | [Class and level-one grants](#ch4--class-and-level-one-grants) | Verified CH1–CH3; ratified non-spellcasting CH0 class; class/HP, AC/equipment, feature, and item owners. | [CH4 plan](character/feature-04/CHARACTER-FEATURE-04-DEPENDENCY-PLAN.md); declaration work awaits CH0–CH3 and playable composition awaits its real ruleset/item owners. |
| CH5 | [Atomic creation](#ch5--atomic-character-creation-runner) | Accepted CH1–CH2; campaign attachment; Items 1–6; all selected derivation/feature owners; confirmed staged composition. | [CH5 plan](character/feature-05/CHARACTER-FEATURE-05-DEPENDENCY-PLAN.md); Slice 0 implements the generic read-only staged-world proof, while the root remains gated on CH3–CH4 and the remaining fixture owners. |
| CH6 | [MCP and play handoff](#ch6--mcp-contract-and-play-handoff) | Verified CH5; current public-surface contracts; active first playable mechanic. | [CH6 plan](character/feature-06/CHARACTER-FEATURE-06-DEPENDENCY-PLAN.md); discovery uses existing kinds unless a protocol walk proves a separately confirmed surface gap. |
| CH7 | [Correction and expansion gate](#ch7--regression-correction-and-expansion-gate) | Verified and played CH6 fixture; CH5 rollback/audit evidence; campaign attachment for profile correction. | [CH7 plan](character/feature-07/CHARACTER-FEATURE-07-DEPENDENCY-PLAN.md); profile-only correction and one source addition remain gated by played evidence and real owners. |
| CH8 | [Guided creation and builder parity](#ch8--guided-creation-and-builder-parity) | Stable CH6 command; CH7 evidence; CH5 pure validation boundary; Website/API semantic-write/exposure decisions for browser work. | [CH8 plan](character/feature-08/CHARACTER-FEATURE-08-DEPENDENCY-PLAN.md); MCP guidance is stateless and browser work remains a separately gated consumer. |
| CH9 | [Level advancement](#ch9--level-advancement) | Played CH6 level-one character; CH7 evidence; campaign advancement authorization/atomic-consume contract; ruleset class/HP and each selected feature/item owner. | [CH9 plan](character/feature-09/CHARACTER-FEATURE-09-DEPENDENCY-PLAN.md); one non-spellcasting 1→2 fixture is planned, while campaign policy and higher-level families remain separately owned. |
| CH10 | [Spellcasting foundation](#ch10--spellcasting-foundation) | Played CH9 evidence; ruleset class-level owner and Feature 31 spellcasting-resource contract; ratified caster source/list; CH5/CH9 composition. | [CH10 plan](character/feature-10/CHARACTER-FEATURE-10-DEPENDENCY-PLAN.md); character integration is planned, but the ruleset owns all spellcasting state and Feature 32 owns casting. |
| CH11 | [Feats and ability-score improvements](#ch11--feats-and-ability-score-improvements) | A level-appropriate CH9 slice; ruleset Features 27–28; campaign authorization; every selected effect owner; CH10/31–32 when spell-related. | [CH11 plan](character/feature-11/CHARACTER-FEATURE-11-DEPENDENCY-PLAN.md); one non-spellcasting feat-or-ASI entitlement is planned, with Feature 28 owning feat/ability state. |
| CH12 | [Multiclassing](#ch12--multiclassing) | A level-appropriate CH9 slice; ruleset Feature 27; accepted CH10/31–32 compatibility; campaign authorization; singular-to-plural membership migration; all selected grant owners. | [CH12 plan](character/feature-12/CHARACTER-FEATURE-12-DEPENDENCY-PLAN.md); one non-spellcasting second-class fixture is planned with one canonical membership migration. |
| CH13 | [Retirement/archive lifecycle](#ch13--retirementarchive-lifecycle) | Verified CH6 character surface; CH1 campaign attachment; campaign participation lifecycle transition; CH5 lifecycle initialization/migration; transaction evidence. | [CH13 plan](character/feature-13/CHARACTER-FEATURE-13-DEPENDENCY-PLAN.md); voluntary `active→retired→archived` is planned without deletion or D&D death semantics. |
| CH14 | [Authenticated player control](#ch14--authenticated-player-control) | Verified CH6/CH13; real principal authentication/context; pre-projection/action authorization hook; campaign administrator/audience policy; one player-safe action owner. | [CH14 plan](character/feature-14/CHARACTER-FEATURE-14-DEPENDENCY-PLAN.md); one scoped principal-to-active-character grant is planned, with identity/policy enforced outside profile data. |

### Dependency flow

~~~text
CH0 ratified first path
└─ CH1 actor shell + immutable content provenance
   └─ CH2 abilities/existing-state composition
      └─ CH3 background choices + Feature-26 species selection
         └─ CH4 class + level-one grants
            └─ Items 1–4 and Items 6/CH5 transaction decision
               └─ CH5 atomic creation
                  └─ CH6 discovery + play handoff
                     ├─ CH7 correction/evidence/content-expansion gate
                     │  └─ CH8 guided creation + builder parity
                     ├─ CH9 advancement
                     │  ├─ CH10 spellcasting foundation
                     │  ├─ CH11 feats/ability-score improvements (CH10 when spell-related)
                     │  └─ CH12 multiclassing (also requires CH10)
                     ├─ CH13 retirement/archive lifecycle
                     └─ CH14 authenticated player control
~~~

## Required procedure contracts

Create each governing contract in the same slice as its capability:

- procedure.character.create
- procedure.character.choose
- procedure.mechanic.dnd2024.character-grant-receipts
- procedure.character.inspect
- procedure.character.correct, deferred until CH7
- procedure.character.advance, deferred until CH9
- procedure.mechanic.dnd2024.background
- procedure.mechanic.dnd2024.species
- procedure.mechanic.dnd2024.class
- procedure.mechanic.dnd2024.equipment

The contracts must name source scope, owner components, allowed choices, prerequisites, failure
codes, transaction behavior, test fixtures, and the recovery call for invalid input.

## Test matrix

- source record/version/provenance and catalog round-trip;
- ability-method boundaries, duplicate assignments, and all invalid score/total inputs;
- valid and invalid background/species/class/equipment choices;
- grants, prerequisites, duplicate suppression, and incompatible choices;
- no derived-field writes and no copied mutable source data;
- rollback at entity creation, every grant, inventory/containment, effect, guard, reaction, and
  audit failure;
- resulting ability checks, saving throws, and supported attack prerequisites;
- source revision preservation, explicit correction, and later migration behaviour;
- MCP protocol walk: discover, validate, create, inspect, and play one action;
- no partial creation state after cancellation, timeout, or abandoned UI input.

## Non-goals

This plan does not build all SRD character options, a public character sheet, multiclassing, feats,
spell selection, level-up automation, map integration, or a client-side creation wizard first. It
also does not loosen the kernel to understand D&D vocabulary: D&D rules remain source-cited content,
mechanics, and components.

## Dependencies

Events and subscriptions may enrich creation with notifications, but are not required for the core
creation transaction. The completed event runtime must still participate atomically when creation
effects emit structural events.

This plan builds on DND_RULESET_IMPLEMENTATION_PLAN.md Stages 1–3 and the existing level,
proficiency, vital-statistics, weapon, and attack work. The later executable-workflow plan can
orchestrate character creation but is not a prerequisite for the first semantic character command.

Items and Inventory Slices 1–4 are prerequisites for the first complete equipped character. Before
CH5 or Items Slice 6 is assigned, the reviewer must ratify one owner for the atomic
character-plus-starting-equipment root transaction, including failure injection and audit/event
ownership. The other plan provides a called capability; it does not open a nested independent root.

## Future dependency-plan protocol

Every CH1–CH14 dependency plan must use this roadmap as its product/ownership parent and contain:

- one player-facing target and explicit included/excluded behavior;
- exact SRD 5.2.1 source identity and locators for rules-bearing content;
- an ownership/overlap search covering character, campaign, item, and existing ruleset owners;
- a recursive dependency graph whose lowest missing leaf becomes the only next slice;
- closed input/state/result contracts, missing/null/empty semantics, canonical ordering, stable IDs,
  source-version behavior, and caller-forbidden derived values;
- exact transaction/effect/event/audit ownership plus replay, rollback, cancellation, corrupt-state,
  readback, restoration, and repository acceptance cases;
- a breadth statement distinguishing reusable capability from the first supported content fixture;
  and
- an exit gate and change rule that stop before the next CH feature.

Plans should link to these sections instead of copying the goal, ownership model, entire feature
roadmap, generic repository workflow, or cross-subsystem boundaries. Durable behavior belongs in
the owning procedure/contracts and tests when implemented; completed evidence belongs in a short
receipt, not backfilled into every future plan.

## Post-foundation boundaries

### CH9 — level advancement

CH9 begins only after a played CH6 character and CH7 evidence. It advances one supported
non-spellcasting class level through versioned declarations, a campaign-owned advancement
authorization, and a frozen receipt; downgrade, duplicate-grant, stale-source, spent-authorization,
and partial-effect attempts fail unchanged. The first bounded fixture is level 1 to level 2; XP or
milestone policy, later levels, subclass, feat/ASI, spellcasting, and multiclassing remain separately
owned.

### CH10 — spellcasting foundation

CH10 is a dedicated spellcasting foundation, not extra fields on a generic character component.
It consumes a separate ruleset owner for spell definitions, known/prepared conventions, slots,
casting ability, save DC, and attack bonus before character creation or advancement may consume
one ratified caster path. Spell attacks, saves, targets, durations, effects, and resource spending
remain ruleset spell-resolution work.

### CH11 — feats and ability-score improvements

CH11 adds feats and ability-score improvements one source-cited family at a time, through the
ruleset owner rather than the creation-time ability policy. Start with one non-spellcasting
feat-or-ASI entitlement at an actually supported class level; reject conflicting prerequisites,
duplicate benefits, invalid ability distributions, and ambiguous grant order.

### CH12 — multiclassing

CH12 owns multiclassing separately because it changes class-level cardinality, prerequisite
evaluation, grant order, total versus per-class levels, and spellcasting interaction. It replaces
CH4's singular class membership with one canonical plural representation and migration; it may not
enter as ordinary CH4 content or leave both representations authoritative.

### CH13 — retirement/archive lifecycle

CH13 owns voluntary retirement/archive lifecycle only. It preserves character state and history,
coordinates campaign-owned active participant state, and permits only `active→retired→archived` in
this feature; D&D unconsciousness, death saves, death, resurrection, and similar mechanical
consequences remain with the ruleset plans.

### CH14 — authenticated player control

CH14 owns player-to-character control only after a real identity/authentication and authorization
capability exists. It binds a verified principal to one active campaign character and enforces that
grant before reads/actions; campaign scope remains campaign-owned, item possession remains
containment, and descriptive visibility never substitutes for access control.

- Advancement, spellcasting, feats, and multiclassing must resolve versioned declarations to a
  frozen receipt before effects are applied. A new option never bypasses existing component owners.
