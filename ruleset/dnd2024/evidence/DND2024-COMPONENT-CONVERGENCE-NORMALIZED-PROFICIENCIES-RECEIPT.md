# D&D 2024 component convergence — normalized proficiencies receipt

Status: **accepted**  
Accepted: 2026-08-28  
Implementation: `prototype/dnd2024/planning/DND2024-NORMALIZED-CREATURE-STATE-SOL-SLICE-1-IMPLEMENTATION.md`

## Delivered boundary

- Activated `dnd2024.creature.proficiencies` with source-qualified rank entries and explicit
  `recordedFamilies` coverage.
- Migrated armor training, saving throws, skills and Expertise, tools, weapon memberships,
  character creation, character-sheet derivation, ability checks, saves, weapon attacks, and
  species proficiency contributions to that owner.
- Preserved unrelated proficiency families and unique grant sources during family corrections.
- Corrected provisional ability and movement references to the authored vocabulary entities.
- Corrected the same provisional prefixes across all 330 prototype creature records and updated
  record-inventory evidence to the accepted replacement catalog schemas.
- Retired the five split proficiency component descriptors and schemas without aliases, dual reads,
  or dual writes. Mechanic and procedure IDs remain stable.
- Tightened family-writer validation so malformed ranks, sources, memberships, family coverage, and
  redundant Martial/property state fail without changing stored state.

## Verification

- `roleplay validate catalog`: passed; 144 records validated, with the 21 pre-existing
  near-duplicate warnings and no live-data writes.
- Focused `Dnd2024AbilityCheckTests`: passed, 346/346.
- Prototype record audit: 2,329/2,329 planned records, zero unresolved references, component errors,
  or archetype-composition errors.
- Prototype tests: passed, 107/107.
- Full `DantesRoleplay.slnx` test suite: passed, 1,404 core tests plus 21 Local AI tests; 1,425/1,425
  total.
- No protocol walk was required because this slice changed no MCP operation or dependency surface.

## Deliberate exclusions

Derived total character level and Armor Class, decomposed weapon/item activities, encounter
lifecycle, live-database migration, companion UI, and new gameplay remain outside this slice. The
next normalized-state slice owns derived level and Armor Class.
