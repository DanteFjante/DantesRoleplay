---
id: procedure.character.playtest-bootstrap
category: character.playtest
name: Create and revise a provisional playtest character record
governs: dnd2024.playtest-character-record; the existing commit(kind: "effects") and commit(kind: "campaign") calls used for provisional actor setup
status: active
createdBy: "seed"
changeNote: "Seeded from bootstrap file."
---

## Description
Provides a temporary, explicitly non-authoritative setup path for a campaign playtest while
governed character creation is incomplete. It records what a player portrays—such as a class,
background, spell, feature, equipment item, species trait, or GM ruling—without claiming that the
record grants or executes any of those things.

## Instructions
1. Read the actor ID and the target campaign before changing either. The actor ID is permanent;
   verify that it is absent before creation.
2. Use one `commit(kind: "effects", dryRun: true)` list to create the actor, its already-supported
   base components, and one add-only `dnd2024.playtest-character-record` in `draft` state. Read
   the dry run, then commit the identical list.
3. Attach the pre-existing actor only through C15's `commit(kind: "campaign")` operation
   `attach-character-participation`. Do not create campaign participation links through direct
   effects.
4. Once C15 succeeds, use one `component.set` effect to replace the complete valid record with
   identical entries and `state: "active"`.
5. Revise a record only through complete `component.set` replacement. Preserve prior meaningful
   entries as `note` or `rule-ruling` entries when the table needs their history; operation history
   also records the replaced component value.
6. When a playtest actor is no longer used, replace the record with `state: "retired"`. A future
   governed CH5/CH6 character is new authoritative state and must not backfill grants, membership,
   spellcasting, items, or derived values from this record.

## Constraints
- The record must contain exactly the schema's format, lifecycle state, and bounded entries.
- An entry is a label plus optional plain-language detail. It has no source reference, target,
  roll, DC, formula, resource, result, effect, component data, item-instance ID, actor ID,
  campaign ID, copied rule text, or executable payload.
- `class`, `spell`, `feature`, `species-trait`, and similar entry kinds are declarations for GM/AI
  adjudication only. No mechanic may treat their presence as an entitlement or infer a rule from
  their label.
- `draft` does not mean a completed character. It is a recoverable provisional actor awaiting C15
  attachment. `active` does not replace C15 as the campaign-scope authority.
- This procedure adds no MCP tool, commit kind, query kind, transaction coordinator, class/origin
  receipt, spellcasting state, or official-character migration.
