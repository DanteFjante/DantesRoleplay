# D&D 2024 CC3E2 implementation — Bard and Monk class tool choices

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC3E2**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3E2](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; *Equipment > Tools* (PDF pp. 93–94),
*Character Classes > Bard > Level 1: Bard Features* (PDF pp. 31–32), and
*Character Classes > Monk > Level 1: Monk Features* (PDF pp. 49–50)
Outcome: allow the existing basic creator to accept and persist the exact level-1 class tool
choices declared by Bard and Monk while retaining the compatible unresolved path when omitted.
Exclusions: tool checks, tool actions, tool expertise, item ownership, equipment packages, tool
discovery UI, multiclassing, replacement/retraining, and tool choices from any other feature.
Allowed files/areas: the existing basic creator and procedure, character-creation record schema,
tool-proficiency schema/writer, Versatile/Skilled resolver, the D&D acceptance-test harness, the
one corrected CC2C vocabulary statement, this dependency plan/roadmap/status line, and this slice's
evidence.
Stop point: selected tool proficiency membership is applied atomically and only its satisfied class
choice deferral is removed; no item or tool behavior is inferred.

## Confirmed decisions

The user's standing direction to continue character creation with correctly shaped models and to
prefer durable data even while mechanics remain incomplete confirms this additive request/record
schema meaning. The same confirmed source-alignment boundary includes correcting the existing
canonical tool vocabulary from 36 to all 37 SRD tools by adding the previously omitted `lyre` to
the component and its two current JavaScript consumers. No new permanent ID, migration, C# rule,
endpoint, optional ruleset, or MCP kind is introduced.

The basic creator accepts one additional optional top-level property, `classToolChoices`. It is an
array because the selected class profile is the authoritative declaration of count and eligible
families. Omission remains supported and leaves the existing class tool entitlement pending. A
provided value is legal only when the selected class declares exactly one choice group:

- Bard: exactly three distinct Musical Instruments;
- Monk: exactly one Artisan's Tool or Musical Instrument.

Other classes reject the property rather than silently accepting irrelevant data. The caller
supplies only selected tool IDs; class, count, families, source attribution, merged proficiency
state, effects, and pending-ledger consequences are derived.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Tool vocabulary | Tools include the canonical Artisan's Tools and Musical Instruments | `dnd2024.tool-proficiencies` schema | Validate selections against the existing canonical tool IDs |
| Bard choice | Bard gains proficiency with three Musical Instruments at level 1 | Bard class creation profile | Require three distinct instrument IDs when provided |
| Monk choice | Monk gains proficiency with one Artisan's Tool or Musical Instrument at level 1 | Monk class creation profile | Require one ID in the union of the declared families when provided |
| Membership composition | A character can receive tool proficiency from background and class | existing complete-membership tool owner | Union background, fixed class, and selected class tools without duplicates |
| Incomplete creation | A caller may defer choices in the basic-playable path | creation record pending ledger | Preserve the class-owned `tool-choice:*` entry only when choices are omitted |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/documents/advancement/trait.mjs`, models configured grants separately from selected choices,
stores chosen values, and unions choices into current trait membership when applying an
advancement. CC3E2 adopts only that separation and additive-membership design through this
repository's existing profiles, immutable creation record, component state, and typed effects.
No Foundry code, data, assets, IDs, or runtime dependency are copied.

## Prerequisite evidence

- [All-class creation](evidence/DND2024-CHARACTER-CREATION-ALL-CLASS-RECEIPT.md) proves the exact
  source-bound Bard and Monk tool choice declarations.
- [CC3B](evidence/DND2024-CHARACTER-CREATION-CC3B-RECEIPT.md) proves the compatible optional-choice,
  immutable-selection, tool-union, replay, and rollback pattern for background choices.
- [CC3E1](evidence/DND2024-CHARACTER-CREATION-CC3E1-RECEIPT.md) proves the current all-class atomic
  actor transaction and pending-ledger boundary.
- `dnd2024.tool-proficiencies` already owns the complete canonical membership set; no new state
  owner is necessary.

## Authoritative state and closed input

The accepted top-level shapes are the required basic input alone, plus `originChoices`, plus
`classToolChoices`, or plus both optional properties. No other property is accepted.
`classToolChoices` is a duplicate-free canonical tool-ID array validated against the selected
class profile's one choice group. It is never accepted for a class with no such group.

When supplied, the immutable creation record stores a canonically ordered
`selections.classToolChoices`. The tool component remains complete current membership and contains
the union of fixed background tools, a selected background tool, fixed class tools, and selected
class tools. Source attribution remains the existing tool-proficiency rule locator.

## Behavior, result, and typed effects

1. Validate the selected class profile before interpreting class tool choices.
2. If the property is present, validate exact count, uniqueness, canonical tool vocabulary, and
   membership in at least one declared option family.
3. Merge valid selections into existing tool membership and persist them in the creation record.
4. Remove only the selected class's satisfied `tool-choice:<count>:<families>` deferral. Omission
   preserves that deferral and all existing behavior/equipment/resource deferrals.
5. Report `classToolChoicesResolved` as derived result evidence. The generic action runner retains
   sole effect ordering, transaction, replay, and rollback ownership.

## Failure, replay, and rollback contract

Wrong count, duplicate, unknown/noncanonical tool, wrong family, choices on an ineligible class,
extra top-level data, corrupt profile declarations, source drift, or any prior creation failure
fails before effects. Exact requests replay. Injected late failure leaves no actor, tool state,
creation record, participation, or relationship.

## Implementation sequence

1. Correct the existing canonical tool vocabulary to include `lyre`, then extend the immutable
   record schema with the optional canonical selection array.
2. Integrate profile-derived validation, membership union, pending resolution, and result evidence
   into the existing JavaScript creator.
3. Update its procedure and add focused success/failure/replay/rollback/matrix tests.
4. Run focused tests, complete D&D tests, disposable catalog validation, and the full solution.
5. Write one receipt and update CC3E2 status once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Bard | exactly three distinct instruments persist in selection and tool membership |
| Monk | one Artisan's Tool and one Musical Instrument are each legal cases |
| Composition | origin and class choices union with fixed grants without duplicate membership |
| Omission | existing 48 background/class creations remain compatible and choices stay pending |
| Failure | wrong count, duplicate, unknown, wrong family, cross-class, and extra input add nothing |
| Pending ledger | only a supplied valid class choice removes its class-owned tool deferral |
| Transactions | exact replay is stable and late failure rolls back every staged effect |

## Verification commands

- focused class-tool-choice and basic-creation tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a fresh disposable database; and
- full solution tests. No protocol walk is required because MCP/protocol registration does not
  change.

## Completion receipt and exit gate

Delivered by the
[CC3E2 completion receipt](evidence/DND2024-CHARACTER-CREATION-CC3E2-RECEIPT.md). This slice stops
without restricted Martial weapon membership, tool behavior, equipment, spells, feature behavior,
multiclassing, or UI work.
