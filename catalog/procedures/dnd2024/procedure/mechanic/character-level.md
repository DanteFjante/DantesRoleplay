---
id: dnd2024.procedure.mechanic.character-level
category: ruleset.dnd2024.core.data.character-level
name: Record total character level
governs: commit(kind: "component") introducing total-character-level storage; commit(kind: "mechanic") validating level records; commit(kind: "action") setting total character level
status: active
createdBy: "llm"
changeNote: "Created as Feature 2 Slice 2 after the SRD source registry was verified."
---

## Description
Defines a character total-level component, its fixed SRD source reference, the derived level-based Proficiency Bonus, and the validated administrative mechanic that records the level. Non-goals include classes, advancement and monster CR.

## Instructions
Source and attribution
- Rule source: dnd2024.source.srd-5.2.1, locator "Character Creation > Character Advancement" in System Reference Document 5.2.1.
- The source registry stores the required Wizards of the Coast LLC CC-BY-4.0 attribution and official URLs.

Purpose and SRD explanation
This component records one character's total level, the base fact from which that character's Proficiency Bonus is derived. For character levels 1-20, derive Proficiency Bonus as 2 + floor((level - 1) / 4): +2 at 1-4, +3 at 5-8, +4 at 9-12, +5 at 13-16, and +6 at 17-20. Store the level; never store the derived bonus.

Non-goals
This does not represent class identity, per-class levels, multiclass composition, experience points, advancement choices, hit points, monster Challenge Rating, Expertise, or a level-up workflow. The recording mechanic is an administrative setup/correction path and does not claim that advancement requirements were met.

Dependencies
- dnd2024.procedure.mechanic.source-registry and entity dnd2024.source.srd-5.2.1.
- The kernel world, mechanic and action contracts.
- No skill, class or proficiency-state component.

Creation order and data
1. Create this contract.
2. Declare component dnd2024.character-level as a closed object with exactly level and sourceRef.
3. sourceRef is fixed to {"sourceId":"dnd2024.source.srd-5.2.1","locator":"Character Creation > Character Advancement"}.
4. Create dnd2024.mechanic.character-level.record in scope dnd2024-srd-5.2.1. It declares subject and may see dnd2024.character-level when already present.
5. Run the mechanic through commit(kind: "action") to add the component when absent or replace the whole component when correcting an existing record. Do not hand-author sourceRef.

Action input and output
- Input is exactly {"level": <integer 1-20>}. Missing, fractional, non-number, out-of-range, non-finite, or extra input fields fail before proposing effects.
- The mechanic returns one component.add or component.set effect, narration that the level was recorded, and data containing level, derived proficiencyBonus, previousLevel (null when absent), and the fixed sourceRef.
- The mechanic uses no randomness. Repeating the same input on the same projection gives the same proposed state and derived bonus.

Deterministic verification
- Accepted boundaries and bonuses: 1->2, 4->2, 5->3, 8->3, 9->4, 12->4, 13->5, 16->5, 17->6, 20->6.
- Reject 0, 21, 1.5, "5", null, missing level, NaN/non-finite where representable, and any extra field such as proficiencyBonus or sourceRef.
- After every rejection, query the character and prove its stored component is unchanged.
- Query the final state and prove it has exactly level and sourceRef, with no proficiencyBonus.
- Query the mechanic and contract back, and confirm intent search selects the scoped recording rule.

Revision and retirement
The component id and meaning are permanent. Future class and advancement features must reference this total level rather than add a second total-level field. If an advancement workflow supersedes administrative recording, deprecate only the recording mechanic after migrating callers; retain this component and its history. Formula or source corrections require a versioned contract and mechanic revision, query-back, and migration analysis before changing stored data.

## Constraints
- dnd2024.character-level contains exactly level and sourceRef; level is an integer 1-20.
- Proficiency Bonus is always derived as 2 + floor((level - 1) / 4) and is never stored in this or another actor component.
- The source reference is fixed to dnd2024.source.srd-5.2.1 and Character Creation > Character Advancement; callers cannot supply or override it.
- Normal creation and correction use dnd2024.mechanic.character-level.record, so invalid values fail before effects. Direct effects are reserved for an explicitly governed migration.
- The recording mechanic accepts only input.level and never infers, clamps, rounds or defaults it.
- The recording mechanic changes only dnd2024.character-level and never grants class features, hit points, proficiencies or other advancement results.
- Monster Challenge Rating and monster Proficiency Bonus are out of scope.
- No repository payload is authoritative; the live database contract, component definition and mechanic are the runtime source of truth.
