# Feature 28 Slice 1 implementation receipt — language and tool proficiency records

Status: **Verified**
Date: 2026-08-20

## Delivered boundary

Slice 1 adds two independent, source-cited D&D 2024 membership records:

- `dnd2024.language-proficiencies`, with the closed 19-language vocabulary;
- `dnd2024.tool-proficiencies`, with the closed 36-tool vocabulary.

Each has a normal recorder that accepts only its complete list, canonicalizes it, fixes the SRD
source reference, adds missing state or replaces valid existing state, and rejects malformed or
corrupt state without a partial result. The slice adds no character, campaign, item, grant, class,
feature, check, crafting, translation, public surface, migration, or persistent-database content.

## Artifacts

- [Feature 28 plan](FEATURE-28-DEPENDENCY-PLAN.md)
- `procedure.mechanic.dnd2024.languages-and-tools`
- `mechanic.dnd2024.language-proficiencies.record`
- `mechanic.dnd2024.tool-proficiencies.record`
- [Focused regression test](../../../DantesRoleplay.Tests/CatalogFeature28Tests.cs)

## Verification

| Gate | Result |
| --- | --- |
| Focused catalog/import/action test | Passed: 1/1. Proves canonicalization, replacement, empty-versus-missing state, malformed rejection, isolation, and corrupt-state preservation. |
| `roleplay validate catalog` | Passed: 171 records. The two new mechanics produced advisory near-duplicate warnings alongside pre-existing catalog advisories; no validation error occurred and no live data was touched. |
| Full test suite | Passed: 455/455. |
| Diff check | No whitespace errors. Existing checkout line-ending advisory output remains unrelated. |
| Persistent database | Not imported or changed. |

## Remaining boundary

Feature 28 Slice 1 is ready for acceptance. It only unblocks the language/tool entries in the CH0
owner map. Origin grants, background/feat effects, items, class state, HP/AC derivation, campaign
attachment, and atomic character creation remain owned by their existing planned features.
