# DND2024-ECS-SCHEMA completion receipt

Status: **accepted**
Completed: 2026-08-28
Delivered boundary: provisional D&D 2024 SRD 5.2.1 value schemas, ECS component schemas,
authored/runtime entity archetypes, and section coverage under `prototype/dnd2024/**`.

## Delivered model surface

- 36 reusable value schemas plus 3 prototype meta-schemas.
- 154 closed, one-concern component schemas across 20 component families.
- 71 entity archetypes: 42 authored definition shapes and 29 runtime shapes.
- 14 SRD coverage entries with exact printed ranges from legal information through Animals.
- 154 of 154 components and 71 of 71 archetypes referenced by the coverage registry.
- The existing JavaScript/Visual Studio project link exposes the prototype without copying its files.

Definitions and runtime state are separate. Logical-key state uses keyed maps where duplicate rows
would create conflicting authority. Class memberships, character choices, resource pools,
spellcasting sources, spell-slot pools, active effects, and item/monster/vehicle/trap instances are
separate addressable entities composed through archetypes and generic relationships.

## Source evidence

- Source: `source.dnd2024.srd-5.2.1`.
- Local PDF: `tmp/pdfs/SRD_CC_v5.2.1.pdf`, 364 pages.
- SHA-256: `8974902D109D6E63672D7C490BDE9CCF052410503D9CFA768237154FBC5E3D87`.
- Section ranges: Playing the Game 5–18; Character Creation 19–27; Classes 28–82;
  Character Origins 83–86; Feats 87–88; Equipment 89–103; Spells 104–175; Rules Glossary
  176–191; Gameplay Toolbox 192–203; Magic Items 204–253; Monsters 254–257; Monsters A–Z
  258–343; Animals 344–364.

## Acceptance evidence

| Command | Result |
| --- | --- |
| `npm test` in `prototype/dnd2024` | passed: 14 tests, 0 failures |
| `dotnet build DantesRoleplay.slnx` | passed: 0 warnings, 0 errors |
| `dotnet build catalog/applications/dnd2024/DantesRoleplay.Dnd2024.Game.esproj` | passed: 0 warnings, 0 errors |
| `git diff --check -- prototype/dnd2024` | passed: no whitespace errors |

The tests compile every Draft 2020-12 schema, resolve every reference, validate component metadata
and lifecycle-compatible archetype composition, prove negative composition cases, exercise value
discriminators and keyed single-authority collections, require every component to participate in an
archetype, trace all major SRD sections, and prohibit concrete content records in this phase.

## Deliberate exclusions

- No concrete Athletics, Barbarian, Soldier, Human, Fireball, Longsword, monster, or other SRD
  content record.
- No JavaScript rule mechanics or calculated outcomes.
- No canonical `catalog/` change, database import, migration, or live-state write.
- No replacement for generic world, campaign, session, quest, containment/inventory, relationship,
  event, operation-history, interaction-receipt, or protocol owners.
- No UI projection; chat remains the intended primary play interface.

`roleplay validate catalog` was not run because this accepted slice did not change the canonical
catalog. Promotion/import and the concrete-record phase require their own explicit synchronization
and confirmation boundary.
