---
id: procedure.mechanic.dnd2024.languages-and-tools
category: ruleset.dnd2024.core.data.languages-and-tools
name: Record language and tool proficiencies
governs: commit(kind: "component") declaring language/tool proficiency storage; commit(kind: "mechanic") recording either closed list; commit(kind: "action") recording or correcting either list
status: active
---

## Description

Owns the two independent closed D&D 2024 membership records for known languages and tool proficiencies and their administrative recorders. It neither grants these capabilities nor resolves language, tool, crafting, translation, or item use.

## Instructions

1. `dnd2024.language-proficiencies` and `dnd2024.tool-proficiencies` are distinct components with exactly a canonical array and fixed `sourceRef`. Missing is unknown; an explicit empty array is known none.
2. The language list uses the SRD 5.2.1 locator `Character Creation > Step 2: Character Origin > Choose Languages`; the tool list uses `Equipment > Tools > Tool Proficiency`. Source attribution remains with `source.dnd2024.srd-5.2.1`.
3. `mechanic.dnd2024.language-proficiencies.record` and `mechanic.dnd2024.tool-proficiencies.record` are the normal creation/correction paths. Each accepts only its named array, rejects malformed/unknown/duplicate/extra data, canonicalizes it, fixes source attribution, and produces exactly one `component.add` or `component.set`.
4. The mechanics store membership only. They accept no source grant, background, species, class, item, ability, Proficiency Bonus, check/craft result, Advantage, or effects.
5. Before an implementation write, query the actor and component definition. After a valid write, query the component back. Invalid input or corrupt prior data leaves state unchanged.

## Constraints

- The records are not a generic proficiency array and must never be merged with skills, saving throws, weapons, items, or inventory.
- A later character-creation transaction validates required language/tool grants and composes these writers atomically; it does not turn this administrative recorder into a grant resolver.
- A later tool/language consumer must derive its own result and may not revise these membership contracts just to add gameplay behaviour.
