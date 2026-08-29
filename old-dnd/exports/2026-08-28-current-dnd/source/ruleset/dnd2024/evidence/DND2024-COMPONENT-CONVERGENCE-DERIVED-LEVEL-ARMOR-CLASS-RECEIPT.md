# D&D 2024 component convergence — derived level and Armor Class receipt

Status: **accepted**  
Accepted: 2026-08-28  
Implementation: `prototype/dnd2024/planning/DND2024-DERIVED-LEVEL-AND-ARMOR-CLASS-SOL-SLICE-2-IMPLEMENTATION.md`

## Delivered boundary

- Activated `dnd2024.character.class-membership` on independently addressable membership entities
  linked to their character by `dnd2024.character.has-class-membership` relationships.
- Added an effect-free total-level reader that validates membership direction, shape, unique class
  identity, and the level-20 aggregate limit, then derives Proficiency Bonus from total level.
- Activated `dnd2024.creature.defenses` plus source-owned
  `dnd2024.creature.defense-basis`, with the first ordinary-unarmored source deriving Armor Class as
  10 plus the Dexterity modifier.
- Migrated ability checks, saves, Initiative, weapon attacks, Experience, character-sheet reads,
  and basic character creation to the derived owners.
- Basic character creation now creates the actor, its level-1 class-membership entity and
  relationship, and its ordinary-unarmored defense selection in one replayable transaction.
- Extended the generic projection contract with bounded relationship-endpoint component
  declarations. Both projection engines filter by kind/direction, expose only declared endpoint
  components, record endpoint revisions, and fail closed on incomplete state. Component references
  accept the ECS `{entityId}` shape as well as the existing direct-string shape.
- Retired the `dnd2024.character-level` and `dnd2024.armor-class` component descriptors and their
  record/write mechanics without aliases, dual reads, or dual writes. Stable procedure identities
  now govern the derived owners.

## Verification

- `roleplay validate catalog`: passed; 144 records validated, with the 21 pre-existing
  near-duplicate warnings and no live-data writes.
- Focused generic projection/application tests: passed, 35/35.
- Focused `Dnd2024AbilityCheckTests`: passed, 348/348, including multiclass aggregation,
  duplicate/overflow rejection, selected AC derivation, unknown-source rejection, creation,
  replay, and rollback coverage.
- Prototype record audit: 2,329/2,329 planned records, zero unresolved references, component errors,
  or archetype-composition errors.
- Prototype tests: passed, 107/107.
- Full `DantesRoleplay.slnx` test suite: passed, 1,410 core tests plus 21 Local AI tests;
  1,431/1,431 total.
- `git diff --check`: no whitespace errors.
- No protocol walk was required because this slice changed no MCP operation or dependency surface.

## Deliberate exclusions

Armor, Shields, Barbarian/Monk unarmored alternatives, cover and temporary AC modifiers;
class-advancement writes and multiclass eligibility; live-database migration; decomposed items;
encounter lifecycle; companion UI; and new gameplay remain outside this slice. Encounter lifecycle
is the next ordered component-convergence leaf.
