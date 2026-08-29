# D&D 2024 CC3A implementation — SRD backgrounds and fixed origin proficiencies

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC3A**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; *Character Creation > Step 2:
Character Origin* (PDF pp. 19–20) and *Character Origins > Character Backgrounds > Parts of a
Background / Acolyte / Criminal / Sage / Soldier* (PDF p. 83)
Outcome: model every background in SRD 5.2.1 and allow the accepted basic creator to select any of
them while applying exact fixed skill, tool, and Common-language state.
Exclusions: player-selected languages, Soldier's Gaming Set choice, feat behavior and spell choices,
equipment instantiation or cash selection, optional/non-SRD backgrounds, class armor, class tool
choices, UI discovery, migrations, and new public protocol kinds.
Allowed files/areas: D&D application background/component/mechanic/procedure catalog records, the
existing D&D acceptance-test harness, this dependency plan, and this slice's evidence receipt.
Stop point: all four SRD backgrounds validate and compose with all twelve level-1 class models;
unsupported choices/grants remain source-specific pending entitlements and no C# rules are added.

## Confirmed decisions

The user's 2026-08-27 request to continue as many character-creation slices as quality permits,
together with the earlier instruction to prefer complete correct models over partial mechanics,
confirms this bounded leaf and:

- permanent component ID `dnd2024.background-creation-profile`;
- permanent background IDs `content.dnd2024.background.acolyte.v1`,
  `content.dnd2024.background.criminal.v1`, and `content.dnd2024.background.sage.v1`, while retaining
  `content.dnd2024.background.soldier.v1`;
- widening `dnd2024.character-creation-record` to the exact four-background by twelve-class level-1
  template set; and
- applying only source-fixed state for which a canonical owner already exists. Unselected tools,
  languages, feats, and equipment grant no behavior.

No optional rule, house rule, source extension, migration, MCP surface, or C# semantic change is
confirmed by this slice.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Background ability scores | Each background names three eligible abilities and permits +2/+1 or +1 each | `dnd2024.background.ability-increase-options` and ability child | Add the other three source declarations; keep resolution generic |
| Background skills | Each SRD background grants exactly two fixed skills | `dnd2024.skill-proficiencies` | Union fixed background skills with deterministic legal class choices without duplicates |
| Background tool | Acolyte, Criminal, and Sage grant a fixed tool; Soldier chooses one Gaming Set | `dnd2024.tool-proficiencies` | Apply fixed tools; keep the Soldier choice pending |
| Origin feat | Each background specifies an Origin feat; Magic Initiate also fixes Cleric or Wizard as its list | feature identities and `dnd2024.feat-profile`; no character feat grant owner | Declare the exact identity/configuration and keep behavior/choices pending |
| Starting equipment | Each background offers its listed package or 50 GP | item/equipment state exists but no creation package planner | Preserve exact package entries/currency as immutable data and defer the branch |
| Languages | Every character knows Common plus two chosen Standard languages; this is origin-wide, not background-specific | `dnd2024.language-proficiencies` | Apply Common and record the two choices pending without calling them a Soldier grant |
| Class skills/tools | A class grants skills and may grant fixed or selectable tools | class creation profile and proficiency components | Choose a legal non-duplicate class-skill set and union any fixed class tool with fixed background tools |

## External implementation reference

Reviewed Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` at
`module/data/item/background.mjs`. Its modern background model separates immutable background data,
trait advancement, feat item grants, and starting equipment. This slice adopts that declaration-
then-grant separation only. No Foundry code, data, assets, IDs, or runtime dependency are copied.

## Prerequisite evidence

- [CC1](evidence/DND2024-CHARACTER-CREATION-CC1-RECEIPT.md) proves generic background ability
  resolution from the selected entity rather than a content switch.
- [All-class basic creation](evidence/DND2024-CHARACTER-CREATION-ALL-CLASS-RECEIPT.md) proves the
  twelve class models, core actor transaction, participation, replay, rollback, and pending ledger.
- Existing language, tool, and skill proficiency schemas are the sole character-state owners used
  here; no duplicate grant component is introduced.

## Runtime artifacts

- New `dnd2024.background-creation-profile` definition/schema holding source-fixed skills, tool
  form, origin-feat identity/configuration, exact starting package data, 50 GP alternative, and
  source reference.
- Three new background entities and a revised Soldier entity, each with matching content,
  ability-option, and creation-profile keys/versions/source.
- Revised creation record, basic creator JavaScript, mechanic description, and governing procedure.
- Focused background/class composition, fixed-grant, pending-choice, source-drift, replay, and
  rollback coverage in the existing D&D harness.
- No database migration, bootstrap copy, C# rule, endpoint, or additional transaction owner.

## Authoritative state and closed input

The existing roles remain exactly `world`, `policy`, `background`, `species`, and `class`. The
`background` role additionally requires `dnd2024.background-creation-profile` and may bind exactly
one active SRD background whose three component keys, versions, and source references agree.

The request remains closed and unchanged: host-reserved character ID, name, ability child input,
and species-selection child input. Background and class selection occur through trusted role
bindings. Callers never supply fixed background grants, Common, final class skills, pending entries,
effects, source references, or transaction identity.

## Behavior, result, and typed effects

1. Validate the background content, ability options, and creation profile as one closed,
   source-matched immutable declaration.
2. Compose ability, species, and class-progression children as before; the ability child must echo
   the selected background ID.
3. Begin with the class profile's deterministic fixed skill choices, remove any that duplicate the
   background skills, then fill from the class's ordered legal options until its exact choice count
   is met. Union and sort the result with the two fixed background skills.
4. Apply Common. Union every fixed background and class tool proficiency into one sorted tool state;
   do not add the tool component when that union is empty.
5. Record the background feat identity/configuration, equipment package-or-50-GP branch, two
   Standard-language choices, selectable tools, and all previously unsupported class/species grants
   as sorted pending entitlements.
6. Commit through the existing generic actor/component/participation transaction. Effect order,
   replay identity, rollback ownership, and no-event/no-notification behavior remain unchanged.

## Failure, replay, and rollback contract

Unknown or inactive backgrounds, mismatched keys/versions/source locators, malformed/extra profile
fields, illegal skills/tools/feat IDs, invalid equipment entries, an ability child for another
background, or an impossible duplicate-free class-skill choice fails before effects. Existing actor
collisions, exact replay, and injected late transaction failure leave no partial actor,
participation, component, or relationship.

## Implementation sequence

1. Add the profile component contract and four source-bound background models.
2. Generalize the record schema and creator/procedure within the unchanged request/role surface.
3. Extend the existing fixture and test matrix across every background and class.
4. Run focused tests, complete D&D tests, disposable catalog validation, and the full solution.
5. Write one completion receipt and update the dependency tree once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Model coverage | exactly Acolyte, Criminal, Sage, and Soldier have matching valid profiles |
| Ability eligibility | each background accepts only its declared three abilities/patterns |
| Fixed skills | exact background skills plus the exact legal class choice count, with no duplicate |
| Fixed tools | Calligrapher's Supplies/Thieves' Tools and fixed class tools are applied once |
| Tool choice | Soldier's one Gaming Set remains pending and grants no guessed set |
| Languages | Common is applied; exactly two Standard-language choices remain pending |
| Feat/equipment | exact declarations are queryable; behavior/items/cash remain absent and pending |
| Composition | every four-background by twelve-class pairing creates atomically |
| Closed/source failure | malformed, mismatched, inactive, or drifted background data creates nothing |
| Transactions | replay, collision, fresh readback, and injected late rollback remain green |
| Compatibility | the existing Soldier/Fighter result remains legal apart from newly exact Common state |

## Verification commands

- focused background/basic-creation tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a fresh disposable database; and
- full solution tests. No protocol walk is required because protocol/dependency registration does
  not change.

## Completion receipt and exit gate

Delivered by the
[CC3A completion receipt](evidence/DND2024-CHARACTER-CREATION-CC3A-RECEIPT.md). This slice stops without selecting languages
or Gaming Sets, granting feat behavior, instantiating equipment, implementing class feature/armor
choices, or adding optional backgrounds.
