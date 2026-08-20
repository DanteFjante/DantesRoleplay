# Character creation plan

Status: **Draft — design plan only; no bulk rules/content import is authorised by this document**  
Last updated: 2026-08-20

## Execution rule

Use [GAME_SYSTEM_MASTER_PLAN.md](GAME_SYSTEM_MASTER_PLAN.md) for cross-subsystem ownership,
[TERRA-FEATURE-PLANNING-GUIDE.md](ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md) for plan quality,
and a populated [SUBSYSTEM_IMPLEMENTATION_HANDOFF.md](SUBSYSTEM_IMPLEMENTATION_HANDOFF.md) for the active assignment. Implement one reviewed delivery
slice, meet its exit gate, record evidence, and stop.

## Goal

Let a player create a source-cited, playable D&D SRD 5.2.1 character through one governed,
auditable character-creation operation. The result is campaign-owned actor state that references
immutable ruleset definitions; it is not a collection of hand-edited JSON components.

The first release is an MCP-guided creation flow. A website character builder is a later consumer
of the same contracts and command; it must not introduce a second set of creation rules.

## Scope and first playable target

The initial target is deliberately narrow:

- one level-1 SRD character;
- one supported species/background/class combination at a time, expanded by vertical slices;
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

- actor identity and character-specific provenance;
- immutable source-cited content definitions for supported origins/classes/features/equipment;
- class membership, class-level inputs, and starting grants;
- choice/grant records and eligibility validation;
- one semantic, transactional character-creation command;
- a sample supported creation path and deterministic fixtures.

## Ownership model

| Layer | Owns | Does not own |
| --- | --- | --- |
| Content definition | Stable, versioned SRD record for a species, background, class, feature, item, or choice set | A particular character's selections or mutable resources |
| Character actor | Identity, chosen definition references, ability scores, current resources, acquired features, and inventory | Rules text or mutable global content |
| Grant/choice record | Which source offered a choice, eligible options, selected option, and resolved result | Re-running an old grant against changed content |
| Creation command | Validation, source resolution, typed effects, transaction, and audit | Narrating the character or bypassing existing components |
| Derived projection | Modifiers, proficiency bonus, AC, DCs, and other computed values | Independently editable truth |

Every source reference records immutable content identity and version. Content corrections must never
silently rewrite an existing character; migration is explicit and separately governed.

## Proposed actor state

Use existing entities/components/containment/relationships. Names below are proposed D&D component
responsibilities, not permission to add them in one large change.

- character identity: player-facing name, pronouns, campaign scope, and actor flag.
- abilities: the existing six-score component; raw values only.
- character level: the existing total level component and source reference.
- class membership: class definition/version, level in that class, hit-die input, and granted
  feature references.
- origins: separate background and species references plus their selected grants.
- proficiencies: existing skill, save, and weapon-proficiency components as their own owners.
- vital statistics: existing hit-point and armor-class input components; class/origin grant records
  identify why their values are legal.
- acquired features: source definition/version and any declared selections, never copied free-form
  rule text.
- inventory: entity containment and stable item-definition references; equipped state is separate
  from possession.
- creation record: creation method, completion status, source set, and root operation id.

Do not store ability modifiers, proficiency bonus, computed armor class, or copied rule prose as
authoritative actor state.

## Character creation command

Add one semantic command, tentatively commit(kind: "character"), with an explicit operation field:

- validate: performs all checks and returns named failures without state change;
- create: resolves one complete submitted character build, validates it, applies all allowed
  structural effects in one transaction, and records one root operation;
- inspect or revise are later operations, only after creation has proven the data model.

The request is a complete, schema-bound build. Version 1 does not hold a partially complete wizard
on the server. A player or host collects choices outside the command, then submits them together.
The future stateless question/answer protocol and executable workflow feature may improve the
experience without changing the final validation and write boundary.

The command does not accept raw effects, arbitrary component data, arbitrary definition IDs, or
derived values. It resolves known content definitions and invokes the existing component owners.

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

## Delivery slices

### Slice 0 — ratify the supported first character

Choose exactly one source-cited vertical slice, such as one background, species, and non-spellcasting
class, plus its legal level-one choices. Decide the ability generation method and what counts as a
creation-complete character. Record the source references and licensing/provenance format.

**Acceptance:** one written example can show every submitted choice and every resulting component,
item, and feature without an unstated rule.

### Slice 1 — actor shell and provenance

Define the character identity and creation-record components, source-reference convention, and
content-definition base for the one supported species/background/class/item set. Add source
registration/catalog records and read-only discovery.

**Acceptance:** the system distinguishes an immutable source definition from a campaign character
that references its exact version; a new character has no copied rules text.

### Slice 2 — abilities and existing state integration

Define the chosen ability-score generation input and validate ranges, total/array rules, duplicate
assignment, and allowed score placement. Integrate existing level, proficiency, HP, AC, and weapon
components without duplicating their validation.

**Acceptance:** valid ability choices produce a character whose existing ability checks and saves
resolve correctly; malformed or derived inputs make no state change.

### Slice 3 — origins and closed choices

Implement separate background and species definition records, their one-purpose grant declarations,
and the closed skill/tool/language/equipment selection forms required by the first supported path.

**Acceptance:** an origin grants only its declared choices; an unavailable, duplicate, or
out-of-scope selection is rejected atomically with a named correction.

### Slice 4 — class and level-one grants

Implement class membership, per-class level, hit-die inputs, level-one feature references, class
proficiencies, and starting-equipment grants. Keep spellcasting classes out until the spellcasting
base exists.

**Acceptance:** the sample class produces all and only its level-one state, and no source rule can
be claimed twice through overlapping grants.

### Slice 5 — atomic character creation runner

Build the validated character request, grant resolver, root transaction, effect application, event
integration, and audit result. It must compose existing component-record mechanics/internal
services rather than call MCP transport handlers or issue raw database writes.

**Acceptance:** successful creation creates one coherent actor with linked equipment and a complete
root receipt; an injected failure at every grant point leaves no partial character or event trail.

### Slice 6 — MCP contract and play handoff

Add query discovery for supported creation options and the closed character commit operation.
Create the governing procedure contracts for character creation, choices, inspection, correction,
and later advancement. The success result gives the host the entity id, chosen source references,
relevant playable capabilities, and the first safe next action.

**Acceptance:** a fresh MCP session can discover one supported build, validate it, create it, query
it back, and use it for an ability check without manual component editing.

### Slice 7 — regression, correction, and expansion gate

Add deterministic fixtures for valid and invalid builds, source-version preservation, duplicate
grants, rollback, replay, and catalog import/export. Add a correction path only for explicitly
owned creation fields. Expand to one additional source choice only after the first slice has a
played-session result.

**Acceptance:** every shipped creation option has a compact source-cited fixture, and a ruleset
revision cannot silently mutate a created character.

### Slice 8 — later experience improvements

After the command is stable, add stateless follow-up questions, registered creation/advancement
workflows, and a human-facing builder page. The UI may present choices and previews, but it submits
the same complete build and displays the same validation/audit result as MCP.

**Acceptance:** MCP and website creation paths create byte-equivalent actor state from the same
input; abandoning a browser wizard creates no persistent partial state.

## Required procedure contracts

Create each governing contract in the same slice as its capability:

- procedure.character.create
- procedure.character.choose
- procedure.character.inspect
- procedure.character.correct
- procedure.character.advance, deferred until level advancement
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
this plan's Slice 5 or Items Slice 6 is assigned, Terra High must ratify one owner for the atomic
character-plus-starting-equipment root transaction, including failure injection and audit/event
ownership. The other plan provides a called capability; it does not open a nested independent root.
