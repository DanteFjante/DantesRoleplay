# D&D code-adoption Slice 10F implementation — Fighter levels 1–2 progression identities

Status: **implemented; acceptance pending confirmation**  
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md), breadth lane  
Dependency tree/leaf: [Slice 10 static-content design](DND-CODE-ADOPTION-SLICE-10-DESIGN.md), leaf 10F  
Ruleset alignment: `dnd2024-owned`  
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Classes > Fighter, PDF pages 47–48`  
Outcome: recover the archived Fighter class identity, its two-level immutable progression, and the
five feature identities referenced at levels 1 and 2.  
Exclusions: feature behavior, Fighting Style choices, Weapon Mastery choices, Second Wind uses,
Action Surge uses, Tactical Mind effects, actor advancement, HP mutation, multiclass rules, later
levels, migrations, public operations, and automatic campaign installation.  
Allowed files/areas: this document and Parent 10 status; the six character-progression entity
files; one transform manifest/tool; focused D&D tests; attribution, roadmap, and receipt evidence.  
Stop point: activated, schema-valid, hash-locked identities consumed by the existing read-only
class-progression mechanic, with all broader character advancement still deferred.

## Confirmed decisions

The user's Slice 10 instruction authorizes reuse of these six permanent archived IDs. This leaf
does not create IDs, change schema meaning, or claim that an identity record implements its feature.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Consequence |
| --- | --- | --- | --- |
| Fighter durability | Fighter Hit Point Die is D10; fixed later-level gain before Constitution is 6 | `dnd2024.class-progression` | Store the accepted D10/6 pair as immutable class content |
| Level 1 features | Fighting Style, Second Wind, and Weapon Mastery | class progression plus separate content identities | Level 1 contains exactly these three sorted IDs |
| Level 2 features | Action Surge and Tactical Mind | class progression plus separate content identities | Level 2 contains exactly these two sorted IDs |
| Behavior status | Identity and entitlement are not executable feature behavior | `mechanic.dnd2024.class-progression.read` | Reader reports each entitlement as `unimplemented`; no effects are emitted |

## External implementation reference

Pinned Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was reviewed at
`packs/_source/classes24/fighter/fighter.yml` and
`module/data/advancement/item-grant-data.mjs`. Its Fighter data independently groups the same three
level-1 and two level-2 feature records as level-indexed grants. The useful engineering evidence is
separate feature identities referenced by class progression; no Foundry bytes, UUIDs, execution
flow, choice data, descriptions, or actor mutation are adopted.

## Prerequisite evidence

- Existing accepted component owners: `dnd2024.character.content-definition` and
  `dnd2024.class-progression`.
- Existing accepted consumer: `mechanic.dnd2024.class-progression.read`, which validates source
  agreement and ordered feature IDs and produces an effect-free diagnostic result.
- Archived sources: one Fighter class entity and five Fighter feature identity entities under
  `old-dnd/catalog/world/entities/`.

## Runtime artifacts

No new runtime type or public surface is introduced. Six existing entity envelopes are relocated
under `catalog/applications/dnd2024/content/entities/character-progression/` and activated through
the registered D&D application source.

## Authoritative state and closed input

The class entity owns immutable source-backed progression. Feature entities own identity only. The
existing reader accepts exactly `{ "classLevel": 1..20 }`; callers cannot supply Hit Die, fixed HP,
feature IDs, content version, status, source, or feature implementation state.

## Behavior, result, and typed effects

For level 1 or 2, the reader returns the exact declared feature identities in progression order and
labels behavior `unimplemented`. Level 3 remains `unsupported-level`. The reader is deterministic,
read-only, and emits no effects, events, or notifications.

## Failure, replay, and rollback contract

Missing/malformed components, invalid IDs/order, or mismatched source locators remain invalid
diagnostic states under the existing mechanic. Transform drift fails before activation. There is no
write, transaction, replay key, or rollback path in this static/read-only leaf.

## Implementation sequence

1. Relocate the exact six archived envelopes.
2. Add a hash-locked transform that proves complete coverage and closed feature references.
3. Validate activation, both schemas, exact entitlement sets, and existing-reader consumption.
4. Record attribution, roadmap state, verification, and deliberate exclusions.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Transform | Six exact source hashes and exact relocated targets pass |
| Activation | All six relative paths are activated winners |
| Schema | Six content definitions and the Fighter progression are valid |
| Reference closure | Every declared feature ID resolves to one activated feature entity |
| Level 1 | Exactly Fighting Style, Second Wind, Weapon Mastery; behavior unimplemented |
| Level 2 | Exactly Action Surge and Tactical Mind; behavior unimplemented |
| Boundary | Level 3 reports unsupported and no behavior/effects are imported |
| Regression | Focused D&D suite and catalog validation pass |

## Verification commands

- `pwsh -File ruleset/dnd2024/adoption/transformation/tools/Test-FighterProgressionContentCohort.ps1`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~Activated_fighter_progression`
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `roleplay validate catalog`
- `git diff --check`

## Completion receipt and exit gate

Record results in `adoption/evidence/DND-CODE-ADOPTION-SLICE-10F-RECEIPT.md`. Final acceptance still
requires user confirmation; this leaf must not expand into executable class features or actor
advancement.
