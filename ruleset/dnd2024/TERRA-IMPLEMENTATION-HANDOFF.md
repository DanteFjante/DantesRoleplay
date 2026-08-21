# Terra implementation handoff

This file is the execution checklist for continuing the D&D ruleset with a lower-cost model while
preserving the same quality bar. It supplements, and never replaces,
`procedure.system.create-feature` and the current feature plan.

For planning a future feature, first follow
`ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md`. That guide governs planning passes; this handoff
governs implementation passes. Never plan and implement a new feature in the same pass.

## Current assignment

Features 1–10 are verified. Feature 10's fresh-import, two-database replay proves the existing
check, save, Initiative, attack, and damage vertical session is deterministic. E1 events and
subscriptions is also complete through its six verified slices; it is a dependency for later
conditions and reactions, not work that remains in this assignment.

**Feature 28 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-28/FEATURE-28-SLICE-1-RECEIPT.md`. Do not begin origin grants,
backgrounds, feats, class features, item state, or character creation until that boundary is
the next lowest prerequisite is selected.

**Feature 23 is accepted.** Its evidence is in
`ruleset/dnd2024/feature-23/FEATURE-23-SLICE-11-RECEIPT.md`. It provides physical items,
containment, quantity, carrying, equipment state, currency, fixed item activities, and a bounded
inventory read model.

**Feature 27 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-27/FEATURE-27-SLICE-1-RECEIPT.md`. It records immutable Fighter levels
1–2 progression data and reads entitlement diagnostics with zero effects. Do not add actor class
membership, HP, Action Surge, Tactical Mind, rest recovery, campaign authorization, or a level-up
transaction until the named later-slice dependencies are accepted.

**Feature 37 is planned but blocked.** Its plan is
`ruleset/dnd2024/feature-37/FEATURE-37-DEPENDENCY-PLAN.md`. Do not implement D&D travel pace
until the official source is registered, a generic on-foot route-distance owner, Feature 33 rests,
and Feature 32 duration lifecycle are each confirmed. Feature 20 Slice 1 supplies base Speed.
Core world travel/time is already
the owner of routes, itineraries, movement, and the root clock.

**Feature 33 rests is planned.** Its complete dependency plan is
`ruleset/dnd2024/feature-33/FEATURE-33-DEPENDENCY-PLAN.md`. Its next and only authorised
implementation candidate is immutable, source-cited standard-rest policy data. Do not add a rest
action, actor rest state, clock copy, scheduler, interruption input, Hit-Die pool, direct HP or
resource reset, or attunement shortcut in that pass. The full lifecycle first needs a ratified
dynamic active-rest/clock-evidence seam; Long-Rest full HP recovery also needs Feature 16's
owner-approved full-recovery transition.

**Feature 34 observation is planned.** Its complete dependency plan is
`ruleset/dnd2024/feature-34/FEATURE-34-DEPENDENCY-PLAN.md`. Its next and only authorised
implementation candidate is immutable, source-cited observation-policy data. Do not add a light
map, senses, `can see` result, passive score, Hide action/state, condition write, or Surprise
Boolean in that pass. Full observation needs Feature 20 placement, Feature 21 geometry/sides, and
ratified derived-result composition into Action, condition, and Initiative owners.

**Feature 35 monsters and stat blocks is planned.** Its complete dependency plan is
`ruleset/dnd2024/feature-35/FEATURE-35-DEPENDENCY-PLAN.md`. Its next and only authorised
implementation candidate is immutable, source-cited monster identity data. Do not create a
monster actor, stat block profile, ability/HP/AC/Speed state, zero-HP policy, gear, sense, trait,
action, D20 roll, encounter member, hostile side, or XP award in that pass. Full monster play
needs the named base-state owners, Feature 17 policy handoff, Feature 24 natural-AC migration,
Features 20–21 tactical state, Feature 34 senses, and ratified staged composition.

**Feature 38 social interaction is planned.** Its complete dependency plan is
`ruleset/dnd2024/feature-38/FEATURE-38-DEPENDENCY-PLAN.md`. Its next and only authorised
implementation candidate is immutable, source-cited social-interaction policy data. Do not record
an attitude, decide GM willingness, run an Influence check, spend an Action, set a cooldown,
read/advance time, alter Charmed, or change campaign/world/quest state in that pass. Live
Influence needs confirmed GM authority, directional attitude state, a Feature-3 social-context
composition seam, action timing, and the root-clock cooldown route.

**Platform E6–E9 are now ordinary enabling features.** Their shared roadmap is
`platform/PLATFORM-ENABLING-FEATURES-ROADMAP.md`, with individual plans under `platform/e6/`
through `platform/e9/`. E6 Slices 1–2 are accepted: typed dependent composition and ordered root
proposal aggregation are available, but a consuming D&D feature still needs its own reviewed
adoption slice. Do not modify a consumer to bypass the closed, deterministic child-result handoff.
E7 may now be scheduled from its own plan; E8 has its own event-contract gate, and E9 is blocked until a human selects
the identity-provider and authorization boundary.

**Feature 36 Slice 1 experience state and eligibility is implemented.**
Its plan is `ruleset/dnd2024/feature-36/FEATURE-36-DEPENDENCY-PLAN.md`, with campaign authority
in `campaign/feature-14/CAMPAIGN-FEATURE-14-ADVANCEMENT-AUTHORIZATION-PLAN.md`. Do not store an
eligibility flag, copied XP total, or campaign policy on a character; C14 owns authorization and
CH9 owns the level-up transaction. Slice 1 records only a closed XP total and reads a derived,
effect-free exact-next-level result. C15 now provides campaign-bound active-character scope; do
not begin the campaign award bridge until C14 semantic confirmation and the CH9 consume seam are
accepted.

Before implementation, resolve the catalog/database drift currently reported by
`roleplay verify catalog`. It is unrelated live/catalog work; export or reconcile it deliberately,
then establish a clean verification baseline. Never use `--force-files` to overwrite it.

**Feature 18 concentration is planned, not assigned for implementation.** Its complete dependency
plan is `ruleset/dnd2024/feature-18/FEATURE-18-DEPENDENCY-PLAN.md`. It found two upstream
ownership gaps: a Feature 32 persistent-effect identity/ending protocol, and a confirmed platform
path for an event reaction to reuse an effect-free child with closed event-payload input. Do not
implement concentration by duplicating the Constitution-save algorithm or by accepting an
unvalidated caller-supplied effect id or DC.

**Feature 19 reactions in play is planned, not assigned for implementation.** Its complete
dependency plan is `ruleset/dnd2024/feature-19/FEATURE-19-DEPENDENCY-PLAN.md`. Opportunity
attacks need Feature 20 to own spatial position, reach, movement classification, and an atomic
pre-departure per-reactor trigger, Feature 21 obstacle geometry, and Feature 34 for its “can see”
condition. They also need a confirmed platform route for reactions to bind the event's dynamic
reactor/mover into the existing turn-budget and weapon-attack owners. Do not fake those facts with
caller-supplied coordinates, targets, or an inline attack roll.

**Feature 21 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-21/FEATURE-21-SLICE-1-RECEIPT.md`. The Feature-7-owned profile now
stores Shortbow's static 80/320-foot range; its writer, attack reader, and damage reader accept
the revised closed data without tactical range enforcement. Do not add cover to permanent Armor
Class, invent a target-side “firing into melee” penalty, or accept a caller-supplied
cover/range/sight result. Slice 2 still requires its confirmed encounter-side vocabulary.

**Feature 20 Slices 1–4 are verified.** Slice 4 accepts only closed cardinal/diagonal paths,
derives their five-foot-per-step cost, validates bounds, blocked terrain, safe occupancy, and
diagonal corner cutting, then uses E6 to pass only the derived cost to the sole Feature-12 budget
spender. Its budget change and position update are atomic; see
ruleset/dnd2024/feature-20/FEATURE-20-SLICE-4-MOVEMENT-RECEIPT.md. Slice 5 alone may add difficult
terrain and SRD pass-through exceptions; do not create another budget spender or accept a caller
cost/destination.

**Feature 22 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-22/FEATURE-22-SLICE-1-RECEIPT.md`. It resolves closed, effect-free
Strength/PB Unarmed Strike Damage evidence from existing ability, level, Armor Class, and condition
contracts. Do not make a fictional unarmed weapon profile or use a caller-supplied position, hand
count, Grapple source, Shove destination, improvised damage profile, or Attack-action history.
Full Grapple, Shove push, improvised weapon, and Light two-weapon behavior remains blocked on the
named Feature 20, 15, 25, capacity, and ledger seams.

**Feature 24 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-24/FEATURE-24-SLICE-1-RECEIPT.md`. It records source-backed mundane
armor and Shield table facts under Feature 23's immutable item-definition owner. Do not change a
creature's current Armor Class, add armor training, or create a player-facing wear/don action.
Derived AC needs a reviewed Feature-6/8 migration; heavy-armor movement and timed don/doff have
their own Feature-20 and clock lifecycle seams.

**Feature 25 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-25/FEATURE-25-SLICE-1-RECEIPT.md`. Feature 7's immutable profile now
declares Dagger, Shortbow, and Battleaxe properties, normal/Thrown ranges, structured ammunition
or Versatile facts, and mastery identity. Do not begin learned mastery state until grant semantics
are confirmed; do not consume ammunition, change an attack/damage result, assume hands, grant
mastery from proficiency, or create a temporary condition/effect.

**Feature 26 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-26/FEATURE-26-SLICE-1-RECEIPT.md`. It supplies nine immutable,
source-cited SRD species profiles attached to existing character-content identity, with declared
Humanoid, Size, base Speed, traits, and source-required choice families only. Do not begin Slice
2 until Feature 30's atomic origin-assembly seam is confirmed; no selection may alter
Size/Speed/proficiencies/HP, grant Darkvision or resistance, spend a resource, resolve a
spell/attack/save, or make Humanoid stand in for Feature 17’s player-character/monster marker.

**Feature 29 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-29/FEATURE-29-SLICE-1-RECEIPT.md`. It supplies immutable, source-cited
Potion of Healing, Boots of Elvenkind, and Amulet of Health profiles with declared downstream
interfaces only. Do not create a physical instance, attunement list, Short-Rest action, charge
balance, item activation, or magic effect. Possession is not attunement; every later item effect
must use its established rule owner.

**Feature 30 guided character creation is planned as an integration boundary.** Its complete plan
is `ruleset/dnd2024/feature-30/FEATURE-30-DEPENDENCY-PLAN.md`. The only current implementation
candidate is Character CH5 Slice 0: prove generic staged composition for a reserved actor before
any source content or root creation action. Do not create a Feature-30 actor component, draft,
duplicate create endpoint, manual character state, or player-facing UI flow.

**Feature 31 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-31/FEATURE-31-SLICE-1-RECEIPT.md`. It provides immutable source-cited
Fire Bolt, Cure Wounds, and Dancing Lights identities only. Do not create a spell list, class
profile, actor slot/preparation state, DC/attack bonus, resource spend, rest recovery, or cast
action. A spell catalog entry never implies that an actor can cast it. Feature 31 Slice 2 remains
blocked on a ratified caster source/class seam.

**Feature 32 Slice 1 is verified.** Its evidence is in
`ruleset/dnd2024/feature-32/FEATURE-32-SLICE-1-RECEIPT.md`. It declares immutable source-cited
resolution profiles only. Do not create an active effect, cast action, target/area, D20 roll,
action/slot spend, duration, concentration state, or HP/condition consequence. Feature 32 Slice
2 remains blocked on a confirmed effect lifecycle and event-composition contract.

## Read in this order

1. Query `procedure.system.create-feature` from the live database.
2. Read `ruleset/dnd2024/TERRA-FEATURE-PLANNING-GUIDE.md` when creating or revising a plan.
3. Read `ruleset/dnd2024/ROADMAP.md`.
4. Read only the current feature's complete dependency plan
   (`ruleset/dnd2024/feature-11/FEATURE-11-DEPENDENCY-PLAN.md` for the current assignment).
5. Query every live dependency and governing procedure named by the selected slice.
6. Read `STATUS.md` only for repository/kernel context; live MCP query results are authoritative
   for runtime game artifacts.

Do not use old JSON payload files under Feature 1 as runtime authority. They are historical
repository material. Current game contracts, components, entities, mechanics, and action history
live in the database.

## One-pass operating contract

At the start of a pass, identify exactly one lowest unimplemented slice. State its dependencies
and exit gate. If any dependency lacks concrete live or repository evidence, descend to it and
revise the plan before writing.

During the pass:

1. Search before creating; revise an owning artifact instead of making a parallel rule.
2. Retrieve the current live contract immediately before its governed write.
3. Dry-run procedure, mechanic, and effects writes whenever supported.
4. Commit the identical dry-run payload. A changed payload requires a new dry run.
5. Query every committed artifact back before using it.
6. Exercise mechanics through real seeded actions selected by intent.
7. Parse and compare structured result data, modifier lists, dice, effects, and selected mechanic.
8. Test invalid input, missing/corrupt state, boundaries, replay, and final state.
9. Restore test state through its normal recording mechanic. Use disposable fixtures only for
   impossible states, and delete them through validated effects.
10. Run the full repository suite and `git diff --check`.
11. Add operation IDs and objective evidence to the plan, mark only that slice complete, and stop.

## MCP payload facts that prevent repeat mistakes

- The tools are `orient`, `query`, and `commit`; do not invent another tool.
- Commit payloads are JSON strings.
- A mechanic's `matches`, `requirements`, and `source` fields are strings;
  `requirements` itself contains encoded JSON.
- Stored mechanic source is executed directly. It must be a source body ending in `return {...}`;
  wrapping it in `function run(ctx)` makes the action return nothing.
- An action's `input` is encoded JSON text; `roleEntityIds` is a role-to-entity object.
- A failed mechanic action can be the expected result of a negative test. Assert its exact reason
  and verify it changed no state.
- The current generic action error may misleadingly say the rule is broken for valid input
  rejection. Judge the mechanic's specific `why` text and state/effect evidence.

## Definition of done

A slice is done only if the live artifact exists at the intended version, its actual behavior
meets every exit-gate assertion, all temporary state is removed or restored, repository checks
pass, and the plan records reproducible evidence. Validation success alone is not implementation;
`ok: true` alone is not a behavioral assertion; plausible narration alone is not evidence.

If time or token budget ends before the gate is met, leave the slice pending and record the exact
last verified point. Never promote a partial matrix to complete.
