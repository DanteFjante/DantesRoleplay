# D&D code-adoption Slice 3B implementation — effect-free raw ability-check wrapper

Status: **accepted 2026-08-25**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption plan, Slice 3 / 3B](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md)
Parent design: [Slice 3 design](DND-CODE-ADOPTION-SLICE-3-DESIGN.md)
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`, `Playing the Game > The Six Abilities >
Ability Modifiers` (PDF pp. 5–6) and `Playing the Game > D20 Tests > Ability Checks > Difficulty
Class` (PDF p. 6); the attack-only `Rolling 20 or 1` heading (PDF p. 7) excludes a special
ability-check branch.
Outcome: run one development-only, first-party-recovery JavaScript candidate against the accepted
Slice 3A operation view and return a validated, deterministic, effect-free result.
Exclusions: production catalog/source/component/projection/mechanic registration; public operations;
effects, events, notifications, transactions, migrations, activation; skills, proficiency, class,
level, conditions, saves, Advantage/Disadvantage, donor package/runtime, whole campaign state, and
archive changes.
Allowed files/areas: this document; development-only files under
`ruleset/dnd2024/adoption/probes/ability-check/`; one generic test file
`src/system/application-execution/tests/ApplicationAdoptionProbeTests.cs`; Slice 3 provenance/receipt
evidence; dependency-plan and roadmap status.
Stop point: stop after one wrapper passes its closed vectors and failure/isolation checks. Do not
register it in `catalog/` or add Slice 3C parity/vector work.

## Confirmed decisions

- Slice 3A is accepted and supplies the only score view: six exact ability scores under `scores`.
- The user-authorized test-only recovery seam permits disposable fixture IDs/files without permanent
  ID or public-surface confirmation.
- Result source references are fixed data fields `sourceId` and `sourceLocator`; they are test-only
  and do not establish a catalog envelope.
- The archived source is narrowed, not copied: its skill, condition, proficiency, level,
  circumstance, extra-RNG, naming, narration, and state-component paths are excluded.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Ability modifier | `floor((score - 10) / 2)` | wrapper JavaScript | derive once from the selected canonical score; do not accept/store it |
| Ability check | d20 + relevant modifier compared with DC | wrapper JavaScript | one kernel-seeded d20 draw and `total >= dc` |
| Natural 1/20 | special result text applies to attack rolls | SRD review | no natural-roll override for this ability check |
| Canonical state | accepted operation view | Slice 3A projections | wrapper can read only the serialized operation view |

## External implementation reference

Foundry dnd5e `module/dice/d20-roll.mjs` at `275bed0be4ccfa15e6b3347acccb8da8784726d9` was
reviewed as reference-only. Its separate die/modifier/target data flow supports this wrapper's
closed inputs, but no Foundry code or asset is copied. The recovery source is
`old-dnd/catalog/mechanics/ruleset/dnd2024/core/gameplay/ability-checks/fixed-dc/mechanic.dnd2024.check.ability.js`
at repository commit `5eaba06d365dcad4fdea0f863491900f87b2c4e3`, SHA-256
`DC59581728DBB536211CA052BE950A23DC62F0188BF889693708D07D0C2F8BC5`.

## Prerequisite evidence

- [Slice 3A receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-3A-RECEIPT.md) proves the exact,
  isolated operation view and reverse-impact chain.
- [Slice 3 source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md) supplies
  official SRD locators and Foundry/donor review.
- `procedure.mechanic.projection` and `JintMechanicEngine` own closed projection and seeded,
  string-only sandbox execution.

## Runtime artifacts

None. The slice may add a wrapper script, a closed wrapper-probe schema/manifest, a result schema,
and a first-party-recovery provenance row under the probe directory. They remain development-only
and are never imported into `catalog/` or a running database.

## Authoritative state and closed input

The wrapper receives exactly one frozen role named `subject`, whose only component is the serialized
Slice 3A operation view. The view must be a closed object containing `scores` and exactly
`str`, `dex`, `con`, `int`, `wis`, and `cha`, each integer 1–30. Action input is exactly
`{"ability":"str|dex|con|int|wis|cha","dc":<nonnegative integer>}`. The seed enters only through
`MechanicProjection.Seed` and is not caller data.

Callers cannot provide a score, modifier, roll, total, result, source reference, RNG, effect, event,
notification, state handle, or extra role/component.

## Behavior, result, and typed effects

Before drawing RNG, the JavaScript validates the entire view and exact input shape. It selects the
named score, derives the modifier with `Math.floor((score - 10) / 2)`, calls
`ctx.randomInt(1, 20)` exactly once, and returns a data object with:
`test`, `ability`, `score`, `dc`, `die`, `roll`, `modifiers`, `total`, `succeeded`, `sourceId`, and
`sourceLocator`. `modifiers` contains exactly one `{ source, value }` entry. Success is solely
`total >= dc`.

The output proposes empty effects, events, and notifications. No transaction opens and there is no
replay write.

## Failure, replay, and rollback contract

Malformed/extra/missing input, invalid ability/DC, malformed/extra/missing view fields, out-of-range
scores, an absent/extra role or component, invalid result schema, sandbox failure, and attempted
context mutation fail closed. Invalid input/view must fail before any RNG draw. Repeating a valid
view/input/seed must produce byte-identical data; the probe never writes ECS or registers runtime
artifacts. There is no rollback because no durable mutation occurs.

## Implementation sequence

1. Add the wrapper/result/probe/provenance artifacts with exact source and transformation evidence.
2. Extend the generic probe harness to load wrapper manifests, execute only the declared source in
   Jint, validate output, and assert zero proposals.
3. Add valid seeded vectors plus all closed-input/view negative cases, determinism, and frozen-context
   checks.
4. Run focused/static/catalog/full validation and write the 3B receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Positive | exact explained result for a declared input/view/seed |
| Boundary | score 1/30, DC 0, and seeds producing natural 1/20 use total comparison |
| Validation | malformed, extra, missing, wrong-type, and out-of-range input/view reject before a result |
| Determinism | same source/view/input/seed yields byte-identical data and one recorded seed |
| Isolation | no store/CLR/network/donor access and empty effects/events/notifications |
| Result | closed result schema accepts only the normalized object |
| Boundary | no catalog/runtime registration, persistent state, or C# rule logic |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~ApplicationAdoptionProbeTests`
- parse/validate every new probe JSON/schema and provenance row;
- `roleplay validate catalog`;
- `dotnet test DantesRoleplay.slnx` at acceptance; and
- local Markdown-link and diff checks.

The protocol walk is not required: this leaf changes no MCP or public surface.

## Completion receipt and exit gate

Write `adoption/evidence/DND-CODE-ADOPTION-SLICE-3B-RECEIPT.md` with wrapper/result/provenance/test
hashes, exact vectors, failed-input-before-RNG evidence, and deliberate exclusions. Mark 3B accepted
and 3C next. Stop before neutral vector conversion, archive/donor parity, permanent registration,
or any state/effect work.
