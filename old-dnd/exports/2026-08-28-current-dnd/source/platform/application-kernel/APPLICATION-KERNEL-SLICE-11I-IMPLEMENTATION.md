# Application kernel Slice 11I implementation — legacy projection-adoption classification

Status: **accepted**  
Owner/roadmap: [Platform enabling features roadmap](../PLATFORM-ENABLING-FEATURES-ROADMAP.md)  
Dependency tree/leaf: [Application-kernel I / required native or donor compatibility projections](APPLICATION-KERNEL-DEPENDENCY-PLAN.md)  
Ruleset alignment: **dnd2024-compatible legacy migration; ruleset-neutral classification**  
Source ID and locator: **not applicable** — no D&D rule, value, formula, or outcome is selected.  
Outcome: Determine from authoritative catalog contracts whether the current `dnd2024` application
requires any application-kernel structural projection definitions, and register none when every
ratified mechanic already consumes exact adopted components through the existing mechanic-
requirements projection owner.  
Exclusions: New projection IDs or definitions; translating mechanic requirements; component or
mechanic changes; derived calculations/defaults; projection caching; ECS/legacy state backfill;
action execution; aliases; public kinds; migrations; vectors; and AI orchestration.  
Allowed files/areas: Focused catalog-readiness and fresh-host `dnd2024` adoption tests; this plan,
receipt, and concise dependency/roadmap status links. Production code and authored catalog content
may change only if the closed classification exposes a generic defect; no such change is expected.  
Stop point: Stop after all 14 ratified mechanics parse through `MechanicRequirements`, every
declared component resolves to one of the 33 adopted schema sidecars, no authored structural-
projection source exists, and the fresh activated application has zero projection definitions and
zero projection persistence rows.

## Confirmed decisions

- Slice 7 owns application-kernel structural projections: immutable structural copies across exact
  ECS component fields/projection outputs. It explicitly does not replace the legacy mechanic
  projection or invent calculations.
- `procedure.mechanic.projection` owns mechanic execution visibility. Its `requirements.roles`
  declarations select exact component IDs, and `IProjectionResolver` freezes those raw components
  for JavaScript. Child declarations remain mechanic composition, not structural projections.
- Slices 11C–11F prove all 33 legacy component contracts have accepted application-qualified
  registration mappings. Slice 11H proves the exact 14 mechanics are active catalog winners.
- Repository search finds no authored application-kernel projection definition, projection source
  format, qualified projection ID, or runtime projection row for `dnd2024`. Therefore the required
  compatibility projection set is empty. Creating a donor would invent identity and semantics.
- The user's request to finish Slice 11 confirms completing this evidence-based zero-adoption leaf;
  it does not authorize state migration or a new projection ID.

## D&D 5e 2024 alignment

| Rule concern | Source meaning | Existing owner | Consequence |
| --- | --- | --- | --- |
| Mechanic visible state | Exact raw components declared per opaque role. | `procedure.mechanic.projection`, mechanic requirements | Preserve; do not translate into derived projections. |
| Structural derivation | No application definition is authored. | Application-kernel projection registry | Register zero definitions. |
| D&D behavior | Existing JavaScript only. | Catalog mechanics | No rule reading, Foundry comparison, or C# rule logic applies. |

## External implementation reference

No Foundry dnd5e review applies. This slice classifies existing repository projection ownership and
introduces no D&D behavior or data transformation.

## Prerequisite evidence

- [Slice 7 receipt](receipts/APPLICATION-KERNEL-SLICE-7-RECEIPT.md) proves the structural projection
  contract, registry, materializer, and explicit legacy-projection exclusion.
- [Slice 11F receipt](receipts/APPLICATION-KERNEL-SLICE-11F-RECEIPT.md) proves all 33 component
  contracts register without state or projection rows.
- [Slice 11H receipt](receipts/APPLICATION-KERNEL-SLICE-11H-RECEIPT.md) proves exactly 14 active
  mechanics materialize from the exact activation winners.
- `catalog/procedures/mechanics/procedure.mechanic.projection.md` is the authoritative runtime
  visibility contract and forbids store access, undeclared data, and kernel role meaning.

## Runtime artifacts

No runtime artifact or authored catalog artifact is added. Add only executable classification
evidence:

- catalog test: all 14 mechanic requirement documents parse; their complete component set maps to
  existing adopted sidecars; no structural projection definition source exists;
- fresh-host test: after registration, preview, and activation, `dnd2024` owns no projection graph
  node and the projection tables contain no `dnd2024` rows.

## Authoritative state and closed input

The ratified mechanic Markdown requirements, component schema sidecars, accepted ownership mapping,
and fresh SQLite registries are authoritative. The test derives the set; no caller/model supplies a
projection identity, mapping, output schema, component substitution, or compatibility assertion.

## Behavior, result, and typed effects

- Every mechanic requirement parses through the same `MechanicRequirements.Parse` used by runtime.
- `AllComponentIds()` yields only component IDs with exact accepted schema sidecars.
- Child mechanic declarations, inputs, relationships, and containment visibility remain under the
  existing mechanic projection/composition owner.
- Structural projection graph and storage remain empty for the application.
- Component registrations, activation fingerprints, catalog results, actions, state, effects,
  events, notifications, and audit behavior remain unchanged. Typed effects: none.

## Failure, replay, and rollback contract

A missing component sidecar, malformed requirement, unexpected authored projection artifact, or
runtime projection row fails the proof rather than being approximated. No fallback projection is
invented. The slice performs no runtime write outside its disposable fresh-host registration/
activation proof, adds no transaction, and requires no rollback.

## Implementation sequence

1. Add the closed catalog classification test over the exact ratified mechanic roots and component
   sidecars.
2. Extend the fresh-host adoption proof with zero projection graph/table assertions after exact
   activation.
3. Run focused catalog/projection/protocol/guard tests, full shared/local-AI suites, catalog
   validation, warning-free isolated solution build, and `git diff --check`; receipt and status.

## Acceptance matrix

| Concern | Required evidence |
| --- | --- |
| Complete inventory | Exactly 14 ratified mechanics are classified. |
| Existing owner | Every requirement parses through `MechanicRequirements` and names exact components. |
| Component parity | Every referenced component maps to one of the 33 adopted schema sidecars. |
| No invention | No authored derived-projection source/ID and no fresh-host projection row exist. |
| Activation | Existing exact winner/activation/catalog evidence remains unchanged. |
| Ruleset neutrality | No D&D rule, formula, role meaning, or application branch enters C#. |
| Compatibility | Legacy `IProjectionResolver` and application-kernel projection behavior remain unchanged. |

## Verification commands

- Focused catalog readiness, projection-materialization, fresh-host `dnd2024`, and game-vocabulary
  guard tests.
- `roleplay validate catalog`; full shared/local-AI suites; warning-free isolated solution build;
  `git diff --check`.

## Completion receipt and exit gate

Record acceptance in `receipts/APPLICATION-KERNEL-SLICE-11I-RECEIPT.md`, mark this document accepted,
update the Slice 11 projection leaf/status links, and stop before legacy state-space identity,
backfill, non-empty migration, execution compatibility, or final parity work.
