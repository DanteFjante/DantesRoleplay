# Feature 10 dependency plan — reproducible vertical D&D 2024 session

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **verified — both slices complete**

## Execution rule

Slice 1 creates only catalog-owned baseline fixtures and fresh-import coverage. It creates no new mechanics, procedures, components, relationships, runtime actions, or replay harness.

## Target

Feature 10 is an acceptance feature, not a source of game rules. It proves that Features 1–9 compose into a deterministic small D&D 2024 session: a trained hero makes a check and saving throw, an encounter determines initiative, and a critical weapon attack damages a training target.

## Scope

Included: catalog-owned baseline hero, target, and encounter fixtures; a fresh-database seeded replay harness; and comparison of structured results, effects, and final component state.

Excluded: new mechanics, procedures, components, intent-routing behavior, production session persistence, shared-database runtime mutations, and comparison of IDs, timestamps, logs, or narration.

## Dependencies

```text
Feature 1–4 foundations
  ├─ Feature 5 initiative snapshot
  ├─ Feature 6 checks and saves
  ├─ Feature 7 attack resolution
  ├─ Feature 8 weapon attack / critical state
  └─ Feature 9 weapon damage / application
       └─ Feature 10 deterministic acceptance replay
```

Use only existing SRD 5.2.1 locators already represented in the catalog: character level, skills, saving throws, weapon proficiency, Armor Class, and Hit Points. The canonical dagger and existing component shapes are dependencies; `creature.orban` is provenance only, not a complete fixture.

## Ownership

| Concern | Owner | Reason |
| --- | --- | --- |
| Hero, target, encounter baseline | Catalog | Importable and inspectable data. |
| Seeds and action sequence | Integration test | Reproducibility evidence is not production data. |
| Initiative and hit-point changes | Fresh test DB | Runtime effects are not baseline content. |
| Transcript comparison | Integration test | No persistence schema is warranted. |

## Slice plan

| Slice | Deliverable | Authorization | Stop gate |
| --- | --- | --- | --- |
| 1 | Baseline catalog fixtures and fresh-import coverage | **Verified** | Review fixtures before replay work. |
| 2 | Two-database deterministic vertical replay | **Verified** | Feature complete; stop before any later roadmap feature. |

## Slice 1 — baseline fixtures

Create these catalog entities with existing `source.dnd2024.srd-5.2.1` locators:

1. `encounter.dnd2024.feature-10.training`: empty baseline, with no `dnd2024.encounter-initiative-order` runtime component.
2. `creature.dnd2024.feature-10.hero`: a `participant` in the encounter; abilities STR 12, DEX 16, CON 14, INT 10, WIS 13, CHA 8; level 5; CON/WIS saving proficiencies; Perception/Stealth skills; simple weapon proficiency; AC 14; hit points 20/20.
3. `creature.dnd2024.feature-10.training-target`: a `participant` in the encounter; abilities STR 10, DEX 10, CON 12, INT 8, WIS 10, CHA 8; AC 12; hit points 12/12.

Do not add a Feature 10 mechanic, procedure, component, relationship, or database fixture. Add `CatalogFeature10Tests` that imports a fresh catalog copy and confirms IDs, containment, baseline values, and absence of runtime encounter state.

Evidence (2026-08-19): import created exactly three records; catalog verification reported 74 unchanged records; `CatalogFeature10Tests` fresh-import coverage passed; the full suite passed 303/303. No Feature 10 operation was run.

Slice 1 review authorized Slice 2. The fixture baseline remains free of runtime state.

## Slice 2 — deterministic vertical replay

An integration test imports the catalog into two independent fresh databases. In each, use the same seeds and run: hero Perception check against a fixed DC; hero Constitution save against a fixed DC; encounter initiative for both participants with a no-tie seed; hero Dagger Dexterity attack against the target with a natural-20 seed (currently 36); then Feature 9 weapon-damage parent using Dexterity and the attack critical state.

Compare parsed action data, effects, initiative ordering, and final relevant components. Ignore operation IDs, timestamps, logs, and narration. The only expected final deltas are one initiative snapshot and decreased target current hit points; target maximum/source, hero state, and canonical weapon data must remain unchanged.

If precise action phrases route to a different intent, fix the existing intent contract before accepting Feature 10; do not add a test-only workaround.

Evidence (2026-08-19): the same copied catalog was imported into two independent fresh databases. Each ran a seeded Perception check (10), Constitution save (11), untied Initiative parent (100), natural-20 Dagger attack (36), and critical damage parent (23). `CatalogFeature10Tests` compares parsed action data and effects, final component state, the single Initiative snapshot, and the single target Hit Point update; it ignores operation IDs, logs, narration, and time. The critical attack hit, damage applied once, and only the expected encounter/target state changed. Catalog verification reported 74 unchanged records and the full suite passed 304/304.

Feature 10 is complete. Stop before planning or implementing a later roadmap feature.

## Audit rule

Any expansion must identify its owning earlier feature contract and be reviewed first. Feature 10 remains an integration proof; new rules require a separately planned feature.
