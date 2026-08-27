# Character creation CC2H1 implementation - automatic weapon-damage rest interruption

Status: **accepted**
Owner/roadmap: [D&D 2024 application roadmap](ROADMAP.md)
Dependency tree/leaf: [character creation dependency tree](DND2024-CHARACTER-CREATION-DEPENDENCY-PLAN.md), CC2H1 within CC2
Ruleset alignment: `dnd2024-owned`
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Rules Glossary > Long Rest* (PDF p. 185) and *Rules Glossary > Short Rest* (PDF p. 187)
Outcome: accepted positive weapon damage automatically applies the matching active-rest interruption in the damage root transaction.
Exclusions: Initiative, spellcasting, walking/physical exertion, ready-rest finish, recovery, Resourceful, non-weapon damage, and administrative HP writes.
Allowed files/areas: the existing weapon-damage apply mechanic/procedure, the rest-episode procedure, focused D&D tests, this implementation document, the dependency plan, roadmap/status, and one completion receipt.
Stop point: stop after the weapon-damage adapter is accepted; do not implement another interruption source in this leaf.
Recommended model: `gpt-5.6-terra`, reasoning `high`; use `gpt-5.6-sol` `high` for review if the transaction or cross-owner boundary changes during implementation.

## Confirmed decisions

- The user's 2026-08-27 continuation and instruction to finish CC2 confirm this bounded cross-owner dependency from the existing weapon-damage root to optional rest state.
- No new permanent ID, schema meaning, migration, public surface, optional rule, or C# rules branch is introduced.
- A caller binds only the existing attacker, weapon, and target roles. Optional rest state and target relationships are projected from the target declaration; the caller supplies no rest kind, world, policy, interruption, counter, duration, or effect.
- Positive final damage interrupts an active rest even when Temporary Hit Points absorb all of it. Zero final damage does not interrupt because the target takes no damage.
- A duration-ready episode is not changed by this adapter, matching the accepted CC2G boundary that ready episodes reject further interruption and await a separate finish owner.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Short Rest interruption | Taking damage interrupts the rest and grants no Short Rest benefit | `dnd2024.rest-episode`, `mechanic.dnd2024.rest.interrupt` | Positive weapon damage removes the active Short Rest episode and its exact world membership atomically with HP/Temporary HP changes. |
| Long Rest interruption | Taking damage interrupts the rest; resuming adds 1 hour | same | Positive weapon damage increments the active Long Rest interruption count and required duration by 60 minutes in the same root. |
| Damage actually taken | Damage is resolved after Immunity, Resistance, Vulnerability, and Temporary HP handling | `mechanic.dnd2024.weapon-damage.apply`, `mechanic.dnd2024.damage.resolve` | The adapter branches only on positive resolved damage, including damage absorbed by Temporary HP. |
| Authority | Rest progress and consequences are ruleset state, not caller input | rest component schema/procedure and damage projection | The target projection supplies optional episode/relationships and JavaScript emits typed effects. |

## External implementation reference

Foundry dnd5e commit `275bed0be4ccfa15e6b3347acccb8da8784726d9` was inspected at
`module/documents/actor/actor.mjs` and `module/applications/actor/rest/base-rest-dialog.mjs`.
Useful evidence is the separation between damage calculation/update hooks and the rest
configuration/completion workflow. It does not provide this repository's durable in-progress rest
episode adapter, so no Foundry code, data, UI, assets, or direct runtime dependency is adopted.

## Prerequisite evidence

- [CC2G receipt](evidence/DND2024-CHARACTER-CREATION-CC2G-RECEIPT.md) proves the active/ready rest shapes, exact interruption outcomes, membership relationship, replay, rollback, and no-benefit boundary.
- Existing weapon-damage tests prove mitigation order, Temporary HP precedence, atomic effects, zero-damage behavior, replay, and rollback.
- Relationship projection tests prove `includeRelationships` returns both incoming and outgoing exact edges only for an opted-in role.

## Runtime artifacts

- Revise `mechanic.dnd2024.weapon-damage.apply` in place; request optional `dnd2024.rest-episode` and target relationships and extend its result with closed interruption evidence.
- Revise `procedure.mechanic.dnd2024.weapon-damage.apply` and `procedure.mechanic.dnd2024.rest-episode` to state the adapter boundary.
- Add focused acceptance cases to `Dnd2024AbilityCheckTests`; no production C# change is allowed.

## Authoritative state and closed input

The input remains exactly `{ability, critical}`. Attacker abilities, weapon profile, target HP,
optional Temporary HP, mitigation children, optional rest episode, and exact target relationships
are projected state. When an episode is present, JavaScript validates its closed source-bound shape
and exactly one incoming `rest.world` edge from `episode.worldId` with `{}` data. No caller may
supply or override any rest value.

## Behavior, result, and typed effects

1. Resolve weapon damage and mitigation exactly as before.
2. Validate optional Temporary HP and optional rest state before emitting any effect.
3. For positive final damage and an active Short Rest, append episode removal and matching
   relationship removal after damage effects.
4. For positive final damage and an active Long Rest, append one episode set with incremented
   interruption count and 60 additional required minutes after damage effects.
5. For absent rest, ready rest, or zero final damage, emit no rest effect.
6. Return `restInterruption` as `null`, `short-stopped`, or `long-resumed` plus the resulting count
   and required minutes when applicable. Emit no event, notification, recovery, or benefit.
7. The existing weapon-damage action remains the single transaction owner; all effects commit,
   replay, or roll back together.

## Failure, replay, and rollback contract

Malformed/corrupt optional episode state, missing/duplicate/corrupt matching membership, unsafe Long
Rest duration arithmetic, invalid damage child/mitigation/HP/buffer, stale effects, injected effect
failure, and operation-ID conflicts fail without HP, Temporary HP, rest, relationship, event, or
notification change. Exact replay returns the stored result without a second interruption.

## Implementation sequence

1. Extend the existing damage projection declaration and catalog JavaScript.
2. Update both governing procedures.
3. Add positive, absent, ready, zero-damage, corrupt-scope, replay, and rollback tests.
4. Run focused tests, the complete D&D test class, fresh catalog validation, then the sequential full suite.
5. Record the receipt and mark CC2H1 accepted before activating CC2H2.

## Acceptance matrix

| Case | Expected evidence |
| --- | --- |
| Active Short Rest + positive damage | HP/buffer changes and episode/membership removal commit together; no benefit. |
| Active Long Rest + positive damage | HP/buffer changes and one 60-minute/count increment commit together. |
| Temporary HP absorbs all positive damage | Temporary HP changes and rest interruption both apply. |
| Immunity/zero final damage | No damage or rest effect. |
| No rest or ready rest | Existing damage behavior remains compatible; no rest effect. |
| Corrupt/missing/duplicate membership or episode | Evaluation fails with no effects. |
| Replay | No second HP or rest revision. |
| Injected/stale failure | Every damage/rest effect rolls back. |

## Verification commands

- Focused filtered `Dnd2024AbilityCheckTests` for weapon damage and automatic rest interruption.
- `dotnet test DantesRoleplay.Tests/DantesRoleplay.Tests.csproj --no-restore --filter FullyQualifiedName~Dnd2024AbilityCheckTests`
- `dotnet run --project src/system/cli/DantesRoleplay.Cli --no-restore -- validate catalog`
- `dotnet test DantesRoleplay.slnx --no-build --no-restore --maxcpucount:1`

No protocol walk is required because CC2H1 changes no MCP surface or dependency registration.

## Completion receipt and exit gate

Accepted by [the CC2H1 completion receipt](evidence/DND2024-CHARACTER-CREATION-CC2H1-RECEIPT.md).
Stop before the Initiative adapter.
