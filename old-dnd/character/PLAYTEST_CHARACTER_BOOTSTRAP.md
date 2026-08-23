# Playtest character bootstrap — two provisional player characters

Status: **Operational playtest runbook; not a CH3–CH6 character-creation implementation**  
Last updated: 2026-08-22

## Purpose

This is the fastest honest route to a first session while the governed character-creation path is
still incomplete. It uses the existing atomic `commit(kind: "effects")` world-change contract to
create two campaign-ready **provisional** actors:

- an Elf portrayed as a Wizard; and
- a Human portrayed as a Bard.

The stored state is deliberately limited to what current mechanics can use—identity/profile,
species selection, raw ability scores, total level, skill and saving-throw proficiencies, hit
points, Size, and Speed—plus one explicitly provisional record of class, spell, equipment, trait,
feature, and table-ruling labels. The current server has no implemented Wizard or Bard membership,
spellcasting, starting-equipment, subclass, background, species-trait, or grant-receipt behavior.
The GM/AI therefore adjudicates those parts at the table and records ordinary supported
consequences through their existing owners.

This runbook neither changes nor supersedes CH0–CH6. In particular, it must not be used as
evidence that the characters were created through CH5 or that Wizard/Bard rules are implemented.

## What works after bootstrap

- Ability checks, including the selected skill proficiencies.
- Saving throws using the selected saving-throw proficiencies.
- Initiative/order when the existing encounter and turn contracts are used.
- Hit-point changes through the existing damage/healing owners.
- Speed-aware encounter turns and ordinary supported inventory/travel operations.
- Campaign participation after the separate C15 attachment call.

## What the GM/AI adjudicates for this playtest

- Spells, cantrips, spell slots, casting foci, ritual casting, and spell effects.
- Class features, class equipment, background benefits, feats, and all species traits.
- Ability-score increases attributable to a background or species.
- Armor, weapons, attacks, and any proficiency not explicitly stored below.

Do not write an invented spell, class, feat, or trait component to make these appear implemented.
Use narration and a normal supported rule/action where it exists; otherwise record only the
settled consequence the table has decided.

Use `dnd2024.playtest-character-record` for the missing pieces. Its entries are durable labels
and details for the campaign, not a hidden rules engine. The governing contract is
`procedure.character.playtest-bootstrap`.

## Chosen provisional sheets

These defaults deliberately resemble ordinary level-one D&D 2024 characters without claiming
source-complete class/origin construction. Change values only before the dry run if the players
choose different sheets.

| Actor ID | Display name | Playtest portrayal | Abilities | HP | Skills | Saves |
| --- | --- | --- | --- | ---: | --- | --- |
| `actor.playtest.elf-wizard` | `Elven Magician (Playtest)` | Elf Wizard | Str 8, Dex 14, Con 13, Int 17, Wis 12, Cha 10 | 7 | Arcana, History, Investigation, Perception | Int, Wis |
| `actor.playtest.human-bard` | `Human Bard (Playtest)` | Human Bard | Str 8, Dex 14, Con 13, Int 10, Wis 12, Cha 17 | 9 | Acrobatics, Insight, Performance, Persuasion | Dex, Cha |

Both are Medium, have 30-foot walk Speed, and carry no recorded armor or equipment. Armor Class
remains a derived/read concern; do not manually add `dnd2024.armor-class` here.

## Create the two actors

Before running this, query the two actor IDs and confirm neither already exists. IDs are permanent,
so replace them (and the display names) before the first dry run if the group wants different
names. Also query the world once to confirm the named component definitions are present.

Before using this against a running campaign database, import the reviewed catalog at its explicit
synchronization boundary so `dnd2024.playtest-character-record` and
`procedure.character.playtest-bootstrap` exist in that database. The existing direct-effects
route is trusted-host infrastructure: its dry run validates the effect list but does not currently
validate nested component JSON against every component schema. Use the closed payload below
unchanged (apart from intentional names/records).

Submit the following as the `payload` for `commit(kind: "effects", dryRun: true)`. The MCP tool
expects `payload` as an encoded JSON string; the object below is shown unescaped for readability.
Read the dry-run result, then submit the **identical** payload without `dryRun`.

```json
{
  "effects": [
    {
      "type": "entity.create",
      "entityId": "actor.playtest.elf-wizard",
      "name": "Elven Magician (Playtest)"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.character.profile",
      "data": "{\"biography\":\"Playtest portrayal: an Elf Wizard. Wizard magic and species traits are GM/AI adjudicated until their ruleset owners exist.\"}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.playtest-character-record",
      "data": "{\"format\":\"dnd2024-playtest-character-record-v1\",\"state\":\"draft\",\"entries\":[{\"kind\":\"class\",\"key\":\"wizard\",\"label\":\"Wizard\",\"details\":\"GM/AI adjudicates unimplemented Wizard behavior.\"},{\"kind\":\"background\",\"key\":\"sage\",\"label\":\"Sage\"},{\"kind\":\"species-trait\",\"key\":\"elf-traits\",\"label\":\"Elf traits\",\"details\":\"GM/AI adjudication until implemented.\"}]}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.selected-species",
      "data": "{\"speciesDefinitionId\":\"content.dnd2024.species.elf.v1\"}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.abilities",
      "data": "{\"str\":8,\"dex\":14,\"con\":13,\"int\":17,\"wis\":12,\"cha\":10}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.character-level",
      "data": "{\"level\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Creation > Character Advancement\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.skill-proficiencies",
      "data": "{\"skills\":[\"arcana\",\"history\",\"investigation\",\"perception\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Proficiency > Skill Proficiencies and Skills\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.saving-throw-proficiencies",
      "data": "{\"abilities\":[\"int\",\"wis\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Proficiency > Saving Throw Proficiencies\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.hit-points",
      "data": "{\"current\":7,\"maximum\":7,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Hit Points\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.creature-size",
      "data": "{\"size\":\"medium\"}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.speed",
      "data": "{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}"
    },
    {
      "type": "entity.create",
      "entityId": "actor.playtest.human-bard",
      "name": "Human Bard (Playtest)"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.character.profile",
      "data": "{\"biography\":\"Playtest portrayal: a Human Bard. Bard magic and Human traits are GM/AI adjudicated until their ruleset owners exist.\"}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.playtest-character-record",
      "data": "{\"format\":\"dnd2024-playtest-character-record-v1\",\"state\":\"draft\",\"entries\":[{\"kind\":\"class\",\"key\":\"bard\",\"label\":\"Bard\",\"details\":\"GM/AI adjudicates unimplemented Bard behavior.\"},{\"kind\":\"background\",\"key\":\"entertainer\",\"label\":\"Entertainer\"},{\"kind\":\"species-trait\",\"key\":\"human-traits\",\"label\":\"Human traits\",\"details\":\"GM/AI adjudication until implemented.\"}]}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.selected-species",
      "data": "{\"speciesDefinitionId\":\"content.dnd2024.species.human.v1\"}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.abilities",
      "data": "{\"str\":8,\"dex\":14,\"con\":13,\"int\":10,\"wis\":12,\"cha\":17}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.character-level",
      "data": "{\"level\":1,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Character Creation > Character Advancement\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.skill-proficiencies",
      "data": "{\"skills\":[\"acrobatics\",\"insight\",\"performance\",\"persuasion\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Proficiency > Skill Proficiencies and Skills\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.saving-throw-proficiencies",
      "data": "{\"abilities\":[\"dex\",\"cha\"],\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Proficiency > Saving Throw Proficiencies\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.hit-points",
      "data": "{\"current\":9,\"maximum\":9,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Playing the Game > Damage and Healing > Hit Points\"}}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.creature-size",
      "data": "{\"size\":\"medium\"}"
    },
    {
      "type": "component.add",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.speed",
      "data": "{\"walkFeet\":30,\"burrowFeet\":0,\"climbFeet\":0,\"flyFeet\":0,\"swimFeet\":0,\"sourceRef\":{\"sourceId\":\"source.dnd2024.srd-5.2.1\",\"locator\":\"Rules Glossary > Speed\"}}"
    }
  ]
}
```

The list is intentionally one transaction: either both provisional actors are present with their
base state, or neither is created.

## Attach each actor to an active campaign

After the actor creation commits, use the existing C15 campaign operation once per actor:

```json
{
  "operation": "attach-character-participation",
  "campaignId": "campaign.your-active-campaign",
  "actorId": "actor.playtest.elf-wizard"
}
```

Repeat with `actor.playtest.human-bard`. The operation accepts the existing campaign kind; it
derives the participation ID and refuses an inactive campaign, a missing actor, or prior
participation history. It does not grant character mechanics.

If there is no campaign yet, create the intended world/campaign through
`procedure.game.core.world.location` and `procedure.campaign.create` first. Do not invent a
campaign relationship with direct effects when C15 owns the participation structure.

## Activate and revise the provisional records

After **both** C15 attachment calls have succeeded, activate the records with an existing
`commit(kind: "effects")` call. Send this object as its payload, dry-run it first, and then submit
the identical object without `dryRun`:

```json
{
  "effects": [
    {
      "type": "component.set",
      "entityId": "actor.playtest.elf-wizard",
      "definitionId": "dnd2024.playtest-character-record",
      "data": "{\"format\":\"dnd2024-playtest-character-record-v1\",\"state\":\"active\",\"entries\":[{\"kind\":\"class\",\"key\":\"wizard\",\"label\":\"Wizard\",\"details\":\"GM/AI adjudicates unimplemented Wizard behavior.\"},{\"kind\":\"background\",\"key\":\"sage\",\"label\":\"Sage\"},{\"kind\":\"species-trait\",\"key\":\"elf-traits\",\"label\":\"Elf traits\",\"details\":\"GM/AI adjudication until implemented.\"}]}"
    },
    {
      "type": "component.set",
      "entityId": "actor.playtest.human-bard",
      "definitionId": "dnd2024.playtest-character-record",
      "data": "{\"format\":\"dnd2024-playtest-character-record-v1\",\"state\":\"active\",\"entries\":[{\"kind\":\"class\",\"key\":\"bard\",\"label\":\"Bard\",\"details\":\"GM/AI adjudicates unimplemented Bard behavior.\"},{\"kind\":\"background\",\"key\":\"entertainer\",\"label\":\"Entertainer\"},{\"kind\":\"species-trait\",\"key\":\"human-traits\",\"label\":\"Human traits\",\"details\":\"GM/AI adjudication until implemented.\"}]}"
    }
  ]
}
```

To record a new spell, item, feature, or table ruling, replace the *complete* record with
`component.set`, retaining the existing entries and adding a bounded entry such as
`{"kind":"spell","key":"fire-bolt","label":"Fire Bolt","details":"GM ruling: ..."}`.
Never use an entry as proof that an unsupported rule works.

## Verify and begin play

1. Query both actor IDs and confirm all ten components are present on each actor, including an
   `active` playtest record.
2. Query the campaign graph and confirm each actor has one active C15 participation.
3. Start with a simple ability check or scene; use the existing action catalogue for supported
   checks, travel, inventory, combat, and damage operations.
4. For every Wizard/Bard-only decision, state the temporary GM ruling in the narration and keep
   it consistent for the campaign. Add a permanent ruleset owner later only after its D&D source,
   state, transaction, and test contract are ready.

## Cleanup and successor

These actor IDs are permanent even if later deleted. Prefer keeping this campaign as the playtest
record rather than deleting/reusing IDs. A later accepted CH5/CH6 creation path should use new
characters and not try to retroactively attach creation/grant receipts to these provisional
actors.

The proper successor remains CH3 origin receipts, CH4 class membership/grants, CH5 atomic governed
creation, and CH6 discovery. Wizard/Bard spellcasting belongs to CH10 and ruleset Features 31–32;
it is intentionally outside this temporary setup.
