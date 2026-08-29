# D&D 2024 CC3B implementation — exact optional origin choices

Status: **accepted**
Feature/slice: **D&D 2024 character creation / CC3B**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation CC3B](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; *Character Creation > Step 2:
Character Origin > Choose Languages* (PDF p. 20), *Character Origins > Character Backgrounds >
Soldier* (PDF p. 83), and *Equipment > Tools > Other Tools > Gaming Set* (PDF p. 94)
Outcome: let a caller optionally complete the two Standard-language choices and, for Soldier, the
specific Gaming Set choice in the existing atomic basic-creation request.
Exclusions: rare languages, extra languages from class/features, equipment package/cash choice,
feat behavior, class tool choices, UI discovery, migrations, and new protocol kinds.
Allowed files/areas: the existing D&D basic creator, its creation-record schema/procedure, the D&D
acceptance-test harness, this dependency plan/roadmap/status line, and this slice's evidence.
Stop point: supplied origin choices are exactly validated/applied/receipted; the accepted omitted-
choice path remains compatible and honest, and no unrelated character feature is implemented.

## Confirmed decisions

The user's 2026-08-27 request to continue as many quality-controlled character-creation slices as
possible confirms this additive schema/input meaning:

- the existing four-field request remains valid and leaves selectable origin grants pending;
- an optional `originChoices` object completes both language choices and every background tool
  choice for the selected background;
- fixed-tool backgrounds reject a caller-supplied tool, while Soldier requires exactly one of the
  four SRD Gaming Set variants whenever `originChoices` is present; and
- the immutable creation record stores only choices actually supplied and successfully applied.

No permanent ID, migration, optional rule, house rule, C# semantic change, endpoint, or MCP kind is
introduced.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Origin languages | Every character knows Common plus two chosen/rolled Standard languages | `dnd2024.language-proficiencies` | Accept exactly two distinct non-Common entries from the Standard table and apply all three |
| Soldier tool | Soldier chooses one kind of Gaming Set | background profile and `dnd2024.tool-proficiencies` | Accept Dice, Dragonchess, Playing Cards, or Three-Dragon Ante and union it with fixed class tools |
| Fixed background tools | Other SRD backgrounds specify their tool and expose no choice | CC3A profile | Reject a redundant or injected background tool choice |
| Compatibility | CC3A permits an incomplete basic actor with explicit pending entries | creation record/pending ledger | Omission preserves Common-only and pending language/Gaming Set entries exactly |

## External implementation reference

The CC3A review of Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` at
`module/data/item/background.mjs` remains the relevant evidence: selection values are separate from
immutable background declarations and are staged before applying actor state. This slice adds only
that selection layer. No Foundry code, data, assets, IDs, or runtime dependency are copied.

## Prerequisite evidence

- [CC3A](evidence/DND2024-CHARACTER-CREATION-CC3A-RECEIPT.md) proves all four background profiles,
  Common, fixed tools, the 48 background/class matrix, and pending semantics.
- Existing language/tool schemas and recorders define the canonical IDs and state source locators.
- The basic creator already owns the atomic actor/participation effect bundle, replay, and rollback.

## Runtime artifacts

- Revised `dnd2024.character-creation-record` selections with optional `languageChoices` and
  `backgroundToolChoice` evidence.
- Revised basic creator JavaScript/procedure for one optional closed `originChoices` input object.
- Focused complete/omitted/invalid choice tests in the existing D&D harness.
- No new component, content ID, mechanic, transaction owner, database object, C# rule, or public
  protocol kind.

## Authoritative state and closed input

The five roles and three children are unchanged. The request is one of exactly two shapes:

1. `characterId`, `name`, `ability`, and `speciesSelection`; or
2. those four fields plus `originChoices`.

For Acolyte, Criminal, or Sage, `originChoices` contains exactly `languages`. For Soldier, it
contains exactly `languages` and `backgroundTool`. `languages` contains exactly two distinct IDs
from Common Sign Language, Draconic, Dwarvish, Elvish, Giant, Gnomish, Goblin, Halfling, and Orc.
`backgroundTool` is exactly `dice-set`, `dragonchess-set`, `playing-cards`, or
`three-dragon-ante`. The caller never supplies Common, fixed tools, merged final state, pending
entries, source references, effects, or transaction identity.

## Behavior, result, and typed effects

1. Validate the request's exact top-level shape, then validate `originChoices` against the selected
   source-bound background profile.
2. When choices are absent, preserve CC3A: apply Common/fixed tools and retain the language/tool
   pending entries.
3. When choices are present, apply Common plus the two selected Standard languages in canonical
   order. For Soldier, add the selected Gaming Set to any fixed class tools.
4. Store the two language choices and optional Soldier tool choice in the immutable creation record;
   omit those properties on the compatible incomplete path.
5. Remove only the pending entries satisfied by supplied choices. Equipment, feat behavior, and all
   other unresolved grants remain unchanged and sorted.
6. Commit through the existing transaction; effect ordering, replay, rollback, and participation
   ownership remain unchanged.

## Failure, replay, and rollback contract

Reject missing/extra choice fields, duplicates, Common, a rare language, an unknown language, fewer
or more than two languages, a missing/invalid Soldier Gaming Set, or any tool choice for a fixed-tool
background before effects. Existing invalid role/source/child/collision failures remain unchanged.
An exact complete-choice request replays; injected late failure leaves no actor or participation.

## Implementation sequence

1. Widen only the record selections schema and creator's closed input validation.
2. Apply/store exact choices and conditionally remove their pending entries.
3. Add complete, omitted, malformed, replay, and rollback coverage.
4. Run focused tests, complete D&D tests, disposable catalog validation, and the full solution.
5. Write one receipt and update CC3B status once.

## Acceptance matrix

| Case | Evidence required |
| --- | --- |
| Omitted choices | every prior basic request remains valid with Common and pending choices |
| Fixed-tool backgrounds | two Standard languages complete; fixed tool remains exact |
| Soldier | two Standard languages plus each legal Gaming Set variant can complete |
| Class fixed tool | selected/fixed background tools union once with fixed class tools |
| Receipt | supplied choices appear in selections and satisfied pending entries disappear |
| Closed failure | invalid/duplicate/rare/Common language and wrong/missing/extra tool choices create nothing |
| Transactions | exact replay and injected late rollback remain atomic |
| Compatibility | the existing 48-pair incomplete matrix remains green |

## Verification commands

- focused origin-choice/basic-creation tests;
- complete `Dnd2024AbilityCheckTests`;
- `roleplay validate catalog` against a fresh disposable database; and
- full solution tests. No protocol walk is required because protocol/dependency registration does
  not change.

## Completion receipt and exit gate

Delivered by the
[CC3B completion receipt](evidence/DND2024-CHARACTER-CREATION-CC3B-RECEIPT.md). This slice stops
without equipment selection/instantiation, feature behavior, rare-language grants, class tool
choices, or UI discovery.
