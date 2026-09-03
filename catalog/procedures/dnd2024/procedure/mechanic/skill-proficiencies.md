---
id: dnd2024.procedure.mechanic.skill-proficiencies
category: ruleset.dnd2024.core.data.skill-proficiencies
name: Record skill and saving-throw proficiencies
governs: commit(kind: "component") declaring dnd2024.skill-proficiencies or dnd2024.saving-throw-proficiencies; commit(kind: "mechanic") recording either closed list; commit(kind: "action") replacing either list
status: active
createdBy: "llm"
changeNote: "Feature 4 Slice 1: revise the existing proficiency-state owner to govern distinct saving-throw state after the anti-sprawl safeguard rejected a parallel contract."
---

## Description
Owns the distinct closed D&D 2024 character proficiency-state records for skills and saving throws, their fixed source locators, and their administrative recorders. It does not resolve checks or saves, derive bonuses, or grant proficiencies.

## Matches

## Instructions
Source and attribution
- Skill state cites dnd2024.source.srd-5.2.1 at "Playing the Game > Proficiency > Skill Proficiencies and Skills".
- Saving-throw state cites dnd2024.source.srd-5.2.1 at "Playing the Game > Proficiency > Saving Throw Proficiencies".
- The source registry retains the required Wizards of the Coast CC-BY-4.0 attribution and official URLs.

One owner, two independent records
1. This is the single contract owner for D&D 2024 character proficiency-state lists. Skill and saving-throw state have separate permanent component ids and never share data.
2. dnd2024.skill-proficiencies is a closed object with exactly skills and sourceRef. skills is a unique alphabetically sorted subset of these stable ids: acrobatics, animal-handling, arcana, athletics, deception, history, insight, intimidation, investigation, medicine, nature, perception, performance, persuasion, religion, sleight-of-hand, stealth, survival.
3. dnd2024.saving-throw-proficiencies is a closed object with exactly abilities and sourceRef. abilities is a unique canonical-order subset of str, dex, con, int, wis, cha; canonical order is exactly that sequence.
4. Each sourceRef is fixed to its listed source id and locator. An explicit empty array means known none in that state; a missing component means unknown state.

Recording mechanics
5. dnd2024.mechanic.skill-proficiencies.record and dnd2024.mechanic.saving-throw-proficiencies.record are the normal creation and correction paths, both scoped to dnd2024-srd-5.2.1.
6. Each accepts exactly one array field (skills or abilities), rejects missing/null/non-array/non-string/unknown/wrong-case/display-name/duplicate/extra input before effects, canonicalizes valid input, and fixes sourceRef.
7. Each returns exactly one component.add when state is absent or component.set when present, along with canonical values, previous values (null when absent), and sourceRef. Neither uses randomness.
8. Before creating a new state component, query the world and affected actor. Dry-run every append-only contract or mechanic write; then submit the identical payload. Query each artifact back.

Verification and limits
- Verify empty, multivalue, all-vocabulary reversed-order, replay, and all rejected input classes for each recorder; after every rejection prove stored bytes are unchanged.
- Saving throws and skills state only records proficiency membership. Do not store ability scores or modifiers, character level, Proficiency Bonus, rolls, DCs, results, Expertise, acquisition provenance, classes, backgrounds, weapons, tools, or outcomes.
- A later check or save resolver may read this state and derive its own result; it must not revise this recording contract merely to add gameplay resolution.

## Constraints
- This revision consolidates ownership because the contract anti-sprawl check found a proposed separate saving-throw contract overlapping existing proficiency-state ownership. Do not add another competing contract for either list.
- The two component ids, vocabularies, canonical orders, and source locators are permanent once written. Vocabulary changes require versioned contract and mechanic revisions plus migration analysis.
- Missing is semantically different from explicit empty state.
- Normal writes use the corresponding recorder; direct effects require an explicitly governed migration.
- No repository payload is authoritative: the live database artifacts are the runtime source of truth.
