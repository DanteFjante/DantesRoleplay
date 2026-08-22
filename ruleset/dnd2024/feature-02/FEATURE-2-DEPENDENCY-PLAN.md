# Feature 2 dependency plan — proficient character skill checks

> **D&D implementation reference:** When this work includes D&D 2024 mechanics, inspect [Foundry VTT dnd5e](https://github.com/foundryvtt/dnd5e) before designing or coding. Use it as a licensed engineering reference—not a direct dependency or rules authority—while retaining the exact `source.dnd2024.srd-5.2.1` locator and all applicable MIT, CC-BY, and asset-license notices for any reused material.

Status: **Feature 2 complete — all four slices implemented and acceptance-tested**
Last updated: 2026-08-18

Planning-method evidence:

- `procedure.system.create-feature` v4 is active from the repository bootstrap contract;
- its live MCP dry run passed all seven checks, operation `cf8cdefe001a413b906b89163ecf9052`;
- its live MCP query-back operation is `f35cf8d85ce949d0ae03f1e0948f7ccd`;
- the full repository suite passes: 213/213 tests.

Runtime storage rule: D&D component contracts, definitions, entities, and mechanics are authored
and versioned only in the live database through MCP. This repository plan records dependency
decisions, status, verification results, and operation IDs; it does not duplicate runtime payloads
or become an alternative source of truth for game data.

Slice 1 evidence:

- contract dry run: `2054e69b69bb42bd9c1b96f8d2771263`; contract
  `procedure.mechanic.dnd2024.source-registry` v1 commit:
  `c56d8c8971b5420da7b925464f8f183f`;
- component `dnd2024.source` commit: `30bf9d9bad5a430ea433063b8d28d77b`;
- atomic source-entity dry run: `182240e4e1314b7f99e2adf885e95cb3`; commit:
  `f0e751b07137432aae67dc76adacd0e6`;
- query by entity: `17a26c1cf396459f970d85504601a946`;
- query by component: `f2efd833d02b4fa4945473c84c491ef4`;
- duplicate dry-run rejection: `5f37dc82f3b24a1580fee39cfe0d625c`;
- unchanged-state query after rejection: `d659e4924c484e51b58135a4267d1a32`;
- all 16 deterministic metadata, shape, duplicate, and unchanged-state assertions passed.

Slice 2 evidence:

- contract dry run: `5e2cf3ee6b464715bacd576b30383af3`; contract
  `procedure.mechanic.dnd2024.character-level` v1 commit:
  `b0883a5273454b6b9049a1d01bb5e195`;
- component `dnd2024.character-level` commit: `26cfe62fb39b42f9a2a94ebe6668d600`;
- recording-mechanic dry run: `afd38ddfa49e454d9f1b208c59741470`; mechanic
  `mechanic.dnd2024.character-level.record` v1 commit:
  `b67949b93742422fac23fb3b92ff7bee`;
- all ten boundary actions passed: level 1 `fc25ae1e298342eeb4b516b16f68c17f`,
  4 `ac8973787e3144bebde5e42f0d79eea1`, 5 `d0aeba5034804974a4abba7fd3f4640f`,
  8 `25f54994a3f745b1a86dd7dfa9f2ef9e`, 9 `dfba35b00b6c4a9f995aca8c4da947f1`,
  12 `8d365cb51c624f1ca243e8d71f9150fe`, 13 `59da98e61ca34dc4910b1faa21edd702`,
  16 `bf137ca8683f419abe9611f83beeead7`, 17 `880bb96189264ed0b62894c9d25b822a`,
  and 20 `c3e0a498a1324c4a9f24e16caf170d56`;
- all eight invalid cases were rejected without changing state: 0
  `8a7a07c3ef404a1a934ae7584ccd286f`, 21 `b3b592d5d08242ec98f04dcee2e59eb5`,
  fraction `6c6710d36cde4b54b260e9c0cee3325c`, string
  `e74a631756df4aeb80442723b028560b`, null `7fe9d8b59f4541d1845a2d8f834fb146`,
  missing level `18921783bfd0416ba6d872317c384d9e`, caller-supplied bonus
  `6d2d0e781e644aa7af513887e9bfe89c`, and caller-supplied source
  `03ff0a9254f54bea96ea51be434c5b8a`;
- final level-5 action: `626ddb73dc4a446a94c68fae5a3c8941`; entity query-back:
  `acc4395b9d6c4f6e8e213c62614eefcd`. Stored data has exactly `level` and
  `sourceRef`; the action returned Proficiency Bonus +3 without storing it.

Slice 3 evidence:

- contract dry run: `e521b4e514ff45fbbee6d874992bfee7`; contract
  `procedure.mechanic.dnd2024.skill-proficiencies` v1 commit:
  `bcc44ddac7404aefaca25430e54ebe54`;
- component `dnd2024.skill-proficiencies` commit: `28585cc8fea14c1c90167f628ef4b3e8`;
- the first mechanic dry run (`96c368083e3f41f0bbdaf6e125c2709f`) exposed intent overlap
  with the character-level recorder. After replacing generic write verbs with trained-skill
  phrases, all checks passed (`68e74d7664994a7abd9e95c674bbdf66`) and
  `mechanic.dnd2024.skill-proficiencies.record` v1 committed as
  `5071af1fc1aa41db9a447e64705845e3`;
- reverse-order full-vocabulary action `43da2aa4d65e4ee2a5f6da72bb7679db` and query
  `05bcde44b00644ba8ddde46a3f099e71` stored all 18 IDs in canonical order and returned every
  expected advisory default ability;
- explicit empty state passed (`096ee3398bf643afbd42311d92282e41`), as did the multi-skill
  canonicalization check (`114bc5b49f3b42d9ae2b7423adf8431c`); their query-backs are
  `33311992d62f413980b435fea9751e59` and `8ba3476257a44813a229eb09dc80529c`;
- all eight invalid cases were rejected with unchanged stored bytes: unknown
  `0f49fbb1ce534aae926db0e56f78f3d7`, duplicate `ded3e9cf556d489989287a4787b28b18`,
  wrong case `2fd2e16a4c6547628482e73dabf1ed44`, display name
  `9d005dcdd88842008465681c1c6f91ed`, non-array
  `e1fd8c164d7e4fb8ae15012e8bc3e2c6`, null `56f7ed79d2d548c2ad0093f763af4f61`,
  non-string member `cbce0b536bd948508e22bc54b61a120c`, and extra field
  `9401c4b4141d4d8e9db40d13ccd3c3bd`;
- unchanged-state queries in the same order are `b6f5fbf7486842c2a24a1ce6705bb539`,
  `be7b72546ed74c158a1b72295ebc7b48`, `46def8de8efb4f92becbf49544481b66`,
  `e8c9a260515441229b4431b26720a5a6`, `6d75aee9084c4b24b38e1411a9b8904d`,
  `45443346f33e440cab235647240cdfd4`, `de8ae2b407e4406ca6929b925481435f`, and
  `8a475171781541959126eb7e015b0583`;
- an identity cross-check found the pre-existing demo `orban` and D&D `creature.orban` share a
  display name. The complete matrix above was replayed on `creature.orban`; the mistaken demo
  attachment was removed after dry run `159eb173419644aea060e1a36ba38a5d` by commit
  `3a6e3a902d5345438c63e8bdaf832a3d`, and query `386e79a66305468682546e497384096d`
  proves the demo entity is back to its original `stats` component only;
- final action `a4c9582689414eaeaf5c7ecc973e16e8` and `creature.orban` query
  `1b52f99ba8bd4386bbef1be2f82809a0` prove the stored component contains exactly
  `skills: ["perception","stealth"]` and the fixed `sourceRef`. Mechanic query
  `5e696d53cb8748b7821e9320d222faf7` and scoped intent search
  `c9eb0f5740854535b0df0bc6e469a0e3` return only the v1 recording rule.

## Target capability

A levelled D&D 2024 player character can make a fixed-DC ability check involving one of the 18
SRD skills. The existing mechanic derives the ability modifier from the character's score and,
when the character is proficient in the named skill, derives and adds the character's proficiency
bonus exactly once. The result remains seeded, replayable, auditable, and effect-free.

This is deliberately narrower than "implement proficiency":

- included: character levels 1–20, the 18 skill identifiers, skill proficiency, ordinary and
  nonstandard ability/skill pairings, and the existing fixed-DC result envelope;
- excluded: monster Challenge Rating, Expertise or half proficiency, advantage/disadvantage,
  tools, saving throws, attacks, passive checks, contested checks, class/background automation,
  and granting or revoking proficiencies during advancement.

All four slices were implemented after their review gates and are complete. Further work starts
from the ruleset roadmap; this plan remains the evidence record for Feature 2.

## Source basis

The authoritative source is the official [System Reference Document 5.2.1 PDF][srd-pdf], made
available from the official [SRD page][srd-page] under [CC BY 4.0][cc-by]. Relevant sections are:

- *Playing the Game → Proficiency* (PDF pages 8–9): level/CR proficiency table, non-stacking,
  skill proficiency, and the skill list;
- *Character Creation → Character Advancement* (table headed Character Advancement): character
  levels 1–20 and proficiency bonuses +2 through +6;
- *Playing the Game → Ability Checks*: the existing fixed-DC rule and result semantics.

The source registry slice must store the attribution statement supplied by the SRD itself. Later
components refer to that registry entity plus a section locator instead of copying publisher,
license, and URL fields into every actor component.

[srd-page]: https://www.dndbeyond.com/srd
[srd-pdf]: https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf
[cc-by]: https://creativecommons.org/licenses/by/4.0/

## Recursive dependency analysis

```text
proficient character skill checks
├─ fixed-DC ability-check resolution                         [implemented]
│  ├─ seeded d20 execution and replay                        [implemented]
│  ├─ dnd2024.abilities and derived ability modifiers        [implemented]
│  └─ auditable, effect-free result envelope                 [implemented]
├─ character proficiency-bonus derivation                    [implemented: Slice 2]
│  ├─ authoritative character level 1–20                     [implemented: Slice 2]
│  │  └─ centralized SRD 5.2.1 source identity               [implemented: Slice 1]
│  └─ level-to-bonus calculation (+2 through +6)             [implemented in Slice 2,
│                                                              consumed in Slice 4]
├─ character skill-proficiency state                         [implemented: Slice 3]
│  ├─ stable vocabulary of 18 SRD skill IDs                  [implemented: Slice 3]
│  ├─ advisory default ability for each skill                [implemented: Slice 3]
│  └─ centralized SRD 5.2.1 source identity                 [implemented: Slice 1]
└─ skill-aware ability-check integration                     [implemented: Slice 4]
   ├─ all dependencies above                                 [verified]
   ├─ no caller-supplied ability or proficiency modifiers    [verified]
   ├─ proficiency bonus added at most once                   [verified]
   └─ skill intent routing only after the rule is skill-aware [verified]
```

The recursion bottomed out at the source registry. Slice 4 consumed the verified leaves and closed
the feature without introducing a second check mechanic.

### Existing dependency evidence

| Dependency | Evidence |
| --- | --- |
| D&D abilities contract | `procedure.mechanic.dnd2024.abilities` v1; live commit operation `dedcaf2b613f4ec0a9181d0e35dc04f3` |
| Ability component | `dnd2024.abilities`; live commit operation `2772eb4861194c9590d8aca70a431dad` |
| Fixed-DC check contract | `procedure.mechanic.dnd2024.check.ability` v1; live commit operation `b9a5e333c15846269a156f43005e10aa` |
| Ability-check mechanic | `mechanic.dnd2024.check.ability` v1; live commit operation `3d0df86785b640f598cd0ce0450c3803` |
| Seeded replay | Seed `8253275941846134235` reproduced `7 +3 = 10`; replay operation `dbf1600ea65b44f8893ab1bc1ec44d72` |
| Negative modifier | Charisma 8 produced −1; operation `6e6726a5b979437eb84f24855a49dda7` |
| Kernel regression suite | 213/213 tests passing after Feature 1 |

Details are in [Feature 1's runbook](../feature-01/FEATURE-1-RUNBOOK.md).

## Dependency order and stop gates

| Order | Slice | Starts only when | Exit gate |
| --- | --- | --- | --- |
| 0 ✅ | Dependency-first planning method | — | Verified: plan exists; contract v4 is active; 213/213 tests pass |
| 1 ✅ | Central SRD source registry | Slice 0 reviewed | Verified: database contract/definition/entity query back exactly; duplicate rejected without change |
| 2 ✅ | Character level and proficiency derivation contract | Slice 1 verified | Verified: all ten band boundaries pass; eight invalid inputs leave state unchanged; no bonus is stored |
| 3 ✅ | Skill vocabulary and character proficiency state | Slices 1–2 verified | Verified: all 18 IDs/default abilities, empty and multi-skill states, eight rejection cases with byte-stable state, and no stored derived data |
| 4 ✅ | Skill-aware ability-check revision | Slices 1–3 verified | Verified: proficiency delta and bands, alternate pairing, routing, malformed/missing state, replay, natural rolls, and zero effects |

Every slice is its own implementation pass. Completing a slice updates this table with contract
versions, commit operation IDs, query-back evidence, and test results, then stops for review.

## Slice 1 — centralized SRD source registry

### Artifacts

1. Contract `procedure.mechanic.dnd2024.source-registry` under
   `ruleset.dnd2024.core.governance.sources`.
2. Component definition `dnd2024.source`.
3. Permanent entity `source.dnd2024.srd-5.2.1` carrying that component.

These are database artifacts. Only their status, verification result, and operation IDs are
recorded in this plan.

### Data shape

The component is source metadata, not a copy of rules prose:

```json
{
  "system": "dnd2024",
  "document": "System Reference Document",
  "version": "5.2.1",
  "publisher": "Wizards of the Coast LLC",
  "canonicalUrl": "https://www.dndbeyond.com/srd",
  "documentUrl": "https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.pdf",
  "publishedOn": "2025-05-01",
  "license": {
    "id": "CC-BY-4.0",
    "url": "https://creativecommons.org/licenses/by/4.0/legalcode",
    "attribution": "<the exact attribution statement supplied by SRD 5.2>"
  },
  "locatorFormat": "section heading plus PDF page(s) when stable"
}
```

Later rules-bearing data uses this compact reference:

```json
{"sourceId":"source.dnd2024.srd-5.2.1","locator":"Playing the Game > Proficiency"}
```

### Invariants and failure behavior

- The source entity ID and version are immutable. A corrected release gets a new source entity;
  it does not rewrite the meaning of old audit records.
- The supplied attribution is stored verbatim. Rules prose is not copied into the registry.
- A source reference with an unknown `sourceId` fails its governing slice's validation; it never
  silently falls back to "current SRD".
- The component has one responsibility: source identity/licensing. It contains no actor state,
  dice rule, skill, level, or campaign data.

### Implementation and verification checklist

1. Retrieve the ruleset, contract-creation, world-model, and world-change procedures.
2. Search contracts, definitions, and entities for an existing registry or overlapping ID.
3. Author the dedicated contract with exact source and license requirements.
4. Dry-run the contract; commit it; query it back.
5. Declare `dnd2024.source` with a closed JSON Schema.
6. Dry-run one atomic effect batch that creates the entity and adds its source component.
7. Commit the batch and query by both entity ID and component definition.
8. Verify exact version, URLs, license ID, attribution, and absence of rules/campaign fields.
9. Verify duplicate entity creation fails and leaves existing data unchanged.
10. Record evidence in this plan, mark only Slice 1 complete, and stop. **Completed 2026-08-18.**

## Slice 2 — character level and proficiency-bonus derivation

### Artifacts

1. Contract `procedure.mechanic.dnd2024.character-level` under
   `ruleset.dnd2024.core.data.character-level`.
2. Component definition `dnd2024.character-level`.
3. Mechanic `mechanic.dnd2024.character-level.record`, the validated administrative write path.
4. Source-cited level data on the Feature 2 test character.
5. Boundary and invalid-input fixtures exercised through live actions.

### Data shape

```json
{
  "level": 5,
  "sourceRef": {
    "sourceId": "source.dnd2024.srd-5.2.1",
    "locator": "Character Creation > Character Advancement"
  }
}
```

The component never stores `proficiencyBonus`. Consumers derive it for levels 1–20 as:

```text
2 + floor((level - 1) / 4)
```

This yields +2 at levels 1–4, +3 at 5–8, +4 at 9–12, +5 at 13–16, and +6 at 17–20.

### Invariants and failure behavior

- `level` is an integer from 1 through 20. Zero, fractions, strings, and 21 fail.
- Character level is authoritative base state; a derived proficiency bonus is never persisted.
- Normal writes go through the recording mechanic because component schemas are descriptive in the
  current kernel. The mechanic accepts exactly one integer level and constructs `sourceRef`.
- Class identity, multiclass levels, hit points, advancement choices, and monster CR are outside
  this component.
- A later class/advancement feature must reference this level rather than introduce another total
  character-level field.

### Implementation and verification checklist

1. Confirm Slice 1 by querying the source entity; retrieve all governing contracts.
2. Search for existing level/progression definitions and contracts.
3. Author and dry-run the dedicated contract; commit and query it back.
4. Declare the component with a closed schema.
5. Author the recording mechanic, dry-run it, commit it, query it back, and use it for attachment.
6. Test rejected values: 0, 1.5, `"5"`, 21, null, missing level, caller-supplied bonus, and
   caller-supplied source. Query after every rejection and prove the stored bytes are unchanged.
7. Test accepted boundaries and derived results: 1→2, 4→2, 5→3, 8→3, 9→4, 12→4,
   13→5, 16→5, 17→6, 20→6.
8. Query the actor back and verify no derived proficiency bonus was stored.
9. Record evidence, mark only Slice 2 complete, and stop. **Completed 2026-08-18.**

## Slice 3 — skill vocabulary and character skill-proficiency state

### Artifacts

1. Contract `procedure.mechanic.dnd2024.skill-proficiencies` under
   `ruleset.dnd2024.core.data.skill-proficiencies`.
2. Component definition `dnd2024.skill-proficiencies`.
3. Mechanic `mechanic.dnd2024.skill-proficiencies.record`, the validated administrative write
   path.
4. Source-cited proficiency state on the Feature 2 test character.

### Stable vocabulary

The IDs are lowercase kebab case. Default abilities are advisory because the SRD says the skill
*most often* applies to that ability and leaves relevance to the GM; Slice 4 therefore requires
the caller to name the ability and permits a nondefault pairing.

| Skill ID | Default ability |
| --- | --- |
| `acrobatics` | `dex` |
| `animal-handling` | `wis` |
| `arcana` | `int` |
| `athletics` | `str` |
| `deception` | `cha` |
| `history` | `int` |
| `insight` | `wis` |
| `intimidation` | `cha` |
| `investigation` | `int` |
| `medicine` | `wis` |
| `nature` | `int` |
| `perception` | `wis` |
| `performance` | `cha` |
| `persuasion` | `cha` |
| `religion` | `int` |
| `sleight-of-hand` | `dex` |
| `stealth` | `dex` |
| `survival` | `wis` |

### Data shape

```json
{
  "skills": ["perception", "stealth"],
  "sourceRef": {
    "sourceId": "source.dnd2024.srd-5.2.1",
    "locator": "Playing the Game > Proficiency > Skill Proficiencies and Skills"
  }
}
```

### Invariants and failure behavior

- `skills` contains only the 18 stable IDs, with no duplicates. An empty list is valid and means
  the character is known to have no skill proficiencies in this scope.
- A missing component means proficiency state is unknown, not an empty list. A skill-aware check
  must report that missing state instead of assuming nonproficiency.
- The component stores no ability mapping, ability modifier, level, proficiency bonus, Expertise
  multiplier, or acquisition automation.
- Display names never become identity keys.

### Implementation and verification checklist

1. Confirm Slices 1–2 by query and retrieve all governing contracts.
2. Search for overlapping proficiency, skill, or vocabulary definitions.
3. Author and dry-run the dedicated contract; commit and query it back.
4. Declare the component with a closed schema and its 18-value enum.
5. Author the recording mechanic, resolve any intent overlap, dry-run it, commit it, query it
   back, and use it for all normal actor writes.
6. Verify all 18 IDs and default mappings against the SRD table.
7. Verify empty and multi-skill lists; reject unknown, duplicate, wrong-case, display-name,
   non-array, null, non-string-member, and extra-field values. Query after every rejection and
   prove the stored bytes are unchanged.
8. Query the actor back and verify no derived modifier or bonus is stored.
9. Record evidence, mark only Slice 3 complete, and stop. **Completed 2026-08-18.**

## Slice 4 — extend the fixed-DC ability check for skills

Implementation evidence (2026-08-18): the live contract is revision 2 (operation
`fd857d40134c4fe8b64b169d3e0c0b72`); the live mechanic is revision 3 after correcting the
database engine's direct-source execution model (operation `1fdbef15cec7434eb1bb1ce2c3ca6db6`).
Seed `202608180401` produced Stealth `9 + 3 + 3 = 15` and Acrobatics `9 + 3 = 12` for
`creature.orban`; both actions had zero effects. Acceptance actions then verified levels
4/5/16/17 at +2/+3/+5/+6, Strength (Intimidation) without remapping, raw replay, empty skill
state, rejected null/wrong-case/extra derived input, natural 20 failure, and natural 1 success.
Temporary level and skill state was restored to level 5 with `perception` and `stealth`.

Post-completion audit (2026-08-18): contract query `1d2974a5509a4868b3dbc7747a5e0215`,
mechanic query `fdf8e6ac094641778dd54409773f1c17`, and actor query
`116eb379731d48e7ac6165bcaa73b2e7` confirmed the live versions and restored state. The audit found
that two contracted negative cases had been described but not actually run. Disposable fixtures
were dry-run/created by `d04e55cf1911421d9660af90cb3b7b08` / `3327aac302d041d1b02e6c9632cfb2de`
and queried by `903b56f239034247b09512c2ed3c5a45`. Missing proficiency state failed with
`737a06512c674b7cb73ea95dee0d6cfb`; invalid level state failed with
`add69213d03f44c7b67d3eb0ffb5fc32`. Fixture deletion dry-run
`78b83c8c146e49aca8f97d73d0df2ce7` and commit `0f660680365045d9975db308d47ba3b1`
closed the gap. Final queries `2b2b7a7f33f24144b7c7df760d99660b` and
`7240a4b6ce0e441eaaa978f0cb9db47b` prove Orban is restored and both fixtures are deleted. No
mechanic revision was needed.

### Artifacts

1. Revision of `procedure.mechanic.dnd2024.check.ability`.
2. Revision of `mechanic.dnd2024.check.ability`.
3. Skill-aware intent phrases added only after the mechanic applies proficiency correctly.
4. Deterministic acceptance evidence and operation IDs recorded in this plan, without copying the
   runtime contract or mechanic payload into the repository.

This extends the existing mechanic instead of creating `mechanic.dnd2024.check.skill`. The
projection resolver supplies only declared components, but a declared component may be absent from
an entity and handled conditionally. One mechanic can therefore preserve the current plain-check
path while activating the new dependencies only when `input.skill` is present. This prevents a
second copy of the d20/DC/ability logic from drifting.

### Input and result changes

Existing input remains valid:

```json
{"ability":"dex","dc":15}
```

A skill check adds one required field:

```json
{"ability":"dex","skill":"stealth","dc":15}
```

The mechanic does not infer `ability` from the default mapping. The GM names it, allowing such
checks as Strength (Intimidation). For a skill check it validates the skill-proficiency component;
if the skill is present, it validates character level, derives the bonus, and appends exactly one
modifier entry such as:

```json
{"source":"proficiency (level 5; stealth)","value":3}
```

The result envelope retains `test: "ability-check"` and adds `skill`, `defaultAbility`,
`usedDefaultAbility`, and `proficient`. It continues to return no effects.

### Invariants and failure behavior

- Plain Feature 1 inputs produce the same calculation and envelope fields as before.
- `ability`, `skill`, and `dc` are caller decisions; the mechanic invents none of them.
- Ability modifier, level bonus, total, and outcome are derived; callers cannot provide them.
- Proficiency is determined only from `dnd2024.skill-proficiencies` and added at most once.
- Natural 20/1 do not override the total for an ability check.
- Missing/invalid abilities, skills, DCs, proficiency state, level, or source references produce
  actionable errors without rolling or changing state.
- Monster proficiency, Expertise, tools, advantage/disadvantage, and passive checks are rejected
  or remain outside the input surface; they are never approximated.

### Implementation and verification checklist

1. Confirm Slices 1–3 by query; retrieve the active ruleset, ability, level, skill, mechanic-write,
   and action-run contracts immediately before the governed changes.
2. Query the current mechanic and search all skill phrases to prove there is no overlap.
3. Revise the ability-check contract with the conditional data requirements and result fields.
4. Revise mechanic requirements to declare `dnd2024.abilities`, `dnd2024.character-level`, and
   `dnd2024.skill-proficiencies`; preserve the plain-check branch.
5. Add the closed skill map and validations, then the derived bonus modifier. Do not accept a
   precomputed bonus or make a second mechanic.
6. Dry-run both revisions and require every blocking check to pass before committing.
7. With one fixed seed and the same Dexterity score, compare proficient Stealth with
   nonproficient Acrobatics; the totals must differ by exactly the derived bonus.
8. Verify level boundaries 4→+2, 5→+3, 16→+5, and 17→+6 through the actual mechanic.
9. Verify a nondefault ability/skill pairing is accepted and reported, not silently remapped.
10. Verify an empty proficiency list makes a valid check with no proficiency modifier.
11. Verify failures for missing/unknown skill, missing proficiency state, invalid level, missing
    ability/DC, caller-supplied derived fields, and unsupported monster/Expertise inputs.
12. Verify a natural 20 can fail when the total is below DC and a natural 1 can succeed when the
    total reaches DC.
13. Verify skill intent selects the revised D&D mechanic above shared rules; verify plain ability
    intent still selects it and has unchanged arithmetic.
14. Replay at least one recorded seed exactly; assert every result has an empty effects list.
15. Query back both revisions, inspect history, record all operation IDs and the full regression
    result, mark Slice 4 complete, and stop. **Completed and post-audited 2026-08-18.**

## Plan-change rule

If a slice reveals a missing lower dependency, do not patch around it. Add it to the recursive
tree, define its own artifacts/checklist/exit gate, place it before the blocked slice, and stop.
Changing a data shape already consumed by a completed slice requires a versioned contract revision
and an explicit migration plan; editing this document alone cannot rewrite live state.

## Completion definition

Feature 2 is complete only when all four implementation slices are verified and the final live
acceptance proves:

```text
same seeded d20 + same ability score + same DC
  proficient skill total     = roll + ability modifier + derived level bonus
  nonproficient skill total  = roll + ability modifier
  difference                 = derived level bonus, added once
```

Until then, the feature remains planned or partial and must not be advertised as playable.
