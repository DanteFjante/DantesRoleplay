# Feature 5 dependency plan — Initiative rolls and encounter ordering

Status: **Slices 0–1 and composition are verified; Slice 2 implementation exists and has initial live evidence**
Last updated: 2026-08-19

## Execution rule

This plan follows live procedure.system.create-feature v4 and the Terra planning guide. Implement
one verified lowest slice with its live contract, record its evidence, and stop. Runtime contracts,
components, entities, mechanics, and actions remain authoritative only in the live MCP database;
this repository file records planning decisions and evidence, never runtime payloads or source.

Feature 5 is deliberately split. Individual Initiative is a complete, useful D20 resolution leaf
once the action pipeline faithfully presents its closed input to a mechanic.
Persistent ordering of an arbitrary encounter is a parent that cannot be implemented faithfully
until the engine can compose individual mechanics over a validated roster. Do not replace that
dependency with caller-supplied Initiative totals, copied Dexterity scores, a fixed two-combatant
limit, or duplicate D20 logic.

## Target capability

A player or GM can resolve a creature's D&D 2024 Initiative roll as a seeded Dexterity-based D20
Test, while a later verified parent will create one stable, rules-derived order for every
participant in an encounter.

### Included

- Individual character or creature Initiative rolls using the subject's authoritative Dexterity.
- The established Advantage/Disadvantage circumstance convention, including Surprise represented
  by a supplied audited Disadvantage circumstance.
- A result envelope that identifies Initiative count, generated dice, selected die, modifiers, and
  source.
- Planning the data ownership and exact blocker for a persistent, variable-size encounter order.

### Excluded

- Encounter creation, spatial positions, sides, turns, actions, movement, reactions, round
  progression, ready/delay, and combat consequences.
- Automatic discovery of Surprise, conditions, or other circumstances; those are future state and
  rule owners.
- Monster stat blocks, monster Initiative bonuses, group-of-identical-monsters shortcuts, and
  player/GM tie decisions being inferred from names.
- A persistent Initiative count or order in Slice 1. The individual roll applies zero effects.
- Any fixed roster size, copied participant ability score, caller-provided modifier/count/roll, or
  generic D20 selector.

## Official source basis

Live source entity source.dnd2024.srd-5.2.1 identifies the official SRD 5.2.1, published
2025-05-01, with the canonical SRD URL and PDF URL. The relevant source is Playing the Game >
Combat > The Order of Combat > Initiative, PDF page 13. It establishes that each participant makes
a Dexterity check at the start of combat; surprised combatants have Disadvantage; the GM ranks
counts from high to low; the order persists between rounds; and tie decisions belong to players
and/or the GM. The individual roll also uses Playing the Game > D20 Tests >
Advantage/Disadvantage, PDF page 7.

The plan intentionally preserves the SRD tie authority. A deterministic persisted order is
deterministic after the authorized player/GM tie decision has been captured; it must not silently
invent a lexical, entity-id, or random tiebreaker.

## Verified existing dependencies

| Dependency | Current live evidence |
| --- | --- |
| Feature workflow | procedure.system.create-feature v4 read in this planning pass, operation 97e540a8bbd440f38c6fdce4c0ebcfe0 |
| Official source registry | source.dnd2024.srd-5.2.1 read at revision 1, operation 6f7544c93cf2446bb7bda98c656e348c |
| Ability state | dnd2024.abilities exists; Orban has closed canonical ability data, operation a8f885b36dd84e14a8fdc2b7891b6169 |
| D20 circumstance convention | procedure.mechanic.dnd2024.check.ability v3 and mechanic.dnd2024.check.ability v4 were read, operation 21c1fff04296433cbb7f0de7f5f22c57 |
| Save regression precedent | mechanic.dnd2024.saving-throw v2 confirms the distinct D20 Test pattern and zero-effect resolver boundary, operation 948ef9d9bfd649a99c42a29f3274b495 |
| Write/action/effect processes | mechanic write v1, action run v1, and world change v2 were read: 93be2584c92549e48965d3bb9a2a2f0a, 84b5b4a1a3c54ac89e7fead97161c9f6, 97d95d58b2e3450f83dd951827c2f10b |
| No existing Initiative owner | Initiative and encounter searches returned no mechanics: 6223cae1b14648efb882d59868f197c1 and 3f233e7377434656b3ba61062cde371b |
| Relevant history | Current audit history confirms Feature 4's verified artifacts and no Initiative action/mechanic, operation a9f62cb8859e485f823de0f96453d0ed |

Searches for turn order and combat order found only the administrative skill/save proficiency
recorders, not an Initiative or order owner. These lexical neighbors must be re-read before any
implementation match phrase is committed.

## Recursive dependency analysis

~~~text
Feature 5: Initiative rolls and persistent encounter order
├─ official Initiative rule and source registry                    [implemented]
├─ closed ability state and derived Dexterity modifier             [implemented]
├─ seeded D20 circumstances                                        [implemented]
├─ closed action-input preservation                                [verified: Slice 0]
├─ individual Initiative resolver                                  [verified: Slice 1]
└─ persistent arbitrary-roster Initiative order                    [blocked parent: Slice 2]
   ├─ validated roster participant projection                      [missing external leaf]
   ├─ invoke individual Initiative once per participant            [missing external leaf]
   ├─ retain only the resolved order for later rounds              [blocked]
   └─ player/GM-authorized tie decision capture                    [blocked]

External leaf: safe mechanic composition
└─ child-mechanic execution with declared child projections,
   unapplied effects, depth/cycle limits, and auditable provenance [missing system capability]

~~~

The external leaves are not disguised as game data. The current mechanic sandbox can read only
statically declared roles and cannot read the database or invoke a child mechanic. This was
confirmed from the live mechanic-write contract and the kernel's sandbox documentation. A roster
component cannot solve that limitation: copying participant Dexterity or Initiative totals into it
would create a second source of truth, while accepting those values in input would bypass the
Initiative resolver. Architecture section 9.7 already identifies ctx.mechanics.run as the needed
engine primitive, but it is not implemented and is explicitly outside the current MVP.

Slice 0 has corrected that input boundary: valid JSON-object text reaches `ctx.input` unchanged,
while null/non-object/malformed JSON is rejected before a mechanic is selected. This remains a
shared kernel invariant, not a JavaScript work-around.

## Dependency and ownership decisions

1. Dexterity score remains authoritative in dnd2024.abilities. Initiative modifier is always
   derived as floor((Dexterity - 10) / 2), never stored or accepted from a caller.
2. Roll circumstances are one-resolution input. Slice 1 uses Feature 3's closed
   rollCircumstances shape; Surprise is represented by a Disadvantage entry whose source is
   surprised. There is no persistent surprise component in this feature.
3. An individual Initiative count is a transient roll result in Slice 1, not a character
   component. Its zero-effect result stays in the action audit.
4. A later encounter order is an authoritative temporal snapshot because the SRD keeps it for
   the encounter's rounds. It must be owned once by a future encounter-order mechanism on an
   encounter entity, not mirrored on each participant. That owner is blocked by composition.
5. The individual resolver is distinct from ability checks: Dexterity is fixed, there is no DC,
   success/failure, skill, or proficiency, and its result is an Initiative count. It reuses the
   verified D20 circumstance semantics without creating a generic selector.
6. The SRD's tie choice is an authorized player/GM decision, not a calculated count. A future
   encounter-order mechanism must accept it only for an actual tie and persist the resulting order
   once. No arbitrary deterministic tiebreaker is authorized.
7. Individual Initiative uses no new component, entity, effect type, migration, C# game helper,
   or repository runtime file. Feature 5 must not revise Feature 3 or Feature 4 unless a live
   dependency read proves an actual defect.

## Slice order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 | Faithful closed action-input transport | This system plan and the user-approved continuation | **Verified 2026-08-19** — invalid roots reject before a mechanic and valid objects are preserved; full regression passed |
| 1 | Individual Initiative roll | Slice 0 verified | **Verified 2026-08-19** — live v3 passes routing, deterministic D20, input/state rejection, zero-effect, boundary, natural-roll, and cleanup evidence |
| 2 | Persistent arbitrary-roster encounter Initiative order | Slice 1 and the now-verified safe composition capability | One order owner creates the complete stable encounter snapshot, honors authorized ties, and passes multi-participant/replay/cleanup tests |

## Slice 0 — faithful closed action-input transport

### Scope and authority

This is a kernel reliability slice, not a D&D mechanic. It implements the lowest Feature 5
dependency under live procedure.system.modify v2 (read as operation
9482ac9146b34d789cb06e96094f2fe3) and procedure.action.run v1 (read as operation
83b54af876784de79eecb4bc6f8a4eb9). It changes only the shared action/projection transport and
its tests; it creates no game runtime artifacts and must not reactivate the draft Initiative
mechanic.

The deliberate compatibility decision is to preserve the established omitted-input default: an
omitted ActionRequest input is already the literal object `{}`. This slice deliberately stops
silently treating an explicitly empty, non-object, or malformed input value as that default.
That is an error-contract correction: a caller that supplied invalid input now receives an
actionable failure instead of an unrelated rule running with altered arguments.

### Contract

1. An action input is exactly one valid JSON object. The default for an omitted input is `{}`.
2. A supplied JSON object is projected byte-for-byte as the input string given to the mechanic;
   the sandbox therefore receives its equivalent object as `ctx.input`.
3. `null`, arrays, strings, numbers, booleans, whitespace-only strings, and malformed JSON are
   rejected. They are never converted to `{}`, never executed, and never allowed to consume
   mechanic randomness or propose effects.
4. `commit(kind: "action")` rejects such input before mechanic selection with code
   `INVALID_INPUT` and a repair instruction that supplies `{}` or an object. A direct projection
   request likewise returns an `INVALID_INPUT:` problem and no projection.
5. Existing role/entity projection failures remain `PROJECTION_FAILED`; their recovery text is
   not repurposed for caller input errors.

### Implementation sequence

1. Establish the single action-input validator in the existing action abstraction, with a
   valid-object predicate and a stable descriptive problem. Both ActionRunner and
   ProjectionResolver use it; neither owns a second normalising policy.
2. Make ActionRunner validate the root JSON kind before selection. Preserve valid object input
   unchanged and retain the current omitted-input `{}` default.
3. Make ProjectionResolver enforce the same rule for direct callers before it materialises roles
   or returns a projection. Preserve accumulation of independent role errors where practical.
4. Revise the live action-run procedure only if the code proves its current free-form-input
   wording incomplete; dry-run, inspect, commit the identical procedure revision, and query it
   back. The live database remains authoritative for that operating contract.
5. Replace the old normalisation test with object-preservation and a parameterised invalid-root
   matrix. Add an ActionRunner test proving invalid root input is rejected before selection and a
   valid empty object still runs normally.
6. Run focused and full tests, inspect the live action contract readback if revised, run
   `git diff --check`, capture operations/results, mark only Slice 0 verified, and stop.

### Acceptance matrix and exit gate

1. Direct resolution preserves `{"cost":4}` exactly and preserves the supplied seed.
2. Direct resolution rejects whitespace-only input, malformed JSON, `null`, `[]`, a quoted
   string, a number, and a boolean with no projection and an `INVALID_INPUT:` problem.
3. An action with each invalid root returns `INVALID_INPUT`, has no candidates/mechanic/
   projection/effects, and no mechanic source can execute; no world state changes.
4. An omitted input and an explicit `{}` retain the existing successful empty-object behavior.
5. Missing role, unknown role, and unknown entity behavior remain covered as
   `PROJECTION_FAILED`, not `INVALID_INPUT`.
6. The focused tests and `dotnet test DantesRoleplay.slnx --no-restore` pass at their complete
   current count; `git diff --check` is clean.

The exit gate is every acceptance group passing with recorded operations/test output and, if the
procedure changes, a committed queried-back live revision. Then update this document and stop.
Only a fresh authorized Slice 1 pass may reactivate and re-test Initiative.

### Slice 0 verification evidence — 2026-08-19

- One shared `ActionInput` validator now enforces a non-empty JSON-object root in both
  ActionRunner and ProjectionResolver. Valid object text remains unchanged in the projection;
  omitted input retains the existing `{}` default.
- The focused ActionRunner/ProjectionResolver regression set passed **32/32**. It covers object
  preservation, whitespace, malformed JSON, `null`, array, string, number, and boolean roots,
  selection prevention, and omitted/explicit-empty behavior.
- A live `commit(kind: "action")` with `input: "[]"` failed with `INVALID_INPUT` before mechanic
  selection (operation `4d33cacf908c46ab81a5c597ab848bbd`). No Initiative rule was reactivated.
- The bootstrap-backed authoritative action contract was re-read as
  `procedure.action.run` v4, including the closed-input rule (operation
  `2dd71a97dba844539e7cfbc1e7a0b652`). The earlier v2 write was superseded automatically by the
  bootstrap seeder; the bootstrap source was corrected, then its v4 readback was verified.
- Full repository regression passed **227/227** using
  `dotnet test DantesRoleplay.slnx --no-restore -p:SelfContained=false`. The property is required
  in this local environment because the self-contained runtime packs are not restored. Known
  dependency-vulnerability warnings were unchanged. `git diff --check` is required below.

Slice 0 is complete. Stop here: the next pass may perform only Feature 5 Slice 1's live
Initiative workflow; it must leave the composition-blocked order parent untouched.

## Slice 1 — individual seeded Initiative roll

### Status and prerequisite

Verified leaf. The previously drafted source was re-read and activated as v3 only after Slice 0
had passed. No rule logic was altered during activation.

### Runtime artifacts

- New procedure contract: procedure.mechanic.dnd2024.initiative
- New mechanic: mechanic.dnd2024.initiative.roll
- Category: ruleset.dnd2024.core.gameplay.initiative.roll
- Scope: dnd2024-srd-5.2.1
- No component, entity, effect, persistent state, or generic D20 selector.

### Governing contracts and source locator

Immediately before writing, re-read procedure.system.create-feature,
procedure.mechanic.write, procedure.action.run, procedure.mechanic.dnd2024.check.ability, and
procedure.mechanic.dnd2024.abilities. Re-read the source entity and the official Initiative and
Advantage/Disadvantage locators above. Read procedure.world.change only when creating disposable
negative-state fixtures.

### Data/input contract and required state

- Input is closed: optional rollCircumstances only. Absent and [] mean normal. No ability, DC,
  skill, proficiency, modifier, count, die, total, outcome, source, effect, participant, tie, or
  extra field is accepted.
- rollCircumstances has exactly the Feature 3 validation: an array of unique objects containing
  only kind and source; kind is advantage or disadvantage; source is nonempty and already trimmed.
  Same-kind sources do not add dice; any mixture cancels. A surprised actor uses one validated
  Disadvantage circumstance with source surprised; the mechanic does not infer Surprise.
- Required role is subject with dnd2024.abilities. Validate the complete closed six-score shape,
  integer 1 through 30 range, and selected fixed Dexterity before randomness. Missing or corrupt
  state fails closed.
- The actor may be a creature or a character; character level, proficiencies, skill state, and
  saving-throw state are neither read nor required.

### Resolution behavior

1. Validate input, all circumstances, role, and full ability state before calling randomInt.
2. Derive Dexterity modifier as floor((dex - 10) / 2).
3. Apply the Feature 3 D20 convention exactly: normal/cancelled rolls one d20; only Advantage or
   only Disadvantage rolls two and selects maximum/minimum in generation order.
4. Initiative count is selected die plus the Dexterity modifier. Natural 1 and 20 have no
   automatic Initiative result beyond that arithmetic.
5. Return no effects and do not alter actor state.

### Result and effects

Return test initiative, ability dex, dexterityModifier as an auditable modifier entry, die 1d20,
rollMode, rolls, roll, rollCircumstances, initiative, source locator, and effects []. There is no
DC, success flag, proficiency, or encounter order in Slice 1.

### Invariants, failure behavior, and non-goals

- One reusable Initiative mechanic owns all individual Initiative rolls; do not create
  creature-specific, normal/advantage/disadvantage, Surprise, or Dexterity-specific siblings.
- Validate before every random call. Reject all malformed input/state before roll or effects.
- A score modifier, raw roll, Initiative count, circumstance, encounter membership, and order must
  not be stored on the subject.
- This slice does not start combat, decide tie order, create an encounter, or advance a turn.

### Slice 1 implementation sequence

1. Re-read this plan, the workflow, Feature 4 evidence, source registry, governing contracts,
   Orban baseline, world inventory, and relevant recent history.
2. Repeat exact-id/category searches and broad searches for initiative, roll initiative, combat
   order, turn order, surprise, and proposed player phrases. Read every returned neighbor and
   revise this plan if ownership is ambiguous.
3. Create the procedure contract by dry run, inspect every check, commit the identical payload,
   and query it back.
4. Create one direct-source mechanic by dry run, inspect every blocking check, commit the
   identical payload, and query it back. Treat lexical duplicate warnings as a stop-and-analyze
   signal; do not waive an actual phrase overlap.
5. Run seeded actions using actual matching Initiative phrases and parse selected mechanic/version,
   structured data, log, effects, and audit history.
6. Run the entire matrix. Use only disposable entities for missing/corrupt ability-state fixtures;
   create and delete them through dry-run-first effects, querying both transitions.
7. Confirm Orban's exact before/after component bytes and revisions, record concise operation
   evidence, run repository checks, mark only Slice 1 verified, and stop.

### Slice 1 acceptance matrix

1. Orban with Dex 16 and a known seed returns ability dex, modifier +3, exact selected die, and
   Initiative equal to die +3; no level/proficiency modifier appears and effects is [].
2. Score boundaries Dex 1, 10, 11, and 30 on disposable fixtures derive -5, 0, 0, and +10. Do
   not modify Orban's ability component.
3. Absent and empty circumstances each make one call; one/multiple Advantage and one/multiple
   Disadvantage make two calls and select max/min; 1v1, 2v1, and 1v2 mixes make one normal call.
4. Fixed seeds demonstrate unequal Advantage/Disadvantage dice and a tie. Same seed, input,
   actor, and mechanic version reproduce data, narration, log, and effects exactly.
5. A natural 1 and a natural 20 prove ordinary Initiative arithmetic, not an automatic special
   result.
6. Surprise represented by the validated Disadvantage source produces the same two-die
   Disadvantage semantics; an arbitrary surprise boolean or stored condition field is rejected.
7. Reject null/non-object input, any ability/DC/skill/proficiency/modifier/count/roll/outcome/
   source/effect/tie/participant field, malformed circumstances, unknown/wrong-case kind,
   blank/untrimmed source, and exact duplicate pair. Assert no effects and exact unchanged Orban
   state after each rejection.
8. Missing abilities, extra ability keys, omitted ability key, noninteger/out-of-range Dexterity,
   and malformed component JSON fail closed before randomness on queried disposable fixtures.
9. Initiative phrases route only to the scoped Initiative mechanic; ability-check, saving-throw,
   and administrative record phrases retain their existing owners.
10. Query the final contract and mechanic at intended active version/scope plus relevant history.
    All fixtures are deleted; Orban remains exactly ability 12/16/14/10/13/8, level 5,
    Perception/Stealth, and Con/Wis saves.
11. Require dotnet test DantesRoleplay.slnx --no-build --no-restore to pass at its current full
    count and git diff --check to be clean.

### Slice 1 exit gate

Every matrix group has objective operation/effect/state evidence; both artifacts read back active
at their intended versions; no fixture remains; Orban bytes are exact; and repository checks pass.
Only then mark Slice 1 verified and stop. Do not begin composition or encounter ordering in the
same pass.

### Slice 1 prior blocking evidence — 2026-08-19

The planned live contract procedure.mechanic.dnd2024.initiative v1 was created
(d78408a942e244cc8137cd0b7a8868f7) and the resolver was tested. Its normal, D20 circumstance,
Surprise-context, replay, zero-effect, exact-Dexterity, and routing assertions passed; roll
initiative selected it above the generic threshold rule (8977a1f903104e0b8f32ff67014c4af8).

The negative matrix then proved a lower dependency: action input null and [] both ran
successfully as the same normal empty-object Initiative request, even though the source begins by
rejecting non-object input. Direct inspection of the shared ProjectionResolver shows why:
Normalise returns {} for every root JSON value other than object and for malformed JSON before the
source receives ctx.input. This makes required null/non-object rejection impossible in the
database rule layer.

The resolver was revised to v2 Draft rather than left routable
(ccd73b02eaf74e4d8779026cc38dc7ee; queried at 7c7e411d18824db680eb985462f5da53). The procedure
remains a discoverable specification, but Feature 5 Slice 1 is not verified. Orban remained
byte-identical (101a535b3d584786a603bc8ab5b9faff). Do not reactivate the mechanic or continue its
matrix until Slice 0 is planned, reviewed, implemented, and verified.

### Slice 1 verification evidence — 2026-08-19

- The existing contract was re-read active (operation `b24b0420a7e3452585c816460d005920`); the
  resolver's v3 activation dry run had all blocking checks pass (`223478a7efe34d4c918702d9469bf457`),
  and the active mechanic write completed as `67343c44cfe14a14b1bd6115c6f38115`. The only advisory
  neighbor is the generic unscoped threshold rule; the scoped Initiative phrases selected v3.
- A disposable Dex 16 fixture proved normal/empty input, Advantage, Disadvantage, cancelled
  mixtures, Surprise-as-Disadvantage, exact replay and zero effects. The fixed seed produced
  normal 11 + 3 = 14, Advantage [11,15] => 18, and Disadvantage [11,15] => 14; representative
  operations are `2a5483a89ccb4b598fa73c028fcf83c6`, `2d7ae328d6dd4a89b9ef3851480e8dde`, and
  `12879d45ebac4365b073397d536f1303`.
- Closed-input and corrupt-state failures were real actions with no effects: forbidden field
  `ab4bd877b4b24d7aa0900a646480ca5c`, malformed circumstance
  `844fae2c4599481db3126128a6f61a62`, and extra ability key
  `317f5afd90854c3985191ac03c4fcddd`.
- Disposable boundary fixtures gave Dexterity modifiers -5, 0, 0, and +10 for scores 1, 10, 11,
  and 30 (operations `a26e06898fb64f0d8cbd2451bcdc2e78`,
  `b07198ac798b4786a5719daa30678b11`, `3d8a8ac31f914c4891a4e5222365907b`, and
  `ab5366d7b65b4c7288b63326a1eda58f`). Natural 1 and 20 remained ordinary counts 4 and 23
  at seeds 35 and 36 (`5dcfe1c6a48c4c85885c4c1dc3e64557`,
  `93991e7829b142268ebfa709011480c6`).
- All six temporary fixtures were deleted through a dry-run-first cleanup
  (`1888cddeef8747dfb9dcb95df818a8e9` then `d672ea8f386743e8b5e699723706c079`); their final
  entity query correctly returned no surviving entities. The current live Orban fixture has no
  D&D ability component, so it was never modified or used as a substitute test subject.

Slice 1 is complete. The only remaining Feature 5 capability is the separately blocked
arbitrary-roster order parent.

## Slice 2 — persistent arbitrary-roster encounter Initiative order

### Status and prerequisite

Planned parent. Slice 1 and the separate system-level composition plan are verified. The parent
starts only with the declarative host orchestration documented in
`COMPOSITION-DEPENDENCY-PLAN.md`; it must not add a CLR callback or any script-visible store.

### Concrete parent contract

1. One new encounter-owned component is the sole persistent Initiative-order owner. Its state is
   an ordered list of participant identity plus derived Initiative count, together with the
   replay/source metadata needed to identify the snapshot. It stores neither ability scores,
   individual raw dice, conditions, turns, rounds, nor a duplicate roster. The ordered list is the
   authorized tie decision as well as the order used between rounds.
2. The parent mechanic has one encounter role, declares that role's contents, and declares one
   `forEachContentsOf` child invocation of the verified individual Initiative resolver. The child
   subject is `$item`; it receives exactly that participant's closed circumstance object through
   the host's per-item input selector. The parent does not read Dexterity or calculate a D20 roll.
3. The caller input is closed and contains exactly a per-participant Initiative-input map and tie
   decisions. The map must have exactly the current roster identities, and every value is passed
   unchanged to that participant's individual resolver. Tie decisions are ordered identity groups;
   they are required exactly for derived tied groups and forbidden for non-ties, missing members,
   repeated members, or additional groups. This captures player/GM authority without accepting a
   count, modifier, die, or fabricated order.
4. The parent collects the child outputs, validates their Initiative result shape and one-result-
   per-roster identity, groups by descending derived count, applies only authorized tie orders,
   then proposes one component-add effect on the encounter. Re-running against an encounter that
   already has the snapshot fails rather than silently overwriting it; correction/lifecycle is a
   later owner.
5. An empty roster, invalid/missing per-participant input, a failed child, malformed child output,
   any bad tie decision, or an existing snapshot produces no parent effect. Child effects are
   ignored rather than merged: the verified Initiative child proposes none, and this parent owns
   only the order component.

### Implementation sequence

1. Re-read the current Feature 5 plan, composition record, Initiative resolver, mechanic-write,
   world-model/change, and action-run contracts. Search exact and broad Initiative/order/encounter
   names before creating anything.
2. Define the one encounter-order component in the live database; it owns only the snapshot above.
3. Create the parent procedure and mechanic in the live database using the declared composition
   shape. Dry-run every supported write, inspect all blocking checks, then commit and read back.
4. Build disposable encounter and participant fixtures with canonical ability state. Test normal,
   per-participant Disadvantage, ties with an authorized decision, empty/malformed map, absent
   participant input, invalid tie decision, existing snapshot, exact seeded replay, and one atomic
   successful snapshot. Verify no participant receives an order/count component.
5. Delete every disposable fixture through dry-run-first effects, run focused and full repository
   tests plus `git diff --check`, record live operation evidence, and mark Feature 5 complete only
   after the full matrix passes.

### Slice 2 initial implementation evidence — 2026-08-19

- The live governing contract was dry-run then committed at v1 (dry run
  `45719013b6114d8cb06dbe338e66bdab`; commit `5922152d7c0147b6b08b1057058e036a`).
- The sole encounter snapshot component was created in the live database (`552fd86541d240a7bf0884b76d528842`),
  followed by the active parent mechanic after all dry-run checks passed
  (`0822db66d40e445e83a307c28d24bc23`; commit `f27daf2674aa44148451cbb3a466b869`).
- A disposable two-participant encounter was created through dry-run-first effects
  (`7004a960c85c4afa9fe7b967c58b6d9d`, `dbd5c87033d241adbe8adfa712e335ba`). A seeded parent action
  composed the active Initiative v3 resolver twice, produced distinct derived child seeds and
  counts 12 then 10, and applied exactly one encounter-owned snapshot
  (`579f3d3c46904067b2d02e84bcfe5a9c`). All three fixtures were deleted through dry-run-first
  cleanup (`9fdffd30d65942308d34c3d90376219a`, `3d6af4e03c36492fbf83042c0b736d02`).
- Repository regression passes 232/232 with `-p:SelfContained=false`; `git diff --check` has no
  whitespace errors (only pre-existing line-ending warnings).

This is not yet the Slice 2 exit gate. The live tie-decision, per-participant Disadvantage,
missing/extra input, invalid tie, empty roster, existing-snapshot, and exact replay matrix still
requires separate disposable-fixture evidence before Feature 5 may be marked complete.

### Intended ownership after the blocker is resolved

One future encounter-order mechanic will receive a validated encounter role and participant roster,
invoke the verified individual Initiative resolver once per roster participant under a single parent
seed, collect unapplied child outputs, request/capture the SRD-authorized tie choices, and apply
one atomic encounter-order snapshot. That snapshot is the only persistent order/count owner for
the encounter and remains constant through rounds until a later encounter lifecycle feature
changes it.

The parent must not accept participant Dexterity, modifiers, raw dice, Initiative counts, derived
ordering, or arbitrary tie-breaking as substitutes for child resolution. It must declare a
variable-cardinality participant projection truthfully rather than hard-code role slots.

### Blocker acceptance requirements

The separate composition plan must define child projection authorization, role binding, child seed
derivation/replay, deterministic execution order, unapplied-effect aggregation, depth/cycle/limit
failure, atomic parent failure, audit provenance, and how only the parent applies permitted
effects. It must demonstrate a non-D&D composition test and avoid exposing database access to
mechanics. Once that is verified, revise this Feature 5 plan with the concrete encounter
component shape, normal creation/correction path, tie-decision input, result envelope, and full
multi-participant matrix before implementing Slice 2.

## Plan-quality audit

1. Yes — one Feature 5 capability and explicit boundaries are defined above.
2. Yes — live SRD 5.2.1 and page-13 Initiative/round/tie locators are concrete.
3. Yes — Initiative, encounter, turn order, combat order, and adjacent mechanics were searched.
4. Yes — each implemented dependency has a live id/version or query evidence.
5. Yes — the ordering parent was recursively expanded to the missing composition leaf.
6. Yes — ability, transient circumstance, roll result, later snapshot, and tie choice ownership
   are explicit.
7. Yes — Slice 0 now enforces the shared closed action-input transport before a mechanic runs.
8. Yes — the verified lowest system leaf makes Slice 1 the next isolated assignment.
9. Yes — null/non-object semantics now fail explicitly rather than being rewritten as `{}`.
10. Yes — roll formula, D20 selection, no-natural-special rule, and result fields are testable.
11. Yes — the Slice 1 matrix covers boundary, negative, missing/corrupt, replay, routing, effects,
    state integrity, fixture cleanup, readback, and repository checks.
12. Yes — dry-run/query-back behavior is specified only for supported procedure/mechanic/effects
    calls; actions are real seeded runs.
13. Yes — Orban restoration and disposable-fixture deletion are explicit.
14. Yes — objective evidence and repository checks are mandatory exit conditions.
15. Yes — no runtime payload, schema, or JavaScript source appears here.
16. Yes — the blocked implementation pass stops without claiming verification.

## Plan-change rule

If a live read finds an existing Initiative owner, a valid closed-input transport,
variable-roster projection/composition facility, or an official rule requirement that changes
source/tie semantics, stop and revise this plan before writing. If a future attempt would need
copied participant statistics, caller-supplied Initiative results, implicit controller identity, a
fixed roster limit, a generic D20 mechanism, or unreviewed engine changes, descend to and plan
that dependency instead. Never reactivate Slice 1 or implement the blocked parent by bypassing
the input-transport or composition dependencies.
