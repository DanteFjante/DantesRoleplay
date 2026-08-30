# Feature 26 — Slice 1 receipt

Date: 2026-08-21

## Delivered boundary

- Added the closed, immutable `dnd2024.species-profile` catalog component and its governing
  static-definition procedure.
- Added source-cited profiles for Dragonborn, Dwarf, Elf, Gnome, Goliath, Halfling, Human, Orc,
  and Tiefling; the existing Human v1 identity was extended rather than duplicated.
- Each profile records only matching content identity/version/source, Humanoid source type,
  permitted Size values, five-mode base Speed facts, and canonical ordered trait/choice-family
  declarations. Human and Tiefling declare Small-or-Medium; Goliath declares 35-foot base Speed.

## Explicitly not delivered

No selected-species state, character-creation action, creature-type state, Size or Speed mutation,
proficiency, Darkvision, resistance, spell, attack, resource, condition, temporary HP, Feat,
effect, event, subscription, migration, or campaign state. Trait declarations are not executable.

## Evidence

- `dotnet test DantesRoleplay.Tests\\DantesRoleplay.Tests.csproj --filter
  "FullyQualifiedName~CatalogFeature26Tests|FullyQualifiedName~CharacterFeature01Slice1Tests"
  --no-restore` — passed, 6/6.
- `roleplay validate catalog` — valid disposable import, 301 records, 28 pre-existing/general
  near-duplicate warnings. No live data was touched.
- Focused tests prove the complete nine-profile inventory, source/key/version agreement, canonical
  Size/Speed/trait/choice declarations, closed malformed-data rejection, and absence of a
  species-profile effect mechanic.

## Next boundary

Slice 2 remains blocked until Feature 30 confirms its atomic origin-assembly seam. It alone will
define selected-species reference semantics without granting a trait or bypassing the existing
Size, Speed, and proficiency state owners.
