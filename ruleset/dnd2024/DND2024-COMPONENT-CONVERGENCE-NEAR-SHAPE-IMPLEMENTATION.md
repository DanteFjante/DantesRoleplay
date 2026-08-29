# Component convergence NS1 implementation - near-shape character state cohort

Status: **accepted**
Feature/slice: **DND2024 component convergence / NS1**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [component convergence dependency tree](../../prototype/dnd2024/planning/DND2024-COMPONENT-CONVERGENCE-DEPENDENCY-TREE.md), leaf 4
Ruleset alignment: `dnd2024-compatible`
Source: existing rule provenance remains `source.dnd2024.srd-5.2.1`; this slice changes ECS ownership and state shape, not D&D rule meaning
Outcome: converge character identity, Experience, Hit Points, and Temporary Hit Points onto their prototype ECS owners and adapt every existing catalog consumer in one cohort.
Exclusions: total-level/class-membership convergence, death saves, maximum-HP reduction mechanics, Heroic Inspiration consumption, rest completion, new damage/healing sources, events, public APIs, C# rules, and all other component families.
Allowed areas: this document/tree/roadmap; the four component descriptors/schemas; direct mechanics/procedures/content writers; companion-interface component bindings; D&D and interface tests; completion receipt.
Stop point: all four old component keys are absent from active runtime artifacts, current tests use
them only as explicit historical-to-current closure evidence, all existing behavior passes on target
state, and the full suite is green.

## Confirmed decisions

- The user's request to move existing mechanics onto the prototype ECS shape confirms the four target keys and their prototype payload meanings for this cohort.
- Canonical target keys are `dnd2024.character.identity`, `dnd2024.character.experience`, `dnd2024.creature.hit-points`, and `dnd2024.creature.temporary-hit-points`.
- Existing mechanic and procedure IDs remain stable. No alias, dual read, or dual write is introduced.
- A read-only audit of `data/dantesroleplay.db` found zero definitions and zero instances for all eight old/target keys. This is a file-only migration with no live database write.

## Alignment and shape decisions

| Family | Target payload | Existing behavior retained |
| --- | --- | --- |
| Identity | one or more of `pronouns`, `appearance`, `biography`, `playerNotes` at prototype limits | trimmed nonblank administrative record/correct; entity still owns name |
| Experience | `{total}` | safe nonnegative total, explicit record/correct, next-level threshold derivation |
| Hit Points | `{current, maximum, maximumReduction?}` | bounded HP, healing cap, damage floor, rest eligibility; optional reduction is preserved but not calculated here |
| Temporary HP | `{amount, sourceRef:{entityId}}` | one positive buffer, keep/replace/expire, damage absorption before HP |

Rule citations removed from mutable Experience/HP state remain fixed in mechanic result evidence. The
Temporary HP target explicitly requires an entity reference, so the existing fixed SRD source becomes
`{"entityId":"source.dnd2024.srd-5.2.1"}` in state while the exact locator remains result evidence.

No new Foundry review is relevant because algorithms and rule outcomes are retained. Existing accepted
owners and Slice 11F/11G receipts already prove the HP/Temporary-HP transitions and transaction ordering.

## Runtime boundary

- Replace the four descriptor/schema file pairs and IDs.
- Rebind profile editing and Heroic Inspiration character eligibility to identity.
- Rebind XP read/write and basic character creation to character Experience without stored citation.
- Rebind HP writer, healing, weapon damage, rest start, and basic creation to creature HP; accept and
  preserve optional `maximumReduction` without adding a reduction mechanic.
- Rebind Temporary HP writer and weapon-damage absorption to creature Temporary HP and validate its
  entity-reference source.
- Rebind the companion interface's identity, Experience, HP, and Temporary-HP reads to the same
  canonical keys.
- Update governing procedures and current tests. Generic effects/transactions remain owners.

## Failure, replay, and rollback

All existing malformed, absent, duplicate, wrong-mode, corrupt-state, replay, no-change, and rollback
contracts remain. Consumers reject extra/invalid target fields. Old keys are not accepted. Same-operation
replay produces no duplicate state, and multi-effect weapon damage remains one generic transaction.

## Acceptance

- focused profile/Heroic, XP, HP/healing/rest, Temporary-HP/damage, and companion-interface tests;
- old-key operational-reference scan across active catalog and current tests;
- JavaScript syntax for every revised mechanic;
- fresh `validate catalog`;
- complete `Dnd2024AbilityCheckTests`;
- full solution tests and scoped whitespace validation;
- receipt at `ruleset/dnd2024/evidence/DND2024-COMPONENT-CONVERGENCE-NEAR-SHAPE-RECEIPT.md`.

No protocol walk is required because no MCP surface or dependency registration changes.

Accepted by the
[NS1 completion receipt](evidence/DND2024-COMPONENT-CONVERGENCE-NEAR-SHAPE-RECEIPT.md). Work stops
before the normalized creature-state cohort.
