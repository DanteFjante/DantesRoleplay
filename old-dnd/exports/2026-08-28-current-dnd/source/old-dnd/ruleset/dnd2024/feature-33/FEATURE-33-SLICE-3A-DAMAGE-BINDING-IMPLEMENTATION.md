# Feature 33 Slice 3A implementation — authenticated damage-target binding

Status: **active**
Owner/roadmap: D&D 2024 Feature 33, `ruleset/dnd2024/ROADMAP.md`
Dependency tree/leaf: `FEATURE-33-DEPENDENCY-PLAN.md` → interruption evidence → damage target
Ruleset alignment: **dnd2024-compatible**
Source ID and locator: `source.dnd2024.srd-5.2.1`, *Playing the Game > Damage and Healing*; rest consumer source remains *Rules Glossary > Short Rest and Long Rest, PDF pages 184 and 186*.
Outcome: The existing final-damage event can bind its already-recorded target to one ordinary reaction role through accepted Platform E8 metadata.
Exclusions: No rest episode transition, subscription, initiative event, spellcasting event, exertion event, new event ID, event payload field, damage arithmetic, C# game rule, recovery, or public surface.
Allowed files/areas: `catalog/event-types/dnd2024.damage.dealt.schema.json`, Feature 33 plan/slice evidence, and focused catalog/event-routing tests.
Stop point: Stop after the existing damage event declares `targetId` as its one payload-bindable entity field, its producer remains compatible, and acceptance evidence is recorded.

## Confirmed decisions

- The accepted E8 Slice 1 metadata bridge is the only dynamic-role mechanism used: an event schema may list direct string entity payload fields in sorted `x-dantes-entity-payload-fields` metadata.
- `dnd2024.damage.dealt.payload.targetId` is the sole field for this slice. It is already a required string and the producer already lists the same target exactly once in `entityIds`.
- The change exposes existing authenticated owner evidence only. It does not author a rest-specific subscription or assert that any damage necessarily interrupts a rest until the complete rest interruption slice is ready.

## D&D 5e 2024 alignment

| Rule concern | SRD 5.2.1 meaning used | Existing owner | Implementation consequence |
| --- | --- | --- | --- |
| Damage recipient | Damage application determines the creature whose Hit Points receive final damage. | `mechanic.dnd2024.weapon-damage.apply` and `dnd2024.damage.dealt` | Bind only the recorded final `targetId`; no caller may nominate a recipient. |
| Rest interruption | Damage interrupts rests under the standard rest policy. | Feature 33 immutable policy | This slice provides evidence only; it does not apply the interruption rule. |

## External implementation reference

Foundry VTT dnd5e's `Actor5e.applyDamage` computes temporary-HP absorption and final HP updates before its damage-application hook. The relevant engineering lesson is adopted: consumers observe the accepted final target after the owner has completed its calculation, rather than receiving a caller-selected target. Foundry is not a source or runtime dependency.

## Prerequisite evidence

- `dnd2024.damage.dealt` has a closed active schema with required `targetId`, and its weapon-damage producer places exactly that ID in `entityIds`.
- Platform E8 Slice 1 accepts one declared direct string payload field as one ordinary reaction role and rejects missing/non-entity/corrupt values atomically: `platform/e8/E8-SLICE-1-RECEIPT.md`.
- Feature 33 Slice 2 is accepted but deliberately has no interruption transition: `FEATURE-33-SLICE-2-RECEIPT.md`.

## Runtime artifacts

| Artifact | Change |
| --- | --- |
| `dnd2024.damage.dealt` payload schema | Add `x-dantes-entity-payload-fields: ["targetId"]` to the existing root schema. |
| Existing producer | No code change expected; focused tests prove its payload/entity-id invariant is sufficient. |

## Authoritative state and closed input

There is no new input. The producer derives `targetId` from its existing target role after all damage/Temporary HP calculation and emits it in the schema-validated event payload and `entityIds`. A future reaction role may resolve only that event-owned field; it cannot be caller supplied, remapped to `sourceId`, or combined with fan-out.

## Behavior, result, and typed effects

The schema metadata is canonical and lexically sorted. It is removed before ordinary JSON-schema validation by the generic E8 host. The existing action result, typed effects, event payload, transaction, and audit behavior remain unchanged. A later subscription can use `{ "creature": "targetId" }` only if its reaction has exactly the E8-permitted role shape.

## Failure, replay, and rollback contract

- Missing, empty, altered, or multiply listed target evidence is rejected by existing event/schema/E8 routing validation before a reaction can run.
- A payload binding to an undeclared field, a missing entity ID, or a mismatched event entity ID aborts the root transaction under E8.
- Existing zero-damage applications still emit the same valid target event; whether zero damage interrupts a rest remains a later policy-consumer decision.
- Replays preserve the same event payload and entity identity; no new effect, random call, or mutation is introduced.

## Implementation sequence

1. Add the one metadata declaration to the existing event schema.
2. Add focused catalog evidence that the schema declares only `targetId` and that the existing producer continues to emit matching target payload/entity evidence.
3. Validate a fresh catalog, run focused regression tests and the full suite, write the receipt, update the Feature 33 dependency state, and stop.

## Acceptance matrix

| Case | Assertion |
| --- | --- |
| Valid final damage | Schema declares only sorted `targetId`; producer event lists it exactly once in `entityIds`. |
| Existing producer compatibility | Normal, buffered, and zero damage retain their closed payload/effects and validate. |
| Invalid binding | Existing E8 tests reject undeclared, absent, malformed, or entity-mismatched payload-role bindings atomically. |
| Isolation | No rest component, subscription, recovery state, Initiative state, spell state, exertion state, or C# game branch changes. |
| Repository | Focused tests, disposable catalog validation, full suite, and diff check pass. |

## Verification commands

```powershell
dotnet test DantesRoleplay.Tests --filter "FullyQualifiedName~CatalogFeature16Tests|FullyQualifiedName~CatalogFeature15Tests|FullyQualifiedName~EventRouterTests|FullyQualifiedName~SubscriptionStoreTests"
dotnet DantesRoleplay.Tools\.codex-build\roleplay.dll validate catalog
dotnet test DantesRoleplay.Tests
git diff --check
```

## Completion receipt and exit gate

Write `FEATURE-33-SLICE-3A-DAMAGE-BINDING-RECEIPT.md`, mark this document accepted, and update the Feature 33 dependency tree to show only the damage-target event bridge accepted. Stop before adding any subscription or rest interruption/resumption behavior.
