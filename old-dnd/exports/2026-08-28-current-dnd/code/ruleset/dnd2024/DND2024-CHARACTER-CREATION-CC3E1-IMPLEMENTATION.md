# D&D 2024 CC3E1 implementation — armor-training state owner and class grants

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC3E1**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3E1](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; *Rules Glossary > Armor Training*
(PDF p. 176) plus the selected class's existing source-bound locator
Outcome: restore the retained armor-training component/read/write owner into the authored D&D
catalog and make basic creation persist every class's exact known level-1 training set.
Exclusions: equipped-armor eligibility, untrained-armor D20/spellcasting drawbacks, Armor Class,
Shield AC, don/doff time, Speed, items, multiclassing, temporary grants, and UI discovery.
Allowed files/areas: the retained armor-training donor artifacts, their current authored catalog
destinations, the existing basic creator/procedure, the D&D acceptance-test harness, this dependency
plan/roadmap/status line, and this slice's evidence.
Stop point: armor-training state is independently recordable/readable and all twelve class models
create exact state atomically; no equipment or consequence rule is inferred from membership.

## Confirmed decisions

The user's direction to reuse compatible old D&D implementation where possible and to continue
quality-controlled character-creation slices confirms reactivating these retained permanent IDs:

- `dnd2024.armor-training`;
- `mechanic.dnd2024.armor-training.read`;
- `mechanic.dnd2024.armor-training.write`; and
- `procedure.mechanic.dnd2024.armor-training`.

The archived artifacts are donor evidence only. The reviewed versions are copied into the authored
application catalog, adapted to current envelope conventions, and validated there. `[]` means
known none; absence remains unknown. No migration, optional rule, house rule, C# semantic change,
endpoint, or MCP kind is introduced.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Training vocabulary | Armor Training is expressed for Light, Medium, Heavy armor and Shields | retained schema plus current class profiles | Store a canonical duplicate-free subset ordered light, medium, heavy, shield |
| Class grants | Each selected class declares its starting armor training | `dnd2024.class-creation-profile.armorTraining` | Basic creation derives the complete set; caller supplies none of it |
| Known none | Some classes begin with no armor training | closed component semantics | Persist `categories: []` for Monk, Sorcerer, and Wizard rather than omitting state |
| Consequences | Training matters when armor or a Shield is used | future equipment/rule consumers | This slice records membership only and calculates no consequence |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/data/actor/character.mjs`, stores `armorProf` as a dedicated actor trait separate from Armor
Class and equipped armor. CC3E1 adopts that separation while retaining this repository's existing
category vocabulary and component/effect architecture. No Foundry code, data, assets, IDs, or
runtime dependency are copied.

## Prerequisite evidence

- [All-class creation](evidence/DND2024-CHARACTER-CREATION-ALL-CLASS-RECEIPT.md) proves exact
  source-bound armor-training declarations for all twelve class models.
- [CC3C](evidence/DND2024-CHARACTER-CREATION-CC3C-RECEIPT.md) proves the current 48-pair actor
  transaction, feature-grant state, and pending-ledger boundary.
- The retained archive inventory identifies the prior component, reader, writer, and procedure;
  the files themselves are reviewed donor code, not runtime authority.
- The generic action runner remains the sole transaction/replay/rollback owner.

## Runtime artifacts

- Restored/adapted `dnd2024.armor-training` definition and closed schema under the authored D&D
  catalog.
- Restored/adapted effect-free reader and one-effect administrative record/correct writer with one
  governing procedure.
- Revised basic creator/procedure to derive and atomically apply exact class training.
- Focused owner and complete class/background matrix tests.
- No new C# rule, content ID, fixture, migration, transaction owner, endpoint, or protocol kind.

## Authoritative state and closed input

The component is exactly `{categories, sourceRef}`. `categories` is a canonical subset of `light`,
`medium`, `heavy`, `shield`; `sourceRef` is fixed to the registered SRD Armor Training glossary
locator. Empty is known none; missing is unknown.

The reader accepts exactly `{}` and gets all state through role `subject`. The writer accepts exactly
`{mode: "record"|"correct", categories: [...]}`; the caller never supplies source attribution,
class, grant provenance, items, AC, drawbacks, or effects. Basic creation takes no new input and
derives categories only from the validated selected class profile.

## Behavior, result, and typed effects

1. The reader reports absent/malformed/invalid/valid diagnostics and never emits effects.
2. The writer validates a duplicate-free category subset, canonicalizes it, and derives the fixed
   rule source. `record` requires absence and emits one `component.add`; `correct` requires valid
   existing state and emits one `component.set`.
3. Basic creation always applies the component, including `[]` for a class with known none, lists it
   in applied-component evidence, and removes class `armor-training:*` state-owner deferrals.
4. The immutable creation record's selected class ID and class source reference remain the grant
   provenance; the component remains the complete current membership state.
5. Existing transaction, effect ordering, replay, rollback, and participation ownership remain
   unchanged.

## Failure, replay, and rollback contract

Unknown/duplicate/noncanonical categories, extra/missing input properties, record-over-existing,
correct-over-absent, or corrupt prior state fails with no effect. The reader never repairs invalid
state. Basic creation retains existing request/role/source/child/collision failures; invalid class
training fails before effects. Exact actions replay and injected late failure leaves no partial
actor, armor state, participation, or relationship.

## Implementation sequence

1. Restore/adapt the component, reader, writer, and procedure into `catalog/applications/dnd2024`.
2. Integrate the component into the existing JavaScript creation proposal and remove only satisfied
   armor-training deferrals.
3. Add owner, all-class, 48-pair, replay, rollback, and compatibility tests.
4. Run focused tests, complete D&D tests, disposable catalog validation, and the full solution.
5. Write one receipt and update CC3E1 status once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Known none | empty set persists and reads valid; absence reads unknown |
| Writer | record/correct canonicalizes and exact replay is stable |
| Writer failure | bad vocabulary/order/duplicate/mode/state leaves state unchanged |
| Class grants | all twelve classes persist exactly their profile declaration |
| 48-pair matrix | background does not alter class armor training |
| Pending ledger | satisfied `armor-training:*` deferrals are absent; equipment/behavior remains pending |
| Transactions | exact creation replay and late rollback remain atomic |
| Compatibility | prior creation/origin-choice/feature-grant tests remain green |

## Verification commands

- focused armor-training and basic-creation tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a fresh disposable database; and
- full solution tests. No protocol walk is required because MCP/protocol registration does not
  change.

## Completion receipt and exit gate

Delivered by the
[CC3E1 completion receipt](evidence/DND2024-CHARACTER-CREATION-CC3E1-RECEIPT.md). This slice stops without
equipped-armor checks, untrained drawbacks, Armor Class derivation, Shield effects, spellcasting,
items, multiclassing, or UI work.
