# Character creation CC1 implementation - ability generation and background increases

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC1
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Character Creation > Step 3: Ability
Scores* (PDF p. 21) and *Character Origins > Character Backgrounds > Parts of a Background /
Soldier* (PDF p. 83)
Outcome: Resolve a source-bound base ability assignment and legal background increases into the
canonical final six raw scores without writing state.
Exclusions: actor creation, ability recording, background selection/receipt, skills, tool, feat,
equipment, species, class, HP, AC, public-surface changes, and optional content.
Allowed files/areas: this document/tree/roadmap; D&D application character-creation components,
content, mechanic, and procedure; focused D&D tests; completion receipt.
Stop point: accepted deterministic zero-effect resolver and Soldier/Standard Array fixtures.

## Confirmed decisions

- Re-adopt the archived permanent IDs `dnd2024.character.ability-assignment-policy` and
  `dnd2024.background.ability-increase-options` rather than minting duplicates.
- Add `content.dnd2024.ability-assignment.standard-array.v1` and
  `content.dnd2024.background.soldier.v1` as immutable SRD content fixtures.
- Add `mechanic.dnd2024.character-abilities.resolve` and
  `procedure.mechanic.dnd2024.character-abilities` as the pure composition leaf.
- Support the policy schema's fixed-multiset and point-budget families, while shipping only the
  Standard Array fixture. Random generation remains a separate seeded leaf.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Standard Array | assign 15, 14, 13, 12, 10, and 8 once each | new immutable assignment policy; `dnd2024.abilities` remains state owner | resolver compares the submitted six-score multiset exactly |
| Point Cost policy | scores 8–15 use the SRD cost table and 27 points | same policy schema | reusable validation exists but no separate fixture is activated in CC1 |
| Background abilities | increase one listed ability by 2 and a different listed ability by 1, or all three by 1 | new background options declaration | resolver accepts only a pattern declared by the bound background |
| Score cap | no background increase may raise a score above 20 | resolver | over-cap input fails before output |
| Derived modifiers | calculated from final scores elsewhere | existing ability consumers | CC1 returns no modifiers or effects |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/data/item/background.mjs`,
`module/documents/advancement/ability-score-improvement.mjs`,
`module/applications/advancement/ability-score-improvement-flow.mjs`, and
`module/applications/advancement/advancement-manager.mjs`. CC1 adopts only the engineering
principles of content-bound advancements, cap enforcement, staged selection, and final bulk
application. It does not copy Foundry code/data or depend on Foundry globals.

## Prerequisite evidence

- `dnd2024.abilities` is an active closed six-integer schema and the only raw-score owner.
- Application role projections already materialize exact declared component dependencies.
- Active D&D readers derive modifiers rather than persisting them.
- The retained archive provides reviewed recovery shapes for both declaration families, but its
  C# rule validators remain excluded.

## Runtime artifacts

| Artifact | Boundary |
| --- | --- |
| `dnd2024.character.ability-assignment-policy` | immutable source-backed allocation policy; never actor state |
| `dnd2024.background.ability-increase-options` | immutable options attached to one matching active background definition |
| Standard Array entity | fixed multiset `[8,10,12,13,14,15]`, bounds 8–15 |
| Soldier entity | background identity plus eligible `str`, `dex`, `con` and both SRD patterns |
| resolver mechanic | pure role-bound validation/derivation; zero effects/events/notifications |

## Authoritative state and closed input

Roles are exactly `policy` and `background`. The policy supplies allocation rules. The background
must carry a matching active `dnd2024.character.content-definition` and ability-options component.
Input is exactly:

```json
{
  "scores": { "str": 15, "dex": 14, "con": 13, "int": 8, "wis": 10, "cha": 12 },
  "increases": { "str": 2, "con": 1 }
}
```

Callers may not supply final scores, modifiers, policy/background IDs, source references, effects,
or derived values. Role binding selects the content entities.

## Behavior, result, and typed effects

Validate both component shapes and SRD source identities, validate exact allocation, validate the
selected background pattern and eligibility, apply increases in canonical ability order, and reject
any result over 20. Return allocation family, canonical base scores, canonical increases, final
scores, bound entity IDs, and source references. Effects, events, and notifications are always
empty. Seed never affects the result.

## Failure, replay, and rollback contract

Missing/extra/wrong-type score or increase fields, unsupported allocation, malformed declaration,
source drift, mismatched content key/version/source, ineligible ability, illegal pattern, and cap
overflow throw before an output. Evaluation changes no state. The same roles/input under any seed
produce byte-identical data; ActionRunner replay therefore has zero applied effects.

## Implementation sequence

1. Add declaration schemas/metadata and source-cited content fixtures.
2. Add the pure JavaScript mechanic and governing procedure.
3. Extend the disposable D&D harness only with the new registered component types.
4. Add positive, negative, source-drift, deterministic, activation, and no-change tests.
5. Run focused tests, catalog validation, full acceptance, and write the receipt.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Standard Array + Soldier +2/+1 | final scores are `17,14,14,8,10,12`; no effects |
| Standard Array +1 each | all three Soldier-listed scores increase once |
| Canonicality | input property order and seed do not affect output bytes |
| Invalid allocation | wrong multiset, missing/extra/decimal/derived fields reject |
| Invalid increase | ineligible ability, duplicate-role pattern, +3, or undeclared pattern rejects |
| Source drift | malformed or mismatched identity/options reject |
| Activation | both components, both entities, mechanic, and procedure are in `dnd2024-core` |
| Compatibility | existing D&D tests and core-only/extension source selection remain green |

## Verification commands

- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project DantesRoleplay.Tools/DantesRoleplay.Tools.csproj -c Release --no-build -- validate catalog`
- `dotnet test DantesRoleplay.slnx -c Release --no-restore`

No protocol walk is required because CC1 adds no MCP surface or dependency registration.

## Completion receipt and exit gate

Accepted by [the CC1 completion receipt](evidence/DND2024-CHARACTER-CREATION-CC1-RECEIPT.md).
CC1 is collapsed to verified in the dependency tree. Work stopped before species/background grants
or actor creation.
