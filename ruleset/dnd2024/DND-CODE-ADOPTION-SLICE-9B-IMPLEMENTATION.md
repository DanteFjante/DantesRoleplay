# D&D code-adoption Slice 9B implementation — stateless core character calculations

Status: **implemented; focused/full acceptance pending**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), donor-gap-filling lane  
Dependency tree/leaf: [Slice 9 design](DND-CODE-ADOPTION-SLICE-9-DESIGN.md), leaf 9B  
Ruleset alignment: `dnd2024-owned`  
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Character Creation > Step 5: Character
Creation Details > Fill In Numbers` (PDF pp. 21–22) and `Rules Glossary > Passive Perception`
(PDF p. 185)  
Outcome: Derive the core numerical character-sheet view from authoritative current components.  
Exclusions: Stored projections, AC/HP/Speed/inventory passthrough, expertise/half proficiency,
temporary bonuses, effect stacks, contextual Advantage/Disadvantage, spell DC/slots, content
imports, writes, public operations, migrations, and live state.  
Allowed files/areas: one D&D mechanic/procedure, focused D&D tests, Slice 9 inventory/conformance
evidence, this plan, owner status, and receipts.  
Stop point: the closed empty-input reader and donor/native/SRD conformance pass; stop before any
state, content, spellcasting, effect, or public-surface owner.

## Confirmation decision

The user confirmed the new permanent IDs `mechanic.dnd2024.character-sheet.read` and
`procedure.mechanic.dnd2024.character-sheet` on 2026-08-26. No new component/schema or
external/public operation is introduced.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Ability modifier | Derive the modifier from each score | `dnd2024.abilities` | Calculate with `floor((score - 10) / 2)`; never store it |
| PB | PB follows total character level | `dnd2024.character-level` | Calculate `2 + floor((level - 1) / 4)` for levels 1–20 |
| Saves | Proficient saves add PB to the relevant ability modifier | saving-throw proficiency component | Return all six deterministic modifiers and proficiency flags |
| Skills | Proficient skills add PB to the associated ability modifier | skill-proficiency component | Return all eighteen canonical skills using the current ability map |
| Initiative | Character-creation sheet records Dexterity modifier | ability component | Return the Dexterity modifier only; do not roll |
| Passive Perception | 10 plus Wisdom (Perception) check modifier | abilities, level, skill proficiency | Return the base score; contextual Advantage/Disadvantage and other modifiers remain excluded and explicit |

## External implementation reference

- Donor `src/derive/character-view.ts` at blob
  `f14e0922ebc2630bec9b811c406141d07f2bd1f6` aggregates deterministic ability, PB, and save fields;
  only that stateless idea is adapted, not its whole `Character`, content, item, pending-choice,
  spell-slot, AC, or effect-stack architecture.
- Donor `src/derive/ability-check.ts` at blob
  `bce546d51233258a8cf991c9fb3b33b255e3d3f5` implements passive score as 10 plus the check modifier
  and an Advantage/Disadvantage adjustment. This cohort adapts the source-backed base calculation
  only because current content/effect owners cannot yet prove every contextual modifier.
- Foundry `module/data/actor/character.mjs` at blob
  `d6fc14cfc7fbf0f5e2d96fda610b3e937601c08b` prepares PB before derived ability/skill data.
  `module/data/actor/templates/creature.mjs` at blob
  `257c7f51f6ea2502a6fd67e4687862abe3289f71` derives passive values after skill totals and applies
  the SRD ±5 rule for resolved Advantage/Disadvantage. These are reference-only data-flow checks.

## Prerequisite evidence

- [Parent Slice 8 receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-8-RECEIPT.md) proves current
  recovery owners and catalog/full-suite acceptance.
- [Slice 9 inventory](adoption/evidence/slice-9-derivation-candidates.json) proves this is the only
  dependency-ready uncovered pure character-calculation cohort.
- Accepted Slice 7A2/7A4 evidence proves ability, level, skill, save, and application-execution
  requirements.
- The development-only `adoption/probes/character-sheet/` wrapper, result schema, operation view,
  source vectors, and reviewed provenance prove the closed behavior without registering either
  proposed permanent ID. This proof is not runtime acceptance.

## Runtime artifacts

- New mechanic ID: `mechanic.dnd2024.character-sheet.read` (pending confirmation).
- New governing procedure ID: `procedure.mechanic.dnd2024.character-sheet` (pending confirmation).
- No component, schema, fixture entity, migration, C# seam, effect type, or public kind.

## Authoritative state and closed input

The sole `subject` role requires exactly the four existing component types named in the parent
design. Input must be the empty object. The caller cannot supply scores, level, PB, proficiency,
skill mappings, modifiers, passive score, or source identity.

Every component must match its closed current schema and exact SRD source reference. Missing roles
fail during materialization; malformed or source-drifted JSON fails in JavaScript before a result.

## Behavior, result, and typed effects

Return deterministic JSON containing level, PB, six `{score, modifier}` ability entries, six save
entries, eighteen skill entries with canonical ability/proficiency/modifier, initiative modifier,
base Passive Perception with its breakdown, and exact SRD source locators. Ordering is fixed by the
canonical ability/skill lists. No RNG is consumed. Effects, events, and notifications are empty.

## Failure, replay, and rollback contract

Nonempty/invalid input, malformed JSON, unexpected fields, invalid values/duplicates, or wrong
source references fail without output effects. Schema-valid proficiency sets are canonicalized to
the fixed D&D ability/skill order, matching the existing recorder contracts. Same component
revisions and input produce byte-equivalent data. There is no mutation to replay or roll back;
application activation and evaluation audit remain generic kernel responsibilities.

## Implementation sequence

1. ~~Confirm the two permanent IDs and mark this document active.~~
2. ~~Add procedure and mechanic contracts, then the sandbox-compatible JavaScript.~~
3. ~~Add focused activated-path and normalized donor/SRD vectors.~~
4. Run syntax, focused/combined tests, catalog validation, build, full suite, and diff check.
5. Record 9B receipt, close the candidate inventory in 9C, and stop at the stated boundary.

## Acceptance matrix

| Case | Evidence |
| --- | --- |
| Ability/PB boundaries | scores 1/30 and levels 1/4/5/20 derive exact values |
| Skills/saves | proficient and nonproficient modifiers across all canonical entries |
| Passive Perception | Wisdom 15 with Perception proficiency at level 1 yields 14 |
| Closed authority | injected PB/modifier/score/skill/source and nonempty input reject |
| Corrupt state | malformed, extra, duplicate, out-of-range, and source-drifted states reject; schema-valid unordered proficiency sets canonicalize |
| Purity/replay | repeated evaluation is identical and has no RNG/effects/events/notifications |
| Compatibility | existing D&D tests and combined kernel/effect seam remain green |

## Verification commands

- `node --check` over all D&D mechanics.
- Focused `Dnd2024AbilityCheckTests` Slice 9 cases and full activated D&D suite.
- Combined application-execution/ECS-effect/application-seam tests.
- `dotnet build DantesRoleplay.slnx --no-restore`.
- `roleplay validate catalog` against a disposable database.
- Full solution test suite and `git diff --check`.
- No protocol walk: the slice changes no MCP surface or dependency registration.

## Completion receipt and exit gate

Record delivery in `adoption/evidence/DND-CODE-ADOPTION-SLICE-9B-RECEIPT.md`. Do not mark Parent 9
accepted until 9C proves every candidate inventory row has its declared current disposition and the
full acceptance commands pass.
