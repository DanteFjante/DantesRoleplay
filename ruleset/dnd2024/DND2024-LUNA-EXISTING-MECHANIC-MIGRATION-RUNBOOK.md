# Luna runbook — migrate the existing D&D 2024 mechanics to the prototype ECS

Status: **ready execution guide; not a second roadmap or implementation slice**
Ruleset alignment: `dnd2024-compatible`
Runtime rule authority: `dnd2024.source.srd-5.2.1`
Owning plan: [D&D 2024 component convergence](DND2024-COMPONENT-CONVERGENCE-DEPENDENCY-TREE.md)
Machine mapping: [canonical component crosswalk](evidence/canonical-component-crosswalk.json)

## Assignment

Use `gpt-5.6-luna` to convert existing catalog JavaScript mechanics from the current canonical
component contracts to the already-declared prototype ECS contracts. Work through the queue one
mechanic at a time, but activate each replacement component owner only after every existing consumer
of that old owner has been converted. This avoids a second state authority.

This assignment ends when the existing 67 mechanics have either been converted or classified as a
larger-model wait. It must not create new D&D mechanics, fill missing gameplay, redesign the generic
kernel, or continue into the later new-mechanics phase.

The companion web interface is owned by another task. Do not read, edit, test, or document it here.

## Fixed inventory and meaning of the counts

- `40` is the number of canonical component schemas that existed when convergence began.
- `67` is the number of active catalog JavaScript mechanics that already existed.
- `154` is the number of prototype component schemas; it is not a mechanic count.
- Five of the 40 old component owners are already migrated: identity, Experience, Heroic
  Inspiration, Hit Points, and Temporary Hit Points.
- Thirty-five old component owners remain.
- Sixty of the 67 mechanics still contain at least one literal reference to a remaining old owner.
  One mechanic can consume several owners, so component and mechanic counts must never be presented
  as one fraction.

The 67 mechanics are only the current implementation. They are not all mechanics needed for a
complete SRD 5.2.1 game.

## Non-negotiable boundaries

1. Keep every existing mechanic ID stable.
2. Preserve existing rule outcomes, narration meaning, failure behavior, replay behavior, effect
   ordering, and transaction ownership unless the crosswalk explicitly requires a replacement.
3. Use only target component keys and shapes already declared in the prototype and crosswalk. Never
   invent an ID, vocabulary entry, field, compatibility alias, or fallback payload.
4. Never dual-read or dual-write old and new component owners.
5. Never leave an old component descriptor active after all its consumers have moved. Never remove
   it before all its consumers have moved.
6. Mutable ECS state contains current game facts. Fixed SRD citations and operation history remain
   result/audit evidence, not duplicated mutable state.
7. Use exact metric measurements. Do not retain feet, pounds, cubic feet, or lossy rounded values in
   target state.
8. D&D rules remain in catalog JavaScript. Do not add D&D IDs, formulas, eligibility, or outcomes to
   C#.
9. Preserve unrelated dirty-worktree changes.
10. Do not touch the live database, the companion interface, retired-source evidence, public protocols, or MCP
    registration.
11. Do not create receipts, feature slices, handoffs, or additional planning documents. This file is
    the only execution guide for the conversion run.
12. Do not implement a missing rule merely to make a conversion pass. Mark it for the required model
    and continue with the next independent owner.

## Model routing

The public model page could not be retrieved when this guide was authored. These routes use the
bundled Codex model guidance: Luna for high-throughput mechanical work, Terra for coordinated
contract adaptation, and Sol for difficult architectural and rule-owned work.

### LUNA

Use Luna only when all of these are true:

- the source and target owners are exact, marker, near-shape, or fully specified field/unit maps;
- the target schema and every referenced vocabulary ID already exist;
- existing tests state the behavior to preserve;
- no derived authority, lifecycle redesign, transaction-root change, missing rule, or missing owner
  is involved; and
- every consumer of the component can be updated before activation.

### WAIT-TERRA

Do not attempt these during the Luna run. Terra is required when several old owners merge into one
component, one authored monolith splits across several known target owners, contribution/source
merging must be coordinated, or several mechanics must agree on one new contract. These waits belong
with the later new-mechanics work unless the user explicitly starts a Terra conversion pass.

### WAIT-SOL

Do not attempt these during the Luna run. Sol is required when final values become derived, a
lifecycle or transaction root changes, a target owner is missing, a rule/timing decision is absent,
or existing behavior cannot be preserved by a declared structural mapping. These waits belong with
the later new-mechanics work unless the user explicitly starts a Sol conversion pass.

If any LUNA task discovers one of those conditions, stop that component family without partial
activation, label it `WAIT-TERRA` or `WAIT-SOL` in the final summary, and continue elsewhere.

## One-owner / one-mechanic execution loop

For each LUNA component family:

1. Read the old descriptor/schema, target prototype schema, crosswalk entry, every JavaScript
   consumer, governing procedures, and focused tests. Do not load unrelated plans or receipts.
2. Search the entire active catalog and current test code for the exact old component key. Record
   the complete consumer list before editing.
3. Define the exact old-to-new payload transformation from the crosswalk and target schema. If any
   field, reference, absence rule, metric conversion, or merge policy is unspecified, stop the
   family and route it upward.
4. Add the target catalog descriptor/schema without changing prototype semantics.
5. Convert consumers one at a time. After each script, parse it as a JavaScript function body and
   run the smallest directly relevant tests.
6. Update that mechanic's Markdown contract, its governing procedure requirements, fixtures, and
   tests in the same edit. Do not create a second behavior owner.
7. After the last consumer moves, remove the old descriptor/schema and reject the old key. Do not
   keep aliases, dual reads, or dual writes.
8. Scan active catalog/code/tests for the exact old key. Historical planning/evidence may retain it;
   runtime artifacts may not.
9. Run `./roleplay.cmd validate catalog` and the D&D test class before starting the next owner.

After all eligible owner families, parse every changed JavaScript body, run the complete shared and
Local AI test suites once, run a scoped diff/whitespace check, and report only: converted owners,
converted mechanics, waits by model, tests, and blockers. Do not author a receipt.

## Luna conversion queue

The following five owner families are suitable for Luna because the crosswalk and prototype already
declare their target meaning. Each family must still pass the preflight loop above. Convert the
listed mechanics one at a time, then activate the owner atomically.

### L1 — ability scores

Owner map: `dnd2024.abilities` -> `dnd2024.creature.ability-scores`

Required transformation: move `str`, `dex`, `con`, `int`, `wis`, and `cha` into `scores` keyed by
the existing ability vocabulary entity IDs. Do not store modifiers or provenance.

1. `dnd2024.mechanic.check.ability`
2. `dnd2024.mechanic.initiative.roll`
3. `dnd2024.mechanic.saving-throw`
4. `dnd2024.mechanic.weapon-attack`
5. `dnd2024.mechanic.weapon-damage.roll`
6. `dnd2024.mechanic.carrying-capacity.read`
7. `dnd2024.mechanic.character.basic.create`
8. `dnd2024.mechanic.character-sheet.read`

### L2 — creature body/Size

Owner map: `dnd2024.creature-size` -> `dnd2024.creature.body`

Required transformation: replace the Size string with `sizeRef.entityId` using the existing Size
vocabulary. Preserve optional active-form/body-state fields when a consumer rewrites existing state.

1. `dnd2024.mechanic.carrying-capacity.read`
2. `dnd2024.mechanic.character.basic.create`
3. `dnd2024.mechanic.creature-size.record`

### L3 — damage responses

Owner map: `dnd2024.damage-mitigation` -> `dnd2024.creature.defenses`

Required transformation: replace resistance, vulnerability, and immunity lists with
source-qualified `damageResponses`. Use existing damage-type and response entity references. Do not
flatten conditional mitigation; use `qualifyingRuleRef` when the existing state has a qualifier.
Preserve `armorClassSource` if it is already present, but do not implement Armor Class derivation.

1. `dnd2024.mechanic.creature.defenses.write`
2. `dnd2024.mechanic.damage.resolve`

### L4 — languages

Owner map: `dnd2024.language-proficiencies` -> `dnd2024.creature.languages`

Required transformation: key each language by its existing vocabulary entity ID; explicitly record
understanding, communication, reading, and writing capabilities; preserve all unique grant sources.

1. `dnd2024.mechanic.character.basic.create`
2. `dnd2024.mechanic.language-proficiencies.record`

### L5 — movement and metric Speed

Owner map: `dnd2024.speed` -> `dnd2024.creature.movement`

Required transformation: replace the five fixed imperial fields with `speeds` keyed by existing
movement-mode entity IDs. Store exact metric `distance`, `enabled`, and all unique `sourceRefs`.
This component owns current Speed, not per-turn movement expenditure.

1. `dnd2024.mechanic.turn-budget.spend`
2. `dnd2024.mechanic.character.basic.create`
3. `dnd2024.mechanic.speed.read`
4. `dnd2024.mechanic.speed.write`

These lists contain 19 owner-specific edits across 15 unique mechanics. A mechanic appearing in
several lists is edited again only if its earlier conversion did not already include the later
owner. Do not broaden any edit into its WAIT-TERRA or WAIT-SOL dependencies.

## Existing mechanics requiring Terra later

These 23 mechanics depend on coordinated merges or decomposition across target owners. Luna must
leave them unchanged except for an independently completed L1-L5 reference conversion:

1. `dnd2024.mechanic.weapon-damage.roll`
2. `dnd2024.mechanic.weapon-proficiencies.write`
3. `dnd2024.mechanic.weapon-profile.write`
4. `dnd2024.mechanic.armor-training.read`
5. `dnd2024.mechanic.armor-training.write`
6. `dnd2024.mechanic.currency-value.read`
7. `dnd2024.mechanic.inventory.read`
8. `dnd2024.mechanic.item-burden.read`
9. `dnd2024.mechanic.item-instance.create-and-place`
10. `dnd2024.mechanic.item-instance.move`
11. `dnd2024.mechanic.item-instance.read`
12. `dnd2024.mechanic.item-instance.record`
13. `dnd2024.mechanic.item-stack.consume`
14. `dnd2024.mechanic.item-stack.create-and-place`
15. `dnd2024.mechanic.item-stack.merge`
16. `dnd2024.mechanic.item-stack.record`
17. `dnd2024.mechanic.item-stack.split`
18. `dnd2024.mechanic.item.equip`
19. `dnd2024.mechanic.item.equipment.read`
20. `dnd2024.mechanic.item.transfer`
21. `dnd2024.mechanic.tool-proficiencies.record`
22. `dnd2024.mechanic.saving-throw-proficiencies.record`
23. `dnd2024.mechanic.skill-proficiencies.record`

The associated component families are unified proficiencies, decomposed weapon/item definitions,
definition-linked item instances and quantities, equipment configuration, background/class/species/
feat profiles, origin selection, feature entitlements, and class progression. Terra may implement
them only after their complete target contract is confirmed.

## Existing mechanics requiring Sol later

These 29 mechanics depend on replacement ownership, derived values, explicit lifecycle entities,
missing target owners, or new transaction composition. Luna must leave them unchanged except for an
independently completed L1-L5 reference conversion:

1. `dnd2024.mechanic.check.ability`
2. `dnd2024.mechanic.encounter-initiative-order`
3. `dnd2024.mechanic.encounter-turn.advance`
4. `dnd2024.mechanic.encounter-turn.end`
5. `dnd2024.mechanic.encounter-turn.start`
6. `dnd2024.mechanic.initiative.roll`
7. `dnd2024.mechanic.saving-throw`
8. `dnd2024.mechanic.armor-class.write`
9. `dnd2024.mechanic.turn-budget.read`
10. `dnd2024.mechanic.turn-budget.spend`
11. `dnd2024.mechanic.turn-budget.write`
12. `dnd2024.mechanic.weapon-attack`
13. `dnd2024.mechanic.weapon-damage.apply`
14. `dnd2024.mechanic.conditions.write`
15. `dnd2024.mechanic.d20-test.state-effects`
16. `dnd2024.mechanic.character-abilities.resolve`
17. `dnd2024.mechanic.character-content-definition.record`
18. `dnd2024.mechanic.character.basic.create`
19. `dnd2024.mechanic.item-activity.use`
20. `dnd2024.mechanic.rest.begin`
21. `dnd2024.mechanic.rest.interrupt`
22. `dnd2024.mechanic.rest.progress`
23. `dnd2024.mechanic.species-selection.resolve`
24. `dnd2024.mechanic.species-skillful.resolve`
25. `dnd2024.mechanic.species-versatile-skilled.resolve`
26. `dnd2024.mechanic.character-experience.read`
27. `dnd2024.mechanic.character-level.record`
28. `dnd2024.mechanic.character-sheet.read`
29. `dnd2024.mechanic.class-progression.read`

The associated component families are derived Armor Class and character level, character creation
and content-definition decomposition, Conditions as active-effect entities, encounter rounds/turns,
counted turn budgets, activities, rest lifecycle/rest policy, and the missing numeric ability-choice
owner. Sol work waits for the new-mechanics phase unless the user explicitly authorizes a conversion
architecture pass first.

## Already converted or independent

These seven mechanics need no remaining-old-owner conversion today:

1. `dnd2024.mechanic.dice` — independent of component state.
2. `dnd2024.mechanic.healing.apply` — target Hit Points.
3. `dnd2024.mechanic.hit-points.write` — target Hit Points.
4. `dnd2024.mechanic.temporary-hit-points.write` — target Temporary Hit Points.
5. `dnd2024.mechanic.character-profile.record` — target Identity.
6. `dnd2024.mechanic.heroic-inspiration.grant` — target Identity and Heroic Inspiration.
7. `dnd2024.mechanic.character-experience.write` — target Experience.

Some converted mechanics still appear in a later-model list because they also consume another old
owner. For example, Experience read uses migrated Experience but still derives its answer from the
old stored-level owner. That mechanic is not fully converged until the later owner changes.

## Verification without receipts or subslices

The conversion run produces code, contract, and test changes only. It creates no receipt and no
additional slice document.

After every mechanic:

- parse the JavaScript as a function body;
- run directly matching tests; and
- inspect the diff for accidental behavior or unrelated-file changes.

After every component owner:

- prove every active consumer uses the target key;
- prove no active runtime artifact uses the old key;
- run `./roleplay.cmd validate catalog`; and
- run `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter
  "FullyQualifiedName~Dnd2024AbilityCheckTests"`.

After the Luna queue:

- run `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore`;
- run `dotnet test src/system/local-ai/DantesRoleplay.LocalAI.Tests/DantesRoleplay.LocalAI.Tests.csproj
  --no-restore`; and
- run `git diff --check` scoped to files changed by the conversion.

Do not claim that skipped Terra/Sol mechanics are migrated. Do not claim that conversion adds
missing gameplay.

## How many mechanics are needed for the complete game?

There is no rules-defined mechanic-file count. The SRD defines behavior and content, while this
repository decides how much behavior one reusable JavaScript mechanic owns. A data-driven engine
can resolve many spells, features, items, and monster actions through shared activity/effect
mechanics; a one-script-per-record design would require hundreds or thousands more scripts.

The current 67 cover only the already implemented foundation. They omit substantial behavior for
dying/death saves, reactions and timing windows, tactical movement and positioning, class and
subclass features, complete advancement and character origins, rest completion and resource reset,
Heroic Inspiration spending, spellcasting and concentration, monster behavior, magic items, hazards,
and other exploration/social consequences.

Until a source-complete capability inventory is authored, the honest number is a planning range,
not an exact target. With reusable data-driven activity/effect mechanics, a complete SRD 5.2.1
engine will likely require roughly **250-380 total JavaScript mechanics**, or approximately
**183-313 new mechanics beyond the current 67**. Treat that range as capacity planning only. The
completion gate must be SRD capability coverage, not reaching a script count.

“All of D&D” beyond SRD 5.2.1 has no finite repository target because additional official books,
optional rules, adventures, and campaign-specific content can continually add behavior.
