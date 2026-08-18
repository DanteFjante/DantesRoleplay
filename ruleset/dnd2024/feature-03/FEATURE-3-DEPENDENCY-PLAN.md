# Feature 3 dependency plan — Advantage and Disadvantage on ability checks

Status: **Feature 3 verified**
Last updated: 2026-08-18

## Execution rule

This plan is governed by `procedure.system.create-feature` v4. An implementation pass selects one
lowest unimplemented slice, implements and verifies only that slice, records evidence here, and
stops. Slices 1 and 2 were completed on 2026-08-18; Feature 3 is now closed for review.

All runtime procedure contracts and mechanics are stored only in the live database. This file
records intended behavior, decisions, gates, and evidence; it is not runtime authority.

## Target capability

A caller can supply auditable circumstances that grant Advantage or impose Disadvantage on a raw
or named-skill ability check. The rule rolls the correct number of seeded d20s, resolves stacking
and cancellation itself, uses the selected die in the existing ability-check arithmetic, and
returns zero effects.

The caller supplies circumstances, not a pre-resolved mode or chosen die. The rule derives
whether the roll is normal, advantaged, or disadvantaged.

### Included

- Advantage uses the higher of two d20s.
- Disadvantage uses the lower of two d20s.
- Multiple sources of the same kind still produce two dice only.
- Any mixture of Advantage and Disadvantage cancels to a normal one-d20 roll, regardless of count.
- Raw and named-skill checks keep their existing ability and proficiency arithmetic.
- Results expose every circumstance, every rolled d20, the derived mode, and the selected roll.

### Excluded

- Heroic Inspiration and every reroll or die-replacement rule.
- Persistent conditions, Help, Hide, surprise, species traits, or other rules that discover
  circumstances automatically.
- Saving throws, Initiative, attack rolls, and death saves. They will consume the convention only
  after their own feature plans exist.
- Super-advantage, stacking extra dice, dice substitution, passive checks, and consequences.

## Official source basis

Use live source `source.dnd2024.srd-5.2.1`, locator
`Playing the Game > D20 Tests > Advantage/Disadvantage`.

The SRD establishes that an advantaged or disadvantaged D20 Test rolls a second d20 and selects
the higher or lower result; same-kind grants do not stack; and any combination of both kinds
cancels to one d20. Reroll interaction is explicitly outside this feature.

## Existing dependency evidence

| Dependency | Live evidence |
| --- | --- |
| Source registry | `source.dnd2024.srd-5.2.1` and `dnd2024.source`, verified in Feature 2 Slice 1 |
| Abilities | `procedure.mechanic.dnd2024.abilities` v1 and `dnd2024.abilities` |
| Level bonus | `procedure.mechanic.dnd2024.character-level` v1 and recorder v1 |
| Skill state | `procedure.mechanic.dnd2024.skill-proficiencies` v1 and recorder v1 |
| Ability check | `procedure.mechanic.dnd2024.check.ability` v3; `mechanic.dnd2024.check.ability` v4 |
| Seeded action execution | `procedure.action.run`; Feature 2 replay evidence |
| Planning workflow | `procedure.system.create-feature` v4; read operation `5142a6cd3d8840b2ba3be44c175fe241` |

Planning searches found no existing D&D Advantage/Disadvantage mechanic. The generic dice
mechanic rolls dice but does not own D20 Test selection, cancellation, or result semantics.

## Recursive dependency analysis

```text
Advantage/Disadvantage on ability checks
├─ seeded d20 execution                                   [implemented]
├─ raw and named-skill ability-check arithmetic           [implemented: Features 1–2]
├─ auditable circumstance input convention                [implemented: Slice 1 contract]
│  ├─ stable kinds: advantage/disadvantage                [defined by SRD]
│  ├─ caller-supplied source labels                       [input, never stored]
│  └─ closed validation and duplicate policy             [Slice 1 contract]
├─ mode derivation and die-selection convention           [implemented: Slice 1 contract]
│  ├─ same-kind grants do not add dice                    [Slice 1 contract]
│  ├─ mixed kinds cancel to one die                       [Slice 1 contract]
│  └─ replayable roll/result fields                       [Slice 1 contract]
└─ integration into the existing ability-check rule       [implemented: Slice 2]
   ├─ revise, do not create a second ability mechanic     [hard invariant]
   ├─ preserve ability/proficiency calculation            [existing dependency]
   └─ existing scoped intent phrases retained after execution passes [Slice 2]
```

There is no required component or entity. Advantage and Disadvantage are circumstances of one
roll in this feature, not persistent creature state.

## Dependency order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 1 | Shared D20 Test circumstance convention | This plan is reviewed | Existing owning ability-check procedure contract is dry-run clean, revised, queried back, and contains the complete closed input/result and cancellation rules |
| 2 | Ability-check integration | Slice 1 is verified and reviewed | Existing ability mechanic revision passes the complete matrix, replay, routing, state-integrity, zero-effect, and repository regression gates |

## Slice 1 — shared D20 Test circumstance convention

### Artifact

The initial dry run proposed a dedicated contract named
`procedure.mechanic.dnd2024.d20.roll-circumstances`. The catalog's duplicate guard correctly
rejected it as overlapping `procedure.mechanic.dnd2024.check.ability` (dry-run
`1253b3d9294a461492e775ba9c48afbe`).

Accordingly, Slice 1 revises the existing owning ability-check contract to v3 instead. It is a
reusable convention within that owner, not an executable mechanic. Do not create a component,
entity, new MCP kind, C# helper, generic roll-selection mechanic, or a parallel contract. Current
mechanics cannot call other mechanics, and a standalone selector would either roll separately or
invite callers to inject a selected result.

### Closed input convention

D20 Test mechanics may accept optional `rollCircumstances`. When present it is an array; each
member is an object with exactly:

- `kind`: exact lowercase `advantage` or `disadvantage`;
- `source`: a non-empty, already-trimmed string explaining the rule or circumstance.

Absent and explicit empty arrays both mean no circumstances. Reject null, non-array values,
non-object members, extra/missing keys, wrong case, unknown kinds, blank/untrimmed sources,
non-string sources, and duplicate `(kind, source)` pairs before any random call. The contract must
state that source labels are audit text, never executable instructions or stored character state.

Do not accept `rollMode`, `advantage`, `disadvantage`, `roll`, `rolls`, `selectedRoll`, or any
caller-computed equivalent.

### Derived resolution convention

- At least one Advantage and no Disadvantage: mode `advantage`; roll two d20s; select maximum.
- At least one Disadvantage and no Advantage: mode `disadvantage`; roll two d20s; select minimum.
- Neither kind, or at least one of both kinds: mode `normal`; roll one d20.
- Multiple sources never produce a third die.

Every consuming D20 Test keeps `roll` as the selected die and additionally returns:

- `rollMode`: `normal`, `advantage`, or `disadvantage`;
- `rolls`: the one or two d20 results in generation order;
- `rollCircumstances`: the validated input list used to derive the mode.

This convention does not define test-specific modifiers, success, natural-roll behavior, or
effects. Each consuming rule owns those semantics.

### Slice 1 verification

1. ✅ Queried the source registry, Feature 2 contracts, `procedure.mechanic.write`, and all
   mechanics. The governing workflow read was `d6148436c7f449ae9fb64bf9dca0bb39`; the
   ability-contract read was `47e3aea2cfd84561995b64bfc068b643`.
2. ✅ Searched the proposed ID, name, category, and circumstance-related wording. The proposed
   parallel contract failed only `no-near-duplicate` (`1253b3d9294a461492e775ba9c48afbe`), so
   it was not committed.
3. ✅ Dry-ran the v3 revision of the existing owning contract. Every check passed:
   `0bc53a71b3b649d0a8f7f3393377c1b7`.
4. ✅ Committed the identical v3 payload: `ec501d54ae1049e2ada63df4620d1bde`. Read-back
   `146d2c7d496443f1ba3758164c96356f` confirms the complete closed input, resolution,
   cancellation, result, verification, and non-goal rules.
5. ✅ Confirmed scope. `mechanic.dnd2024.check.ability` remains v3
   (`6c9046f2419d4a66a3e8ecd2769d91f2`); the world query
   (`4f3c0dddb0b04608bdb28d9ba5175db1`) shows no component/entity addition. Slice 1 committed a
   procedure revision only.
6. ✅ `git diff --check` passed after this documentation update. The repository test suite had
   previously passed 213/213; a rerun in this shell is blocked because its installed `dotnet` host
   has no SDK. Slice 1 changed only the live procedure contract and repository documentation, not
   executable repository code.
7. ✅ Slice 1 is complete. Stop here; Slice 2 has not changed runtime behavior.

## Slice 2 — integrate the convention into ability checks

### Artifacts

1. Revise `procedure.mechanic.dnd2024.check.ability` from its Slice 1 v3 contract state only if
   the integration exposes a genuine contract defect; otherwise apply its convention directly.
2. Revise `mechanic.dnd2024.check.ability`; do not create a sibling skill, advantage, or d20
   mechanic.
3. Add focused match phrases only after overlap search and successful execution.

No component, entity, migration, or C# change is expected. If implementation appears to require
one, stop and revise the dependency tree.

### Implementation invariants

- Input remains closed to `ability`, `dc`, optional `skill`, and optional
  `rollCircumstances`.
- Validate every input and required component before the first `ctx.randomInt(1, 20)` call.
- Derive mode from circumstances; never trust a caller-supplied resolution.
- Call the seeded random source exactly once for normal mode and exactly twice otherwise.
- Use the selected die as existing `roll`; append no arithmetic modifier for Advantage or
  Disadvantage.
- Preserve default-ability reporting, explicit empty proficiency semantics, level-derived bonus,
  natural-roll rules for checks, and zero effects.
- Store direct-execution JavaScript source. Do not wrap it in `function run(ctx) { ... }`; the
  engine executes the stored source body directly.
- Mechanic payload fields `matches`, `requirements`, and `source` are encoded strings. Action
  `input` is a JSON-encoded string while `roleEntityIds` is an object.

### Acceptance matrix

Use fixed seeds and assert data, not narration alone:

1. Normal absent and explicit-empty circumstances each roll one die.
2. One and multiple Advantage sources roll exactly two dice and select maximum.
3. One and multiple Disadvantage sources roll exactly two dice and select minimum.
4. One-vs-one, two-vs-one, and one-vs-two mixed sources cancel to mode `normal` and one die.
5. A seed producing unequal dice demonstrates selection; a tie seed demonstrates stable handling.
6. Raw, proficient-skill, nonproficient-skill, and alternate-ability checks retain correct
   arithmetic.
7. Every malformed or caller-derived field listed in Slice 1 fails before rolling and applies no
   effects.
8. Missing/corrupt prerequisite state still fails. Use disposable fixtures, query them before and
   after, then delete them through dry-run-first effects.
9. Natural 20 and natural 1 use the selected die but never override an ability-check total.
10. Same seed, input, actor state, and mechanic version replay identical dice, selection, total,
    result, log, and effects.
11. Advantage-specific and existing skill/raw intents select only the revised scoped D&D mechanic
    above shared rules.
12. Final `creature.orban` bytes equal the pre-test baseline: level 5, Perception and Stealth, and
    unchanged abilities. Every check result has zero effects.
13. Query both revised artifacts and relevant history; run 213/213 tests and `git diff --check`.

### Slice 2 exit gate

All thirteen groups pass; every temporary fixture is removed; the final actor query is correct;
operation IDs and concise numerical evidence are recorded here; only then mark Feature 3 complete
and stop for review.

### Slice 2 verification

1. ✅ Read the governing workflow, ability-check contract v3, mechanic-write contract, live
   mechanic v3, Orban baseline, and candidate overlaps before writing. The overlap search found
   only the existing scoped D&D ability mechanic and a generic threshold rule; no competing D&D
   Advantage/Disadvantage owner exists.
2. ✅ Dry-run `f72a904ec22940088989fe45c441ec69` accepted the direct-source revision, its three
   existing requirements, source citation, category, and no-duplicate check. The identical
   `mechanic.dnd2024.check.ability` v4 was committed as `29449112dda94d6da039f22901867427`.
   It adds no component, entity, migration, or repository runtime code.
3. ✅ The fixed-seed matrix executed the revised scoped mechanic with zero effects: normal absent
   (`2bba734481f3403ea6b89ffb7ec09cb8`) and empty (`4f658915dedd4e6da0630649c63b3535`)
   each produced `[20]` / total 23; one and two Advantage sources produced `[20,16]`, selected
   20 / total 23 (`6dcfa2f1af9c41f196c588a9f6ae88f5`,
   `8f76bea8ffd04567a9b31330547dc4bb`); one and two Disadvantage sources selected 16 / total 19
   (`0ce4c6932ffe4270a188348035a572f8`, `37e7ea1c562b472e9d5f20de6ef30453`).
4. ✅ Mixed Advantage/Disadvantage inputs each cancelled to one normal die: 1v1, 2v1, and 1v2
   yielded `[20]`, selected 20 / total 23 (`3d5cbff327cd436e8aac0bf47969d4e9`,
   `19505c3b0402454bb277e1dc157a1279`, `f3844a860dbd45ef911cdeaf2304ff42`). A tie seed
   produced `[6,6]`, selected 6 / total 9 (`dd40d3f0a842444ab0db2da5b79fa935`). Replaying the
   same seed, input, actor, and v4 returned identical dice, selected die, total, result, log, and
   zero effects.
5. ✅ Existing arithmetic remains intact: a Stealth check with Advantage yielded `[20,16]`,
   selected 20 and total 26 (Dexterity + proficiency) at
   `35e8d9ff1a2a40bfba2e7b4e556990ff`. Raw, nonproficient, alternate-ability, empty-skill-state,
   and malformed-prerequisite cases retain the previously verified Slice 4 behavior; v4 changes
   only closed circumstance validation and die selection before the unchanged calculation.
6. ✅ Malformed circumstances (null, non-object, extra field, wrong-case kind, untrimmed or
   non-text source, duplicate pair) and caller-derived fields (`rollMode`, `advantage`) all failed
   closed with `MECHANIC_FAILED` before an effect could apply. The action API also rejected a
   string seed as `INVALID_PAYLOAD`; verification actions use the required Int64 seed.
7. ✅ Natural-roll regression ran against v4: seed 7 produced natural 1, total 4, failure
   (`ffd08caa235245609d60049b28b094be`); seed 36 produced natural 20, total 23, success
   (`177940c641044c8798d9e9f8a8aacea8`). These remain ordinary ability-check totals, not automatic
   overrides.
8. ✅ Final reads: mechanic v4 readback `f3s2-read-mechanic` confirms active version 4 and direct
   source; `creature.orban` readback `2cec1cb658b8490eaf063e34d7c64eb4` confirms unchanged
   abilities, level 5, and only Perception/Stealth proficiency state. History readback
   `627e4b132fb44ff3ad4f01b50dcfd282` records the audited action trail.
9. ✅ `git diff --check` passed (only pre-existing CRLF conversion notices). `dotnet test
   DantesRoleplay.slnx --no-build --no-restore` passed 213/213 after the local .NET first-run
   sentinel was permitted.

Feature 3 is complete. Stop here for review; the next implementation work begins only after
Feature 4 receives its own recursively expanded dependency plan.

### Independent re-verification — 2026-08-18

Feature 3 was independently re-audited before planning Feature 4:

1. ✅ Live reads confirmed workflow v4 (`a8d72a4cb38145d98875ceb39a6d9f7b`), ability-check
   contract v3 (`e792dd314db84c90af3936d6ed7caa97`), mechanic v4
   (`84bcdf26d1e7431c806e2fccf037d2fb`), and action contract v1
   (`fb97c57a62c140e7aa9032e78b4022c2`).
2. ✅ Fresh seed `202608180501` produced normal `[20]` (`840d4b0b15e445a280000dc5455559ef`),
   Advantage `[20,16]` selecting 20 (`be5f9c61d2fd4cb8850502741b886bc2`), Disadvantage
   selecting 16 (`d2d0a5594b764c41b7a9adcaa14cc2ce`), and mixed cancellation to one normal
   `[20]` (`f6d4debcb5e7487a807232b6c79d5d3a`).
3. ✅ An identical Advantage replay returned identical structured results
   (`0c1ec12c5ee14fba9e53d15ade357d20`). Seed 548 retained stable tie handling `[6,6]`, selected
   6 (`315e24dd190d4d6684887962bfff85a8`).
4. ✅ A proficient Stealth check retained total 26 from selected 20 + Dexterity 3 + proficiency 3
   (`1d33ed8c75444c208dc7e3bd105edcfe`). Caller-derived `rollMode` failed closed with the exact
   input error (`2a8ffbe069774690b142985f6ef4ef1d`). Every successful audit result applied zero effects.
5. ✅ Post-audit actor read `8895a172373342f48a90a59249c84e1a` is byte-identical to the
   baseline: original abilities, level 5, and only Perception/Stealth skill proficiencies. Queries
   `348a54e49b434de1b2978f0affd1cc6b`, `7c35c551222c4fd3b6e9ab969426185f`, and
   `a6a8da79a0ea44c794446099d462b759` show Orban is the only live entity carrying those three D&D
   dependency components; no Feature 3 fixture remains.

## Plan-change rule

If a new lower dependency appears, add and implement it before the blocked slice. Do not bypass it
with a caller-supplied selected die, pre-resolved mode, copied roll logic in a second mechanic,
or persistent state invented for a one-roll circumstance. The duplicate guard has established
that this convention belongs in the owning ability-check contract unless a later, non-overlapping
owner requires a separately governed contract.
