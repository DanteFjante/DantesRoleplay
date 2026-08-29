# Feature 24 Slice 4 receipt — derived Armor Class migration

Status: **accepted**
Owner: Feature 24, armor, shields, armor training, and derived Armor Class
Source: `source.dnd2024.srd-5.2.1`, `Rules Glossary > Armor Class and Armor Training`, PDF p. 176; `Equipment > Armor`, PDF p. 91

## Delivered boundary

- Added effect-free `mechanic.dnd2024.armor-class.read`, deriving default, Light, Medium, Heavy,
  and valid trained-Shield AC from Dexterity and direct equipped-item evidence.
- A directly held Shield requires explicit valid armor-training state; known-untrained produces no
  Shield bonus and absent/corrupt training fails closed. The reader never falls back to legacy
  manually recorded AC.
- Deprecated `mechanic.dnd2024.armor-class.write`; the historical component remains readable but
  is not routable and no normal combat mechanic consumes it.
- Migrated Weapon Attack and Unarmed Strike to consume exactly one derived-AC child result.
- Migrated the fixed Feature 10 target and hero to direct worn Studded Leather/Dexterity 10 (AC
  12) and Leather/Dexterity 16 (AC 14) instances.

## Evidence

- `dotnet test --filter FullyQualifiedName~CatalogFeature24Slice4Tests --no-restore` — passed (1 test).
- Focused Features 6, 8, 10, and 22 catalog tests — passed (7 tests).
- `dotnet run --project DantesRoleplay.Tools -- validate catalog` — valid: 415 records and 80
  non-blocking near-duplicate warnings; no live data touched.
- `git diff --check` — no whitespace errors.
- The full suite was attempted but is currently blocked outside this slice by
  `CatalogCoverageTests`: `subscription_version.RoleFromEventPayloadJson` lacks a carried/not-carried
  decision. The required protocol walk was then blocked by an existing long-running `testhost.exe`
  holding the test output assemblies. Neither blocker was changed by this slice.

## Deliberate exclusions

No alternative/natural/magical base AC, armor D20 drawback, Speed adjustment, spellcasting
restriction, equipment mutation, action/timing transition, generic C# rule change, public MCP
surface change, live database import, or automatic campaign migration was added.
