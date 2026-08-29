# D&D 2024 CC3E3 implementation — restricted Martial weapon proficiency state

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC3E3**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3E3](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; *Equipment > Weapons > Weapon Proficiency*,
*Character Classes > Monk > Level 1: Monk Features* (PDF pp. 49–50), and
*Character Classes > Rogue > Level 1: Rogue Features* (PDF pp. 61–62)
Outcome: evolve the canonical weapon-proficiency membership state so new characters preserve
Monk's Martial-Light and Rogue's Martial-Finesse-or-Light grants without claiming attack behavior.
Exclusions: weapon-property definitions, conditional attack enforcement, weapon mastery, specific
weapon grants, physical weapon items, equipment, multiclass aggregation, and temporary grants.
Allowed files/areas: the existing weapon-proficiency descriptor/schema/writer/procedure,
weapon-attack mechanic/procedure state reader, basic creator/procedure, D&D acceptance-test harness,
this dependency plan/roadmap/status line, and this slice's evidence.
Stop point: all twelve class models persist exact complete-category and restricted-property
membership; conditional attacks remain explicitly pending because weapon profiles have no property
owner yet.

## Confirmed decisions and compatibility

The user's standing direction to continue source-correct character state even when behavior remains
incomplete confirms this additive schema/public meaning. No permanent ID, migration, C# rule,
endpoint, MCP kind, item, or new transaction owner is introduced.

The existing `dnd2024.weapon-proficiencies` component remains the sole complete membership owner.
It gains `restrictedMartialProperties`, a canonical duplicate-free any-of set using `finesse` and
`light`. New writers and character creation always store the field, including `[]` for known none.
At the schema boundary the field remains optional so category-only state already stored by a live
campaign remains readable; omission means legacy/unmigrated restricted membership, not a new
source-complete assertion. The writer accepts its omission as the backward-compatible request for
known empty and upgrades every successful write to the explicit current shape.

Basic-creation caller input does not change. Monk and Rogue values come only from their validated
class profiles. A full `martial` category and a nonempty restriction are redundant and rejected by
the writer/profile validator. Restrictions express property alternatives, not a requirement to
have all listed properties.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Complete categories | Simple/Martial category proficiency remains complete membership | `categories` | Preserve existing canonical category state and attack behavior |
| Monk grant | Simple weapons and Martial weapons with Light | Monk creation profile | Store `categories:[simple]`, `restrictedMartialProperties:[light]` |
| Rogue grant | Simple weapons and Martial weapons with Finesse or Light | Rogue creation profile | Store `categories:[simple]`, `restrictedMartialProperties:[finesse,light]` |
| Other classes | Their current declarations have no property-qualified grant | twelve class profiles | Store explicit `[]` for known none |
| Conditional use | A Martial weapon must expose properties before the qualified grant can be checked | no current weapon-property owner | Keep attack enforcement pending; never infer it from name/category |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/documents/advancement/trait.mjs`, separates configured trait grants/choices from their actor
membership application. CC3E3 adopts only that separation: source declarations produce durable
membership, while a later property-aware attack consumer implements behavior. No Foundry code,
data, assets, IDs, or runtime dependency are copied.

## Prerequisite evidence

- [All-class creation](evidence/DND2024-CHARACTER-CREATION-ALL-CLASS-RECEIPT.md) proves exact
  source-bound weapon declarations for all twelve class models.
- [CC3E2](evidence/DND2024-CHARACTER-CREATION-CC3E2-RECEIPT.md) proves current atomic creation,
  immutable pending evidence, and compatibility-first schema evolution.
- The active weapon profile has category/kind/attack ability/damage only. No property data or
  authored Martial Light/Finesse weapon exists, so enforcement is deliberately outside this leaf.
- The generic action runner remains the sole transaction/replay/rollback owner.

## Authoritative state and closed input

Current state is exactly `{categories, restrictedMartialProperties, sourceRef}`. Categories are a
canonical subset of `simple`, `martial`; restrictions are a canonical subset of `finesse`, `light`
and mean any matching property. Legacy state may omit only `restrictedMartialProperties`.

The administrative writer accepts record/correct mode, categories, and optional restrictions. It
derives source attribution and canonical order; callers never supply class, weapon, property
matches, Proficiency Bonus, attacks, effects, or merged state. Basic creation accepts no new input
and copies exact state only from the validated class profile.

## Behavior, result, and typed effects

1. The writer validates exact input shape, canonical vocabulary, uniqueness, nonredundancy, mode,
   and valid prior current/legacy state; every successful write emits one current-shape component.
2. Basic creation stores exact profile categories and restrictions for all twelve classes.
3. Monk/Rogue replace `state-owner-unavailable` with an explicit class-owned
   `behavior-unimplemented` conditional-attack-enforcement entry. No membership remains deferred.
4. Weapon attack accepts valid legacy/current state but continues to use complete category
   membership only. Restricted state never silently grants an attack bonus until a later
   property-aware slice.
5. Existing typed-effect order, transaction, replay, and rollback ownership do not change.

## Failure, replay, and rollback contract

Unknown/duplicate properties, redundant full-Martial restrictions, extra input, bad category,
malformed/invalid prior state, wrong mode/state, corrupt class declarations, source drift, or any
prior creation failure adds nothing. Exact writes/creations replay. Injected late failure leaves no
actor, weapon state, creation record, participation, or relationship.

## Implementation sequence

1. Widen the component schema compatibly and revise writer/procedure to emit exact current state.
2. Make weapon attack validate both legacy and current envelopes without adding property behavior.
3. Integrate exact all-class state and enforcement-only pending evidence into basic creation.
4. Add writer/schema, legacy attack, all-class/matrix, replay, rollback, and drift tests.
5. Run focused tests, complete D&D tests, disposable catalog validation, and the full solution;
   then write one receipt and update status once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Writer | omitted restrictions becomes explicit empty; values canonicalize; exact replay is stable |
| Writer failure | unknown/duplicate/redundant/extra/bad-state cases preserve state |
| Compatibility | a valid legacy category-only actor still makes an existing category attack |
| All classes | Monk `[light]`, Rogue `[finesse,light]`, all others `[]` |
| Pending ledger | Monk/Rogue membership is present while only enforcement remains pending |
| 48-pair matrix | background never changes class weapon membership |
| Transactions | exact creation replay and late rollback remain atomic |

## Verification commands

- focused weapon-owner/attack/basic-creation tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a fresh disposable database; and
- full solution tests. No protocol walk is required because MCP/protocol registration does not
  change.

## Completion receipt and exit gate

Delivered by the
[CC3E3 completion receipt](evidence/DND2024-CHARACTER-CREATION-CC3E3-RECEIPT.md). This slice stops
without weapon properties, conditional attack enforcement, equipment, spells, feature behavior,
multiclassing, or UI work.
