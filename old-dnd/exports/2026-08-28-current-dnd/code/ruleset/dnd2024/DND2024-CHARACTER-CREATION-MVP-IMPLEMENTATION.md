# D&D 2024 CC-MVP implementation - basic-playable character creation

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC-MVP**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency leaf: [basic character-creation MVP](DND2024-CHARACTER-CREATION-MVP-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locators: `source.dnd2024.srd-5.2.1`; *Character Creation > Create Your
Character / Step 3 / Step 5* (PDF pp. 19-22), *Character Origins > Soldier* (PDF p. 83),
*Character Origins > Character Species* (PDF pp. 83-86), and *Classes > Fighter* (PDF pp. 47-48)
Outcome: create and attach one explicit `basic-playable` Soldier/Fighter level-1 actor while
recording, but not applying, every deferred entitlement.
Exclusions: executable species traits, feats, class features, armor training/equipment, languages,
gaming-set choice, spellcasting, rest completion, advancement, UI wizard state, migration, and a
new public protocol kind.
Allowed areas: `catalog/applications/dnd2024/components/data`, `mechanics/data`, `procedures/data`,
the existing D&D application acceptance-test harness, this plan's roadmap/dependency/evidence docs,
and no unrelated cleanup.
Stop point: one catalog mechanic commits a valid actor plus campaign participation through the
existing generic application action transaction, with focused and full acceptance evidence.

The accepted [CC-MVP-C1 all-class expansion](DND2024-CHARACTER-CREATION-ALL-CLASS-IMPLEMENTATION.md)
later widened the trusted class role from Fighter to all twelve SRD level-1 class models. It keeps
this transaction and request shape while deriving class-specific HP, saves, skills, and complete
weapon categories. Missing feature, spell, armor, tool, equipment, and restricted-weapon mechanics
remain explicit no-behavior pending entitlements.

## Confirmed decisions

The user's 2026-08-27 instruction to implement the basic plan confirms:

- permanent component ID `dnd2024.character-creation-record`;
- permanent mechanic ID `mechanic.dnd2024.character.basic.create`;
- permanent procedure ID `procedure.mechanic.dnd2024.character-basic-create`;
- application-local relationship IDs `dnd2024.campaign.has-character-participation` and
  `dnd2024.campaign.character-participation.for-actor`, which preserve the C15 graph shape inside
  the D&D state space;
- `basic-playable` means deferred entries grant no behavior; and
- the first template fixes Soldier, Fighter level 1, recommended Fighter skills Perception and
  Survival, and no starting equipment while accepting name, reserved actor ID, species/Size, and
  legal Standard Array/Soldier increases.

The low-level application action request supplies `characterId` as a host-reserved ID. It is not a
player rules choice. No new public request kind or server-selected ID algorithm is introduced.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Ability scores | Standard Array is 15, 14, 13, 12, 10, 8; Soldier increases Str/Dex/Con by +2/+1 or +1 each | CC1 policy, Soldier content, and ability resolver | Parent passes only its `ability` subobject to the accepted child and stores the returned final scores |
| Species | Nine SRD species define Humanoid type, Size, Speed, traits, and choices | CC2A profiles and resolver | Parent passes only `speciesSelection`; stores species, Size, and Speed; records every trait/choice pending |
| Level/class | Fighter uses D10 Hit Dice, Str/Con saves, Simple/Martial weapons, and two listed skills | Fighter content/progression reader and existing proficiency schemas | Fix level 1, Perception/Survival, Str/Con, and both weapon categories; class features remain pending |
| Starting HP | Fighter level-1 maximum is 10 + Constitution modifier | Fighter D10 declaration and HP component | Derive in JavaScript, minimum 1; caller cannot supply HP |
| Baseline AC | Without armor or Shield, base AC is 10 + Dexterity modifier | Armor Class component | Derive the unequipped baseline in JavaScript; equipment remains pending |
| Background grants | Soldier grants Athletics, Intimidation, Savage Attacker, one Gaming Set, and equipment choice | Soldier source identity; only ability options are active content | Apply fixed skills; record feat, tool choice, and equipment as pending |
| Campaign scope | A complete participation has active state and campaign/actor links | active base C15 catalog meaning; generic application ECS effects | Create participation beside the actor in the same transaction, using D&D-owned relationship IDs because state-space relationship kinds are application-scoped |

## External implementation reference

Reviewed Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`:

- `module/applications/advancement/advancement-manager.mjs` applies choices to a cloned actor and
  commits only after the flow completes;
- `module/applications/advancement/hit-points-flow.mjs` derives HP from class advancement plus the
  Constitution modifier and floors per-level contribution at 1; and
- `module/applications/advancement/size-flow.mjs` presents only source-declared Size options.

The useful pattern is validate/derive against a staged view before one commit. No Foundry source,
data, assets, IDs, or runtime dependency are copied.

## Prerequisite evidence

- CC1 and CC2A receipts prove the accepted effect-free ability/species child contracts.
- Slice 10F proves the Fighter D10 and level-1 feature identity content.
- The application action runner already translates bounded entity/component/relationship effects
  and the application ECS applier commits or rolls back the full batch.
- C15's active catalog procedure supplies the participation state and two relationship meanings.

## Runtime artifacts

- `dnd2024.character-creation-record`: immutable applied-versus-pending actor evidence.
- `mechanic.dnd2024.character.basic.create`: the sole D&D-owned planner/effect producer.
- `procedure.mechanic.dnd2024.character-basic-create`: usage, failure, and ownership contract.
- No C# rules, migration, public operation kind, fixture actor, or database bootstrap copy.

## Authoritative state and closed input

Roles are exactly `world`, `policy`, `background`, `species`, and `class`. They bind an active base
world root and the active Standard Array, Soldier, selected species, and Fighter definitions.

Input is exactly:

```json
{
  "characterId": "actor.*",
  "name": "non-empty trimmed display name",
  "ability": { "scores": {}, "increases": {} },
  "speciesSelection": {}
}
```

`speciesSelection` is empty for fixed-Size species and exactly `{ "size": "small|medium" }` when
the selected profile offers a choice. The host reserves the actor ID. The caller never supplies
final scores, modifiers, HP, AC, Speed, level, proficiencies, effects, pending entries,
participation ID/status, source references, or audit identity.

## Behavior, result, and typed effects

1. Validate closed root input, active world, fixed policy/background/class IDs, and bounded derived
   participation ID.
2. Run the existing ability, species-selection, and class-progression children with closed mapped
   inputs; reject missing, malformed, stale, source-drifted, or effectful child output.
3. Derive final abilities, Constitution/Dexterity modifiers, Fighter starting HP, unarmored AC,
   source-owned core proficiencies, empty Conditions, and an unavailable initial turn budget.
4. Build a canonical creation record whose applied component IDs and unresolved entries are sorted
   and disjoint. Pending entries produce no typed effects beyond storing the record.
5. Return ordered create/add/link effects for actor and participation. The generic application
   action transaction is the sole writer and audit/replay owner.

The result data identifies the actor, participation, status, applied IDs, and pending count. It
contains no duplicated sheet state.

## Failure, replay, and rollback contract

- Unknown/missing roles, inactive world/content, noncanonical fixed definitions, bad source
  locators, illegal scores/increases/Size, malformed child results, caller-derived fields, invalid
  names/IDs, or oversized participation IDs fail before effects.
- Existing actor/participation/link/component collisions fail the transaction unchanged.
- An exact operation replay returns replay without another actor.
- Any component/link/schema/persistence failure rolls back both entities, all components, and both
  relationships.

## Implementation sequence

1. Add the record schema/definition and governing procedure.
2. Add the composed catalog JavaScript mechanic and declared dependency tree.
3. Extend only the current D&D test harness registrations and add focused acceptance cases.
4. Validate catalog, run focused/full tests, inspect stored state, then record a completion receipt.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Legal Human and fixed-Size species | one actor, one participation, core state, canonical record, no trait effects |
| Child reuse | ability/species/class results are source-bound, closed, deterministic, and effect-free |
| Core formulas | recommended Fighter array yields 12 HP and AC 12; alternate legal array follows modifiers |
| Proficiencies | Athletics, Intimidation, Perception, Survival; Str/Con saves; Simple/Martial weapons |
| Pending visibility | species traits, Savage Attacker, tool/equipment/languages, armor training, and Fighter features are explicit |
| Closed input/source | extra/derived fields, illegal choices, wrong role IDs, source drift, and inactive world fail unchanged |
| Atomicity | injected late relationship failure leaves no actor or participation evidence |
| Replay | same execution identity creates no duplicate |
| Fresh readback | a new context reads the actor components and both participation links |
| Compatibility | existing CC1-CC2H2 and D&D regression tests remain green |

## Verification commands

- focused `dotnet test` filter for basic character creation plus existing CC1/CC2A tests;
- `roleplay validate catalog`;
- full solution tests; and
- protocol walk only if protocol registration changes (none planned).

## Completion receipt and exit gate

Write `ruleset/dnd2024/evidence/DND2024-CHARACTER-CREATION-MVP-RECEIPT.md`, mark this document and
the MVP dependency plan accepted, and update the roadmap/`STATUS.md` once. Stop without beginning
deferred entitlement behavior or source-complete conversion.
