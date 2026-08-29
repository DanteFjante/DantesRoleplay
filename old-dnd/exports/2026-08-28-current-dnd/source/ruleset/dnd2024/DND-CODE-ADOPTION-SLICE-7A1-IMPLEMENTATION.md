# D&D code-adoption Slice 7A1 implementation — raw ability-score fixed-DC check

Status: **accepted 2026-08-25**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [D&D code-adoption dependency tree](DND-CODE-ADOPTION-DEPENDENCY-PLAN.md), Parent 7 / 7A1
Ruleset alignment: **dnd2024-owned**
Source ID and locator: `source.dnd2024.srd-5.2.1`; `Playing the Game > The Six Abilities > Ability Scores/Ability Modifiers` (PDF pp. 5–6), `Playing the Game > D20 Tests > Ability Checks/Difficulty Class` (PDF p. 6), and the attack-only `Playing the Game > D20 Tests > Attack Rolls > Rolling 20 or 1` (PDF p. 7)
Outcome: activate one D&D application catalog source containing six authoritative ability scores and one seeded, effect-free raw fixed-DC ability check.
Exclusions: proficiency, skills, conditions, Advantage/Disadvantage, saves, Initiative, natural-1/20 overrides, content imports, state migration, and any effects, events, or notifications.
Allowed files/areas: `catalog/applications/dnd2024/**`, this implementation document, the Slice 7 design/roadmap/dependency status, and focused D&D application tests.
Stop point: a fresh state space can activate the source and resolve exactly this one raw check. Do not implement 7A2 or later behavior.

## Confirmed decisions

On 2026-08-25, the user confirmed the exact 7A1 gate by replying **Continue** to the request to
create the D&D source plus `dnd2024.abilities`, `procedure.mechanic.dnd2024.abilities`,
`procedure.mechanic.dnd2024.check.ability`, and `mechanic.dnd2024.check.ability`; adopt the
stated raw 2024 semantics; and activate it only through the reviewed application boundary. The
collision search in the [Slice 7 design](DND-CODE-ADOPTION-SLICE-7-DESIGN.md) found no active
owner for these identities.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Ability scores | A creature has six scores, each in the documented 1–30 range; its modifier is derived | application component type registry; Slice 3 review | one closed six-field `dnd2024.abilities` schema; do not store a modifier |
| Raw ability check | Roll 1d20, add the named ability modifier, and compare the total to the supplied DC | application execution sandbox and seeded RNG | caller supplies only ability ID and bounded integer DC; JavaScript derives modifier and uses `ctx.randomInt(1, 20)` |
| Natural 20 / 1 | The reviewed automatic-outcome text is for attack rolls | Slice 3 review | no special outcome branch for an ability check |
| Consequence | This check reports a resolution; it does not itself change state | generic application action/effect owner | output has empty effects/events/notifications |

## External implementation reference

The [Slice 3 source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md) examined
Foundry dnd5e at `275bed0be4ccfa15e6b3347acccb8da8784726d9`,
`module/dice/d20-roll.mjs`. Its useful reference evidence is the separation of die, modifiers, and
target; no Foundry code, assets, or runtime dependency is reused. The recovery source is the
archived Feature 1 ability component and check, narrowed to this raw seam rather than copied
wholesale.

## Prerequisite evidence

- Slice 2C selected the raw ability-score/fixed-DC seam and deferred all broader Feature 1 state:
  [receipt](adoption/evidence/DND-CODE-ADOPTION-SLICE-2C-RECEIPT.md).
- Slice 3 verified the exact SRD locators, Foundry reference review, operation view, kernel RNG,
  and effect-free parity boundary: [source review](adoption/evidence/DND-CODE-ADOPTION-SLICE-3-SOURCE-REVIEW.md).
- Slices 4–6 supplied conformance, transformed-candidate, mapping, allowlist, impact, replay, and
  rollback evidence. Their candidate records remain evidence only; 7A1 authors the reviewed
  active catalog records.

## Runtime artifacts

| Artifact | Owner and status | Purpose |
| --- | --- | --- |
| `dnd2024` / `dnd2024-core` source registration | application registry/source registry; registered and activated only in fresh-test state | maps `catalog/applications/dnd2024/**` into the normal exact source overlay |
| `dnd2024.abilities` | D&D application component schema | six canonical integer scores `str`, `dex`, `con`, `int`, `wis`, `cha` |
| `procedure.mechanic.dnd2024.abilities` | D&D application procedure | governs stored-score/derived-modifier discipline |
| `procedure.mechanic.dnd2024.check.ability` | D&D application procedure | governs the closed raw-check request and exclusions |
| `mechanic.dnd2024.check.ability` | D&D application catalog JavaScript | validates, rolls, derives, and returns one effect-free result |

No C# game rule, migration, public protocol kind, source overlay precedence change, or live database
record is introduced. Production registration remains an operator action through the existing
application/source/activation services; the fresh activation test proves the exact same path.

## Authoritative state and closed input

The `subject` role must contain exactly one `dnd2024.abilities` component. Its JSON has exactly
the six schema fields, each an integer from 1 through 30. The score is authoritative. The modifier,
die result, total, success, and narration are derived and callers may not provide them.

Input is exactly `{ "ability": "str|dex|con|int|wis|cha", "dc": <integer> }`. DC is an integer
from 0 through 2,147,483,637, keeping the derived total in the accepted numeric range. Missing,
null, extra, malformed, or wrong-case input fails before RNG is read.

## Behavior, result, and typed effects

1. Validate the role, closed input, and all six ability values before rolling.
2. Read the selected score and derive `Math.floor((score - 10) / 2)`.
3. Obtain exactly one inclusive d20 from kernel-owned `ctx.randomInt(1, 20)`.
4. Calculate `total = roll + modifier`; `succeeded = total >= dc`.
5. Return auditable data: test name, ability, DC, die, roll, modifier, total, success, and source.

The mechanic returns empty `effects`, `events`, and `notifications`; the generic application action
therefore commits only its replay/audit record and never modifies canonical component state.

## Failure, replay, and rollback contract

Malformed input, a missing role/component, corrupt ability JSON, a schema-incompatible score, or an
invalid DC fails with no effect proposal and no component change. The focused action test executes
the same exact request twice: the first succeeds with zero applied effects and the second replays
without another execution. There is no write set to roll back; generic action transaction handling
still owns the audit transaction.

## Implementation sequence

1. Author the component schema/metadata and two procedures in the application catalog.
2. Adapt the archived mechanic as a small catalog-JavaScript-only raw-check implementation.
3. Register, preview, activate, materialize, and execute that source in a disposable D&D state
   space; assert schema, deterministic output, negative input, zero effects, and replay.
4. Validate the catalog and run the full suite at acceptance; record unrelated failures without
   changing their owners.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Valid raw check | activated source materializes the mechanic; one d20 plus derived modifier determines total and success |
| Derived negative modifier | score 7 produces modifier -2, proving `Math.floor` rather than truncation |
| Natural endpoints | roll 1 and 20 remain ordinary total-versus-DC checks when reached by a seeded test vector |
| Closed state/input | extra/missing/wrong-case fields, missing component, and invalid score/DC fail before output |
| Determinism | equal state/input/seed returns byte-identical result data |
| Effect/state boundary | result has no effects/events/notifications; application action applies zero effects and leaves the component unchanged |
| Replay | identical action identity is replayed rather than rerun |
| Fresh source activation | registered `dnd2024-core` source previews and activates through the normal overlay path |

## Verification commands

Run the focused D&D test while iterating, then `roleplay validate catalog` after catalog changes and
the full solution test suite at acceptance. A protocol walk is not required: this slice adds no MCP
surface or dependency registration type.

## Completion receipt and exit gate

Write `adoption/evidence/DND-CODE-ADOPTION-SLICE-7A1-RECEIPT.md` only after the artifacts have been
read back and the stated verification is run. The user confirmed feature acceptance on 2026-08-25.
7A1 is accepted and stops here; 7A2 needs its own source/proficiency/skill decision and
implementation document.
